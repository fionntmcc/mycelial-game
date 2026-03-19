namespace Mycorrhiza.Data;

using Mycorrhiza.World;

/// <summary>
/// Pure data container for a single chunk's tile data.
/// Has NO Godot dependencies — safe to create and populate on background threads.
/// 
/// Tile data is stored as a flat array indexed [y * ChunkSize + x].
/// This is cache-friendly for row-by-row iteration (which is how we generate and render).
/// </summary>
public class ChunkData
{
    public readonly int ChunkX;  // Chunk coordinate (not pixel, not tile)
    public readonly int ChunkY;
    public readonly TileType[] Tiles;
    public bool IsDirty;         // True if modified since last render update

    /// <summary>World-space tile coordinate of this chunk's top-left corner.</summary>
    public int WorldTileX => ChunkX * WorldConfig.ChunkSize;
    public int WorldTileY => ChunkY * WorldConfig.ChunkSize;

    /// <summary>World-space pixel coordinate of this chunk's top-left corner.</summary>
    public int WorldPixelX => ChunkX * WorldConfig.ChunkPixelSize;
    public int WorldPixelY => ChunkY * WorldConfig.ChunkPixelSize;

    public ChunkData(int chunkX, int chunkY)
    {
        ChunkX = chunkX;
        ChunkY = chunkY;
        Tiles = new TileType[WorldConfig.ChunkSize * WorldConfig.ChunkSize];
        IsDirty = true;
    }

    /// <summary>Get tile at local chunk coordinates (0 to ChunkSize-1).</summary>
    public TileType GetTile(int localX, int localY)
    {
        return Tiles[localY * WorldConfig.ChunkSize + localX];
    }

    /// <summary>Set tile at local chunk coordinates. Marks chunk as dirty.</summary>
    public void SetTile(int localX, int localY, TileType type)
    {
        int idx = localY * WorldConfig.ChunkSize + localX;
        if (Tiles[idx] != type)
        {
            Tiles[idx] = type;
            IsDirty = true;
        }
    }

    /// <summary>Fill entire chunk with a single tile type.</summary>
    public void Fill(TileType type)
    {
        System.Array.Fill(Tiles, type);
        IsDirty = true;
    }

    /// <summary>Check if local coordinates are within chunk bounds.</summary>
    public static bool InBounds(int localX, int localY)
    {
        return localX >= 0 && localX < WorldConfig.ChunkSize
            && localY >= 0 && localY < WorldConfig.ChunkSize;
    }

    /// <summary>Convert world tile coords to chunk coords + local offset.</summary>
    public static (int chunkX, int chunkY, int localX, int localY) WorldToLocal(int worldTileX, int worldTileY)
    {
        // Use integer division that floors for negative coords
        int cx = worldTileX >= 0
            ? worldTileX / WorldConfig.ChunkSize
            : (worldTileX - WorldConfig.ChunkSize + 1) / WorldConfig.ChunkSize;
        int cy = worldTileY >= 0
            ? worldTileY / WorldConfig.ChunkSize
            : (worldTileY - WorldConfig.ChunkSize + 1) / WorldConfig.ChunkSize;

        int lx = worldTileX - cx * WorldConfig.ChunkSize;
        int ly = worldTileY - cy * WorldConfig.ChunkSize;

        return (cx, cy, lx, ly);
    }
}
