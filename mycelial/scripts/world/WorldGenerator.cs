namespace Mycorrhiza.World;

using Mycorrhiza.Data;

/// <summary>
/// Generates chunk tile data using noise-based procedural generation.
/// 
/// All methods are THREAD-SAFE — they only read from the NoiseGenerator
/// (which is read-only after construction) and write to the ChunkData
/// (which is owned by the calling context).
/// 
/// Biomes are now selected per-tile using 2D noise patches (not depth strata).
/// The area around the Origin Tree is always the Neutral biome.
/// </summary>
public class WorldGenerator
{
	private readonly NoiseGenerator _noise;
	private readonly OriginTreeGenerator _tree;

	/// <summary>Access the origin tree data (root tip positions, etc.)</summary>
	public OriginTreeGenerator OriginTree => _tree;

	public WorldGenerator(NoiseGenerator noise)
	{
		_noise = noise;
		_tree = new OriginTreeGenerator(noise.Seed);
	}

	/// <summary>
	/// Generate all tile data for a single chunk. Thread-safe.
	/// </summary>
	public ChunkData GenerateChunk(int chunkX, int chunkY)
	{
		var chunk = new ChunkData(chunkX, chunkY);

		int originX = chunk.WorldTileX;
		int originY = chunk.WorldTileY;

		// Sky chunks (above ground)
		if (originY + WorldConfig.ChunkSize < 0)
		{
			chunk.Fill(TileType.Air);
			if (_tree.OverlapsChunk(chunkX, chunkY))
				_tree.StampOnChunk(chunk);
			return chunk;
		}

		// --- Pass 1: Fill solid terrain based on biome patches ---
		FillTerrain(chunk, originX, originY);

		// --- Pass 2: Carve caves ---
		CarveCaves(chunk, originX, originY);

		// --- Pass 3: Place resources ---
		PlaceResources(chunk, originX, originY);

		// --- Pass 4: Scatter accent/decoration ---
		PlaceAccents(chunk, originX, originY);

		// --- Pass 5: Fill low points with liquid ---
		FillLiquids(chunk, originX, originY);

		// --- Pass 6: Stamp the Origin Tree ---
		if (_tree.OverlapsChunk(chunkX, chunkY))
			_tree.StampOnChunk(chunk);

		chunk.IsDirty = true;
		return chunk;
	}

	/// <summary>
	/// Pass 1: Fill every tile with the appropriate solid block for its biome.
	/// Biomes are selected per-tile using 2D noise patches.
	/// </summary>
	private void FillTerrain(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
		{
			int worldY = originY + ly;

			// Above ground = air
			if (worldY < 0)
			{
				for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
					chunk.SetTile(lx, ly, TileType.Air);
				continue;
			}

			// Surface level — rolling hills
			if (worldY == 0)
			{
				for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
				{
					int worldX = originX + lx;
					float surfaceNoise = _noise.TerrainNoise.GetNoise1D(worldX);
					int surfaceOffset = (int)(surfaceNoise * 5);
					chunk.SetTile(lx, ly, worldY >= surfaceOffset ? TileType.Topsoil : TileType.Air);
				}
				continue;
			}

			for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
			{
				int worldX = originX + lx;

				// Get biome from the patch-based noise system
				BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

				// Choose between primary and secondary tile
				float variation = _noise.GetVariation(worldX, worldY);
				TileType tile = (variation > 0.3f) ? biome.SecondaryTile : biome.PrimaryTile;

				chunk.SetTile(lx, ly, tile);
			}
		}
	}

	/// <summary>
	/// Pass 2: Carve caves. Cave density varies by biome patch.
	/// </summary>
	private void CarveCaves(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
		{
			int worldY = originY + ly;
			if (worldY < 3) continue;

			for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
			{
				int worldX = originX + lx;
				BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

				if (_noise.IsCave(worldX, worldY, biome.CaveDensity))
				{
					chunk.SetTile(lx, ly, TileType.Air);
				}
			}
		}
	}

	/// <summary>
	/// Pass 3: Place resource/ore nodes in solid tiles.
	/// </summary>
	private void PlaceResources(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
		{
			int worldY = originY + ly;
			if (worldY < 0) continue;

			for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
			{
				TileType current = chunk.GetTile(lx, ly);
				if (!TileProperties.Is(current, TileFlags.Solid)) continue;

				int worldX = originX + lx;
				BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

				if (_noise.IsOre(worldX, worldY, biome.OreDensity))
				{
					chunk.SetTile(lx, ly, biome.AccentTile);
				}
			}
		}
	}

	/// <summary>
	/// Pass 4: Add accent tiles along cave walls based on biome.
	/// </summary>
	private void PlaceAccents(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 1; ly < WorldConfig.ChunkSize - 1; ly++)
		{
			int worldY = originY + ly;
			if (worldY < 0) continue;

			for (int lx = 1; lx < WorldConfig.ChunkSize - 1; lx++)
			{
				TileType current = chunk.GetTile(lx, ly);
				if (!TileProperties.Is(current, TileFlags.Solid)) continue;

				// Check if adjacent to air (cave wall)
				bool isWall = chunk.GetTile(lx - 1, ly) == TileType.Air
						   || chunk.GetTile(lx + 1, ly) == TileType.Air
						   || chunk.GetTile(lx, ly - 1) == TileType.Air
						   || chunk.GetTile(lx, ly + 1) == TileType.Air;

				if (!isWall) continue;

				int worldX = originX + lx;
				float variation = _noise.GetVariation(worldX, worldY);

				if (variation > 0.6f)
				{
					BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

					if (biome.Type == BiomeType.WetDark && variation > 0.75f)
						chunk.SetTile(lx, ly, TileType.BioluminescentVein);

					else if (biome.Type == BiomeType.BoneStrata && variation > 0.7f)
						chunk.SetTile(lx, ly, TileType.FossilRib);

					else if (biome.Type == BiomeType.Thermovents && variation > 0.8f)
						chunk.SetTile(lx, ly, TileType.ThermalVent);

					else if (biome.Type == BiomeType.MycelialGraveyard && variation > 0.7f)
						chunk.SetTile(lx, ly, TileType.LivingFossil);
				}
			}
		}
	}

	/// <summary>
	/// Pass 5: Fill cave floors with liquid pools.
	/// </summary>
	private void FillLiquids(ChunkData chunk, int originX, int originY)
	{
		for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
		{
			int worldX = originX + lx;

			for (int ly = WorldConfig.ChunkSize - 2; ly >= 1; ly--)
			{
				int worldY = originY + ly;
				if (worldY < 10) continue;

				TileType current = chunk.GetTile(lx, ly);
				TileType below = chunk.GetTile(lx, ly + 1);

				if (current == TileType.Air && TileProperties.Is(below, TileFlags.Solid))
				{
					float variation = _noise.GetVariation(worldX, worldY);
					if (variation < -0.3f)
					{
						BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

						for (int poolY = ly; poolY >= System.Math.Max(0, ly - 3); poolY--)
						{
							if (chunk.GetTile(lx, poolY) != TileType.Air) break;
							chunk.SetTile(lx, poolY, biome.LiquidType);
						}
					}
				}
			}
		}
	}
}
