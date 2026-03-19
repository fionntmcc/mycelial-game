namespace Mycorrhiza.Data;

/// <summary>
/// Tile types organized to match the tileset grid.
/// Atlas is 16 columns wide. Row = ID / 16, Column = ID % 16.
///
/// LAYOUT:
///   Row 0 (0-15):   Basic solids
///   Row 1 (16-23):  Grass edges (floor, ceil, walls, inner corners)
///   Row 2 (32-35):  Grass outer corners
///   Row 3 (48-55):  Infected grass edges (same layout as row 1)
///   Row 4 (64-67):  Infected grass outer corners (same layout as row 2)
///   Row 5 (80-83):  Mycelium gradient + infected dirt
///   Row 6+ :        Biome accents, creatures (future)
///
/// INFECTION RULE: Grass tile + 32 = infected variant. Same position, 2 rows down.
/// </summary>
public enum TileType : ushort
{
    // === Row 0: Basic Solids (IDs 0-15) ===
    Air = 0,
    Dirt = 1,
    Stone = 2,
    Water = 3,
    Sand = 4,
    Gravel = 5,
    Clay = 6,
    Leaf = 7,
    Wood = 8,          // Tree trunk/branches
    Roots = 9,         // Tree roots (all thicknesses)
    RootTip = 10,      // Root endpoints — where mycelium begins
    InfectedDirt = 11, // Dirt consumed by mycelium

    // === Row 1: Grass Edges (IDs 16-23) ===
    GrassFloor = 16,       // Air above, solid below
    GrassCeiling = 17,     // Solid above, air below
    GrassLWall = 18,       // Air to left
    GrassRWall = 19,       // Air to right
    GrassInnerTL = 20,     // Air above AND left
    GrassInnerTR = 21,     // Air above AND right
    GrassInnerBL = 22,     // Air below AND left
    GrassInnerBR = 23,     // Air below AND right

    // === Row 2: Grass Outer Corners (IDs 32-35) ===
    GrassOuterTL = 32,     // Solid on all cardinals, air at top-left diagonal
    GrassOuterTR = 33,     // Solid on all cardinals, air at top-right diagonal
    GrassOuterBL = 34,     // Solid on all cardinals, air at bottom-left diagonal
    GrassOuterBR = 35,     // Solid on all cardinals, air at bottom-right diagonal

    // === Row 3: Infected Grass Edges (IDs 48-55) ===
    // Exactly +32 offset from normal grass
    InfGrassFloor = 48,
    InfGrassCeiling = 49,
    InfGrassLWall = 50,
    InfGrassRWall = 51,
    InfGrassInnerTL = 52,
    InfGrassInnerTR = 53,
    InfGrassInnerBL = 54,
    InfGrassInnerBR = 55,

    // === Row 4: Infected Grass Outer Corners (IDs 64-67) ===
    InfGrassOuterTL = 64,
    InfGrassOuterTR = 65,
    InfGrassOuterBL = 66,
    InfGrassOuterBR = 67,

    // === Row 5: Mycelium Gradient (IDs 80-83) ===
    Mycelium = 80,          // Lightest — oldest territory
    MyceliumDense = 81,     // Medium — recent trail
    MyceliumDark = 82,      // Dark — fresh trail near head
    MyceliumCore = 83,      // Darkest — the tendril head itself

    // === Row 6: Biome Accents (IDs 96-111) — add art later ===
    BioluminescentVein = 96,
    Boneite = 97,
    FossilRib = 98,
    BoneMarrow = 99,
    Basalt = 100,
    Obsidian = 101,
    CrystalGrotte = 102,
    PetrifiedMycelium = 103,
    LivingFossil = 104,
    AncientSporeNode = 105,
    ThermalVent = 106,

    // === Row 8: Liquids (IDs 128-131) ===
    Lava = 128,
    ToxicWater = 129,
    AcidPool = 130,

    // === Row 9-10: Creatures (IDs 144-165) ===
    CreatureEarthworm = 144,
    CreatureBeetle = 145,
    CreatureGrub = 146,
    CreatureMoleRat = 147,
    CreatureRootBorer = 148,
    CreatureFungusGnat = 149,
    CreatureCaveFish = 150,
    CreatureBlindSalamander = 151,
    CreatureCaveSpider = 152,
    CreatureBoneCrab = 153,
    CreatureWormColony = 160,
    CreatureMarrowLeech = 161,
    CreatureMagmaBeetle = 162,
    CreatureTubeWorm = 163,
    CreatureMemorySlug = 164,
    CreatureFungalPredator = 165,
}

/// <summary>
/// Tile property flags.
/// </summary>
[System.Flags]
public enum TileFlags : byte
{
    None = 0,
    Solid = 1 << 0,
    Liquid = 1 << 1,
    Breakable = 1 << 2,
    Emissive = 1 << 3,
    Hazardous = 1 << 4,
    Organic = 1 << 5,
    Resource = 1 << 6,
    PlayerOwned = 1 << 7,
}

public static class TileProperties
{
    private static readonly TileFlags[] _flags = new TileFlags[256];

    static TileProperties()
    {
        // Default: solid + breakable
        for (int i = 1; i < 256; i++)
            _flags[i] = TileFlags.Solid | TileFlags.Breakable;

        // Air
        _flags[0] = TileFlags.None;

        // Basic solids
        SetFlags(TileType.Dirt, TileFlags.Solid | TileFlags.Breakable | TileFlags.Organic);
        SetFlags(TileType.Stone, TileFlags.Solid | TileFlags.Breakable);
        SetFlags(TileType.Sand, TileFlags.Solid | TileFlags.Breakable | TileFlags.Organic);
        SetFlags(TileType.Gravel, TileFlags.Solid | TileFlags.Breakable);
        SetFlags(TileType.Clay, TileFlags.Solid | TileFlags.Breakable);
        SetFlags(TileType.Leaf, TileFlags.Organic | TileFlags.Breakable);
        SetFlags(TileType.Wood, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.Roots, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.RootTip, TileFlags.Organic | TileFlags.Emissive | TileFlags.PlayerOwned);
        SetFlags(TileType.InfectedDirt, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);

        // Liquids
        SetFlags(TileType.Water, TileFlags.Liquid);
        SetFlags(TileType.Lava, TileFlags.Liquid | TileFlags.Hazardous | TileFlags.Emissive);
        SetFlags(TileType.ToxicWater, TileFlags.Liquid | TileFlags.Hazardous);
        SetFlags(TileType.AcidPool, TileFlags.Liquid | TileFlags.Hazardous);

        // All grass variants: solid, organic, breakable
        for (int i = 16; i <= 23; i++)
            _flags[i] = TileFlags.Solid | TileFlags.Organic | TileFlags.Breakable;
        for (int i = 32; i <= 35; i++)
            _flags[i] = TileFlags.Solid | TileFlags.Organic | TileFlags.Breakable;

        // Infected grass: organic, player-owned, breakable
        for (int i = 48; i <= 55; i++)
            _flags[i] = TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable;
        for (int i = 64; i <= 67; i++)
            _flags[i] = TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable;

        // Mycelium gradient: organic, player-owned
        SetFlags(TileType.Mycelium, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumDense, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumDark, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumCore, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable | TileFlags.Emissive);

        // Biome accents
        AddFlag(TileType.BioluminescentVein, TileFlags.Emissive);
        AddFlag(TileType.ThermalVent, TileFlags.Emissive | TileFlags.Hazardous);
        AddFlag(TileType.LivingFossil, TileFlags.Emissive);
        AddFlag(TileType.BoneMarrow, TileFlags.Resource);
        AddFlag(TileType.CrystalGrotte, TileFlags.Resource);
        AddFlag(TileType.AncientSporeNode, TileFlags.Resource);
        RemoveFlag(TileType.Obsidian, TileFlags.Breakable);

        // Creature tiles: not solid, visual only
        for (int i = 144; i <= 165; i++)
            _flags[i] = TileFlags.None;
    }

    public static TileFlags Get(TileType type) => _flags[(ushort)type];
    public static bool Is(TileType type, TileFlags flag) => (_flags[(ushort)type] & flag) != 0;

    private static void SetFlags(TileType type, TileFlags flags) => _flags[(ushort)type] = flags;
    private static void AddFlag(TileType type, TileFlags flag) => _flags[(ushort)type] |= flag;
    private static void RemoveFlag(TileType type, TileFlags flag) => _flags[(ushort)type] &= ~flag;

    // === Infection Helpers ===

    /// <summary>Is this a grass variant that has an infected counterpart?</summary>
    public static bool IsGrass(TileType t)
    {
        int id = (ushort)t;
        return (id >= 16 && id <= 23) || (id >= 32 && id <= 35);
    }

    /// <summary>Is this an infected grass variant?</summary>
    public static bool IsInfectedGrass(TileType t)
    {
        int id = (ushort)t;
        return (id >= 48 && id <= 55) || (id >= 64 && id <= 67);
    }

    /// <summary>Is this any mycelium/infected tile?</summary>
    public static bool IsMycelium(TileType t)
    {
        int id = (ushort)t;
        return (id >= 80 && id <= 83) || id == 11 || IsInfectedGrass(t);
    }

    /// <summary>
    /// Get the infected version of a tile.
    /// Grass → infected grass (+32). Dirt → InfectedDirt.
    /// Everything else organic → Mycelium (lightest).
    /// Non-organic → returns the same tile (can't infect).
    /// </summary>
    public static TileType GetInfectedVariant(TileType t)
    {
        // Grass variants: +32 offset
        if (IsGrass(t))
            return (TileType)((ushort)t + 32);

        // Already infected: return as-is
        if (IsMycelium(t))
            return t;

        // Dirt → infected dirt
        if (t == TileType.Dirt)
            return TileType.InfectedDirt;

        // Other organic tiles → generic mycelium
        if (Is(t, TileFlags.Organic))
            return TileType.Mycelium;

        // Non-organic: can't infect
        return t;
    }
}
