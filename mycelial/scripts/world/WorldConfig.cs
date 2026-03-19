namespace Mycorrhiza.World;

/// <summary>
/// All tunable world generation constants in one place.
/// Adjust these to change world size, chunk behavior, and generation parameters.
/// </summary>
public static class WorldConfig
{
	// --- Tile & Chunk Dimensions ---
	public const int TileSize = 16;          // Pixels per tile
	public const int ChunkSize = 32;         // Tiles per chunk (both axes)
	public const int ChunkPixelSize = ChunkSize * TileSize; // 512px

	// --- World Dimensions (in tiles) ---
	public const int WorldWidthTiles = 2048;  // ~64 chunks wide
	public const int WorldDepthTiles = 1280;  // ~40 chunks deep

	// --- World Dimensions (in chunks) ---
	public const int WorldWidthChunks = WorldWidthTiles / ChunkSize;   // 64
	public const int WorldDepthChunks = WorldDepthTiles / ChunkSize;   // 40

	// --- Chunk Loading ---
	public const int LoadRadiusChunks = 3;    // Load chunks this far beyond viewport
	public const int UnloadRadiusChunks = 5;  // Unload chunks beyond this distance
	public const int MaxChunksPerFrame = 2;   // Max chunks to instantiate per frame (prevent stutter)

	// --- Biome Depth Boundaries (in tiles from surface) ---
	// These define the CENTER of each biome transition zone.
	// Actual biome assignment uses noise to create jagged, organic boundaries.
	public const int SurfaceLevel = 0;
	public const int TopsoilFloor = 30;             // ~15m  (0-30 tiles)
	public const int RootMazeFloor = 120;            // ~60m  (30-120 tiles)
	public const int WetDarkFloor = 300;             // ~150m (120-300 tiles)
	public const int BoneStrataFloor = 600;          // ~300m (300-600 tiles)
	public const int ThermoventFloor = 1000;         // ~500m (600-1000 tiles)
	public const int MycelialGraveyardFloor = 1600;  // ~800m (1000-1600 tiles, if world extended)
	// Deep Rot and The Below extend beyond — reserved for late development

	// --- Generation Noise Parameters ---
	public const int TerrainSeed = 0;         // 0 = random seed each run
	public const float TerrainFrequency = 0.02f;
	public const float CaveFrequency = 0.03f;
	public const float CaveThreshold = 0.36f;  // Higher = rarer caves
	public const int CaveMinDepth = 30;        // No caves above this depth
	public const float BiomeBoundaryNoise = 0.01f; // Frequency for biome edge wobble
	public const int BiomeBoundaryWobble = 15; // Max tile offset for biome transitions

	// --- Biome Patch System ---
	public const float BiomePatchFrequency = 0.008f;  // Low frequency = large patches
	public const float BiomePatchFrequency2 = 0.012f;  // Second layer for irregularity
	public const int NeutralZoneRadius = 60;           // Tiles radius around tree = safe/neutral

	// --- Cave Cellular Automata ---
	public const int CaveCAIterations = 4;    // Smoothing passes
	public const float CaveInitialFill = 0.52f; // Initial wall density (higher = fewer caves)
	public const int CaveCABirthLimit = 4;    // Neighbors needed to become wall
	public const int CaveCADeathLimit = 3;    // Neighbors needed to stay wall

	// --- Origin Tree ---
	public const int TreeWorldX = WorldWidthTiles / 2;  // Center of the world
	public const int TreeTrunkWidth = 6;       // Tiles wide at base
	public const int TreeTrunkHeight = 18;     // Tiles tall above ground
	public const int TreeCanopyRadius = 14;    // Leaf spread radius
	public const int TreeCanopyHeight = 10;    // How tall the canopy blob is
	public const int TreeRootDepth = 80;       // How deep roots can reach (tiles)
	public const int TreeRootBranches = 5;     // Major root branches from trunk base
	public const int TreeRootMinBranch = 8;    // Min tiles before a root can sub-branch
	public const int TreeRootMaxBranch = 20;   // Max tiles before a root must sub-branch
}
