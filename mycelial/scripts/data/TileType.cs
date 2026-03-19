namespace Mycorrhiza.Data;

/// <summary>
/// Every distinct tile type in the game. Organized by biome of first appearance.
/// The integer value is used as the atlas index in the TileSet.
/// </summary>
public enum TileType : ushort
{
    // --- Meta ---
    Air = 0,

    // --- Universal ---
    Dirt = 1,
    Stone = 2,
    Water = 3,
    Sand = 4,
    Clay = 5,
    Gravel = 6,

    // --- Topsoil (0-30) ---
    Topsoil = 10,
    RichSoil = 11,
    MulchLayer = 12,
    SmallRoots = 13,

    // --- Root Maze (30-120) ---
    DenseRoots = 20,
    AncientRoot = 21,      // Thick, almost stone-like petrified roots
    ClayDeposit = 22,
    CrystallizedSap = 23,  // Rare resource node
    RootChamberWall = 24,  // Hollow root interiors

    // --- Wet Dark (120-300) ---
    WetStone = 30,
    Limestone = 31,
    StalactiteBase = 32,
    BioluminescentVein = 33,  // Glows — provides light in caves
    SubmergedStone = 34,       // Stone under water
    PhosphorescentMineral = 35,

    // --- Bone Strata (300-600) ---
    Boneite = 40,              // Calcified bone-rock
    CompressedSkull = 41,      // Decorative/lore — enormous skulls in the rock
    FossilRib = 42,            // Curved structures forming natural arches
    BoneMarrow = 43,           // Resource node — neural compounds
    ToothFormation = 44,       // Jagged protrusions

    // --- Thermovents (600-1000) ---
    Basalt = 50,
    Obsidian = 51,
    MagmaStone = 52,           // Hot — damages non-thermophilic entities
    ThermalVent = 53,          // Emits heat particles, resource source
    CooledLava = 54,
    CrystalGrotte = 55,        // Rare mineral deposits

    // --- Mycelial Graveyard (1000+) ---
    PetrifiedMycelium = 60,    // Fossilized ancient fungal network
    LivingFossil = 61,         // Twitches, reacts to player presence
    FungalForestFloor = 62,    // Underground fungal biome terrain
    AncientSporeNode = 63,     // Major resource — ancient genetic material
    MemoryStone = 64,          // Lore object — contains memory fragments

    // --- Player Structures ---
    Mycelium = 100,            // Player's basic spread tile — lightest
    MyceliumDense = 101,       // Slightly darker — recent trail
    MyceliumDark = 102,        // Darker — fresh trail near head
    MyceliumCore = 103,        // Darkest — the tendril head itself
    SporeEmitter = 104,        // Defensive structure
    FruitingBodyBase = 105,    // Anchor for combat units

    // --- Origin Tree ---
    Bark = 110,                // Trunk exterior — thick, dark, gnarled
    Heartwood = 111,           // Trunk interior — dense, alive
    DeadHeartwood = 112,       // Rotting interior — where the infection started
    BranchWood = 113,          // Thinner canopy branches
    Canopy = 114,              // Dense leaf cover (blocks light)
    DeadCanopy = 115,          // Dying leaves — sparse, holes
    ThickRoot = 116,           // Major roots near trunk base
    MediumRoot = 117,          // Branching roots mid-depth
    ThinRoot = 118,            // Fine root tendrils at depth
    RootTip = 119,             // The very end of a root — where mycelium first spreads from

    // --- Liquids ---
    Lava = 200,
    ToxicWater = 201,
    AcidPool = 202,
}

/// <summary>
/// Properties that can be queried for any tile type.
/// Stored as flags for efficient lookup.
/// </summary>
[System.Flags]
public enum TileFlags : byte
{
    None = 0,
    Solid = 1 << 0,        // Blocks movement
    Liquid = 1 << 1,       // Flows, can be swum through
    Breakable = 1 << 2,    // Can be destroyed/mined
    Emissive = 1 << 3,     // Produces light
    Hazardous = 1 << 4,    // Damages entities on contact
    Organic = 1 << 5,      // Can be infected/consumed by mycelium
    Resource = 1 << 6,     // Harvestable resource node
    PlayerOwned = 1 << 7,  // Part of player's network
}

/// <summary>
/// Static lookup table for tile properties.
/// Call TileProperties.Get(TileType) to get the flags for any tile.
/// </summary>
public static class TileProperties
{
    private static readonly TileFlags[] _flags = new TileFlags[256];

    static TileProperties()
    {
        // Default everything to solid + breakable
        for (int i = 1; i < 256; i++)
            _flags[i] = TileFlags.Solid | TileFlags.Breakable;

        // Air
        _flags[(int)TileType.Air] = TileFlags.None;

        // Liquids
        SetFlags(TileType.Water, TileFlags.Liquid);
        SetFlags(TileType.Lava, TileFlags.Liquid | TileFlags.Hazardous | TileFlags.Emissive);
        SetFlags(TileType.ToxicWater, TileFlags.Liquid | TileFlags.Hazardous);
        SetFlags(TileType.AcidPool, TileFlags.Liquid | TileFlags.Hazardous);

        // Emissive tiles
        AddFlag(TileType.BioluminescentVein, TileFlags.Emissive);
        AddFlag(TileType.PhosphorescentMineral, TileFlags.Emissive);
        AddFlag(TileType.ThermalVent, TileFlags.Emissive | TileFlags.Hazardous);
        AddFlag(TileType.MagmaStone, TileFlags.Emissive | TileFlags.Hazardous);
        AddFlag(TileType.LivingFossil, TileFlags.Emissive);

        // Organic tiles (infectable by mycelium)
        AddFlag(TileType.Topsoil, TileFlags.Organic);
        AddFlag(TileType.RichSoil, TileFlags.Organic);
        AddFlag(TileType.MulchLayer, TileFlags.Organic);
        AddFlag(TileType.SmallRoots, TileFlags.Organic);
        AddFlag(TileType.DenseRoots, TileFlags.Organic);
        AddFlag(TileType.AncientRoot, TileFlags.Organic);
        AddFlag(TileType.Dirt, TileFlags.Organic);
        AddFlag(TileType.FungalForestFloor, TileFlags.Organic);

        // Resource nodes
        AddFlag(TileType.CrystallizedSap, TileFlags.Resource);
        AddFlag(TileType.PhosphorescentMineral, TileFlags.Resource);
        AddFlag(TileType.BoneMarrow, TileFlags.Resource);
        AddFlag(TileType.CrystalGrotte, TileFlags.Resource);
        AddFlag(TileType.AncientSporeNode, TileFlags.Resource);

        // Unbreakable
        RemoveFlag(TileType.Obsidian, TileFlags.Breakable);

        // Player structures — gradient from light (old) to dark (head)
        SetFlags(TileType.Mycelium, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumDense, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumDark, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable);
        SetFlags(TileType.MyceliumCore, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable | TileFlags.Emissive);
        SetFlags(TileType.SporeEmitter, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable | TileFlags.Solid);
        SetFlags(TileType.FruitingBodyBase, TileFlags.Organic | TileFlags.PlayerOwned | TileFlags.Breakable | TileFlags.Solid);

        // Origin Tree — solid, organic, unbreakable (the tree is permanent)
        SetFlags(TileType.Bark, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.Heartwood, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.DeadHeartwood, TileFlags.Solid | TileFlags.Organic | TileFlags.Emissive); // Faint glow from infection
        SetFlags(TileType.BranchWood, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.Canopy, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.DeadCanopy, TileFlags.Organic); // Not solid — sparse enough to pass through
        SetFlags(TileType.ThickRoot, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.MediumRoot, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.ThinRoot, TileFlags.Solid | TileFlags.Organic);
        SetFlags(TileType.RootTip, TileFlags.Organic | TileFlags.Emissive | TileFlags.PlayerOwned); // Where infection begins
    }

    public static TileFlags Get(TileType type) => _flags[(ushort)type];
    public static bool Is(TileType type, TileFlags flag) => (_flags[(ushort)type] & flag) != 0;

    private static void SetFlags(TileType type, TileFlags flags) => _flags[(ushort)type] = flags;
    private static void AddFlag(TileType type, TileFlags flag) => _flags[(ushort)type] |= flag;
    private static void RemoveFlag(TileType type, TileFlags flag) => _flags[(ushort)type] &= ~flag;
}
