namespace Mycorrhiza.World;

using System.Collections.Generic;

/// <summary>
/// Visual state of a sub-grid cell. The sub-grid is the fine-grained overlay
/// where the tendril lives — 4× resolution of the terrain grid.
/// </summary>
public enum SubCellState : byte
{
	Empty = 0,
	/// <summary>Active tendril head blob — pulsing, bright.</summary>
	Core = 1,
	/// <summary>Fresh trail immediately behind the head.</summary>
	Fresh = 2,
	/// <summary>Settled trail — permanent territory marker.</summary>
	Trail = 3,
	/// <summary>Root tendrils that spread from the main body.</summary>
	Root = 4,
}

/// <summary>
/// A single cell in the sub-grid. Packed small for memory efficiency.
/// </summary>
public struct SubCell
{
	/// <summary>Visual state of this cell.</summary>
	public SubCellState State;

	/// <summary>
	/// Age counter. Increments each time the head moves away.
	/// Used by the renderer to fade trail cells from fresh to old.
	/// </summary>
	public ushort Age;

	/// <summary>
	/// Intensity value 0–255 for visual variation.
	/// Core cells use this for pulse animation. Trail cells for thickness variation.
	/// </summary>
	public byte Intensity;
}

/// <summary>
/// Sparse storage for the tendril sub-grid layer.
///
/// The sub-grid overlays the terrain at 4× resolution (configurable via WorldConfig.SubGridScale).
/// Each terrain tile maps to SubGridScale² sub-cells. Only cells the tendril has
/// touched are stored, keeping memory usage proportional to tendril length, not world size.
///
/// Coordinate system: sub-grid coordinates = terrain tile coords × SubGridScale.
/// A terrain tile at (10, 20) maps to sub-cells (40, 80) through (43, 83) with scale=4.
/// </summary>
public class SubGridData
{
	// Sparse storage — only occupied cells exist in memory.
	private readonly Dictionary<long, SubCell> _cells = new();

	// Spatial buckets for fast rendering: maps terrain-tile key → count of sub-cells in that tile.
	// Used by the renderer to quickly skip empty terrain tiles.
	private readonly Dictionary<long, int> _tileBuckets = new();

	/// <summary>Total number of occupied sub-cells.</summary>
	public int CellCount => _cells.Count;

	/// <summary>Direct access to the cell dictionary for iteration during rendering.</summary>
	public Dictionary<long, SubCell> Cells => _cells;

	// =========================================================================
	//  CELL ACCESS
	// =========================================================================

	/// <summary>Get the cell at sub-grid coordinates. Returns empty cell if not present.</summary>
	public SubCell GetCell(int subX, int subY)
	{
		long key = PackCoords(subX, subY);
		return _cells.TryGetValue(key, out SubCell cell) ? cell : default;
	}

	/// <summary>Set a cell at sub-grid coordinates.</summary>
	public void SetCell(int subX, int subY, SubCellState state, ushort age = 0, byte intensity = 255)
	{
		long key = PackCoords(subX, subY);
		bool existed = _cells.ContainsKey(key);

		_cells[key] = new SubCell { State = state, Age = age, Intensity = intensity };

		// Update spatial bucket
		if (!existed)
		{
			long tileKey = TerrainKeyFromSub(subX, subY);
			_tileBuckets.TryGetValue(tileKey, out int count);
			_tileBuckets[tileKey] = count + 1;
		}
	}

	/// <summary>Remove a cell at sub-grid coordinates.</summary>
	public void ClearCell(int subX, int subY)
	{
		long key = PackCoords(subX, subY);
		if (_cells.Remove(key))
		{
			long tileKey = TerrainKeyFromSub(subX, subY);
			if (_tileBuckets.TryGetValue(tileKey, out int count))
			{
				if (count <= 1)
					_tileBuckets.Remove(tileKey);
				else
					_tileBuckets[tileKey] = count - 1;
			}
		}
	}

	/// <summary>Check if a cell exists at sub-grid coordinates.</summary>
	public bool HasCell(int subX, int subY)
	{
		return _cells.ContainsKey(PackCoords(subX, subY));
	}

	/// <summary>Check if any sub-cells exist within a terrain tile.</summary>
	public bool HasCellsInTerrainTile(int terrainX, int terrainY)
	{
		long tileKey = PackTerrainCoords(terrainX, terrainY);
		return _tileBuckets.ContainsKey(tileKey);
	}

	/// <summary>Clear all cells.</summary>
	public void Clear()
	{
		_cells.Clear();
		_tileBuckets.Clear();
	}

	/// <summary>
	/// Transition all Core cells to Fresh. Called when the head moves
	/// so the old head position becomes trail.
	/// </summary>
	public void DemoteCoreToFresh(List<(int X, int Y)> coreCells, ushort age)
	{
		foreach (var (x, y) in coreCells)
		{
			long key = PackCoords(x, y);
			if (_cells.TryGetValue(key, out SubCell cell) && cell.State == SubCellState.Core)
			{
				cell.State = SubCellState.Fresh;
				cell.Age = age;
				_cells[key] = cell;
			}
		}
	}

	/// <summary>
	/// Age all Fresh cells past a threshold into Trail state.
	/// </summary>
	public void AgeFreshCells(ushort currentAge, ushort freshDuration)
	{
		// We collect keys to update to avoid modifying during iteration.
		// For large trails this could be optimized with a frontier list,
		// but the tendril trail is typically only a few thousand cells.
		var toAge = new List<long>();

		foreach (var (key, cell) in _cells)
		{
			if (cell.State == SubCellState.Fresh && (currentAge - cell.Age) > freshDuration)
				toAge.Add(key);
		}

		foreach (long key in toAge)
		{
			var cell = _cells[key];
			cell.State = SubCellState.Trail;
			_cells[key] = cell;
		}
	}

	// =========================================================================
	//  COORDINATE HELPERS
	// =========================================================================

	/// <summary>Pack sub-grid coordinates into a single long key.</summary>
	public static long PackCoords(int subX, int subY)
		=> ((long)(subX + 262144) << 20) | (long)(subY + 262144);

	/// <summary>Unpack sub-grid coordinates from a key.</summary>
	public static (int X, int Y) UnpackCoords(long key)
	{
		int y = (int)(key & 0xFFFFF) - 262144;
		int x = (int)(key >> 20) - 262144;
		return (x, y);
	}

	/// <summary>Convert sub-grid coordinates to terrain tile coordinates.</summary>
	public static (int TerrainX, int TerrainY) SubToTerrain(int subX, int subY)
	{
		int scale = WorldConfig.SubGridScale;
		// Floor division for negative coordinates
		int tx = subX >= 0 ? subX / scale : (subX - scale + 1) / scale;
		int ty = subY >= 0 ? subY / scale : (subY - scale + 1) / scale;
		return (tx, ty);
	}

	/// <summary>Convert terrain tile coordinates to the top-left sub-grid coordinate.</summary>
	public static (int SubX, int SubY) TerrainToSub(int terrainX, int terrainY)
	{
		int scale = WorldConfig.SubGridScale;
		return (terrainX * scale, terrainY * scale);
	}

	/// <summary>Get the terrain tile key for a sub-grid position (for bucket tracking).</summary>
	private static long TerrainKeyFromSub(int subX, int subY)
	{
		var (tx, ty) = SubToTerrain(subX, subY);
		return PackTerrainCoords(tx, ty);
	}

	/// <summary>Pack terrain coordinates the same way TendrilController does.</summary>
	public static long PackTerrainCoords(int terrainX, int terrainY)
		=> ((long)(terrainX + 65536) << 20) | (long)(terrainY + 65536);
}
