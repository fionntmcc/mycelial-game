namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// The core mycelium spreading system. The fungal network grows outward from
/// the Origin Tree's root tips, consuming organic tiles and converting them
/// into mycelium.
///
/// Spreading uses a frontier-based algorithm:
///   1. Root tips are the initial frontier
///   2. Each tick, a number of frontier tiles are activated
///   3. Activated tiles become Mycelium
///   4. Their organic neighbors are added to the frontier
///   5. Repeat forever — the network never stops growing (unless blocked or starved)
///
/// SETUP:
///   - Add as a child of the World node
///   - Assign the ChunkManager node path in the inspector
///   - The system auto-initializes from root tip positions when ready
/// </summary>
public partial class MyceliumSystem : Node2D
{
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public bool EnableAutonomousSpread = false;

	// --- Tunable Parameters ---

	/// <summary>Seconds between each spread tick.</summary>
	[Export] public float SpreadInterval = 0.15f;

	/// <summary>How many tiles spread per tick. Ramps up as network grows.</summary>
	[Export] public int BaseSpreadsPerTick = 3;

	/// <summary>Max spreads per tick (caps the ramp).</summary>
	[Export] public int MaxSpreadsPerTick = 20;

	/// <summary>Every N mycelium tiles placed, spreads per tick increases by 1.</summary>
	[Export] public int RampEveryNTiles = 15;

	// --- State ---
	private ChunkManager _chunkManager;
	private TendrilController _tendril;
	private float _timer;

	// The frontier — tiles that are candidates for becoming mycelium next.
	// Stored as a list for random access (we pick randomly from the frontier
	// so growth looks organic, not like a flood fill wavefront).
	private readonly List<(int X, int Y)> _frontier = new();

	// Set for O(1) duplicate checking — prevents the same tile being queued twice
	private readonly HashSet<long> _frontierSet = new();

	// Track all mycelium tiles for stats/gameplay
	private readonly HashSet<long> _myceliumTiles = new();

	// Track tiles that are part of the tree roots (don't overwrite these)
	private readonly HashSet<long> _treeTiles = new();

	// RNG for random frontier selection
	private readonly System.Random _rng = new();

	/// <summary>Total number of mycelium tiles placed.</summary>
	public int TotalMyceliumTiles => _myceliumTiles.Count;

	/// <summary>Current frontier size (tiles waiting to be consumed).</summary>
	public int FrontierSize => _frontier.Count;

	// --- Lifecycle ---

	public override void _Ready()
	{
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		if (_chunkManager == null)
		{
			GD.PrintErr("MyceliumSystem: No ChunkManager assigned!");
			return;
		}

		if (!EnableAutonomousSpread)
		{
			SetProcess(false);
			GD.Print("MyceliumSystem: Autonomous spread disabled. Tendril movement is the only spread source.");
			return;
		}

		// Wait a moment for chunks to generate before seeding
		// (root tips need to exist in generated chunks first)
		CallDeferred(nameof(InitializeFromRootTips));
	}

	/// <summary>
	/// Seed the frontier from the Origin Tree's root tips.
	/// Also seeds from the tree trunk base (the infection origin).
	/// </summary>
	private void InitializeFromRootTips()
	{
		var rootTips = _chunkManager.GetRootTipPositions();

		if (rootTips.Count == 0)
		{
			GD.PrintErr("MyceliumSystem: No root tips found! Is the tree generated?");
			return;
		}

		// Mark tree root/trunk tiles so we don't overwrite them with mycelium
		// (we want the tree to stay visible)
		RegisterTreeTiles();

		// Add root tips as initial frontier
		foreach (var (x, y) in rootTips)
		{
			AddToFrontier(x, y);

			// Also add immediate neighbors of root tips to get things moving
			AddNeighborsToFrontier(x, y);
		}

		// Seed from trunk base too — infection started there
		int treeX = WorldConfig.TreeWorldX;
		for (int dx = -3; dx <= 3; dx++)
		{
			for (int dy = 0; dy <= 3; dy++)
			{
				AddToFrontier(treeX + dx, dy);
			}
		}

		GD.Print($"MyceliumSystem initialized: {rootTips.Count} root tips, {_frontier.Count} frontier tiles");
	}

	/// <summary>
	/// Register tree tile positions so we don't overwrite them with mycelium.
	/// The tree should remain visible — mycelium grows AROUND and FROM it.
	/// </summary>
	private void RegisterTreeTiles()
	{
		// Scan the area around the tree trunk and roots
		int treeX = WorldConfig.TreeWorldX;
		int scanRadius = WorldConfig.TreeCanopyRadius + 5;

		for (int x = treeX - scanRadius; x <= treeX + scanRadius; x++)
		{
			for (int y = -WorldConfig.TreeTrunkHeight - WorldConfig.TreeCanopyHeight - 5;
				 y <= WorldConfig.TreeRootDepth + 10; y++)
			{
				TileType tile = _chunkManager.GetTileAt(x, y);
				if (IsTreeTile(tile))
				{
					_treeTiles.Add(PackCoords(x, y));
				}
			}
		}
	}

	private static bool IsTreeTile(TileType t)
	{
		return t == TileType.Wood || t == TileType.Leaf
			|| t == TileType.Roots || t == TileType.RootTip;
	}

	public override void _Process(double delta)
	{
		if (_chunkManager == null || _frontier.Count == 0) return;

		_timer += (float)delta;

		if (_timer >= SpreadInterval)
		{
			_timer -= SpreadInterval;
			SpreadTick();
		}
	}

	// --- Core Spreading Logic ---

	/// <summary>
	/// One tick of mycelium spreading. Picks random frontier tiles,
	/// converts them to mycelium, and adds their neighbors to the frontier.
	/// </summary>
	private void SpreadTick()
	{
		// Calculate how many tiles to spread this tick (ramps with network size)
		int spreadsThisTick = BaseSpreadsPerTick + (TotalMyceliumTiles / RampEveryNTiles);
		spreadsThisTick = System.Math.Min(spreadsThisTick, MaxSpreadsPerTick);

		// Scale by vigor root spread multiplier
		if (_tendril != null)
			spreadsThisTick = (int)(spreadsThisTick * _tendril.RootSpreadMultiplier);

		spreadsThisTick = System.Math.Min(spreadsThisTick, _frontier.Count);

		for (int i = 0; i < spreadsThisTick; i++)
		{
			if (_frontier.Count == 0) break;

			// Pick a random frontier tile (organic, natural-looking growth)
			int idx = _rng.Next(_frontier.Count);
			var (x, y) = _frontier[idx];

			// Remove from frontier (swap with last for O(1) removal)
			RemoveFromFrontier(idx);

			// Check if this tile is still valid for conversion
			TileType current = _chunkManager.GetTileAt(x, y);

			// Skip if it's already mycelium, a tree tile, or not organic
			long key = PackCoords(x, y);
			if (_myceliumTiles.Contains(key)) continue;
			if (_treeTiles.Contains(key)) continue;

			// Only spread into organic tiles or air adjacent to mycelium
			bool canSpread = TileProperties.Is(current, TileFlags.Organic)
						  || current == TileType.Air
						  || current == TileType.Dirt;

			if (!canSpread) continue;

			// Convert to mycelium!
			_chunkManager.SetTileAt(x, y, TileType.Mycelium);
			_myceliumTiles.Add(key);

			// Add organic neighbors to frontier
			AddNeighborsToFrontier(x, y);
		}
	}

	/// <summary>
	/// Check all 4 cardinal neighbors of a tile and add valid ones to the frontier.
	/// A neighbor is valid if it's organic, dirt, or air (mycelium can creep through air gaps).
	/// </summary>
	private void AddNeighborsToFrontier(int x, int y)
	{
		TryAddNeighbor(x - 1, y);
		TryAddNeighbor(x + 1, y);
		TryAddNeighbor(x, y - 1);
		TryAddNeighbor(x, y + 1);

		// Diagonal spread (slower — only sometimes)
		if (_rng.Next(3) == 0) // 33% chance for diagonals
		{
			TryAddNeighbor(x - 1, y - 1);
			TryAddNeighbor(x + 1, y - 1);
			TryAddNeighbor(x - 1, y + 1);
			TryAddNeighbor(x + 1, y + 1);
		}
	}

	private void TryAddNeighbor(int x, int y)
	{
		long key = PackCoords(x, y);

		// Skip if already in frontier, already mycelium, or a tree tile
		if (_frontierSet.Contains(key)) return;
		if (_myceliumTiles.Contains(key)) return;
		if (_treeTiles.Contains(key)) return;

		TileType tile = _chunkManager.GetTileAt(x, y);

		// Can spread into organic tiles
		if (TileProperties.Is(tile, TileFlags.Organic))
		{
			AddToFrontier(x, y);
			return;
		}

		// Can creep through air (but slower — air tiles get added with lower priority)
		if (tile == TileType.Air)
		{
			// Only spread through air if adjacent to at least one mycelium tile
			// (prevents spreading through huge open caves instantly)
			if (_rng.Next(4) == 0) // 25% chance — air spread is slow
			{
				AddToFrontier(x, y);
			}
			return;
		}

		// Blocked by stone, water, lava, etc. — no spread
	}

	// --- Frontier Management ---

	private void AddToFrontier(int x, int y)
	{
		long key = PackCoords(x, y);
		if (_frontierSet.Add(key)) // Returns false if already present
		{
			_frontier.Add((x, y));
		}
	}

	private void RemoveFromFrontier(int idx)
	{
		// Swap with last element for O(1) removal
		var removed = _frontier[idx];
		int lastIdx = _frontier.Count - 1;
		_frontier[idx] = _frontier[lastIdx];
		_frontier.RemoveAt(lastIdx);
		_frontierSet.Remove(PackCoords(removed.X, removed.Y));
	}

	// --- Utility ---

	private static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);
}
