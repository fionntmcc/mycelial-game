namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Player-controlled mycelium tendril — orchestrator for the continuous movement system.
///
/// This is the rewritten controller that delegates physics to TendrilHead,
/// rendering to TendrilSplineRenderer, and tile conversion to TendrilInfection.
///
/// WHAT THIS CLASS DOES:
///   - Hunger management (drain, regen, starvation → retreat)
///   - Retreat and regeneration state machine
///   - Root spread system (unchanged from original)
///   - Signal emission for HUD, camera, creatures, fog-of-war
///   - Claimed territory tracking
///
/// WHAT IT DELEGATES:
///   - Movement physics → TendrilHead
///   - Visual trail → TendrilSplineRenderer
///   - Tile infection → TendrilInfection
///
/// BACKWARD COMPATIBILITY:
///   HeadX, HeadY, ClaimedTiles, PackCoords, IsOnTerritory, OverlapsHead,
///   AddHunger, DrainHunger, GetHeadPixelPosition all work exactly as before.
///   External systems (CreatureManager, FogOfWar, PassiveCorruption, TendrilHUD)
///   require NO changes.
///
/// SETUP:
///   - Assign ChunkManagerPath in inspector
///   - TendrilHead, TendrilSplineRenderer are created as child nodes
///   - TendrilInfection is created programmatically
/// </summary>
public partial class TendrilController : Node2D
{
	[Export] public NodePath ChunkManagerPath { get; set; }

	// =========================================================================
	//  HUNGER CONFIG
	// =========================================================================

	[ExportGroup("Hunger")]
	[Export] public float MaxHunger = 100f;

	/// <summary>Base hunger drain per second while moving through normal soil.</summary>
	[Export] public float HungerDrainPerSecond = 6f;

	/// <summary>Hunger drain multiplier for hard tiles (clay, gravel).</summary>
	[Export] public float HardTileDrainMultiplier = 2.5f;

	/// <summary>Hunger drain when on own territory (very low — territory is cheap to retrace).</summary>
	[Export] public float HungerDrainOnTerritory = 1.0f;

	/// <summary>Passive hunger regen per second when stationary on corrupted land.</summary>
	[Export] public float HungerRegenOnCorrupted = 0.8f;

	[ExportGroup("Retreat")]
	[Export] public float RetreatSpeed = 0.03f;

	// =========================================================================
	//  ROOT SPREAD CONFIG (unchanged from original)
	// =========================================================================

	[ExportGroup("Root Spread")]
	[Export] public bool EnableRootSpread = true;
	[Export] public float RootSpreadInterval = 0.9f;
	[Export] public int RootGrowthStepsPerTick = 3;
	[Export] public int RootSpawnEveryTiles = 15;
	[Export] public int RootSpawnIntervalJitter = 2;
	[Export] public int RootSpawnBurstMin = 2;
	[Export] public int RootSpawnBurstMax = 4;
	[Export] public float RootBranchChance = 0.18f;
	[Export] public int MaxActiveRoots = 20;
	[Export] public int RootMinLength = 8;
	[Export] public int RootMaxLength = 22;

	// Root spawn offsets relative to head tile
	private static readonly (int dx, int dy)[] RootSpawnOffsets = new[]
	{
		(-1, 0), (-1, 1), (-1, 2),
		(3, 0), (3, 1), (3, 2),
		(0, 3), (1, 3), (2, 3),
	};

	private struct RootTip
	{
		public int X, Y;
		public int DirX, DirY;
		public int Length, MaxLength;
		public int StuckSteps;
	}

	// =========================================================================
	//  PUBLIC STATE — Backward-compatible API
	// =========================================================================

	/// <summary>Tile X of the head (backward compat for creatures, fog, etc.).</summary>
	public int HeadX => _head != null ? _head.CurrentTile.X : _fallbackHeadX;

	/// <summary>Tile Y of the head.</summary>
	public int HeadY => _head != null ? _head.CurrentTile.Y : _fallbackHeadY;

	public float Hunger { get; private set; }
	public bool IsRetreating { get; private set; }
	public bool IsRegenerating { get; private set; }

	public int ClaimedTileCount => _claimedTiles.Count;
	public HashSet<long> ClaimedTiles => _claimedTiles;

	/// <summary>Pixel position of the head (for camera, new systems).</summary>
	public Vector2 HeadPixelPosition => _head?.HeadPosition ?? GetFallbackPixelPos();

	// =========================================================================
	//  SIGNALS — Same signatures as before
	// =========================================================================

	[Signal] public delegate void HungerChangedEventHandler(float current, float max);
	[Signal] public delegate void TendrilMovedEventHandler(int x, int y);
	[Signal] public delegate void RetreatStartedEventHandler();
	[Signal] public delegate void RetreatEndedEventHandler();
	[Signal] public delegate void TileConsumedEventHandler(int x, int y, float hungerGain);

	// =========================================================================
	//  INTERNAL STATE
	// =========================================================================

	private ChunkManager _chunkManager;

	// New subsystems
	private TendrilHead _head;
	private TendrilSplineRenderer _splineRenderer;
	private TendrilInfection _infection;

	// Territory tracking (shared with infection system)
	private readonly HashSet<long> _claimedTiles = new();
	private readonly HashSet<long> _treeTiles = new();
	private readonly Dictionary<long, TileType> _originalTiles = new();

	// Root spread state
	private readonly List<RootTip> _activeRootTips = new();
	private float _rootSpreadTimer;
	private int _tilesSinceLastRootSpawn;
	private int _nextRootSpawnDistance;

	// Spawn points
	private List<(int X, int Y)> _rootTips;
	private int _currentRootTipIdx;

	// Retreat state
	private float _retreatTimer;
	private float _regenTimer;

	// Track last tile position for root spawn triggering
	private (int X, int Y) _lastTileForRoots;

	// Fallback position before head initializes
	private int _fallbackHeadX;
	private int _fallbackHeadY;

	private readonly System.Random _rng = new();

	// =========================================================================
	//  LIFECYCLE
	// =========================================================================

	public override void _Ready()
	{
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);

		if (_chunkManager == null)
		{
			GD.PrintErr("TendrilController: No ChunkManager assigned!");
			return;
		}

		// --- Create subsystems as child nodes ---
		_head = new TendrilHead { Name = "TendrilHead" };
		AddChild(_head);

		_splineRenderer = new TendrilSplineRenderer { Name = "TendrilSplineRenderer" };
		AddChild(_splineRenderer);

		_infection = new TendrilInfection { Name = "TendrilInfection" };
		AddChild(_infection);
		_infection.Initialize(_chunkManager, _claimedTiles, _treeTiles, _originalTiles);
		_infection.TileInfected += OnTileInfected;

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
		GD.Print($"Tendril spawned at ({HeadX}, {HeadY}) — continuous movement active.");
	}

	private void SpawnAtRootTip(int idx)
	{
		if (idx >= _rootTips.Count) idx = 0;
		var (tileX, tileY) = _rootTips[idx];

		_fallbackHeadX = tileX;
		_fallbackHeadY = tileY;

		Vector2 pixelPos = TendrilHead.TileToPixel(tileX, tileY);
		_head.Initialize(_chunkManager, pixelPos, Mathf.Pi * 0.5f); // Facing down

		Hunger = MaxHunger;
		IsRetreating = false;
		IsRegenerating = false;

		_splineRenderer.ClearTrail();
		_infection.Reset();

		_activeRootTips.Clear();
		_rootSpreadTimer = 0f;
		_tilesSinceLastRootSpawn = 0;
		_lastTileForRoots = (tileX, tileY);
		ScheduleNextRootSpawnDistance();

		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	// =========================================================================
	//  MAIN LOOP
	// =========================================================================

	public override void _Process(double delta)
	{
		if (_chunkManager == null || _head == null) return;

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

		// --- 1. Update head physics ---
		_head.Update(dt);

		// --- 2. Record trail for spline renderer ---
		_splineRenderer.RecordPoint(_head.HeadPosition, _head.Speed);
		_splineRenderer.UpdateRenderer(dt, _head.HeadPosition);

		// --- 3. Infection (exposure-based tile conversion) ---
		float hungerGained = _infection.Update(dt, _head.HeadPosition, _head.Speed);
		if (hungerGained > 0f)
			Hunger = System.Math.Min(MaxHunger, Hunger + hungerGained);

		// --- 4. Hunger drain (continuous, speed-based) ---
		if (_head.Speed > _head.DeadZoneSpeed)
		{
			float drain = GetMovementHungerDrain();
			Hunger -= drain * dt;

			if (Hunger <= 0f)
			{
				Hunger = 0f;
				StartRetreat();
				EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
				return;
			}
		}
		else
		{
			// Stationary on territory = passive regen
			if (_claimedTiles.Contains(PackCoords(HeadX, HeadY)))
				Hunger = System.Math.Min(MaxHunger, Hunger + HungerRegenOnCorrupted * dt);
		}

		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);

		// --- 5. Root spread tracking ---
		TrackTravelAndSpawnRoots();
		ProcessRootSpread(dt);
	}

	// =========================================================================
	//  HUNGER DRAIN
	// =========================================================================

	private float GetMovementHungerDrain()
	{
		TileType tile = _chunkManager.GetTileAt(HeadX, HeadY);

		if (_claimedTiles.Contains(PackCoords(HeadX, HeadY)))
			return HungerDrainOnTerritory;

		if (IsHardTile(tile))
			return HungerDrainPerSecond * HardTileDrainMultiplier;

		return HungerDrainPerSecond;
	}

	private static bool IsHardTile(TileType tile)
	{
		return tile == TileType.Clay || tile == TileType.Gravel || tile == TileType.Roots;
	}

	// =========================================================================
	//  INFECTION CALLBACK
	// =========================================================================

	private void OnTileInfected(int tileX, int tileY, int originalTileType)
	{
		float gain = originalTileType switch
		{
			(int)TileType.Dirt => 2.0f,
			(int)TileType.Leaf => 4.0f,
			_ => 1.0f,
		};

		EmitSignal(SignalName.TileConsumed, tileX, tileY, gain);
	}

	// =========================================================================
	//  RETREAT — Animate backward along spline then regenerate
	// =========================================================================

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

		// Trim the spline trail from the tip (visual shrink)
		if (!_splineRenderer.IsEmpty)
		{
			_splineRenderer.TrimFromBase(2);

			// Move head toward base of trail
			Vector2? basePos = _splineRenderer.GetBasePosition();
			if (basePos.HasValue)
			{
				_head.Teleport(basePos.Value, _head.Heading);
				EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
			}
		}

		// When trail is exhausted, start regeneration
		if (_splineRenderer.IsEmpty || _splineRenderer.PointCount < 3)
		{
			StartRegeneration();
		}
	}

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

	// =========================================================================
	//  ROOT SPREAD (preserved from original — operates on tile grid)
	// =========================================================================

	private void TrackTravelAndSpawnRoots()
	{
		if (!EnableRootSpread) return;

		// Only spawn roots when moving downward
		var currentTile = _head.CurrentTile;
		if (currentTile.Y <= _lastTileForRoots.Y)
		{
			_lastTileForRoots = currentTile;
			return;
		}

		int tilesMoved = System.Math.Abs(currentTile.Y - _lastTileForRoots.Y)
					   + System.Math.Abs(currentTile.X - _lastTileForRoots.X);
		_tilesSinceLastRootSpawn += tilesMoved;
		_lastTileForRoots = currentTile;

		if (_tilesSinceLastRootSpawn >= _nextRootSpawnDistance)
		{
			SpawnRootBurst();
			_tilesSinceLastRootSpawn = 0;
			ScheduleNextRootSpawnDistance();
		}
	}

	private void ScheduleNextRootSpawnDistance()
	{
		int jitter = System.Math.Max(0, RootSpawnIntervalJitter);
		int min = System.Math.Max(1, RootSpawnEveryTiles - jitter);
		int max = System.Math.Max(min, RootSpawnEveryTiles + jitter);
		_nextRootSpawnDistance = _rng.Next(min, max + 1);
	}

	private void SpawnRootBurst()
	{
		if (_activeRootTips.Count >= MaxActiveRoots) return;

		int burstMin = System.Math.Max(1, RootSpawnBurstMin);
		int burstMax = System.Math.Max(burstMin, RootSpawnBurstMax);
		int desired = _rng.Next(burstMin, burstMax + 1);
		int available = MaxActiveRoots - _activeRootTips.Count;
		int spawnCount = System.Math.Min(desired, available);
		if (spawnCount <= 0) return;

		int spawned = 0;
		int attempts = 0;
		while (spawned < spawnCount && attempts < spawnCount * 6)
		{
			attempts++;
			if (TrySpawnSingleRootTip())
				spawned++;
		}
	}

	private bool TrySpawnSingleRootTip()
	{
		if (_activeRootTips.Count >= MaxActiveRoots) return false;

		var offset = RootSpawnOffsets[_rng.Next(RootSpawnOffsets.Length)];
		int startX = HeadX + offset.dx;
		int startY = HeadY + offset.dy;

		int dirX = offset.dx < 0 ? -1 : (offset.dx > 2 ? 1 : (_rng.Next(3) - 1));
		int dirY = 1;

		if (!TrySpreadRootInto(startX, startY)) return false;

		int maxLength = RootMinLength;
		if (RootMaxLength > RootMinLength)
			maxLength += _rng.Next(RootMaxLength - RootMinLength + 1);

		_activeRootTips.Add(new RootTip
		{
			X = startX, Y = startY,
			DirX = dirX, DirY = dirY,
			Length = 1, MaxLength = maxLength,
			StuckSteps = 0,
		});

		return true;
	}

	private void ProcessRootSpread(float dt)
	{
		if (!EnableRootSpread || _activeRootTips.Count == 0) return;

		_rootSpreadTimer += dt;
		if (_rootSpreadTimer < RootSpreadInterval) return;
		_rootSpreadTimer -= RootSpreadInterval;

		int steps = System.Math.Max(1, RootGrowthStepsPerTick);
		for (int i = 0; i < steps; i++)
		{
			if (_activeRootTips.Count == 0) break;
			GrowOneRootTipStep();
		}
	}

	private void GrowOneRootTipStep()
	{
		if (_activeRootTips.Count == 0) return;

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

		dirX = System.Math.Clamp(dirX, -1, 1);
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
			X = branchX, Y = branchY,
			DirX = branchDirX, DirY = 1,
			Length = 1, MaxLength = branchMaxLength,
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

		if (tile != TileType.Mycelium)
			_chunkManager.SetTileAt(x, y, TileType.Mycelium);

		if (!_originalTiles.ContainsKey(key) && !TileProperties.IsMycelium(tile))
			_originalTiles[key] = tile;

		_claimedTiles.Add(key);
		return true;
	}

	// =========================================================================
	//  TREE REGISTRATION
	// =========================================================================

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

	// =========================================================================
	//  PUBLIC API — Backward compatible
	// =========================================================================

	/// <summary>Get the head's pixel position (for camera tracking).</summary>
	public Vector2 GetHeadPixelPosition()
	{
		return _head?.HeadPosition ?? GetFallbackPixelPos();
	}

	/// <summary>Check if a world tile overlaps the tendril head area.</summary>
	public bool OverlapsHead(int worldX, int worldY)
	{
		// With continuous head, check if the tile is within ~1 tile of the head
		int dx = System.Math.Abs(worldX - HeadX);
		int dy = System.Math.Abs(worldY - HeadY);
		return dx <= 1 && dy <= 1;
	}

	/// <summary>Check if a world tile is on claimed mycelium territory.</summary>
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

	// =========================================================================
	//  UTILITY
	// =========================================================================

	public static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);

	private Vector2 GetFallbackPixelPos()
	{
		return new Vector2(
			_fallbackHeadX * WorldConfig.TileSize + WorldConfig.TileSize / 2f,
			_fallbackHeadY * WorldConfig.TileSize + WorldConfig.TileSize / 2f
		);
	}
}
