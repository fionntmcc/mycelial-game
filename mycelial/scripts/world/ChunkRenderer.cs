namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Godot node that renders a single chunk of tile data.
/// Created and managed by ChunkManager. One of these exists per loaded chunk.
///
/// Uses a TileMapLayer for rendering. When the chunk data changes (IsDirty),
/// the renderer re-applies all tiles from the ChunkData to the TileMapLayer.
///
/// IMPORTANT: This node must only be created/modified on the main thread.
/// </summary>
public partial class ChunkRenderer : Node2D
{
	private TileMapLayer _tileMapLayer;
	private ChunkData _data;

	/// <summary>Chunk coordinates (not tile, not pixel).</summary>
	public int ChunkX => _data?.ChunkX ?? 0;
	public int ChunkY => _data?.ChunkY ?? 0;

	/// <summary>
	/// Initialize the renderer with chunk data and the shared TileSet.
	/// Call this immediately after creating the node.
	/// </summary>
	public void Initialize(ChunkData data, TileSet tileSet)
	{
		_data = data;

		// Position node at the chunk's world pixel coordinates
        Position = new Vector2(_data.WorldPixelX, _data.WorldPixelY);

        // Create the TileMapLayer for rendering
        _tileMapLayer = new TileMapLayer();
        _tileMapLayer.TileSet = tileSet;
        _tileMapLayer.Name = $"ChunkTiles_{_data.ChunkX}_{_data.ChunkY}";
        AddChild(_tileMapLayer);

        // Initial render
        ApplyAllTiles();
    }

    /// <summary>
    /// Apply all tile data from ChunkData to the TileMapLayer.
    /// Called on initial load and whenever the chunk is marked dirty.
    /// </summary>
    public void ApplyAllTiles()
    {
        if (_data == null || _tileMapLayer == null) return;

        for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
        {
            for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
            {
                TileType tile = _data.GetTile(lx, ly);
                ApplyTile(lx, ly, tile);
            }
        }

        _data.IsDirty = false;
    }

    /// <summary>
    /// Update only tiles that have changed since last render.
    /// More efficient than ApplyAllTiles for runtime modifications
    /// (e.g., mycelium spreading, tiles being mined).
    /// 
    /// For now, falls back to ApplyAllTiles. A proper implementation
    /// would track a dirty list of individual tile positions.
    /// TODO: Implement per-tile dirty tracking for runtime updates.
    /// </summary>
    public void ApplyDirtyTiles()
    {
        if (_data == null || !_data.IsDirty) return;
        ApplyAllTiles(); // Fallback — replace with granular update later
    }

    /// <summary>
    /// Set a single tile in the TileMapLayer.
    /// Maps TileType to the appropriate atlas coordinates in the TileSet.
    /// </summary>
    private void ApplyTile(int lx, int ly, TileType tile)
    {
        if (tile == TileType.Air)
        {
            // Clear the cell — Air = no tile rendered
            _tileMapLayer.EraseCell(new Vector2I(lx, ly));
            return;
        }

        // Map TileType to TileSet atlas coordinates.
        // The TileSet should be set up as an atlas where each tile type
        // occupies a specific position. For a simple prototype:
        //   - Source ID 0 = main atlas
        //   - Atlas coords = (tileTypeValue % atlasColumns, tileTypeValue / atlasColumns)
        //
        // For the prototype, we use a simple mapping where the TileType
        // integer value directly maps to a position in the atlas.
		// You'll customize this when you create your actual tileset art.

		int atlasColumns = 16; // Tiles per row in your atlas texture
		int tileId = (ushort)tile;
		var atlasCoords = new Vector2I(tileId % atlasColumns, tileId / atlasColumns);

		_tileMapLayer.SetCell(
			new Vector2I(lx, ly),
			sourceId: 0,          // Atlas source index in TileSet
			atlasCoords: atlasCoords
		);
	}

	/// <summary>
	/// Update a single tile at runtime (e.g., mycelium spread, mining).
	/// Updates both the data and the visual.
	/// </summary>
	public void SetTileAt(int localX, int localY, TileType newType)
	{
		if (_data == null) return;
		_data.SetTile(localX, localY, newType);
		ApplyTile(localX, localY, newType);
	}
}
