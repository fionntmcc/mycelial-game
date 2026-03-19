namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Player-controlled mycelium tendril with an organic multi-tile head,
/// diagonal movement, gradient trail, and hunger system.
///
/// The tendril head is an organic blob (~3x3 with irregular edges).
/// As it moves, it leaves a gradient trail:
///   MyceliumCore  → the head itself (darkest)
///   MyceliumDark  → 1-2 moves behind (dark)
///   MyceliumDense → 3-5 moves behind (medium)
///   Mycelium      → everything older (lightest, permanent territory)
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
	[Export] public float MoveDelay = 0.08f;     // Fast — the head is big so it covers ground
	[Export] public float RetreatSpeed = 0.03f;

	// --- Gradient Trail Config ---
	private const int DarkTrailLength = 2;       // Moves that stay MyceliumDark
	private const int DenseTrailLength = 5;      // Moves that stay MyceliumDense
	// Everything older than DarkTrailLength + DenseTrailLength = Mycelium (lightest)

	// --- Tip Shape ---
	// Organic blob offsets from center. Not a perfect square — irregular, creepy.
	// This is a ~3x3 core with some extra tendrils poking out.
	// The shape rotates/varies slightly based on movement direction.
	private static readonly (int dx, int dy)[] TipShapeBase = new[]
	{
		// Core 3x3
		( 0,  0), (-1,  0), ( 1,  0),
		( 0, -1), (-1, -1), ( 1, -1),
		( 0,  1), (-1,  1), ( 1,  1),
		// Tendrils poking out — makes it look organic, not square
		(-2,  0), ( 2,  0), ( 0, -2), ( 0,  2),
		// Diagonal wisps
		(-2, -1), ( 2,  1), ( 1, -2), (-1,  2),
	};

	// Alternate shapes for movement direction — the blob shifts as it moves
	private static readonly (int dx, int dy)[] TipShapeRight = new[]
	{
		( 0,  0), (-1,  0), ( 1,  0),
		( 0, -1), (-1, -1), ( 1, -1),
		( 0,  1), (-1,  1), ( 1,  1),
		( 2,  0), ( 2, -1), ( 2,  1), // Leading edge stretches right
		( 3,  0), // Point reaching forward
		(-2,  0), ( 0, -2), ( 0,  2), // Trailing wisps
	};

	private static readonly (int dx, int dy)[] TipShapeLeft = new[]
	{
		( 0,  0), (-1,  0), ( 1,  0),
		( 0, -1), (-1, -1), ( 1, -1),
		( 0,  1), (-1,  1), ( 1,  1),
		(-2,  0), (-2, -1), (-2,  1),
		(-3,  0),
		( 2,  0), ( 0, -2), ( 0,  2),
	};

	private static readonly (int dx, int dy)[] TipShapeDown = new[]
	{
		( 0,  0), (-1,  0), ( 1,  0),
		( 0, -1), (-1, -1), ( 1, -1),
		( 0,  1), (-1,  1), ( 1,  1),
		( 0,  2), (-1,  2), ( 1,  2),
		( 0,  3),
		(-2,  0), ( 2,  0), ( 0, -2),
	};

	private static readonly (int dx, int dy)[] TipShapeUp = new[]
	{
		( 0,  0), (-1,  0), ( 1,  0),
		( 0, -1), (-1, -1), ( 1, -1),
		( 0,  1), (-1,  1), ( 1,  1),
		( 0, -2), (-1, -2), ( 1, -2),
		( 0, -3),
		(-2,  0), ( 2,  0), ( 0,  2),
	};

	// --- State ---
	private ChunkManager _chunkManager;
	public int HeadX { get; private set; }
	public int HeadY { get; private set; }
	public float Hunger { get; private set; }
	public bool IsRetreating { get; private set; }
	public bool IsRegenerating { get; private set; }

	// Trail: ordered list of head positions (most recent first)
	// Each entry = one move's head position
	private readonly List<(int X, int Y)> _trail = new();
	private readonly HashSet<long> _claimedTiles = new();
	private readonly HashSet<long> _treeTiles = new();

	// Current tip tiles (for clearing when head moves)
	private readonly List<(int X, int Y)> _currentTipTiles = new();

	private float _moveTimer;
	private float _retreatTimer;
	private float _regenTimer;
	private List<(int X, int Y)> _rootTips;
	private int _currentRootTipIdx;
	private (int dx, int dy) _lastDirection = (0, 1); // Default facing down

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
		_trail.Insert(0, (HeadX, HeadY));

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

			// Check the tile is passable (don't stamp through stone)
			TileType existing = _chunkManager.GetTileAt(tx, ty);
			if (TileProperties.Is(existing, TileFlags.Solid) && !TileProperties.Is(existing, TileFlags.Breakable))
				continue;
			if (TileProperties.Is(existing, TileFlags.Liquid))
				continue;

			_chunkManager.SetTileAt(tx, ty, TileType.MyceliumCore);
			_claimedTiles.Add(key);
			_currentTipTiles.Add((tx, ty));
		}
	}

	/// <summary>
	/// Get tip shape variant based on movement direction.
	/// The blob stretches in the direction of movement.
	/// </summary>
	private static (int dx, int dy)[] GetTipShapeForDirection((int dx, int dy) dir)
	{
		if (dir.dx > 0 && dir.dy == 0) return TipShapeRight;
		if (dir.dx < 0 && dir.dy == 0) return TipShapeLeft;
		if (dir.dy > 0 && dir.dx == 0) return TipShapeDown;
		if (dir.dy < 0 && dir.dx == 0) return TipShapeUp;
		// Diagonal or neutral — use base shape
		return TipShapeBase;
	}

	// --- Trail Gradient ---

	/// <summary>
	/// Update the gradient trail behind the head.
	/// Recent positions = dark, older = lighter.
	/// </summary>
	private void UpdateTrailGradient()
	{
		for (int i = 0; i < _trail.Count; i++)
		{
			var (tx, ty) = _trail[i];
			TileType gradientTile;

			if (i < DarkTrailLength)
				gradientTile = TileType.MyceliumDark;
			else if (i < DarkTrailLength + DenseTrailLength)
				gradientTile = TileType.MyceliumDense;
			else
				gradientTile = TileType.Mycelium;

			// Apply gradient to the tip footprint at this trail position
			// Use the base shape (not directional) for trail to keep it consistent
			foreach (var (dx, dy) in TipShapeBase)
			{
				int gx = tx + dx;
				int gy = ty + dy;
				long key = PackCoords(gx, gy);

				if (_treeTiles.Contains(key)) continue;

				// Don't downgrade tiles that are part of the current head
				TileType current = _chunkManager.GetTileAt(gx, gy);
				if (current == TileType.MyceliumCore && i > 0) // Don't touch the active head
				{
					// Only downgrade if this isn't a current tip tile
					bool isCurrentTip = false;
					foreach (var (ctx, cty) in _currentTipTiles)
					{
						if (ctx == gx && cty == gy) { isCurrentTip = true; break; }
					}
					if (isCurrentTip) continue;
				}

				// Only change if it would be a downgrade (darker → lighter over time)
				if (ShouldDowngrade(current, gradientTile))
				{
					_chunkManager.SetTileAt(gx, gy, gradientTile);
				}
			}

			// Once we've set the lightest gradient, stop processing — rest are already Mycelium
			if (i >= DarkTrailLength + DenseTrailLength + 2)
				break;
		}
	}

	/// <summary>
	/// Check if we should replace 'current' with 'target'.
	/// Only downgrades: Core → Dark → Dense → Mycelium (never upgrades old tiles).
	/// </summary>
	private static bool ShouldDowngrade(TileType current, TileType target)
	{
		int currentRank = GetGradientRank(current);
		int targetRank = GetGradientRank(target);
		// Lower rank = lighter. Only change if target is lighter than current.
		return targetRank < currentRank;
	}

	private static int GetGradientRank(TileType t)
	{
		return t switch
		{
			TileType.Mycelium => 0,
			TileType.MyceliumDense => 1,
			TileType.MyceliumDark => 2,
			TileType.MyceliumCore => 3,
			_ => -1 // Non-mycelium tiles
		};
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
		var (prevX, prevY) = _trail[0];
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
			TileType.Topsoil => 2.0f,
			TileType.Sand => 1.0f,
			TileType.RichSoil => 5.0f,
			TileType.MulchLayer => 5.0f,
			TileType.SmallRoots => 4.0f,
			TileType.DenseRoots => 6.0f,
			TileType.AncientRoot => 8.0f,
			TileType.FungalForestFloor => 6.0f,
			TileType.CrystallizedSap => 15.0f,
			TileType.BoneMarrow => 15.0f,
			TileType.AncientSporeNode => 20.0f,
			TileType.PhosphorescentMineral => 10.0f,
			TileType.CrystalGrotte => 12.0f,
			TileType.Clay => 0.5f,
			TileType.Air => 0f,
			TileType.Mycelium => 0f,
			TileType.MyceliumDense => 0f,
			TileType.MyceliumDark => 0f,
			TileType.MyceliumCore => 0f,
			_ => TileProperties.Is(tile, TileFlags.Organic) ? 1.0f : 0f,
		};
	}

	private static bool IsHardTile(TileType tile)
	{
		return tile == TileType.Clay || tile == TileType.ClayDeposit
			|| tile == TileType.DenseRoots || tile == TileType.AncientRoot
			|| tile == TileType.Gravel;
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
		return t == TileType.Bark || t == TileType.Heartwood || t == TileType.DeadHeartwood
			|| t == TileType.BranchWood || t == TileType.Canopy || t == TileType.DeadCanopy
			|| t == TileType.ThickRoot || t == TileType.MediumRoot
			|| t == TileType.ThinRoot || t == TileType.RootTip;
	}

	// --- Utility ---

	public Vector2 GetHeadPixelPosition()
	{
		return new Vector2(
			HeadX * WorldConfig.TileSize + WorldConfig.TileSize / 2f,
			HeadY * WorldConfig.TileSize + WorldConfig.TileSize / 2f
		);
	}

	public static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);
}
