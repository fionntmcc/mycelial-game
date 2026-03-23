namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Handles slow passive spreading of tendril sub-grid cells from territory
/// the player has already claimed.
///
/// Now operates entirely on the sub-grid — terrain tiles are never modified.
/// Spreads Trail cells outward from existing tendril cells, creating a creeping
/// fungal mat that visually expands on its own.
///
/// Algorithm:
///   - Maintains a frontier of sub-grid positions adjacent to existing cells
///   - Every tick, places a small number of Trail cells at frontier positions
///   - Only spreads into passable terrain (not stone, water, air)
///   - Spread rate is configurable and deliberately slow
///
/// SETUP:
///   - Add as child of World node
///   - Assign ChunkManagerPath and TendrilControllerPath
/// </summary>
public partial class PassiveCorruption : Node2D
{
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public bool EnablePassiveSpread = true;

	/// <summary>Seconds between passive spread ticks.</summary>
	[Export] public float SpreadInterval = 0.2f;

	/// <summary>Max sub-cells to spread per tick.</summary>
	[Export] public int SpreadsPerTick = 8;

	private ChunkManager _chunkManager;
	private TendrilController _tendril;
	private float _timer;
	private readonly System.Random _rng = new();

	// Frontier for passive spread — sub-grid coordinates
	private readonly List<(int X, int Y)> _frontier = new();
	private readonly HashSet<long> _frontierSet = new();

	// Track what we've already processed
	private int _lastKnownSubGridCount;

	public override void _Ready()
	{
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		if (_chunkManager == null || _tendril == null)
		{
			GD.PrintErr("PassiveCorruption: Missing ChunkManager or TendrilController!");
		}

		if (!EnablePassiveSpread)
		{
			GD.Print("PassiveCorruption: Passive spread disabled.");
		}
	}

	public override void _Process(double delta)
	{
		if (!EnablePassiveSpread) return;
		if (_chunkManager == null || _tendril == null) return;

		// Refresh frontier when the sub-grid grows
		int currentCount = _tendril.SubGrid.CellCount;
		if (currentCount != _lastKnownSubGridCount)
		{
			RefreshFrontier();
			_lastKnownSubGridCount = currentCount;
		}

		_timer += (float)delta;
		if (_timer < SpreadInterval) return;
		_timer -= SpreadInterval;

		SpreadTick();
	}

	/// <summary>
	/// Rebuild the frontier from all existing sub-grid cells.
	/// Finds empty sub-grid neighbors that sit in passable terrain.
	/// </summary>
	private void RefreshFrontier()
	{
		_frontier.Clear();
		_frontierSet.Clear();

		SubGridData subGrid = _tendril.SubGrid;

		foreach (var (key, cell) in subGrid.Cells)
		{
			if (cell.State == SubCellState.Empty) continue;

			// Only expand from settled trail and root cells (not active core/fresh)
			if (cell.State != SubCellState.Trail && cell.State != SubCellState.Root)
				continue;

			var (x, y) = SubGridData.UnpackCoords(key);
			AddAdjacentToFrontier(subGrid, x, y);
		}
	}

	private void AddAdjacentToFrontier(SubGridData subGrid, int x, int y)
	{
		// Cardinals
		TryAddToFrontier(subGrid, x - 1, y);
		TryAddToFrontier(subGrid, x + 1, y);
		TryAddToFrontier(subGrid, x, y - 1);
		TryAddToFrontier(subGrid, x, y + 1);

		// Diagonals (less often, for organic shape)
		if (_rng.Next(3) == 0)
		{
			TryAddToFrontier(subGrid, x - 1, y - 1);
			TryAddToFrontier(subGrid, x + 1, y - 1);
			TryAddToFrontier(subGrid, x - 1, y + 1);
			TryAddToFrontier(subGrid, x + 1, y + 1);
		}
	}

	private void TryAddToFrontier(SubGridData subGrid, int subX, int subY)
	{
		long key = SubGridData.PackCoords(subX, subY);

		// Skip if already in frontier or already has a cell
		if (_frontierSet.Contains(key)) return;
		if (subGrid.HasCell(subX, subY)) return;

		// Check terrain passability
		var (terrainX, terrainY) = SubGridData.SubToTerrain(subX, subY);
		TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);

		// Can only spread into organic/breakable solid terrain
		if (tile == TileType.Air) return;
		if (TileProperties.Is(tile, TileFlags.Liquid)) return;
		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
			return;

		_frontierSet.Add(key);
		_frontier.Add((subX, subY));
	}

	private void SpreadTick()
	{
		if (_frontier.Count == 0) return;

		SubGridData subGrid = _tendril.SubGrid;
		int count = System.Math.Min(SpreadsPerTick, _frontier.Count);

		for (int i = 0; i < count; i++)
		{
			if (_frontier.Count == 0) break;

			// Pick a random frontier cell
			int idx = _rng.Next(_frontier.Count);
			var (subX, subY) = _frontier[idx];

			// Swap-remove
			int lastIdx = _frontier.Count - 1;
			_frontier[idx] = _frontier[lastIdx];
			_frontier.RemoveAt(lastIdx);
			_frontierSet.Remove(SubGridData.PackCoords(subX, subY));

			// Verify still valid
			if (subGrid.HasCell(subX, subY)) continue;

			var (terrainX, terrainY) = SubGridData.SubToTerrain(subX, subY);
			TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
			if (tile == TileType.Air) continue;
			if (TileProperties.Is(tile, TileFlags.Liquid)) continue;
			if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
				continue;

			// Place a trail cell on the sub-grid
			byte intensity = (byte)(160 + _rng.Next(96));
			subGrid.SetCell(subX, subY, SubCellState.Trail, 0, intensity);

			// Mark terrain tile as claimed for gameplay
			_tendril.ClaimedTiles.Add(
				TendrilController.PackCoords(terrainX, terrainY));

			// Add neighbors to frontier
			AddAdjacentToFrontier(subGrid, subX, subY);
		}
	}
}
