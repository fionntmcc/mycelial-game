namespace Mycorrhiza.World;

using Mycorrhiza.Data;

/// <summary>
/// Generates chunk tile data. Thread-safe.
///
/// Pipeline:
///   1. Fill terrain with Dirt/Stone based on biome
///   2. Carve caves
///   3. Auto-tile: Dirt adjacent to air becomes grass variants
///   4. Place resources
///   5. Place accents
///   6. Fill liquids
///   7. Stamp Origin Tree
/// </summary>
public class WorldGenerator
{
	private readonly NoiseGenerator _noise;
	private readonly OriginTreeGenerator _tree;

	public OriginTreeGenerator OriginTree => _tree;

	public WorldGenerator(NoiseGenerator noise)
	{
		_noise = noise;
		_tree = new OriginTreeGenerator(noise.Seed);
	}

	public ChunkData GenerateChunk(int chunkX, int chunkY)
	{
		var chunk = new ChunkData(chunkX, chunkY);

		int originX = chunk.WorldTileX;
		int originY = chunk.WorldTileY;

		// Sky chunks
		if (originY + WorldConfig.ChunkSize < 0)
		{
			chunk.Fill(TileType.Air);
			if (_tree.OverlapsChunk(chunkX, chunkY))
				_tree.StampOnChunk(chunk);
			return chunk;
		}

		// --- Pass 1: Fill solid terrain ---
		FillTerrain(chunk, originX, originY);

		// --- Pass 2: Carve caves ---
		CarveCaves(chunk, originX, originY);

		// --- Pass 3: Auto-tile grass edges ---
		AutoTiler.Apply(chunk);

		// --- Pass 4: Place resources ---
		PlaceResources(chunk, originX, originY);

		// --- Pass 5: Place accents ---
		PlaceAccents(chunk, originX, originY);

		// --- Pass 6: Fill liquids ---
		FillLiquids(chunk, originX, originY);

		// --- Pass 7: Stamp Origin Tree ---
		if (_tree.OverlapsChunk(chunkX, chunkY))
			_tree.StampOnChunk(chunk);

		chunk.IsDirty = true;
		return chunk;
	}

	private void FillTerrain(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
		{
			int worldY = originY + ly;

			if (worldY < 0)
			{
				for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
					chunk.SetTile(lx, ly, TileType.Air);
				continue;
			}

			// Surface level
			if (worldY == 0)
			{
				for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
				{
					int worldX = originX + lx;
					float surfaceNoise = _noise.TerrainNoise.GetNoise1D(worldX);
					int surfaceOffset = (int)(surfaceNoise * 5);
					chunk.SetTile(lx, ly, worldY >= surfaceOffset ? TileType.Dirt : TileType.Air);
				}
				continue;
			}

			for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
			{
				int worldX = originX + lx;
				BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

				// Choose tile based on biome
				float variation = _noise.GetVariation(worldX, worldY);
				TileType tile = (variation > 0.3f) ? biome.SecondaryTile : biome.PrimaryTile;

				chunk.SetTile(lx, ly, tile);
			}
		}
	}

	private void CarveCaves(ChunkData chunk, int originX, int originY)
	{
		for (int ly = 0; ly < WorldConfig.ChunkSize; ly++)
		{
			int worldY = originY + ly;
			if (worldY < WorldConfig.CaveMinDepth) continue;

			for (int lx = 0; lx < WorldConfig.ChunkSize; lx++)
			{
				int worldX = originX + lx;
				BiomeConfig biome = _noise.GetBiomeAt(worldX, worldY);

				if (_noise.IsCave(worldX, worldY, biome.CaveDensity))
					chunk.SetTile(lx, ly, TileType.Air);
			}
		}
	}

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
					chunk.SetTile(lx, ly, biome.AccentTile);
			}
		}
	}

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
