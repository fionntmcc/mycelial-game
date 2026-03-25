namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Root spread component for the tendril.
///
/// Owns all root growth configuration, active root tips, and the algorithms
/// that grow roots downward from the tendril's path. Roots are placed on the
/// sub-grid as visual overlay and claim terrain tiles for gameplay.
///
/// TendrilController calls <see cref="OnTerrainTileEntered"/> when the head
/// crosses into a new tile and <see cref="Process"/> each frame.
/// No back-reference to the controller.
/// </summary>
public partial class TendrilRoots : Node
{
	// =========================================================================
	//  CONFIG
	// =========================================================================

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

	// =========================================================================
	//  INTERNALS
	// =========================================================================

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

	// References — set once in Initialize, never changed.
	private ChunkManager _chunkManager;
	private SubGridData _subGrid;
	private HashSet<long> _claimedTiles;
	private HashSet<long> _treeTiles;
	private System.Random _rng;

	// Head snapshot — updated each time the tendril enters a new terrain tile.
	private int _headSubX;
	private int _headSubY;

	// Root spread state
	private float _rootSpreadTimer;
	private int _tilesSinceLastRootSpawn;
	private int _nextRootSpawnDistance;
	private readonly List<RootTip> _activeRootTips = new();

	// =========================================================================
	//  PUBLIC API
	// =========================================================================

	/// <summary>
	/// Wire up shared references. Called once from TendrilController.Initialize.
	/// </summary>
	public void Initialize(ChunkManager chunkManager, SubGridData subGrid,
						   HashSet<long> claimedTiles, HashSet<long> treeTiles,
						   System.Random rng)
	{
		_chunkManager = chunkManager;
		_subGrid = subGrid;
		_claimedTiles = claimedTiles;
		_treeTiles = treeTiles;
		_rng = rng;
	}

	/// <summary>
	/// Grow active root tips each frame.
	/// </summary>
	public void Process(float dt)
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

	/// <summary>
	/// Called by TendrilController when the head enters a new terrain tile
	/// during forward movement. Tracks distance and spawns root bursts.
	/// </summary>
	public void OnTerrainTileEntered(int subHeadX, int subHeadY, Vector2 lastMoveDir)
	{
		_headSubX = subHeadX;
		_headSubY = subHeadY;

		if (!EnableRootSpread) return;
		if (lastMoveDir.Y <= 0) return;

		_tilesSinceLastRootSpawn++;
		if (_tilesSinceLastRootSpawn < _nextRootSpawnDistance) return;

		SpawnRootBurst();
		_tilesSinceLastRootSpawn = 0;
		ScheduleNextRootSpawnDistance();
	}

	/// <summary>
	/// Clear all active roots and reset timers. Called on spawn/respawn.
	/// </summary>
	public void Reset()
	{
		_activeRootTips.Clear();
		_rootSpreadTimer = 0f;
		_tilesSinceLastRootSpawn = 0;
		ScheduleNextRootSpawnDistance();
	}

	// =========================================================================
	//  SPAWN LOGIC
	// =========================================================================

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
		int startX = _headSubX + offset.dx;
		int startY = _headSubY + offset.dy;

		int dirX = offset.dx < 0 ? -1 : (offset.dx > 2 * _S ? 1 : (_rng.Next(3) - 1));
		int dirY = 1;

		if (!TrySpreadRootInto(startX, startY)) return false;

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

	// =========================================================================
	//  GROWTH LOGIC
	// =========================================================================

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

	// =========================================================================
	//  TERRAIN INTERACTION
	// =========================================================================

	private bool TrySpreadRootInto(int subX, int subY)
	{
		if (_subGrid.HasCell(subX, subY)) return false;

		var (terrainX, terrainY) = SubGridData.SubToTerrain(subX, subY);
		long terrainKey = TendrilController.PackCoords(terrainX, terrainY);
		if (_treeTiles.Contains(terrainKey)) return false;

		TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
		if (TileProperties.Is(tile, TileFlags.Liquid)) return false;
		if (tile == TileType.Air) return false;
		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
			return false;

		byte intensity = (byte)(180 + _rng.Next(76));
		_subGrid.SetCell(subX, subY, SubCellState.Root, 0, intensity);

		_claimedTiles.Add(terrainKey);
		return true;
	}
}
