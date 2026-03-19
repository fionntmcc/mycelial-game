namespace Mycorrhiza.World;

using Mycorrhiza.Data;

/// <summary>
/// Auto-tiling pass that replaces plain Dirt tiles with the correct grass variant
/// based on adjacent air tiles.
///
/// Run this after terrain fill and cave carving. It examines each Dirt tile,
/// checks its 8 neighbors, and assigns the appropriate grass variant:
///   - Floor: air above
///   - Ceiling: air below
///   - Walls: air to left/right
///   - Inner corners: air on two cardinal sides
///   - Outer corners: no cardinal air, diagonal air
///
/// Works within a single chunk. For edge tiles, defaults to "solid" which
/// may cause minor artifacts at chunk boundaries — acceptable for prototype.
/// </summary>
public static class AutoTiler
{
    /// <summary>
    /// Run auto-tiling on a chunk. Call after terrain generation and cave carving.
    /// Replaces Dirt tiles adjacent to air with the correct grass variant.
    /// </summary>
    public static void Apply(ChunkData chunk)
    {
        // Work on a snapshot to avoid reads being affected by writes
        var snapshot = new TileType[WorldConfig.ChunkSize * WorldConfig.ChunkSize];
        System.Array.Copy(chunk.Tiles, snapshot, snapshot.Length);

        for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
        {
            for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
            {
                TileType current = snapshot[ly * WorldConfig.ChunkSize + lx];

                // Only auto-tile Dirt
                if (current != TileType.Dirt) continue;

                // Sample 8 neighbors
                bool airUp    = IsAir(snapshot, lx, ly - 1);
                bool airDown  = IsAir(snapshot, lx, ly + 1);
                bool airLeft  = IsAir(snapshot, lx - 1, ly);
                bool airRight = IsAir(snapshot, lx + 1, ly);
                bool airTL    = IsAir(snapshot, lx - 1, ly - 1);
                bool airTR    = IsAir(snapshot, lx + 1, ly - 1);
                bool airBL    = IsAir(snapshot, lx - 1, ly + 1);
                bool airBR    = IsAir(snapshot, lx + 1, ly + 1);

                int cardinalAir = (airUp ? 1 : 0) + (airDown ? 1 : 0)
                                + (airLeft ? 1 : 0) + (airRight ? 1 : 0);

                // No adjacent air at all — check diagonals for outer corners
                if (cardinalAir == 0)
                {
                    // Outer corners: solid on all 4 sides, air on a diagonal
                    // Priority: TL, TR, BL, BR (pick first found)
                    if (airTL)
                        chunk.SetTile(lx, ly, TileType.GrassOuterTL);
                    else if (airTR)
                        chunk.SetTile(lx, ly, TileType.GrassOuterTR);
                    else if (airBL)
                        chunk.SetTile(lx, ly, TileType.GrassOuterBL);
                    else if (airBR)
                        chunk.SetTile(lx, ly, TileType.GrassOuterBR);
                    // else: fully surrounded by solid, stays as Dirt
                    continue;
                }

                // One cardinal side is air — simple edge
                if (cardinalAir == 1)
                {
                    if (airUp) chunk.SetTile(lx, ly, TileType.GrassFloor);
                    else if (airDown) chunk.SetTile(lx, ly, TileType.GrassCeiling);
                    else if (airLeft) chunk.SetTile(lx, ly, TileType.GrassLWall);
                    else if (airRight) chunk.SetTile(lx, ly, TileType.GrassRWall);
                    continue;
                }

                // Two cardinal sides are air — inner corner
                if (cardinalAir == 2)
                {
                    if (airUp && airLeft) chunk.SetTile(lx, ly, TileType.GrassInnerTL);
                    else if (airUp && airRight) chunk.SetTile(lx, ly, TileType.GrassInnerTR);
                    else if (airDown && airLeft) chunk.SetTile(lx, ly, TileType.GrassInnerBL);
                    else if (airDown && airRight) chunk.SetTile(lx, ly, TileType.GrassInnerBR);
                    // Opposite sides (up+down or left+right) — thin pillar, use floor
                    else if (airUp && airDown) chunk.SetTile(lx, ly, TileType.GrassFloor);
                    else if (airLeft && airRight) chunk.SetTile(lx, ly, TileType.GrassLWall);
                    continue;
                }

                // 3+ cardinal sides air — heavily exposed. Use floor as fallback.
                chunk.SetTile(lx, ly, TileType.GrassFloor);
            }
        }
    }

    /// <summary>
    /// Check if a tile at local coords is "air-like" (empty, liquid, or out of bounds).
    /// Out of bounds defaults to solid (not air) to avoid grass at chunk edges.
    /// </summary>
    private static bool IsAir(TileType[] snapshot, int lx, int ly)
    {
        if (lx < 0 || lx >= WorldConfig.ChunkSize || ly < 0 || ly >= WorldConfig.ChunkSize)
            return false; // Out of bounds = assume solid

        TileType t = snapshot[ly * WorldConfig.ChunkSize + lx];
        return t == TileType.Air || TileProperties.Is(t, TileFlags.Liquid);
    }
}
