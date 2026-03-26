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

	// --- Connection Config ---
	[Export] public int ConnectionBfsMaxNodes = 5000;

	// --- Movement / Retreat ---
	[Export] public float RetreatSpeed = 0.03f;

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

	/// <summary>Path to CreatureManager node (for auto-steering toward nearby prey).</summary>
	[Export] public NodePath CreatureManagerPath { get; set; }
	
	/// <summary>Path to TendrilHarpoon node (freezes movement while harpoon is active).</summary>
	[Export] public NodePath HarpoonPath { get; set; }
	
	/// <summary>Current movement direction for external systems (fog cone, harpoon, etc).</summary>
	public Vector2 LastMoveDirection => Movement.LastMoveDir;

	/// <summary>Current movement speed (0–1) for camera shake scaling.</summary>
	public float CurrentSpeed => Movement.CurrentSpeed;

	/// <summary>Current harpoon instance for render/UI systems.</summary>
	public TendrilHarpoon Harpoon => _harpoon;

	/// <summary>
	/// Collision impulse for the camera. Set when the tendril hits a wall,
	/// points in the direction of the blocked movement. Magnitude = impact force.
	/// Decays to zero each frame — the camera reads and consumes it.
	/// </summary>
	public Vector2 CollisionImpulse { get; set; }

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
			(_subHeadX + Movement.MoveAccumulator.X) * cellSize + cellSize / 2f,
			(_subHeadY + Movement.MoveAccumulator.Y) * cellSize + cellSize / 2f
		);
	}

	/// <summary>The vitality/vigor component. Found automatically as a child node.</summary>
	public TendrilVitality Vitals { get; private set; }

	/// <summary>The root spread component. Found automatically as a child node.</summary>
	public TendrilRoots Roots { get; private set; }

	/// <summary>The rush state machine component. Found automatically as a child node.</summary>
	public TendrilRush Rush { get; private set; }

	/// <summary>The movement/input/momentum component. Found automatically as a child node.</summary>
	public TendrilMovement Movement { get; private set; }

	// --- Delegation properties so external code can still read off the controller ---
	public float Vitality => Vitals.Vitality;
	public float MaxVitality => Vitals.MaxVitality;
	public float Vigor => Vitals.Vigor;
	public float MaxVigor => Vitals.MaxVigor;
	public new bool IsConnected => Vitals.IsConnected;
	public float VigorGainSlamKill => Vitals.VigorGainSlamKill;
	public float VigorGainHarpoonCatch => Vitals.VigorGainHarpoonCatch;
	public bool IsRetreating { get; private set; }
	public bool IsRegenerating { get; private set; }

	// Vigor multipliers — delegated to TendrilVitality
	public float SpeedMultiplier => Vitals.SpeedMultiplier;
	public float BlobSizeMultiplier => Vitals.BlobSizeMultiplier;
	public float HarpoonRangeMultiplier => Vitals.HarpoonRangeMultiplier;
	public float RootSpreadMultiplier => Vitals.RootSpreadMultiplier;
	public float CorruptionSpeedMultiplier => Vitals.CorruptionSpeedMultiplier;

	// Rush state flags — delegated to TendrilRush
	public bool IsRushFollowing => Rush.IsFollowing;
	public bool IsRushHolding => Rush.IsHolding;
	public bool IsRushDashing => Rush.IsDashing;
	public bool IsRetractFollowing => Rush.IsRetractFollowing;

	// Trail: sub-grid positions (most recent first) for retreat path.
	// Only records one entry per terrain tile crossing to keep retreat smooth
	// but not excessively granular.
	private readonly List<(int SubX, int SubY)> _subTrail = new();

	// Sub-grid cells currently occupied by the core blob (for transitioning to trail).
	private readonly List<(int X, int Y)> _currentCoreCells = new();

	/// <summary>Read-only access to current core blob cells. Used by TendrilRush.</summary>
	public IReadOnlyList<(int X, int Y)> CoreCells => _currentCoreCells;

	// Terrain tiles claimed by the tendril (same as before).
	private readonly HashSet<long> _claimedTiles = new();
	private readonly HashSet<long> _treeTiles = new();

	// Retreat / regen state
	private float _retreatTimer;
	private float _regenTimer;
	private List<(int X, int Y)> _rootTips;
	private int _currentRootTipIdx;
	private readonly System.Random _rng = new();

	// Blob animation
	private FastNoiseLite _blobNoise;
	private float _blobAnimTime;
	private ushort _trailAgeCounter;

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

	[Signal] public delegate void TendrilMovedEventHandler(int x, int y);
	[Signal] public delegate void RetreatStartedEventHandler();
	[Signal] public delegate void RetreatEndedEventHandler();
	[Signal] public delegate void TileConsumedEventHandler(int x, int y, float vigorGain);

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

		// Find TendrilVitality child component
		Vitals = GetNode<TendrilVitality>("TendrilVitality");
		if (Vitals == null)
		{
			GD.PrintErr("TendrilController: Missing TendrilVitality child node!");
			return;
		}
		Vitals.VitalityDepleted += StartRetreat;

		// Find TendrilRoots child component
		Roots = GetNode<TendrilRoots>("TendrilRoots");
		if (Roots == null)
		{
			GD.PrintErr("TendrilController: Missing TendrilRoots child node!");
			return;
		}

		// Find TendrilRush child component
		Rush = GetNode<TendrilRush>("TendrilRush");
		if (Rush == null)
		{
			GD.PrintErr("TendrilController: Missing TendrilRush child node!");
			return;
		}

		// Find TendrilMovement child component
		Movement = GetNode<TendrilMovement>("TendrilMovement");
		if (Movement == null)
		{
			GD.PrintErr("TendrilController: Missing TendrilMovement child node!");
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

		Vitals.Reset();
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
		Roots.Initialize(_chunkManager, SubGrid, _claimedTiles, _treeTiles, _rng);
		Rush.Initialize(this, _harpoon);
		Movement.Initialize(this, _creatureManager);
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

		Vitals.Reset();
		IsRetreating = false;
		IsRegenerating = false;
		_subTrail.Clear();
		_currentCoreCells.Clear();
		Roots.Reset();
		_trailAgeCounter = 0;
		_subStepsSinceTrailRecord = 0;
		SubGrid.Clear();

		// Place initial blob and claim the starting terrain tile
		PlaceBlob();
		ClaimTerrainTile(HeadX, HeadY);

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

		// --- Vitality/Vigor/Connection (delegated to TendrilVitality) ---
		bool onTree = _treeTiles.Contains(PackCoords(HeadX, HeadY));
		bool onTerritory = _claimedTiles.Contains(PackCoords(HeadX, HeadY));
		Vitals.Process(dt, onTree, onTerritory, CheckNetworkConnection);

		if (IsRushDashing)
		{
			Rush.ProcessDash(dt);
		}
		else if (IsRushFollowing)
		{
			Rush.ProcessFollow(dt);
		}
		else if (IsRushHolding)
		{
			Movement.ClearMomentum();
		}
		else if (IsRetractFollowing)
		{
			Movement.ClearMomentum();
		}
		else if (_harpoon != null && _harpoon.IsActive)
		{
			// Freeze: kill momentum so it doesn't resume after retract
			Movement.ClearMomentum();
		}
		else
		{
			Movement.Process(dt);
		}
		
		Roots.Process(dt);

		// Age fresh trail cells into settled trail
		SubGrid.AgeFreshCells(_trailAgeCounter, 30);
	}

	// =========================================================================
	//  SUB-GRID MOVEMENT — TrySubMove
	// =========================================================================

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

			// During rush dash, allow air traversal — land when hitting traversible solid
			if (IsRushDashing)
			{
				if (centerTile == TileType.Air)
				{
					// Pass through air freely
				}
				else if (TileProperties.Is(centerTile, TileFlags.Liquid)
					|| TileProperties.Is(centerTile, TileFlags.Hazardous))
				{
					return false;
				}
				else if (TileProperties.Is(centerTile, TileFlags.Solid)
					&& !TileProperties.Is(centerTile, TileFlags.Breakable)
					&& !_treeTiles.Contains(PackCoords(terrainX, terrainY)))
				{
					return false;
				}
				else
				{
					// Landed on traversible solid ground — claim tile and end dash
					ClaimTerrainTile(terrainX, terrainY);
					Rush.NotifyDashLanded();
				}
			}
			else
			{
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


			// Track distance for root spawning
			Roots.OnTerrainTileEntered(_subHeadX, _subHeadY, Movement.LastMoveDir);

			} // end normal (non-dash) branch
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

		int radius = (int)(BlobBaseRadius * BlobSizeMultiplier);
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
				float moveDot = dx * Movement.LastMoveDir.X + dy * Movement.LastMoveDir.Y;
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
		Rush.CancelFollow();
		Rush.ForceEndRetract();
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
	//  NETWORK CONNECTION CHECK
	// =========================================================================

	/// <summary>
	/// Check if the head can trace a path through sub-grid cells to the tree.
	/// Uses BFS with a node cap to prevent frame spikes.
	/// </summary>
	private bool CheckNetworkConnection()
	{
		var queue = new Queue<long>();
		var visited = new HashSet<long>();

		// Start from cells adjacent to the head
		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				int sx = _subHeadX + dx;
				int sy = _subHeadY + dy;
				if (SubGrid.HasCell(sx, sy))
				{
					long key = SubGridData.PackCoords(sx, sy);
					queue.Enqueue(key);
					visited.Add(key);
				}
			}
		}

		while (queue.Count > 0 && visited.Count < ConnectionBfsMaxNodes)
		{
			long key = queue.Dequeue();
			var (x, y) = SubGridData.UnpackCoords(key);

			// Check if this cell is on a tree tile
			var (tileX, tileY) = SubGridData.SubToTerrain(x, y);
			if (_treeTiles.Contains(PackCoords(tileX, tileY)))
				return true;

			// Expand to neighbors
			for (int dy = -1; dy <= 1; dy++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0) continue;
					int nx = x + dx;
					int ny = y + dy;
					long nkey = SubGridData.PackCoords(nx, ny);
					if (visited.Contains(nkey)) continue;
					if (!SubGrid.HasCell(nx, ny)) continue;

					var cell = SubGrid.GetCell(nx, ny);
					if (cell.State == SubCellState.Empty) continue;

					visited.Add(nkey);
					queue.Enqueue(nkey);
				}
			}
		}

		return false;
	}

	/// <summary>Add vigor from any source. Delegates to TendrilVitality.</summary>
	public void AddVigor(float amount) => Vitals.AddVigor(amount);

	/// <summary>Damage vitality from external source. Delegates to TendrilVitality.</summary>
	public void DamageVitality(float amount) => Vitals.DamageVitality(amount);

	/// <summary>Returns the name of the current vigor tier.</summary>
	public string GetVigorTierName() => Vitals.GetVigorTierName();

	private static bool IsHardTile(TileType tile)
	{
		return tile == TileType.Clay || tile == TileType.Gravel
			|| tile == TileType.Roots;
	}

	/// <summary>
	/// Force-move the tendril head to a sub-cell during special attacks.
	/// Ignores normal passability and hunger costs, but still updates trail/blob state.
	/// </summary>
	public void RushMoveToSub(int targetSubX, int targetSubY)
	{
		if (IsRetreating || IsRegenerating) return;
		if (_subHeadX == targetSubX && _subHeadY == targetSubY) return;

		_subTrail.Insert(0, (_subHeadX, _subHeadY));
		_subStepsSinceTrailRecord = 0;
		Movement.ClearMomentum();

		_trailAgeCounter++;
		SubGrid.DemoteCoreToFresh(_currentCoreCells, _trailAgeCounter);

		_subHeadX = targetSubX;
		_subHeadY = targetSubY;

		var (terrainX, terrainY) = SubGridData.SubToTerrain(targetSubX, targetSubY);
		_lastTerrainX = terrainX;
		_lastTerrainY = terrainY;

		TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
		if (tile != TileType.Air && !TileProperties.Is(tile, TileFlags.Liquid) && !TileProperties.Is(tile, TileFlags.Hazardous))
			ClaimTerrainTile(terrainX, terrainY);

		PlaceBlob();
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	// ---- Rush primitive helpers (called by TendrilRush) ---------------------

	public void ClearMomentum()
	{
		Movement.ClearMomentum();
	}

	public void SetSubHeadDirect(int subX, int subY)
	{
		_subHeadX = subX;
		_subHeadY = subY;
		var (tx, ty) = SubGridData.SubToTerrain(subX, subY);
		_lastTerrainX = tx;
		_lastTerrainY = ty;
	}

	public void PlaceBlobAndEmitMoved()
	{
		PlaceBlob();
		EmitSignal(SignalName.TendrilMoved, HeadX, HeadY);
	}

	public bool TrySubMoveStep(int newSubX, int newSubY)
		=> TrySubMove(newSubX, newSubY);

	public void SetLastMoveDir(Vector2 dir)
	{
		Movement.LastMoveDir = dir;
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
	/// Position the camera should follow. Returns harpoon tip during extension,
	/// otherwise returns the normal smooth head position.
	/// </summary>
	public Vector2 GetFocusPosition()
	{
		if (_harpoon != null && _harpoon.IsCameraTracking)
			return _harpoon.GetTipPixelPosition();
		return GetHeadPixelPositionSmooth();
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
