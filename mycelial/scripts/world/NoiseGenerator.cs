namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Configures and provides access to all noise layers used in world generation.
/// Create ONE instance at world init, then pass it to WorldGenerator.
/// 
/// FastNoiseLite.GetNoise2D() is read-only after configuration, so it's safe
/// to call from multiple threads concurrently.
/// </summary>
public class NoiseGenerator
{
	/// <summary>Main terrain shape — determines where solid ground vs air is.</summary>
	public FastNoiseLite TerrainNoise { get; }

	/// <summary>Cave carving — combined with terrain to create underground openings.</summary>
	public FastNoiseLite CaveNoise { get; }

	/// <summary>Secondary cave layer — overlaid for more organic shapes.</summary>
	public FastNoiseLite CaveDetail { get; }

	/// <summary>Biome boundary wobble — makes biome transitions jagged, not flat.</summary>
	public FastNoiseLite BiomeBoundaryNoise { get; }

	/// <summary>Ore/resource placement.</summary>
	public FastNoiseLite OreNoise { get; }

	/// <summary>General-purpose variation noise for decoration, accent tiles, etc.</summary>
	public FastNoiseLite VariationNoise { get; }

	/// <summary>Large-scale noise for biome patch placement.</summary>
	public FastNoiseLite BiomePatchNoise { get; }

	/// <summary>Second biome noise layer for irregular patch shapes.</summary>
	public FastNoiseLite BiomePatchNoise2 { get; }

	public int Seed { get; }

	public NoiseGenerator(int seed = 0)
	{
		// Use random seed if 0
		Seed = seed == 0 ? (int)(GD.Randi() % int.MaxValue) : seed;

		TerrainNoise = CreateNoise(Seed, WorldConfig.TerrainFrequency,
			FastNoiseLite.NoiseTypeEnum.Perlin, FastNoiseLite.FractalTypeEnum.Fbm, 4);

		CaveNoise = CreateNoise(Seed + 1, WorldConfig.CaveFrequency,
			FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FastNoiseLite.FractalTypeEnum.Fbm, 3);

		CaveDetail = CreateNoise(Seed + 2, WorldConfig.CaveFrequency * 1.2f,
			FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FastNoiseLite.FractalTypeEnum.Ridged, 1);

		BiomeBoundaryNoise = CreateNoise(Seed + 3, WorldConfig.BiomeBoundaryNoise,
			FastNoiseLite.NoiseTypeEnum.Perlin, FastNoiseLite.FractalTypeEnum.Fbm, 2);

		OreNoise = CreateNoise(Seed + 4, 0.1f,
			FastNoiseLite.NoiseTypeEnum.Cellular, FastNoiseLite.FractalTypeEnum.None, 1);

		VariationNoise = CreateNoise(Seed + 5, 0.08f,
			FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FastNoiseLite.FractalTypeEnum.Fbm, 2);

		BiomePatchNoise = CreateNoise(Seed + 6, WorldConfig.BiomePatchFrequency,
			FastNoiseLite.NoiseTypeEnum.Cellular, FastNoiseLite.FractalTypeEnum.None, 1);

		BiomePatchNoise2 = CreateNoise(Seed + 7, WorldConfig.BiomePatchFrequency2,
			FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FastNoiseLite.FractalTypeEnum.Fbm, 2);
	}

	private static FastNoiseLite CreateNoise(
		int seed, float frequency,
		FastNoiseLite.NoiseTypeEnum type,
		FastNoiseLite.FractalTypeEnum fractal,
		int octaves)
	{
		var noise = new FastNoiseLite();
		noise.Seed = seed;
		noise.Frequency = frequency;
		noise.NoiseType = type;
		noise.FractalType = fractal;
		noise.FractalOctaves = octaves;
		return noise;
	}

	/// <summary>
	/// Sample the biome boundary wobble at a given world X position.
	/// Returns a tile offset that should be added to depth when determining biome.
	/// </summary>
	public int GetBiomeWobble(int worldTileX)
	{
		float n = BiomeBoundaryNoise.GetNoise1D(worldTileX);
		return (int)(n * WorldConfig.BiomeBoundaryWobble);
	}

	/// <summary>
	/// Determine if a tile at world coordinates should be a cave (air).
	/// Uses two overlapping noise layers for organic cave shapes.
	/// Returns true if the tile should be open air.
	/// </summary>
	public bool IsCave(int worldTileX, int worldTileY, float biomeCaveDensity)
	{
		float n1 = CaveNoise.GetNoise2D(worldTileX, worldTileY);
		float n2 = CaveDetail.GetNoise2D(worldTileX, worldTileY);

		// Combine the two noise values with low detail influence for smoother cave outlines.
		// Biome cave density still affects threshold, but with a narrow range so caves stay rare.
		float combined = (n1 + n2 * 0.20f) / 1.20f;
		float threshold = WorldConfig.CaveThreshold + (0.5f - biomeCaveDensity) * 0.12f;

		return combined > threshold;
	}

	/// <summary>
	/// Check if a position should contain a resource/ore node.
	/// Uses cellular noise for natural-looking cluster placement.
	/// </summary>
	public bool IsOre(int worldTileX, int worldTileY, float biomeOreDensity)
	{
		float n = OreNoise.GetNoise2D(worldTileX, worldTileY);
		// Cellular noise returns values in [-1, 1]. Use a tight threshold for rare clusters.
		return n < -1.0f + biomeOreDensity * 2.0f;
	}

	/// <summary>
	/// General variation value at a position. Returns [-1, 1].
	/// Use for deciding between primary/secondary tiles, decoration, etc.
	/// </summary>
	public float GetVariation(int worldTileX, int worldTileY)
	{
		return VariationNoise.GetNoise2D(worldTileX, worldTileY);
	}

	/// <summary>
	/// Get the biome config at a world position using patch-based selection.
	/// Uses two noise layers to create large irregular biome regions.
	/// </summary>
	public BiomeConfig GetBiomeAt(int worldTileX, int worldTileY)
	{
		float n1 = BiomePatchNoise.GetNoise2D(worldTileX, worldTileY);
		float n2 = BiomePatchNoise2.GetNoise2D(worldTileX, worldTileY);
		return BiomeRegistry.SelectBiome(worldTileX, worldTileY, n1, n2);
	}
}
