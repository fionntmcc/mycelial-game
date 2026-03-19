namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Player-controlled mycelium tendril with a compact 2x2 head,
/// diagonal movement, and hunger system.
///
/// The tendril head uses a fixed 2x2 footprint.
/// Tiles the tendril travels over settle to Mycelium (lightest)
/// while the active head remains MyceliumCore.
///
/// Movement: WASD for cardinal, diagonals by holding two keys.
/// Hunger drains per move, replenished by consuming organic tiles.
/// At 0 hunger, auto-retreat along the trail back to the tree.
/// </summary>
public partial class TendrilController : Node2D
{
	[Export] public NodePath ChunkManagerPath { get; set; }

	// --- Hunger Config ---
	[Export] public float MaxHunger = 100f;
	[Export] public float HungerPerMove = 0.8f;
	[Export] public float HungerPerHardMove = 2.5f;
	[Export] public float HungerOnCorrupted = 0.2f;
	[Export] public float HungerRegenOnCorrupted = 0.8f;

	// --- Movement Config ---
	[Export] public float MoveDelay = 0.08f;
	[Export] public float RetreatSpeed = 0.03f;

	// --- Root Spread Config ---
	[Export] public bool EnableRootSpread = true;
	[Export] public float RootSpreadInterval = 1.6f;
	[Export] public float RootSpawnChanceOnDownMove = 0.20f;
	[Export] public float RootBranchChance = 0.08f;
	[Export] public int MaxActiveRoots = 8;
	[Export] public int RootMinLength = 8;
	[Export] public int RootMaxLength = 22;

	// --- Tip Shape ---
	// Fixed 2x2 head footprint.
	private static readonly (int dx, int dy)[] TipShapeBase = new[]
	{
		(0, 0),
		(1, 0),
		(0, 1),
		(1, 1),
	};

	// Root sprouts can emerge from edges and underside of the 2x2 head.
	private static readonly (int dx, int dy)[] RootSpawnOffsets = new[]
	{
		(-1, 0), (2, 0),
		(-1, 1), (2, 1),
		(0, 2), (1, 2),
	};

	private struct RootTip
	{
		public int X;
		public int Y;
		public int DirX;
		public int DirY;
		public int Length;
		public int MaxLength;
		public int StuckSteps;
	}

	// --- State ---
	private ChunkManager _chunkManager;
	public int HeadX { get; private set; }
	public int HeadY { get; private set; }
	public float Hunger { get; private set; }
	public bool IsRetreating { get; private set; }
	public bool IsRegenerating { get; private set; }

	// Trail: ordered list of head positions (most recent first)
	// Stores original tile at each position for terrain-aware infection
	private readonly List<(int X, int Y, TileType OriginalTile)> _trail = new();
	private readonly HashSet<long> _claimedTiles = new();
	private readonly HashSet<long> _treeTiles = new();

	// Stores the original tile type at each position before we overwrote it
	private readonly Dictionary<long, TileType> _originalTiles = new();

	// Current tip tiles (for clearing when head moves)
	private readonly List<(int X, int Y)> _currentTipTiles = new();

	private float _moveTimer;
	private float _retreatTimer;
	private float _regenTimer;
	private float _rootSpreadTimer;
	private List<(int X, int Y)> _rootTips;
	private int _currentRootTipIdx;
	private (int dx, int dy) _lastDirection = (0, 1); // Default facing down
	private readonly List<RootTip> _activeRootTips = new();
	private readonly System.Random _rng = new();

	public int ClaimedTileCount => _claimedTiles.Count;
	public HashSet<long> ClaimedTiles => _claimedTiles;

	// --- Signals ---
	[Signal] public delegate void HungerChangedEventHandler(float current, float max);
	[Signal] public delegate void TendrilMovedEventHandler(int x, int y);
	[Signal] public delegate void RetreatStartedEventHandler();
	[Signal] public delegate void RetreatEndedEventHandler();
	[Signal] public delegate void TileConsumedEventHandler(int x, int y, float hungerGain);

	// --- Lifecycle ---

	public override void _Ready()
	{
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);

		if (_chunkManager == null)
		{
			GD.PrintErr("TendrilController: No ChunkManager assigned!");
			return;
		}

		Hunger = MaxHunger;
		CallDeferred(nameof(Initialize));
	}

	private void Initialize()
	{
		_rootTips = _chunkManager.GetRootTipPositions();
		if (_rootTips.Count == 0)
		{
			GD.PrintErr("TendrilController: No root tips found!");
			return;
		}

		RegisterTreeTiles();
		_currentRootTipIdx = 0;
		SpawnAtRootTip(_currentRootTipIdx);
		GD.Print($"Tendril spawned at ({HeadX}, {HeadY}). Tip size: {TipShapeBase.Length} tiles.");
	}

	private void SpawnAtRootTip(int idx)
	{
		if (idx >= _rootTips.Count) idx = 0;
		var (x, y) = _rootTips[idx];

		HeadX = x;
		HeadY = y;
		Hunger = MaxHunger;
		IsRetreating = false;
		IsRegenerating = false;
		_trail.Clear();
		_currentTipTiles.Clear();
		_activeRootTips.Clear();
		_rootSpreadTimer = 0f;

		// Place initial tip
		PlaceTip();

		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	public override void _Process(double delta)
	{
		if (_chunkManager == null) return;

		float dt = (float)delta;

		if (IsRegenerating)
		{
			ProcessRegeneration(dt);
			return;
		}

		if (IsRetreating)
		{
			ProcessRetreat(dt);
			return;
		}

		// Passive hunger regen when stationary on corrupted land
		if (_claimedTiles.Contains(PackCoords(HeadX, HeadY)))
		{
			Hunger = System.Math.Min(MaxHunger, Hunger + HungerRegenOnCorrupted * dt);
			EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		}

		ProcessMovement(dt);
		ProcessRootSpread(dt);
	}

	// --- Movement ---

	private void ProcessMovement(float dt)
	{
		_moveTimer -= dt;

		int dirX = 0, dirY = 0;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			dirY -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			dirY += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			dirX -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			dirX += 1;

		if (dirX == 0 && dirY == 0)
		{
			_moveTimer = 0;
			return;
		}

		if (_moveTimer > 0) return;
		_moveTimer = MoveDelay;

		int newX = HeadX + dirX;
		int newY = HeadY + dirY;

		// Update facing direction for tip shape
		_lastDirection = (dirX, dirY);

		TryMove(newX, newY);
	}

	private void TryMove(int newX, int newY)
	{
		// Check if the tip can fit at the new position
		// Only need the center and immediate core to be passable
		TileType centerTile = _chunkManager.GetTileAt(newX, newY);

		// Can't move into unbreakable solids
		if (TileProperties.Is(centerTile, TileFlags.Solid) && !TileProperties.Is(centerTile, TileFlags.Breakable))
			return;

		if (TileProperties.Is(centerTile, TileFlags.Liquid))
			return;

		if (TileProperties.Is(centerTile, TileFlags.Hazardous))
			return;

		// Calculate hunger cost
		bool isOwnTerritory = _claimedTiles.Contains(PackCoords(newX, newY));
		float cost;

		if (isOwnTerritory)
			cost = HungerOnCorrupted;
		else if (IsHardTile(centerTile))
			cost = HungerPerHardMove;
		else
			cost = HungerPerMove;

		if (Hunger - cost <= 0 && !isOwnTerritory)
		{
			StartRetreat();
			return;
		}

		// Calculate hunger gain from ALL tiles the tip will consume
		float totalGain = 0f;
		var tipShape = GetTipShapeForDirection(_lastDirection);
		foreach (var (dx, dy) in tipShape)
		{
			int tx = newX + dx;
			int ty = newY + dy;
			if (!_claimedTiles.Contains(PackCoords(tx, ty)))
			{
				TileType t = _chunkManager.GetTileAt(tx, ty);
				totalGain += GetHungerGain(t);
			}
		}

		// Record trail position BEFORE moving (for gradient and retreat)
		// Look up the original tile at current head position
		long headKey = PackCoords(HeadX, HeadY);
		TileType origTile = _originalTiles.TryGetValue(headKey, out TileType ot) ? ot : TileType.Dirt;
		_trail.Insert(0, (HeadX, HeadY, origTile));

		// Move head
		HeadX = newX;
		HeadY = newY;

		// Apply hunger
		Hunger = System.Math.Max(0, Hunger - cost);
		Hunger = System.Math.Min(MaxHunger, Hunger + totalGain);

		if (totalGain > 0)
			EmitSignal(SignalName.TileConsumed, newX, newY, totalGain);

		// Update visuals: place new tip, update gradient trail
		PlaceTip();
		UpdateTrailGradient();
		MaybeSpawnRootTipFromDownwardMove();

		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);

		if (Hunger <= 0)
			StartRetreat();
	}

	// --- Tip Placement ---

	/// <summary>
	/// Place the tip shape at the current head position as MyceliumCore tiles.
	/// </summary>
	private void PlaceTip()
	{
		_currentTipTiles.Clear();
		var tipShape = GetTipShapeForDirection(_lastDirection);

		foreach (var (dx, dy) in tipShape)
		{
			int tx = HeadX + dx;
			int ty = HeadY + dy;
			long key = PackCoords(tx, ty);

			if (_treeTiles.Contains(key)) continue;

			TileType existing = _chunkManager.GetTileAt(tx, ty);
			if (TileProperties.Is(existing, TileFlags.Solid) && !TileProperties.Is(existing, TileFlags.Breakable))
				continue;
			if (TileProperties.Is(existing, TileFlags.Liquid))
				continue;

			// Record original tile before we overwrite (only first time)
			if (!_originalTiles.ContainsKey(key) && !TileProperties.IsMycelium(existing))
				_originalTiles[key] = existing;

			_chunkManager.SetTileAt(tx, ty, TileType.MyceliumCore);
			_claimedTiles.Add(key);
			_currentTipTiles.Add((tx, ty));
		}
	}

	/// <summary>
	/// Get the current tip shape.
	/// </summary>
	private static (int dx, int dy)[] GetTipShapeForDirection((int dx, int dy) dir)
	{
		return TipShapeBase;
	}

	// --- Trail Gradient ---

	/// <summary>
	/// Update tiles behind the head to permanent light mycelium.
	/// </summary>
	private void UpdateTrailGradient()
	{
		for (int i = 0; i < _trail.Count; i++)
		{
			var (tx, ty, _) = _trail[i];

			// Apply light mycelium to the tip footprint at this trail position.
			foreach (var (dx, dy) in TipShapeBase)
			{
				int gx = tx + dx;
				int gy = ty + dy;
				long key = PackCoords(gx, gy);

				if (_treeTiles.Contains(key)) continue;

				TileType current = _chunkManager.GetTileAt(gx, gy);
				if (current == TileType.MyceliumCore)
				{
					// Keep active head tiles as MyceliumCore.
					bool isCurrentTip = false;
					foreach (var (ctx, cty) in _currentTipTiles)
					{
						if (ctx == gx && cty == gy) { isCurrentTip = true; break; }
					}
					if (isCurrentTip) continue;
				}

				if (current != TileType.Mycelium)
				{
					_chunkManager.SetTileAt(gx, gy, TileType.Mycelium);
				}
			}
		}
	}

	// --- Root Spread ---

	private void MaybeSpawnRootTipFromDownwardMove()
	{
		if (!EnableRootSpread) return;
		if (_lastDirection.dy <= 0) return;
		if (_activeRootTips.Count >= MaxActiveRoots) return;
		if (_rng.NextDouble() > RootSpawnChanceOnDownMove) return;

		var offset = RootSpawnOffsets[_rng.Next(RootSpawnOffsets.Length)];
		int startX = HeadX + offset.dx;
		int startY = HeadY + offset.dy;

		int dirX = offset.dx < 0 ? -1 : (offset.dx > 1 ? 1 : (_rng.Next(3) - 1));
		int dirY = 1;

		if (!TrySpreadRootInto(startX, startY)) return;

		int maxLength = RootMinLength;
		if (RootMaxLength > RootMinLength)
			maxLength += _rng.Next(RootMaxLength - RootMinLength + 1);

		_activeRootTips.Add(new RootTip
		{
			X = startX,
			Y = startY,
			DirX = dirX,
			DirY = dirY,
			Length = 1,
			MaxLength = maxLength,
			StuckSteps = 0,
		});
	}

	private void ProcessRootSpread(float dt)
	{
		if (!EnableRootSpread || _activeRootTips.Count == 0) return;

		_rootSpreadTimer += dt;
		if (_rootSpreadTimer < RootSpreadInterval) return;
		_rootSpreadTimer -= RootSpreadInterval;

		int idx = _rng.Next(_activeRootTips.Count);
		var tip = _activeRootTips[idx];

		if (tip.Length >= tip.MaxLength || tip.StuckSteps >= 3)
		{
			_activeRootTips.RemoveAt(idx);
			return;
		}

		var (nextDirX, nextDirY) = ChooseNextRootDirection(tip);
		int nextX = tip.X + nextDirX;
		int nextY = tip.Y + nextDirY;

		if (TrySpreadRootInto(nextX, nextY))
		{
			tip.X = nextX;
			tip.Y = nextY;
			tip.DirX = nextDirX;
			tip.DirY = nextDirY;
			tip.Length++;
			tip.StuckSteps = 0;
			_activeRootTips[idx] = tip;
			TryBranchRootTip(tip);
			return;
		}

		tip.StuckSteps++;
		tip.Length++;
		tip.DirX = _rng.Next(3) - 1;
		tip.DirY = 1;

		if (tip.Length >= tip.MaxLength || tip.StuckSteps >= 3)
			_activeRootTips.RemoveAt(idx);
		else
			_activeRootTips[idx] = tip;
	}

	private (int dirX, int dirY) ChooseNextRootDirection(RootTip tip)
	{
		int dirX = tip.DirX;

		if (_rng.NextDouble() < 0.35f)
			dirX += _rng.Next(3) - 1;

		if (dirX < -1) dirX = -1;
		if (dirX > 1) dirX = 1;

		if (dirX == 0 && _rng.NextDouble() < 0.25f)
			dirX = _rng.Next(2) == 0 ? -1 : 1;

		return (dirX, 1);
	}

	private void TryBranchRootTip(RootTip parent)
	{
		if (_activeRootTips.Count >= MaxActiveRoots) return;
		if (_rng.NextDouble() > RootBranchChance) return;

		int branchDirX = parent.DirX == 0
			? (_rng.Next(2) == 0 ? -1 : 1)
			: -parent.DirX;

		int branchX = parent.X + branchDirX;
		int branchY = parent.Y + 1;

		if (!TrySpreadRootInto(branchX, branchY)) return;

		int branchMaxLength = RootMinLength;
		if (RootMaxLength > RootMinLength)
			branchMaxLength += _rng.Next(RootMaxLength - RootMinLength + 1);

		_activeRootTips.Add(new RootTip
		{
			X = branchX,
			Y = branchY,
			DirX = branchDirX,
			DirY = 1,
			Length = 1,
			MaxLength = branchMaxLength,
			StuckSteps = 0,
		});
	}

	private bool TrySpreadRootInto(int x, int y)
	{
		long key = PackCoords(x, y);
		if (_treeTiles.Contains(key)) return false;

		TileType tile = _chunkManager.GetTileAt(x, y);
		if (TileProperties.Is(tile, TileFlags.Liquid)) return false;
		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
			return false;

		bool isHeadTile = false;
		foreach (var (hx, hy) in _currentTipTiles)
		{
			if (hx == x && hy == y)
			{
				isHeadTile = true;
				break;
			}
		}

		if (!isHeadTile && tile != TileType.Mycelium)
			_chunkManager.SetTileAt(x, y, TileType.Mycelium);

		if (!_originalTiles.ContainsKey(key) && !TileProperties.IsMycelium(tile))
			_originalTiles[key] = tile;

		_claimedTiles.Add(key);
		return true;
	}

	// --- Retreat ---

	private void StartRetreat()
	{
		if (IsRetreating) return;
		IsRetreating = true;
		_retreatTimer = 0;
		GD.Print("Tendril retreating!");
		EmitSignal(SignalName.RetreatStarted);
	}

	private void ProcessRetreat(float dt)
	{
		_retreatTimer -= dt;
		if (_retreatTimer > 0) return;
		_retreatTimer = RetreatSpeed;

		if (_trail.Count == 0)
		{
			StartRegeneration();
			return;
		}

		// Clear current tip tiles (territory shrinks)
		foreach (var (tx, ty) in _currentTipTiles)
		{
			long key = PackCoords(tx, ty);
			_claimedTiles.Remove(key);
			_chunkManager.SetTileAt(tx, ty, TileType.Air);
		}

		// Move head back along trail
		var (prevX, prevY, _) = _trail[0];
		_trail.RemoveAt(0);
		HeadX = prevX;
		HeadY = prevY;

		// Place tip at new position (so you can see the retreat)
		_currentTipTiles.Clear();
		foreach (var (dx, dy) in TipShapeBase)
		{
			int gx = HeadX + dx;
			int gy = HeadY + dy;
			long key = PackCoords(gx, gy);
			if (_claimedTiles.Contains(key))
			{
				_chunkManager.SetTileAt(gx, gy, TileType.MyceliumCore);
				_currentTipTiles.Add((gx, gy));
			}
		}

		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	// --- Regeneration ---

	private void StartRegeneration()
	{
		IsRetreating = false;
		IsRegenerating = true;
		_regenTimer = 2.0f;
		GD.Print("Tendril regenerating at tree...");
		EmitSignal(SignalName.RetreatEnded);
	}

	private void ProcessRegeneration(float dt)
	{
		_regenTimer -= dt;
		if (_regenTimer > 0) return;

		_currentRootTipIdx = (_currentRootTipIdx + 1) % _rootTips.Count;
		SpawnAtRootTip(_currentRootTipIdx);
		GD.Print($"Tendril regenerated at root tip {_currentRootTipIdx}!");
	}

	// --- Hunger Gain ---

	private static float GetHungerGain(TileType tile)
	{
		return tile switch
		{
			TileType.Dirt => 2.0f,
			TileType.Sand => 1.0f,
			TileType.Clay => 0.5f,
			TileType.Leaf => 4.0f,
			TileType.Roots => 6.0f,
			TileType.RootTip => 8.0f,

			// Grass variants — consuming the living surface
			TileType.GrassFloor or TileType.GrassCeiling
			or TileType.GrassLWall or TileType.GrassRWall => 4.0f,

			TileType.GrassInnerTL or TileType.GrassInnerTR
			or TileType.GrassInnerBL or TileType.GrassInnerBR => 4.0f,

			TileType.GrassOuterTL or TileType.GrassOuterTR
			or TileType.GrassOuterBL or TileType.GrassOuterBR => 4.0f,

			// Resource nodes — big gains
			TileType.BoneMarrow => 15.0f,
			TileType.AncientSporeNode => 20.0f,
			TileType.CrystalGrotte => 12.0f,
			TileType.BioluminescentVein => 8.0f,

			// Air — no gain
			TileType.Air => 0f,

			// Already infected — no gain
			TileType.Mycelium or TileType.MyceliumDense
			or TileType.MyceliumDark or TileType.MyceliumCore
			or TileType.InfectedDirt => 0f,

			// Infected grass — no gain (already consumed)
			_ when TileProperties.IsInfectedGrass(tile) => 0f,

			// Default — tiny gain if organic, nothing otherwise
			_ => TileProperties.Is(tile, TileFlags.Organic) ? 1.0f : 0f,
		};
	}

	private static bool IsHardTile(TileType tile)
	{
		return tile == TileType.Clay || tile == TileType.Gravel
			|| tile == TileType.Roots;
	}

	// --- Tree Registration ---

	private void RegisterTreeTiles()
	{
		int treeX = WorldConfig.TreeWorldX;
		int scanRadius = WorldConfig.TreeCanopyRadius + 5;

		for (int x = treeX - scanRadius; x <= treeX + scanRadius; x++)
		{
			for (int y = -WorldConfig.TreeTrunkHeight - WorldConfig.TreeCanopyHeight - 5;
				 y <= WorldConfig.TreeRootDepth + 10; y++)
			{
				TileType tile = _chunkManager.GetTileAt(x, y);
				if (IsTreeTile(tile))
					_treeTiles.Add(PackCoords(x, y));
			}
		}
	}

	private static bool IsTreeTile(TileType t)
	{
		return t == TileType.Wood || t == TileType.Leaf
			|| t == TileType.Roots || t == TileType.RootTip;
	}

	// --- Utility ---

	public Vector2 GetHeadPixelPosition()
	{
		return new Vector2(
			HeadX * WorldConfig.TileSize + WorldConfig.TileSize / 2f,
			HeadY * WorldConfig.TileSize + WorldConfig.TileSize / 2f
		);
	}

	/// <summary>Check if a world tile position overlaps the current tendril head.</summary>
	public bool OverlapsHead(int worldX, int worldY)
	{
		foreach (var (tx, ty) in _currentTipTiles)
		{
			if (tx == worldX && ty == worldY) return true;
		}
		return false;
	}

	/// <summary>Check if a world tile position is on claimed mycelium territory.</summary>
	public bool IsOnTerritory(int worldX, int worldY)
	{
		return _claimedTiles.Contains(PackCoords(worldX, worldY));
	}

	/// <summary>Add hunger from external source (e.g. consuming a creature).</summary>
	public void AddHunger(float amount)
	{
		Hunger = System.Math.Min(MaxHunger, Hunger + amount);
		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
	}

	/// <summary>Drain hunger from external source (e.g. creature attack).</summary>
	public void DrainHunger(float amount)
	{
		Hunger = System.Math.Max(0, Hunger - amount);
		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		if (Hunger <= 0) StartRetreat();
	}

	public static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);
}
