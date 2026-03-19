namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Player-controlled mycelium tendril with a directional cone-shaped head,
/// momentum-based movement, and hunger system.
///
/// The tendril head uses a directional cone footprint.
/// Tiles the tendril travels over settle to Mycelium (lightest)
/// while the active head remains MyceliumCore.
///
/// Movement: keyboard + controller left-stick.
/// Movement has inertia: it accelerates with input and coasts briefly.
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

	// --- Tip Shape ---
	// Directional cone-shaped footprints.
	private static readonly (int dx, int dy)[] TipShapeRight = new[]
	{
		(2, 1),
		(1, 0), (1, 1), (1, 2),
	};

	private static readonly (int dx, int dy)[] TipShapeLeft = new[]
	{
		(0, 1),
		(1, 0), (1, 1), (1, 2),
	};

	private static readonly (int dx, int dy)[] TipShapeDown = new[]
	{
		(1, 2),
		(0, 1), (1, 1), (2, 1),
	};

	private static readonly (int dx, int dy)[] TipShapeUp = new[]
	{
		(1, 0),
		(0, 1), (1, 1), (2, 1),
	};

	private static readonly (int dx, int dy)[] TrailStampShape = new[]
	{
		(1, 0),
		(0, 1), (1, 1), (2, 1),
		(1, 2),
	};

	// Root sprouts can emerge from edges and underside of the head footprint.
	private static readonly (int dx, int dy)[] RootSpawnOffsets = new[]
	{
		(-1, 0), (-1, 1), (-1, 2),
		(3, 0), (3, 1), (3, 2),
		(0, 3), (1, 3), (2, 3),
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

	private Vector2 _moveAccumulator = Vector2.Zero;
	private Vector2 _momentum = Vector2.Zero;
	private float _retreatTimer;
	private float _regenTimer;
	private float _rootSpreadTimer;
	private int _tilesSinceLastRootSpawn;
	private int _nextRootSpawnDistance;
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
		GD.Print($"Tendril spawned at ({HeadX}, {HeadY}). Tip size: {TipShapeDown.Length} tiles.");
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
		_tilesSinceLastRootSpawn = 0;
		ScheduleNextRootSpawnDistance();

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
					// Turning directly around should feel heavy: brake first, then reverse.
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

		float speed = Mathf.Clamp(_momentum.Length(), 0f, 1f);
		if (speed <= MomentumDeadZone)
		{
			if (input == Vector2.Zero)
				_moveAccumulator = Vector2.Zero;
			return;
		}

		float delayMultiplier = Mathf.Lerp(MaxMoveDelayMultiplier, MinMoveDelayMultiplier, speed);
		// Guard against inspector misconfiguration (e.g., MoveDelay = 0) causing extreme movement speed.
		float effectiveMoveDelay = Mathf.Max(0.06f, MoveDelay * delayMultiplier);
		float stepsPerSecond = 1.0f / effectiveMoveDelay;
		_moveAccumulator += _momentum * stepsPerSecond * dt;

		int iterations = 0;
		while ((Mathf.Abs(_moveAccumulator.X) >= 1f || Mathf.Abs(_moveAccumulator.Y) >= 1f) && iterations < 4)
		{
			iterations++;

			int stepX = Mathf.Abs(_moveAccumulator.X) >= 1f ? System.Math.Sign(_moveAccumulator.X) : 0;
			int stepY = Mathf.Abs(_moveAccumulator.Y) >= 1f ? System.Math.Sign(_moveAccumulator.Y) : 0;

			bool moved = false;

			if (stepX != 0 || stepY != 0)
			{
				_lastDirection = (stepX, stepY);
				moved = TryMove(HeadX + stepX, HeadY + stepY);

				if (moved)
				{
					_moveAccumulator.X -= stepX;
					_moveAccumulator.Y -= stepY;
					continue;
				}
			}

			// If diagonal is blocked, try axis-separated movement for smoother glancing.
			if (stepX != 0)
			{
				_lastDirection = (stepX, 0);
				moved = TryMove(HeadX + stepX, HeadY);
				if (moved)
				{
					_moveAccumulator.X -= stepX;
					continue;
				}
			}

			if (stepY != 0)
			{
				_lastDirection = (0, stepY);
				moved = TryMove(HeadX, HeadY + stepY);
				if (moved)
				{
					_moveAccumulator.Y -= stepY;
					continue;
				}
			}

			// Fully blocked: dampen accumulation and momentum to prevent sticky wall pushing.
			_moveAccumulator *= 0.35f;
			_momentum *= BlockedMomentumDamping;
			break;
		}
	}

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

	private bool TryMove(int newX, int newY)
	{
		// Check if the tip can fit at the new position
		// Only need the center and immediate core to be passable
		TileType centerTile = _chunkManager.GetTileAt(newX, newY);

		// Tendrils cannot move through open air.
		if (centerTile == TileType.Air)
			return false;

		// Can't move into unbreakable solids
		if (TileProperties.Is(centerTile, TileFlags.Solid) && !TileProperties.Is(centerTile, TileFlags.Breakable))
			return false;

		if (TileProperties.Is(centerTile, TileFlags.Liquid))
			return false;

		if (TileProperties.Is(centerTile, TileFlags.Hazardous))
			return false;

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
			return false;
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
		TrackTravelAndSpawnRoots();

		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
		EmitSignal(SignalName.HungerChanged, Hunger, MaxHunger);

		if (Hunger <= 0)
			StartRetreat();

		return true;
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
		dir = NormalizeDirection(dir);

		if (dir.dx > 0 && dir.dy == 0) return TipShapeRight;
		if (dir.dx < 0 && dir.dy == 0) return TipShapeLeft;
		if (dir.dy > 0 && dir.dx == 0) return TipShapeDown;
		if (dir.dy < 0 && dir.dx == 0) return TipShapeUp;
		return TipShapeDown;
	}

	private static (int dx, int dy) NormalizeDirection((int dx, int dy) dir)
	{
		if (dir.dx == 0 && dir.dy == 0)
			return (0, 1);

		if (System.Math.Abs(dir.dx) >= System.Math.Abs(dir.dy))
			return (System.Math.Sign(dir.dx), 0);

		return (0, System.Math.Sign(dir.dy));
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
			foreach (var (dx, dy) in TrailStampShape)
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

	private void TrackTravelAndSpawnRoots()
	{
		if (!EnableRootSpread) return;
		if (_lastDirection.dy <= 0) return;

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
			// _claimedTiles.Remove(key);
			 _chunkManager.SetTileAt(tx, ty, TileType.Mycelium);
		}

		// Move head back along trail
		var (prevX, prevY, _) = _trail[0];
		_trail.RemoveAt(0);
		HeadX = prevX;
		HeadY = prevY;

		// Place tip at new position (so you can see the retreat)
		_currentTipTiles.Clear();
		foreach (var (dx, dy) in GetTipShapeForDirection(_lastDirection))
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
