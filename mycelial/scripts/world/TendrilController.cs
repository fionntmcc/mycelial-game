namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Player-controlled mycelium tendril with organic sub-grid movement.
///
/// The tendril lives on a fine sub-grid (4× terrain resolution) for smooth, creepy
/// movement that isn't locked to terrain tiles. The sub-grid is a visual overlay —
/// all gameplay interactions (hunger, creatures, territory) still work on terrain tiles.
///
/// ARCHITECTURE:
///   - Movement happens in sub-grid coordinates (4px steps instead of 16px)
///   - The head is an organic blob rendered on the sub-grid (see TendrilRenderer)
///   - When the blob enters a new terrain tile, gameplay effects trigger (hunger, claiming)
///   - Retreat walks back along the sub-grid trail for smooth reversal
///   - Root spread still operates on terrain tiles
///
/// The sub-grid position (_subHeadX, _subHeadY) is the source of truth.
/// HeadX/HeadY are derived terrain-tile coordinates for backward compatibility
/// with CreatureManager, FogOfWar, PassiveCorruption, etc.
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
	[Export] public float MomentumAcceleration = 8.0f;
	[Export] public float MomentumSteering = 14.0f;
	[Export] public float MomentumTurnAroundBrake = 10.0f;
	[Export] public float MomentumReverseLockThreshold = -0.55f;
	[Export] public float MomentumDrag = 3.2f;
	[Export] public float MomentumDeadZone = 0.08f;
	[Export] public float ControllerDeadZone = 0.22f;
	[Export] public float MinMoveDelayMultiplier = 0.58f;
	[Export] public float MaxMoveDelayMultiplier = 1.0f;
	[Export] public float BlockedMomentumDamping = 0.45f;
	[Export] public int MaxSubStepsPerFrame = 8;

	// --- Root Spread Config ---
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

	// --- Sub-Grid Blob Config ---
	/// <summary>Base radius of the organic head blob in sub-cells.</summary>
	[Export] public int BlobBaseRadius = 5;

	/// <summary>How much noise distorts the blob edge (in sub-cells).</summary>
	[Export] public float BlobNoiseAmplitude = 2.2f;

	/// <summary>Frequency of the blob edge noise. Higher = more jagged.</summary>
	[Export] public float BlobNoiseFrequency = 2.8f;

	/// <summary>How much the blob stretches in the movement direction (multiplier).</summary>
	[Export] public float BlobStretchFactor = 0.45f;

	/// <summary>Speed of the blob shape animation (makes it pulse/shift).</summary>
	[Export] public float BlobAnimSpeed = 1.8f;

	// --- Creature Auto-Steer ---
	/// <summary>Path to CreatureManager node (for auto-steering toward nearby prey).</summary>
	[Export] public NodePath CreatureManagerPath { get; set; }

	/// <summary>Radius in terrain tiles to scan for creatures to steer toward.</summary>
	[Export] public int CreatureSteerRadius = 6;

	/// <summary>How strongly the tendril steers toward nearby creatures (0–1).</summary>
	[Export] public float CreatureSteerStrength = 0.15f;
	
	/// <summary>Path to TendrilHarpoon node (freezes movement while harpoon is active).</summary>
	[Export] public NodePath HarpoonPath { get; set; }
	
	/// <summary>Current movement direction for external systems (fog cone, harpoon, etc).</summary>
	public Vector2 LastMoveDirection => _lastMoveDir;

	/// <summary>Current movement speed (0–1) for camera shake scaling.</summary>
	public float CurrentSpeed => Mathf.Clamp(_momentum.Length(), 0f, 1f);

	/// <summary>Current harpoon instance for render/UI systems.</summary>
	public TendrilHarpoon Harpoon => _harpoon;

	/// <summary>
	/// Collision impulse for the camera. Set when the tendril hits a wall,
	/// points in the direction of the blocked movement. Magnitude = impact force.
	/// Decays to zero each frame — the camera reads and consumes it.
	/// </summary>
	public Vector2 CollisionImpulse { get; private set; }

	// =========================================================================
	//  ROOT TIP GROWTH (same structures as before — operates on terrain tiles)
	// =========================================================================

	// Root sprout offsets — in sub-grid coordinates, relative to head.
	// These are the original terrain-tile offsets × SubGridScale (4).
	private const int _S = WorldConfig.SubGridScale; // 4
	private static readonly (int dx, int dy)[] RootSpawnOffsets = new[]
	{
		(-_S, 0),       (-_S, _S),       (-_S, 2 * _S),
		(3 * _S, 0),    (3 * _S, _S),    (3 * _S, 2 * _S),
		(0, 3 * _S),    (_S, 3 * _S),    (2 * _S, 3 * _S),
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

	// =========================================================================
	//  STATE
	// =========================================================================

	private ChunkManager _chunkManager;

	// --- Sub-Grid Position (source of truth) ---
	private int _subHeadX;
	private int _subHeadY;

	/// <summary>
	/// The sub-grid data — public so TendrilRenderer can read it.
	/// Contains all sub-cells the tendril has touched.
	/// </summary>
	public SubGridData SubGrid { get; private set; } = new();

	/// <summary>Terrain-tile X coordinate of the head (derived from sub-grid).</summary>
	public int HeadX => FloorDiv(_subHeadX, WorldConfig.SubGridScale);

	/// <summary>Terrain-tile Y coordinate of the head (derived from sub-grid).</summary>
	public int HeadY => FloorDiv(_subHeadY, WorldConfig.SubGridScale);

	/// <summary>Floor division that works correctly for negative values.</summary>
	private static int FloorDiv(int a, int b)
		=> a >= 0 ? a / b : (a - b + 1) / b;

	/// <summary>Sub-grid X coordinate of the head center.</summary>
	public int SubHeadX => _subHeadX;

	/// <summary>Sub-grid Y coordinate of the head center.</summary>
	public int SubHeadY => _subHeadY;
	
	/// <summary>
	/// Smoothly interpolated head position in world pixels.
	/// Includes fractional sub-cell offset from the move accumulator
	/// so the camera can track between discrete 4px steps.
	/// </summary>
	public Vector2 GetHeadPixelPositionSmooth()
	{
		int cellSize = WorldConfig.SubCellSize;
		return new Vector2(
			(_subHeadX + _moveAccumulator.X) * cellSize + cellSize / 2f,
			(_subHeadY + _moveAccumulator.Y) * cellSize + cellSize / 2f
		);
	}

	public float Hunger { get; private set; }
	public bool IsRetreating { get; private set; }
	public bool IsRegenerating { get; private set; }

	// Trail: sub-grid positions (most recent first) for retreat path.
	// Only records one entry per terrain tile crossing to keep retreat smooth
	// but not excessively granular.
	private readonly List<(int SubX, int SubY)> _subTrail = new();

	// Sub-grid cells currently occupied by the core blob (for transitioning to trail).
	private readonly List<(int X, int Y)> _currentCoreCells = new();

	// Terrain tiles claimed by the tendril (same as before).
	private readonly HashSet<long> _claimedTiles = new();
	private readonly HashSet<long> _treeTiles = new();

	// Movement state
	private Vector2 _moveAccumulator = Vector2.Zero;
	private Vector2 _momentum = Vector2.Zero;
	private float _retreatTimer;
	private float _regenTimer;
	private float _rootSpreadTimer;
	private int _tilesSinceLastRootSpawn;
	private int _nextRootSpawnDistance;
	private List<(int X, int Y)> _rootTips;
	private int _currentRootTipIdx;
	private Vector2 _lastMoveDir = new Vector2(0, 1); // Continuous direction (not grid-locked)
	private readonly List<RootTip> _activeRootTips = new();
	private readonly System.Random _rng = new();

	// Blob animation
	private FastNoiseLite _blobNoise;
	private float _blobAnimTime;
	private ushort _trailAgeCounter;

	// Creature auto-steering
	private CreatureManager _creatureManager;
	
	private TendrilHarpoon _harpoon;

	// Track which terrain tile we last entered (to trigger gameplay effects once per tile)
	private int _lastTerrainX;
	private int _lastTerrainY;

	// Sub-trail: record every N sub-steps for smooth retreat rendering
	private int _subStepsSinceTrailRecord;
	private const int SubStepsPerTrailRecord = 2;

	public int ClaimedTileCount => _claimedTiles.Count;
	public HashSet<long> ClaimedTiles => _claimedTiles;

	// =========================================================================
	//  SIGNALS
	// =========================================================================

	[Signal] public delegate void HungerChangedEventHandler(float current, float max);
	[Signal] public delegate void TendrilMovedEventHandler(int x, int y);
	[Signal] public delegate void RetreatStartedEventHandler();
	[Signal] public delegate void RetreatEndedEventHandler();
	[Signal] public delegate void TileConsumedEventHandler(int x, int y, float hungerGain);

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

		// Initialize blob noise for organic head shape
		_blobNoise = new FastNoiseLite();
		_blobNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
		_blobNoise.Frequency = 0.5f; // Adjusted at sample time
		_blobNoise.Seed = _rng.Next();

		// Creature auto-steering (optional — works fine without)
		if (CreatureManagerPath != null)
			_creatureManager = GetNode<CreatureManager>(CreatureManagerPath);
		
		if (HarpoonPath != null)
			_harpoon = GetNode<TendrilHarpoon>(HarpoonPath);

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
		GD.Print($"Tendril spawned at sub-grid ({_subHeadX}, {_subHeadY}), " +
				 $"terrain ({HeadX}, {HeadY}). Blob radius: {BlobBaseRadius} sub-cells.");
	}

	private void SpawnAtRootTip(int idx)
	{
		if (idx >= _rootTips.Count) idx = 0;
		var (tileX, tileY) = _rootTips[idx];

		// Convert terrain tile position to sub-grid center
		int scale = WorldConfig.SubGridScale;
		_subHeadX = tileX * scale + scale / 2;
		_subHeadY = tileY * scale + scale / 2;
		_lastTerrainX = HeadX;
		_lastTerrainY = HeadY;

		Hunger = MaxHunger;
		IsRetreating = false;
		IsRegenerating = false;
		_subTrail.Clear();
		_currentCoreCells.Clear();
		_activeRootTips.Clear();
		_rootSpreadTimer = 0f;
		_tilesSinceLastRootSpawn = 0;
		_trailAgeCounter = 0;
		_subStepsSinceTrailRecord = 0;
		SubGrid.Clear();
		ScheduleNextRootSpawnDistance();

		// Place initial blob and claim the starting terrain tile
		PlaceBlob();
		ClaimTerrainTile(HeadX, HeadY);

		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	public override void _Process(double delta)
	{
		if (_chunkManager == null) return;

		float dt = (float)delta;
		_blobAnimTime += dt * BlobAnimSpeed;

		// Decay collision impulse — camera reads this, then it fades out
		CollisionImpulse = CollisionImpulse.MoveToward(Vector2.Zero, 8f * dt);

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

		if (_harpoon != null && _harpoon.IsActive)
		{
			// Freeze: kill momentum so it doesn't resume after retract
			_momentum = Vector2.Zero;
			_moveAccumulator = Vector2.Zero;
		}
		else
		{
			ProcessMovement(dt);
		}
		
		ProcessRootSpread(dt);

		// Age fresh trail cells into settled trail
		SubGrid.AgeFreshCells(_trailAgeCounter, 30);
	}

	// =========================================================================
	//  MOVEMENT — operates on sub-grid coordinates
	// =========================================================================

	private void ProcessMovement(float dt)
	{
		Vector2 input = GetMovementInputVector();

		if (input != Vector2.Zero)
		{
			Vector2 inputDir = input.Normalized();

			if (_momentum == Vector2.Zero)
			{
				_momentum = _momentum.MoveToward(input, MomentumAcceleration * dt);
			}
			else
			{
				Vector2 momentumDir = _momentum.Normalized();
				float alignment = momentumDir.Dot(inputDir);

				if (alignment <= MomentumReverseLockThreshold)
				{
					// Heavy brake before reversing direction
					_momentum = _momentum.MoveToward(Vector2.Zero, MomentumTurnAroundBrake * dt);

					if (_momentum.Length() <= MomentumDeadZone * 1.25f)
						_momentum = _momentum.MoveToward(input, MomentumAcceleration * dt);
				}
				else
				{
					float fromAngle = Mathf.Atan2(momentumDir.Y, momentumDir.X);
					float toAngle = Mathf.Atan2(inputDir.Y, inputDir.X);
					float steerT = Mathf.Clamp(MomentumSteering * dt, 0f, 1f);
					float steeredAngle = Mathf.LerpAngle(fromAngle, toAngle, steerT);
					Vector2 steeredDir = new Vector2(Mathf.Cos(steeredAngle), Mathf.Sin(steeredAngle));

					float targetMagnitude = input.Length();
					float nextMagnitude = Mathf.MoveToward(_momentum.Length(), targetMagnitude, MomentumAcceleration * dt);
					_momentum = steeredDir * nextMagnitude;
				}
			}
		}
		else
		{
			_momentum = _momentum.MoveToward(Vector2.Zero, MomentumDrag * dt);
		}

		// --- Creature Auto-Steer (sub-grid precision) ---
		// Subtly nudge momentum toward nearby creatures (prey attraction).
		if (_creatureManager != null && _momentum.Length() > MomentumDeadZone && CreatureSteerStrength > 0f)
		{
			int steerRadiusSub = CreatureSteerRadius * WorldConfig.SubGridScale;
			var nearest = _creatureManager.GetNearestCreatureSubPosition(_subHeadX, _subHeadY, steerRadiusSub);
			if (nearest.HasValue)
			{
				// Direction from head to creature — both in sub-grid space, no conversion needed
				Vector2 creatureSubPos = new Vector2(nearest.Value.SubX, nearest.Value.SubY);
				Vector2 headPos = new Vector2(_subHeadX, _subHeadY);
				Vector2 toCreature = (creatureSubPos - headPos).Normalized();

				// Blend toward creature direction — subtle so player stays in control
				float steerAmount = CreatureSteerStrength * _momentum.Length();
				_momentum += toCreature * steerAmount * dt;

				// Don't let steering increase speed beyond normal range
				if (_momentum.Length() > 1f)
					_momentum = _momentum.Normalized() * 1f;
			}
		}

		float speed = Mathf.Clamp(_momentum.Length(), 0f, 1f);
		if (speed <= MomentumDeadZone)
		{
			if (input == Vector2.Zero)
				_moveAccumulator = Vector2.Zero;
			return;
		}

		// Update continuous direction for blob stretching
		if (_momentum.Length() > MomentumDeadZone)
			_lastMoveDir = _momentum.Normalized();

		float delayMultiplier = Mathf.Lerp(MaxMoveDelayMultiplier, MinMoveDelayMultiplier, speed);
		// Guard against misconfiguration. Floor is much lower than the terrain-grid
		// version because sub-steps are 4× smaller, so we need 4× more of them.
		float effectiveMoveDelay = Mathf.Max(0.005f, MoveDelay * delayMultiplier);
		float stepsPerSecond = 1.0f / effectiveMoveDelay;
		_moveAccumulator += _momentum * stepsPerSecond * dt;

		// Each step is one sub-cell (4px), so we move more frequently than before.
		// Allow more iterations per frame since sub-steps are smaller.
		int iterations = 0;
		while ((Mathf.Abs(_moveAccumulator.X) >= 1f || Mathf.Abs(_moveAccumulator.Y) >= 1f) && iterations < MaxSubStepsPerFrame)
		{
			iterations++;

			int stepX = Mathf.Abs(_moveAccumulator.X) >= 1f ? System.Math.Sign(_moveAccumulator.X) : 0;
			int stepY = Mathf.Abs(_moveAccumulator.Y) >= 1f ? System.Math.Sign(_moveAccumulator.Y) : 0;

			bool moved = false;

			// Try diagonal first
			if (stepX != 0 || stepY != 0)
			{
				moved = TrySubMove(_subHeadX + stepX, _subHeadY + stepY);
				if (moved)
				{
					if (stepX != 0) _moveAccumulator.X -= stepX;
					if (stepY != 0) _moveAccumulator.Y -= stepY;
					continue;
				}
			}

			// Axis-separated fallback for wall glancing
			if (stepX != 0)
			{
				moved = TrySubMove(_subHeadX + stepX, _subHeadY);
				if (moved)
				{
					_moveAccumulator.X -= stepX;
					continue;
				}
			}

			if (stepY != 0)
			{
				moved = TrySubMove(_subHeadX, _subHeadY + stepY);
				if (moved)
				{
					_moveAccumulator.Y -= stepY;
					continue;
				}
			}

			// Fully blocked — record collision for camera shake
			Vector2 blockedDir = new Vector2(stepX, stepY).Normalized();
			float impactForce = _momentum.Length();
			CollisionImpulse = blockedDir * impactForce;

			_moveAccumulator *= 0.35f;
			_momentum *= BlockedMomentumDamping;
			break;
		}
	}

	/// <summary>
	/// Attempt to move the head one sub-cell step to a new position.
	/// Checks terrain passability and triggers gameplay effects when entering new tiles.
	/// </summary>
	private bool TrySubMove(int newSubX, int newSubY)
	{
		// Determine which terrain tile this sub-cell falls in
		var (terrainX, terrainY) = SubGridData.SubToTerrain(newSubX, newSubY);

		// Check terrain passability (only when entering a new terrain tile)
		bool newTerrainTile = (terrainX != _lastTerrainX || terrainY != _lastTerrainY);

		if (newTerrainTile)
		{
			TileType centerTile = _chunkManager.GetTileAt(terrainX, terrainY);

			// Can't move through air
			if (centerTile == TileType.Air)
				return false;

			// Can't move into unbreakable solids — but the Origin Tree is home
			if (TileProperties.Is(centerTile, TileFlags.Solid) && !TileProperties.Is(centerTile, TileFlags.Breakable))
			{
				if (!_treeTiles.Contains(PackCoords(terrainX, terrainY)))
					return false;
			}

			if (TileProperties.Is(centerTile, TileFlags.Liquid))
				return false;

			if (TileProperties.Is(centerTile, TileFlags.Hazardous))
				return false;

			// Calculate hunger cost
			bool isOwnTerritory = _claimedTiles.Contains(PackCoords(terrainX, terrainY));
			bool isTreeTile = _treeTiles.Contains(PackCoords(terrainX, terrainY));
			float cost;

			if (isOwnTerritory)
				cost = HungerOnCorrupted;
			else if (isTreeTile)
				cost = HungerOnCorrupted; // Tree is home — nearly free to traverse
			else if (IsHardTile(centerTile))
				cost = HungerPerHardMove;
			else
				cost = HungerPerMove;

			if (Hunger - cost <= 0 && !isOwnTerritory && !isTreeTile)
			{
				StartRetreat();
				return false;
			}

			// Calculate hunger gain from the terrain tile we're entering
			float totalGain = 0f;
			if (!_claimedTiles.Contains(PackCoords(terrainX, terrainY)))
			{
				TileType t = _chunkManager.GetTileAt(terrainX, terrainY);
				totalGain = GetHungerGain(t);
			}

			// Apply hunger
			Hunger = System.Math.Max(0, Hunger - cost);
			Hunger = System.Math.Min(MaxHunger, Hunger + totalGain);

			if (totalGain > 0)
				EmitSignal(SignalName.TileConsumed, terrainX, terrainY, totalGain);

			// Claim the new terrain tile
			ClaimTerrainTile(terrainX, terrainY);

			// Track distance for root spawning
			TrackTravelAndSpawnRoots();

			EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);

			if (Hunger <= 0)
			{
				StartRetreat();
				return false;
			}
		}

		// --- Move the sub-grid head ---

		// Record trail position periodically for retreat
		_subStepsSinceTrailRecord++;
		if (_subStepsSinceTrailRecord >= SubStepsPerTrailRecord)
		{
			_subTrail.Insert(0, (_subHeadX, _subHeadY));
			_subStepsSinceTrailRecord = 0;
		}

		// Demote old core cells to fresh trail
		_trailAgeCounter++;
		SubGrid.DemoteCoreToFresh(_currentCoreCells, _trailAgeCounter);

		// Move head
		_subHeadX = newSubX;
		_subHeadY = newSubY;
		_lastTerrainX = terrainX;
		_lastTerrainY = terrainY;

		// Place new organic blob
		PlaceBlob();

		// Emit terrain-coordinate signal for other systems
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);

		return true;
	}

	// =========================================================================
	//  ORGANIC BLOB — the creepy, pulsing head shape
	// =========================================================================

	/// <summary>
	/// Generate an organic blob shape on the sub-grid around the current head position.
	/// Uses noise-based edge distortion and directional stretching for an organic feel.
	/// </summary>
	private void PlaceBlob()
	{
		_currentCoreCells.Clear();

		int centerX = _subHeadX;
		int centerY = _subHeadY;

		int radius = BlobBaseRadius;
		int scanRange = radius + (int)BlobNoiseAmplitude + 2;

		for (int dy = -scanRange; dy <= scanRange; dy++)
		{
			for (int dx = -scanRange; dx <= scanRange; dx++)
			{
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				if (dist > scanRange) continue;

				// Calculate distorted radius at this angle
				float angle = Mathf.Atan2(dy, dx);

				// Noise-based edge distortion — samples shift with _blobAnimTime for pulsing
				float noiseVal = _blobNoise.GetNoise2D(
					angle * BlobNoiseFrequency + _blobAnimTime * 0.4f,
					_blobAnimTime * 0.25f
				);
				float edgeRadius = radius + noiseVal * BlobNoiseAmplitude;

				// Stretch in movement direction
				float moveDot = dx * _lastMoveDir.X + dy * _lastMoveDir.Y;
				if (moveDot > 0)
					edgeRadius += moveDot * BlobStretchFactor;

				// Small inner irregularity for texture
				float innerNoise = _blobNoise.GetNoise2D(
					dx * 0.8f + _blobAnimTime * 0.6f,
					dy * 0.8f
				);
				edgeRadius += innerNoise * 0.6f;

				if (dist <= edgeRadius)
				{
					int sx = centerX + dx;
					int sy = centerY + dy;

					// Intensity falls off toward edge for visual gradient
					float edgeFade = 1f - Mathf.Clamp((dist / edgeRadius), 0f, 1f);
					byte intensity = (byte)(edgeFade * 200 + 55);

					SubGrid.SetCell(sx, sy, SubCellState.Core, 0, intensity);
					_currentCoreCells.Add((sx, sy));
				}
			}
		}
	}

	// =========================================================================
	//  TERRAIN CLAIMING — tracks territory without modifying terrain tiles
	// =========================================================================

	/// <summary>
	/// Mark a terrain tile as claimed territory for gameplay purposes.
	/// The terrain is NOT visually modified — the tendril overlay lives
	/// entirely on the sub-grid. This only updates the _claimedTiles set
	/// used by hunger regen, creature interaction, and passive corruption.
	/// </summary>
	private void ClaimTerrainTile(int terrainX, int terrainY)
	{
		long key = PackCoords(terrainX, terrainY);
		if (_treeTiles.Contains(key)) return;

		_claimedTiles.Add(key);
	}

	// =========================================================================
	//  RETREAT — walks back along the sub-grid trail
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

		if (_subTrail.Count == 0)
		{
			StartRegeneration();
			return;
		}

		// Demote current core to trail
		SubGrid.DemoteCoreToFresh(_currentCoreCells, _trailAgeCounter++);

		// Move head back along sub-trail
		var (prevSubX, prevSubY) = _subTrail[0];
		_subTrail.RemoveAt(0);
		_subHeadX = prevSubX;
		_subHeadY = prevSubY;
		_lastTerrainX = HeadX;
		_lastTerrainY = HeadY;

		// Place blob at retreated position
		PlaceBlob();

		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	// =========================================================================
	//  REGENERATION — respawn at a root tip
	// =========================================================================

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
	//  ROOT SPREAD — still operates on terrain tiles
	// =========================================================================

	private void TrackTravelAndSpawnRoots()
	{
		if (!EnableRootSpread) return;
		if (_lastMoveDir.Y <= 0) return;

		_tilesSinceLastRootSpawn++;
		if (_tilesSinceLastRootSpawn < _nextRootSpawnDistance) return;

		SpawnRootBurst();
		_tilesSinceLastRootSpawn = 0;
		ScheduleNextRootSpawnDistance();
	}

	private void ScheduleNextRootSpawnDistance()
	{
		int jitter = System.Math.Max(0, RootSpawnIntervalJitter);
		int minDistance = System.Math.Max(1, RootSpawnEveryTiles - jitter);
		int maxDistance = System.Math.Max(minDistance, RootSpawnEveryTiles + jitter);
		_nextRootSpawnDistance = _rng.Next(minDistance, maxDistance + 1);
	}

	private void SpawnRootBurst()
	{
		if (_activeRootTips.Count >= MaxActiveRoots) return;

		int burstMin = System.Math.Max(1, RootSpawnBurstMin);
		int burstMax = System.Math.Max(burstMin, RootSpawnBurstMax);
		int desiredCount = _rng.Next(burstMin, burstMax + 1);
		int available = MaxActiveRoots - _activeRootTips.Count;
		int spawnCount = System.Math.Min(desiredCount, available);
		if (spawnCount <= 0) return;

		int spawned = 0;
		int attempts = 0;
		int maxAttempts = spawnCount * 6;

		while (spawned < spawnCount && attempts < maxAttempts)
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
		int startX = _subHeadX + offset.dx;
		int startY = _subHeadY + offset.dy;

		int dirX = offset.dx < 0 ? -1 : (offset.dx > 2 * _S ? 1 : (_rng.Next(3) - 1));
		int dirY = 1;

		if (!TrySpreadRootInto(startX, startY)) return false;

		// Root lengths are in sub-grid steps (4× longer than terrain-tile lengths)
		int maxLength = RootMinLength * WorldConfig.SubGridScale;
		int range = (RootMaxLength - RootMinLength) * WorldConfig.SubGridScale;
		if (range > 0)
			maxLength += _rng.Next(range + 1);

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

		int branchMaxLength = RootMinLength * WorldConfig.SubGridScale;
		int range = (RootMaxLength - RootMinLength) * WorldConfig.SubGridScale;
		if (range > 0)
			branchMaxLength += _rng.Next(range + 1);

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

	/// <summary>
	/// Try to grow a root into a sub-grid position.
	/// Checks terrain passability, then places a Root cell on the sub-grid.
	/// Does NOT modify terrain tiles.
	/// </summary>
	private bool TrySpreadRootInto(int subX, int subY)
	{
		// Already occupied on the sub-grid
		if (SubGrid.HasCell(subX, subY)) return false;

		// Check terrain passability at this position
		var (terrainX, terrainY) = SubGridData.SubToTerrain(subX, subY);
		long terrainKey = PackCoords(terrainX, terrainY);
		if (_treeTiles.Contains(terrainKey)) return false;

		TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
		if (TileProperties.Is(tile, TileFlags.Liquid)) return false;
		if (tile == TileType.Air) return false;
		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
			return false;

		// Place root cell on sub-grid
		byte intensity = (byte)(180 + _rng.Next(76)); // Slight variation
		SubGrid.SetCell(subX, subY, SubCellState.Root, 0, intensity);

		// Track territory on terrain grid for gameplay
		_claimedTiles.Add(terrainKey);
		return true;
	}

	// =========================================================================
	//  INPUT
	// =========================================================================

	private Vector2 GetMovementInputVector()
	{
		int keyX = 0;
		int keyY = 0;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			keyY -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			keyY += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			keyX -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			keyX += 1;

		Vector2 keyboard = new Vector2(keyX, keyY);
		if (keyboard != Vector2.Zero)
			keyboard = keyboard.Normalized();

		Vector2 stick = Vector2.Zero;
		var joypads = Input.GetConnectedJoypads();
		if (joypads.Count > 0)
		{
			int joyId = joypads[0];
			stick = new Vector2(
				Input.GetJoyAxis(joyId, JoyAxis.LeftX),
				Input.GetJoyAxis(joyId, JoyAxis.LeftY)
			);

			float len = stick.Length();
			if (len < ControllerDeadZone)
			{
				stick = Vector2.Zero;
			}
			else if (len > 0f)
			{
				float normalizedLen = (len - ControllerDeadZone) / (1f - ControllerDeadZone);
				normalizedLen = Mathf.Clamp(normalizedLen, 0f, 1f);
				stick = stick.Normalized() * normalizedLen;
			}
		}

		Vector2 combined = keyboard + stick;
		if (combined.Length() > 1f)
			combined = combined.Normalized();

		return combined;
	}

	// =========================================================================
	//  HUNGER
	// =========================================================================

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

			TileType.GrassFloor or TileType.GrassCeiling
			or TileType.GrassLWall or TileType.GrassRWall => 4.0f,

			TileType.GrassInnerTL or TileType.GrassInnerTR
			or TileType.GrassInnerBL or TileType.GrassInnerBR => 4.0f,

			TileType.GrassOuterTL or TileType.GrassOuterTR
			or TileType.GrassOuterBL or TileType.GrassOuterBR => 4.0f,

			TileType.BoneMarrow => 15.0f,
			TileType.AncientSporeNode => 20.0f,
			TileType.CrystalGrotte => 12.0f,
			TileType.BioluminescentVein => 8.0f,

			TileType.Air => 0f,

			TileType.Mycelium or TileType.MyceliumDense
			or TileType.MyceliumDark or TileType.MyceliumCore
			or TileType.InfectedDirt => 0f,

			_ when TileProperties.IsInfectedGrass(tile) => 0f,

			_ => TileProperties.Is(tile, TileFlags.Organic) ? 1.0f : 0f,
		};
	}

	private static bool IsHardTile(TileType tile)
	{
		return tile == TileType.Clay || tile == TileType.Gravel
			|| tile == TileType.Roots;
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
	//  PUBLIC API — all in terrain-tile coordinates for backward compatibility
	// =========================================================================

	/// <summary>
	/// Get the head position in world pixels.
	/// The camera should follow this for fluid tracking.
	/// </summary>
	public Vector2 GetHeadPixelPosition()
	{
		int cellSize = WorldConfig.SubCellSize;
		return new Vector2(
			_subHeadX * cellSize + cellSize / 2f,
			_subHeadY * cellSize + cellSize / 2f
		);
	}

	/// <summary>
	/// Check if a world tile position overlaps the current tendril head blob.
	/// Used by CreatureManager for collision detection.
	/// </summary>
	public bool OverlapsHead(int worldX, int worldY)
	{
		// Check if any core sub-cells fall within this terrain tile
		int scale = WorldConfig.SubGridScale;
		int subMinX = worldX * scale;
		int subMinY = worldY * scale;
		int subMaxX = subMinX + scale - 1;
		int subMaxY = subMinY + scale - 1;

		foreach (var (sx, sy) in _currentCoreCells)
		{
			if (sx >= subMinX && sx <= subMaxX && sy >= subMinY && sy <= subMaxY)
				return true;
		}
		return false;
	}

	/// <summary>Check if a world tile position is on claimed mycelium territory.</summary>
	public bool IsOnTerritory(int worldX, int worldY)
	{
		return _claimedTiles.Contains(PackCoords(worldX, worldY));
	}

	/// <summary>
	/// Check if any sub-grid cells (trail/root) exist within a terrain tile.
	/// Used by Grazer creatures to navigate toward tendril territory.
	/// </summary>
	public bool HasSubGridCellsInTile(int worldX, int worldY)
	{
		return SubGrid.HasCellsInTerrainTile(worldX, worldY);
	}

	/// <summary>Pack terrain coordinates into a single long key.</summary>
	public static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);
}
