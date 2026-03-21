namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Bridges continuous-space head movement to discrete tile infection.
///
/// Uses an exposure/dwell model instead of instant infection:
///   - As the head passes through a tile, infection progress accumulates
///   - Clipping a tile's corner = partial progress
///   - Passing through the center = full credit
///   - Lingering or coiling through = fast saturation
///
/// This creates meaningful gameplay: rushing leaves sparse infection,
/// deliberate movement leaves thick, thorough territory.
///
/// SETUP:
///   - Created programmatically by TendrilController
///   - Call Update() every frame with head position
///   - Uses shared _claimedTiles set from TendrilController
/// </summary>
public partial class TendrilInfection : Node2D
{
	// =========================================================================
	//  CONFIGURATION
	// =========================================================================

	[ExportGroup("Infection")]

	/// <summary>Exposure needed to fully infect a tile.</summary>
	[Export] public float FullInfectionThreshold = 1.0f;

	/// <summary>Exposure gained per second when head is at the exact center of a tile.</summary>
	[Export] public float BaseExposureRate = 10f;

	/// <summary>Radius (pixels) around the head that contributes exposure.</summary>
	[Export] public float ExposureRadius = 14f;

	/// <summary>How many tiles around the head to check each frame.</summary>
	[Export] public int CheckRadius = 2;

	/// <summary>Instant exposure bonus when entering a new tile.</summary>
	[Export] public float EntryExposureBonus = 0.35f;

	/// <summary>Max tracked partial-infection tiles before cleanup.</summary>
	[Export] public int MaxTrackedTiles = 256;

	// =========================================================================
	//  PUBLIC STATE
	// =========================================================================

	public int TotalInfected { get; private set; }

	[Signal] public delegate void TileInfectedEventHandler(int tileX, int tileY, int originalTileType);

	// =========================================================================
	//  INTERNAL STATE
	// =========================================================================

	private ChunkManager _chunkManager;
	private readonly Dictionary<long, float> _infectionProgress = new();
	private readonly HashSet<long> _fullyInfected = new();
	private HashSet<long> _claimedTiles;
	private HashSet<long> _treeTiles;
	private Dictionary<long, TileType> _originalTiles;
	private (int X, int Y) _lastEnteredTile = (-99999, -99999);

	// =========================================================================
	//  INITIALIZATION
	// =========================================================================

	public void Initialize(
		ChunkManager chunkManager,
		HashSet<long> claimedTiles,
		HashSet<long> treeTiles,
		Dictionary<long, TileType> originalTiles)
	{
		_chunkManager = chunkManager;
		_claimedTiles = claimedTiles;
		_treeTiles = treeTiles;
		_originalTiles = originalTiles;
	}

	public void Reset()
	{
		_infectionProgress.Clear();
		_fullyInfected.Clear();
		TotalInfected = 0;
		_lastEnteredTile = (-99999, -99999);
	}

	// =========================================================================
	//  UPDATE
	// =========================================================================

	/// <summary>
	/// Process infection for the current head position.
	/// Returns total hunger gained this frame.
	/// </summary>
	public float Update(float delta, Vector2 headPosition, float speed)
	{
		if (_chunkManager == null) return 0f;

		float hungerGained = 0f;
		int ts = WorldConfig.TileSize;

		int headTileX = Mathf.FloorToInt(headPosition.X / ts);
		int headTileY = Mathf.FloorToInt(headPosition.Y / ts);

		// --- Entry bonus for new tiles ---
		if (headTileX != _lastEnteredTile.X || headTileY != _lastEnteredTile.Y)
		{
			long entryKey = PackCoords(headTileX, headTileY);
			if (!_fullyInfected.Contains(entryKey))
				hungerGained += AddExposure(entryKey, headTileX, headTileY, EntryExposureBonus);

			_lastEnteredTile = (headTileX, headTileY);
		}

		// --- Continuous exposure to nearby tiles ---
		for (int dy = -CheckRadius; dy <= CheckRadius; dy++)
		{
			for (int dx = -CheckRadius; dx <= CheckRadius; dx++)
			{
				int tx = headTileX + dx;
				int ty = headTileY + dy;
				long key = PackCoords(tx, ty);

				if (_fullyInfected.Contains(key)) continue;

				// Distance from head to tile center
				Vector2 tileCenter = new(
					(tx + 0.5f) * ts,
					(ty + 0.5f) * ts
				);

				float dist = headPosition.DistanceTo(tileCenter);
				if (dist > ExposureRadius) continue;

				// Quadratic falloff
				float proximity = 1f - (dist / ExposureRadius);
				proximity *= proximity;

				float exposure = BaseExposureRate * proximity * delta;
				hungerGained += AddExposure(key, tx, ty, exposure);
			}
		}

		if (_infectionProgress.Count > MaxTrackedTiles)
			PruneOldestPartials();

		return hungerGained;
	}

	// =========================================================================
	//  INFECTION LOGIC
	// =========================================================================

	private float AddExposure(long key, int tileX, int tileY, float amount)
	{
		if (_fullyInfected.Contains(key)) return 0f;
		if (_treeTiles != null && _treeTiles.Contains(key)) return 0f;

		TileType current = _chunkManager.GetTileAt(tileX, tileY);
		if (!CanInfect(current)) return 0f;

		_infectionProgress.TryGetValue(key, out float existing);
		float newProgress = existing + amount;
		_infectionProgress[key] = newProgress;

		if (newProgress >= FullInfectionThreshold)
		{
			return InfectTile(key, tileX, tileY, current);
		}

		// Partial infection visual feedback
		if (newProgress > FullInfectionThreshold * 0.5f && existing <= FullInfectionThreshold * 0.5f)
		{
			RecordOriginal(key, current);
			_chunkManager.SetTileAt(tileX, tileY, TileType.MyceliumDark);
		}
		else if (newProgress > FullInfectionThreshold * 0.15f && existing <= FullInfectionThreshold * 0.15f)
		{
			RecordOriginal(key, current);
			_chunkManager.SetTileAt(tileX, tileY, TileType.MyceliumDense);
		}

		return 0f;
	}

	private float InfectTile(long key, int tileX, int tileY, TileType originalTile)
	{
		// Figure out what the REAL original was (before partial infection visuals)
		TileType trueOriginal = originalTile;
		if (_originalTiles != null && _originalTiles.TryGetValue(key, out TileType recorded))
			trueOriginal = recorded;
		else if (TileProperties.IsMycelium(originalTile) && _originalTiles != null)
			trueOriginal = TileType.Dirt; // Fallback

		_infectionProgress.Remove(key);
		_fullyInfected.Add(key);
		_claimedTiles?.Add(key);

		RecordOriginal(key, trueOriginal);

		// Convert grass to infected variant, everything else to Mycelium
		if (TileProperties.IsGrass(trueOriginal))
		{
			int infectedId = (int)trueOriginal + 32;
			_chunkManager.SetTileAt(tileX, tileY, (TileType)infectedId);
		}
		else
		{
			_chunkManager.SetTileAt(tileX, tileY, TileType.Mycelium);
		}

		TotalInfected++;
		EmitSignal(SignalName.TileInfected, tileX, tileY, (int)trueOriginal);

		return GetHungerGain(trueOriginal);
	}

	private void RecordOriginal(long key, TileType tile)
	{
		if (_originalTiles == null) return;
		if (_originalTiles.ContainsKey(key)) return;
		if (TileProperties.IsMycelium(tile)) return;
		_originalTiles[key] = tile;
	}

	private static bool CanInfect(TileType tile)
	{
		if (tile == TileType.Air) return false;
		if (tile == TileType.Stone) return false;
		if (tile == TileType.Water) return false;
		if (tile == TileType.Lava) return false;
		if (tile == TileType.ToxicWater) return false;
		if (tile == TileType.AcidPool) return false;
		if (tile == TileType.Wood) return false;
		if (tile == TileType.Obsidian) return false;
		if (tile == TileType.Basalt) return false;
		if (tile == TileType.Mycelium) return false;
		if (tile == TileType.MyceliumCore) return false;

		// Infected grass is done
		if (TileProperties.IsInfectedGrass(tile)) return false;

		// Intermediate states: still being infected, allow continued exposure
		if (tile == TileType.MyceliumDense || tile == TileType.MyceliumDark) return true;

		return true;
	}

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

			TileType.MyceliumDense or TileType.MyceliumDark => 0f,

			_ => TileProperties.Is(tile, TileFlags.Organic) ? 1.0f : 0f,
		};
	}

	private void PruneOldestPartials()
	{
		var toRemove = new List<long>();
		foreach (var kvp in _infectionProgress)
		{
			if (kvp.Value < FullInfectionThreshold * 0.1f)
				toRemove.Add(kvp.Key);
		}
		foreach (long key in toRemove)
			_infectionProgress.Remove(key);
	}

	// =========================================================================
	//  UTILITY — matches TendrilController.PackCoords exactly
	// =========================================================================

	public static long PackCoords(int x, int y)
		=> ((long)(x + 65536) << 20) | (long)(y + 65536);
}
