namespace Mycorrhiza.Data;

using Mycorrhiza.World;

/// <summary>
/// Underground biome identifiers.
/// Biomes appear as large irregular patches influenced by depth,
/// not strict horizontal layers.
/// </summary>
public enum BiomeType : byte
{
	Sky = 0,
	Neutral = 1,             // Safe zone around origin tree — basic dirt/soil
	Topsoil = 2,
	RootMaze = 3,
	WetDark = 4,
	BoneStrata = 5,
	Thermovents = 6,
	MycelialGraveyard = 7,
	DeepRot = 8,
	TheBelow = 9,
}

/// <summary>
/// Configuration data for a single biome. Describes generation parameters.
/// </summary>
public readonly struct BiomeConfig
{
	public readonly BiomeType Type;
	public readonly int MinDepth;          // Earliest depth this biome can appear
	public readonly int PeakDepth;         // Depth where this biome is most likely
	public readonly int MaxDepth;          // Deepest this biome can appear
	public readonly TileType PrimaryTile;
	public readonly TileType SecondaryTile;
	public readonly TileType AccentTile;
	public readonly TileType LiquidType;
	public readonly float CaveDensity;
	public readonly float OreDensity;
	public readonly float CaveScale;

	public BiomeConfig(
		BiomeType type, int minDepth, int peakDepth, int maxDepth,
		TileType primary, TileType secondary, TileType accent,
		TileType liquid, float caveDensity, float oreDensity, float caveScale)
	{
		Type = type;
		MinDepth = minDepth;
		PeakDepth = peakDepth;
		MaxDepth = maxDepth;
		PrimaryTile = primary;
		SecondaryTile = secondary;
		AccentTile = accent;
		LiquidType = liquid;
		CaveDensity = caveDensity;
		OreDensity = oreDensity;
		CaveScale = caveScale;
	}

	/// <summary>
	/// How strongly this biome "wants" to appear at a given depth.
	/// Returns 0-1, where 1 = peak depth, tapering to 0 at min/max.
	/// </summary>
	public float GetDepthWeight(int depth)
	{
		if (depth < MinDepth || depth > MaxDepth) return 0f;
		if (depth <= PeakDepth)
		{
			if (PeakDepth == MinDepth) return 1f;
			return (float)(depth - MinDepth) / (PeakDepth - MinDepth);
		}
		else
		{
			if (MaxDepth == PeakDepth) return 1f;
			return 1f - (float)(depth - PeakDepth) / (MaxDepth - PeakDepth);
		}
	}
}

/// <summary>
/// Biome registry with patch-based selection.
///
/// Instead of flat strata, biomes are placed as large irregular regions:
///   1. Cellular noise divides the world into large "cells"
///   2. Each cell is assigned a biome based on depth weight + noise value
///   3. Multiple biomes can exist at the same depth
///   4. Deeper = more dangerous biomes become available
///   5. Near the Origin Tree = always Neutral
/// </summary>
public static class BiomeRegistry
{
	public static readonly BiomeConfig Neutral = new(
		BiomeType.Neutral,
		minDepth: 0, peakDepth: 0, maxDepth: 9999,
		primary: TileType.Dirt,
		secondary: TileType.Dirt,
		accent: TileType.Roots,
		liquid: TileType.Water,
		caveDensity: 0.12f,
		oreDensity: 0.005f,
		caveScale: 0.06f);

	/// <summary>
	/// All non-neutral biomes that can be selected by the patch system.
	/// </summary>
	public static readonly BiomeConfig[] Biomes = new BiomeConfig[]
	{
		new(BiomeType.Topsoil,
			minDepth: 0, peakDepth: 15, maxDepth: 80,
			primary: TileType.Dirt,
			secondary: TileType.Dirt,
			accent: TileType.Roots,
			liquid: TileType.Water,
			caveDensity: 0.15f, oreDensity: 0.01f, caveScale: 0.06f),

		new(BiomeType.RootMaze,
			minDepth: 10, peakDepth: 60, maxDepth: 200,
			primary: TileType.Clay,
			secondary: TileType.Dirt,
			accent: TileType.Roots,
			liquid: TileType.Water,
			caveDensity: 0.25f, oreDensity: 0.02f, caveScale: 0.07f),

		new(BiomeType.WetDark,
			minDepth: 40, peakDepth: 180, maxDepth: 500,
			primary: TileType.Stone,
			secondary: TileType.Stone,
			accent: TileType.BioluminescentVein,
			liquid: TileType.Water,
			caveDensity: 0.40f, oreDensity: 0.03f, caveScale: 0.04f),

		new(BiomeType.BoneStrata,
			minDepth: 150, peakDepth: 400, maxDepth: 800,
			primary: TileType.Boneite,
			secondary: TileType.Stone,
			accent: TileType.BoneMarrow,
			liquid: TileType.Water,
			caveDensity: 0.30f, oreDensity: 0.04f, caveScale: 0.03f),

		new(BiomeType.Thermovents,
			minDepth: 300, peakDepth: 700, maxDepth: 1100,
			primary: TileType.Basalt,
			secondary: TileType.Obsidian,
			accent: TileType.CrystalGrotte,
			liquid: TileType.Lava,
			caveDensity: 0.35f, oreDensity: 0.05f, caveScale: 0.05f),

		new(BiomeType.MycelialGraveyard,
			minDepth: 600, peakDepth: 1000, maxDepth: 1280,
			primary: TileType.PetrifiedMycelium,
			secondary: TileType.Stone,
			accent: TileType.AncientSporeNode,
			liquid: TileType.ToxicWater,
			caveDensity: 0.45f, oreDensity: 0.06f, caveScale: 0.04f),
	};

	/// <summary>
	/// Select a biome for a world position using noise-based patch selection.
	///
	/// Algorithm:
	///   1. If within neutral radius of tree -> Neutral
	///   2. Sample two noise values to create large irregular patches
	///   3. For each biome, compute depth weight at this Y
	///   4. Combine depth weight with noise-derived preference
	///   5. The biome with the highest combined score wins
	/// </summary>
	public static BiomeConfig SelectBiome(int worldX, int worldY, float biomeNoise, float biomeNoise2)
	{
		if (worldY < 0) return Neutral;

		// Neutral zone around origin tree
		int dx = worldX - WorldConfig.TreeWorldX;
		int distSq = dx * dx + worldY * worldY;
		if (distSq < WorldConfig.NeutralZoneRadius * WorldConfig.NeutralZoneRadius)
			return Neutral;

		// Soft transition from neutral
		float neutralEdge = WorldConfig.NeutralZoneRadius * 1.3f;
		float distFromTree = System.MathF.Sqrt(distSq);
		bool inTransition = distFromTree < neutralEdge;

		// Score each biome
		float bestScore = -1f;
		int bestIdx = 0;

		// Remap noise from [-1,1] to [0,1]
		float n1 = (biomeNoise + 1f) * 0.5f;
		float n2 = (biomeNoise2 + 1f) * 0.5f;

		for (int i = 0; i < Biomes.Length; i++)
		{
			float depthWeight = Biomes[i].GetDepthWeight(worldY);
			if (depthWeight <= 0f) continue;

			// Each biome responds to a different "phase" of the noise.
			// This creates spatial separation — different biomes dominate
			// in different noise regions.
			float biomePhase = (float)i / Biomes.Length;
			float noisePref = 1f - System.MathF.Abs(n1 - biomePhase) * 2f;
			noisePref = System.MathF.Max(0f, noisePref);

			// Second noise adds irregularity to patch shapes
			float noiseBoost = (n2 * 0.3f) * ((i % 2 == 0) ? 1f : -1f);

			float score = depthWeight * (0.4f + noisePref * 0.6f) + noiseBoost;

			if (score > bestScore)
			{
				bestScore = score;
				bestIdx = i;
			}
		}

		// In the transition zone, bias toward Neutral
		if (inTransition && bestIdx > 1)
		{
			float transitionFactor = (distFromTree - WorldConfig.NeutralZoneRadius)
								   / (neutralEdge - WorldConfig.NeutralZoneRadius);
			if (n1 > transitionFactor)
				return Neutral;
		}

		return Biomes[bestIdx];
	}

	/// <summary>
	/// Get a biome config by type.
	/// </summary>
	public static BiomeConfig GetByType(BiomeType type)
	{
		if (type == BiomeType.Neutral || type == BiomeType.Sky)
			return Neutral;

		foreach (var b in Biomes)
		{
			if (b.Type == type) return b;
		}
		return Neutral;
	}
}
