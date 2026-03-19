namespace Mycorrhiza.World;

using System;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Generates the Origin Tree — a massive, menacing tree at the center of the world
/// with procedurally generated roots that burrow deep into the earth.
/// 
/// The tree is the player's starting point. Its roots are the first mycelium network.
/// Root tips glow faintly, marking where the fungal infection begins to spread.
///
/// This generator works per-chunk: call StampOnChunk() during chunk generation
/// for any chunk that might overlap the tree's footprint. It writes tree tiles
/// over existing terrain tiles.
///
/// The root system uses a seeded random branching algorithm:
///   - Major roots extend from the trunk base at spread angles
///   - Each root grows downward with random horizontal drift
///   - Roots branch into thinner sub-roots at random intervals
///   - Roots thin as they branch: ThickRoot → MediumRoot → ThinRoot → RootTip
///   - Root tips are the endpoints where the player's mycelium begins
/// </summary>
public class OriginTreeGenerator
{
    /// <summary>Represents a single point in the root network.</summary>
    private struct RootSegment
    {
        public int X;
        public int Y;
        public TileType TileType;
        public int Thickness; // Radius in tiles (for thick roots)
    }

    // Pre-computed root segments (generated once, stamped per chunk)
    private readonly List<RootSegment> _rootSegments = new();
    private readonly List<RootSegment> _trunkSegments = new();
    private readonly List<RootSegment> _canopySegments = new();

    /// <summary>World-space positions of all root tips — where mycelium begins spreading.</summary>
    public List<(int X, int Y)> RootTipPositions { get; } = new();

    // Bounding box for quick chunk overlap test
    private int _minX, _maxX, _minY, _maxY;

    private readonly int _treeBaseX;
    private readonly Random _rng;

    public OriginTreeGenerator(int seed)
    {
        _treeBaseX = WorldConfig.TreeWorldX;
		_rng = new Random(seed + 9999); // Offset seed so tree doesn't correlate with terrain

		GenerateTrunk();
		GenerateCanopy();
		GenerateRoots();

		// Calculate bounding box
		_minX = int.MaxValue;
		_maxX = int.MinValue;
		_minY = int.MaxValue;
		_maxY = int.MinValue;

		foreach (var seg in _trunkSegments) ExpandBounds(seg);
		foreach (var seg in _canopySegments) ExpandBounds(seg);
		foreach (var seg in _rootSegments) ExpandBounds(seg);

		// Add padding for thickness
		_minX -= 4;
		_maxX += 4;
		_minY -= 4;
		_maxY += 4;
	}

	private void ExpandBounds(RootSegment seg)
	{
		int pad = seg.Thickness;
		if (seg.X - pad < _minX) _minX = seg.X - pad;
		if (seg.X + pad > _maxX) _maxX = seg.X + pad;
		if (seg.Y - pad < _minY) _minY = seg.Y - pad;
		if (seg.Y + pad > _maxY) _maxY = seg.Y + pad;
	}

	/// <summary>
	/// Check if a chunk could possibly contain tree tiles.
	/// Fast rejection test — call this before StampOnChunk.
	/// </summary>
	public bool OverlapsChunk(int chunkX, int chunkY)
	{
		int chunkWorldMinX = chunkX * WorldConfig.ChunkSize;
		int chunkWorldMinY = chunkY * WorldConfig.ChunkSize;
		int chunkWorldMaxX = chunkWorldMinX + WorldConfig.ChunkSize;
		int chunkWorldMaxY = chunkWorldMinY + WorldConfig.ChunkSize;

		return chunkWorldMaxX >= _minX && chunkWorldMinX <= _maxX
			&& chunkWorldMaxY >= _minY && chunkWorldMinY <= _maxY;
	}

	/// <summary>
	/// Stamp tree tiles onto a chunk. Overwrites existing terrain where the tree exists.
	/// Call this after terrain generation for chunks that pass OverlapsChunk.
	/// </summary>
	public void StampOnChunk(ChunkData chunk)
	{
		int originX = chunk.WorldTileX;
		int originY = chunk.WorldTileY;

		// Stamp trunk
		StampSegments(_trunkSegments, chunk, originX, originY);

		// Stamp roots (before canopy so roots render behind)
		StampSegments(_rootSegments, chunk, originX, originY);

		// Stamp canopy last (on top)
		StampSegments(_canopySegments, chunk, originX, originY);
	}

	private void StampSegments(List<RootSegment> segments, ChunkData chunk, int originX, int originY)
	{
		foreach (var seg in segments)
		{
			if (seg.Thickness <= 1)
			{
				// Single tile
				int lx = seg.X - originX;
				int ly = seg.Y - originY;
				if (ChunkData.InBounds(lx, ly))
				{
					chunk.SetTile(lx, ly, seg.TileType);
				}
			}
			else
			{
				// Thick segment — fill a circle of tiles
				for (int dy = -seg.Thickness; dy <= seg.Thickness; dy++)
				{
					for (int dx = -seg.Thickness; dx <= seg.Thickness; dx++)
					{
						if (dx * dx + dy * dy <= seg.Thickness * seg.Thickness)
						{
							int lx = seg.X + dx - originX;
							int ly = seg.Y + dy - originY;
							if (ChunkData.InBounds(lx, ly))
							{
								chunk.SetTile(lx, ly, seg.TileType);
							}
						}
					}
				}
			}
		}
	}

	// =========================================================================
	//  TRUNK GENERATION
	// =========================================================================

	private void GenerateTrunk()
	{
		int baseX = _treeBaseX;
		int surfaceY = 0; // Surface level
		int halfWidth = WorldConfig.TreeTrunkWidth / 2;
		int trunkTop = surfaceY - WorldConfig.TreeTrunkHeight;

		// Build trunk from base to top, getting slightly narrower
		for (int y = surfaceY + 2; y >= trunkTop; y--)
		{
			// Trunk narrows toward the top
			float progress = (float)(surfaceY - y) / WorldConfig.TreeTrunkHeight;
			int width = Math.Max(1, (int)(halfWidth * (1.0f - progress * 0.4f)));

			// Add some gnarly wobble to the trunk edges
			int wobbleL = (_rng.Next(3) == 0) ? 1 : 0;
			int wobbleR = (_rng.Next(3) == 0) ? 1 : 0;

			for (int dx = -(width + wobbleL); dx <= width + wobbleR; dx++)
			{
				bool isEdge = Math.Abs(dx) >= width;
				TileType tile;

				if (isEdge)
					tile = TileType.Bark;
				else if (y > surfaceY - 4) // Bottom of trunk — dead/infected
					tile = TileType.DeadHeartwood;
				else
					tile = TileType.Heartwood;

				_trunkSegments.Add(new RootSegment
				{
					X = baseX + dx,
					Y = y,
					TileType = tile,
					Thickness = 1
				});
			}
		}

		// Gnarled base — trunk flares out where it meets the ground
		for (int dx = -(halfWidth + 3); dx <= halfWidth + 3; dx++)
		{
			for (int dy = 0; dy <= 2; dy++)
			{
				float dist = Math.Abs(dx) / (float)(halfWidth + 3);
				if (dist < 1.0f - dy * 0.3f)
				{
					bool isEdge = Math.Abs(dx) >= halfWidth + 2;
					_trunkSegments.Add(new RootSegment
					{
						X = baseX + dx,
						Y = surfaceY + dy,
						TileType = isEdge ? TileType.Bark : TileType.DeadHeartwood,
						Thickness = 1
					});
				}
			}
		}

		// Major branches extending from upper trunk
		GenerateBranch(baseX - halfWidth, trunkTop + 3, -1, -0.6f, 12);
		GenerateBranch(baseX + halfWidth, trunkTop + 3, 1, -0.6f, 12);
		GenerateBranch(baseX - halfWidth + 1, trunkTop + 6, -1, -0.3f, 10);
		GenerateBranch(baseX + halfWidth - 1, trunkTop + 6, 1, -0.3f, 10);
		GenerateBranch(baseX, trunkTop, 0, -1f, 6); // Straight up from top
	}

	private void GenerateBranch(int startX, int startY, int dirX, float dirY, int length)
	{
		float x = startX;
		float y = startY;

		for (int i = 0; i < length; i++)
		{
			x += dirX + (_rng.NextSingle() - 0.5f) * 0.5f;
			y += dirY + (_rng.NextSingle() - 0.5f) * 0.3f;

			_trunkSegments.Add(new RootSegment
			{
				X = (int)x,
				Y = (int)y,
				TileType = TileType.BranchWood,
				Thickness = Math.Max(1, (length - i) / 5)
			});
		}
	}

	// =========================================================================
	//  CANOPY GENERATION
	// =========================================================================

	private void GenerateCanopy()
	{
		int baseX = _treeBaseX;
		int canopyCenterY = -WorldConfig.TreeTrunkHeight - WorldConfig.TreeCanopyHeight / 2;
		int radiusX = WorldConfig.TreeCanopyRadius;
		int radiusY = WorldConfig.TreeCanopyHeight;

		// Generate a noisy elliptical canopy
		for (int dy = -radiusY; dy <= radiusY; dy++)
		{
			for (int dx = -radiusX; dx <= radiusX; dx++)
			{
				// Ellipse test with noise
				float ex = (float)dx / radiusX;
				float ey = (float)dy / radiusY;
				float dist = ex * ex + ey * ey;

				// Add noise to the edge for an organic shape
				float noise = (_rng.NextSingle() - 0.5f) * 0.3f;
				float threshold = 0.85f + noise;

				if (dist < threshold)
				{
					// Outer ring = dead canopy (sparse, dying)
					// Inner = living canopy (dense)
					bool isDying = dist > 0.5f + (_rng.NextSingle() * 0.2f);

					// Random holes in the canopy for organic feel
					if (_rng.NextSingle() < 0.08f) continue;

					// More holes in dead areas
					if (isDying && _rng.NextSingle() < 0.25f) continue;

					_canopySegments.Add(new RootSegment
					{
						X = baseX + dx,
						Y = canopyCenterY + dy,
						TileType = isDying ? TileType.DeadCanopy : TileType.Canopy,
						Thickness = 1
					});
				}
			}
		}
	}

	// =========================================================================
	//  ROOT GENERATION — Recursive branching algorithm
	// =========================================================================

	private void GenerateRoots()
	{
		int baseX = _treeBaseX;
		int baseY = 1; // Just below surface

		// Generate major root branches spreading outward from the trunk base
		int numBranches = WorldConfig.TreeRootBranches;

		for (int i = 0; i < numBranches; i++)
		{
			// Spread roots evenly-ish across the bottom of the trunk
			float angle = ((float)i / numBranches) * MathF.PI; // 0 to PI (left to right, downward)
			angle += (_rng.NextSingle() - 0.5f) * 0.4f; // Randomize angle slightly

			// Convert angle to direction — roots go mostly DOWN with some horizontal spread
			float dirX = MathF.Cos(angle) * 0.7f;
			float dirY = 0.6f + _rng.NextSingle() * 0.4f; // Always going down

			int startX = baseX + (int)((i - numBranches / 2.0f) * 2);

			GrowRoot(startX, baseY, dirX, dirY, TileType.ThickRoot, 3, 0);
		}

		// One deep taproot straight down from center
		GrowRoot(baseX, baseY + 1, 0, 1.0f, TileType.ThickRoot, 3, 0);
	}

	/// <summary>
	/// Recursively grow a root from a starting point.
	/// </summary>
	/// <param name="startX">World tile X start</param>
	/// <param name="startY">World tile Y start</param>
	/// <param name="dirX">Horizontal drift per step (-1 to 1)</param>
	/// <param name="dirY">Vertical drift per step (positive = down)</param>
	/// <param name="tileType">Current root tile type (thins with depth)</param>
	/// <param name="thickness">Drawing thickness in tiles</param>
	/// <param name="depth">Recursion depth (limits sub-branching)</param>
	private void GrowRoot(float startX, float startY, float dirX, float dirY,
						  TileType tileType, int thickness, int depth)
	{
		// Max recursion depth — prevents infinite branching
		if (depth > 4) return;

		float x = startX;
		float y = startY;

		// How long this root segment grows before stopping or branching
		int segmentLength = WorldConfig.TreeRootMinBranch +
			_rng.Next(WorldConfig.TreeRootMaxBranch - WorldConfig.TreeRootMinBranch);

		// Roots get shorter as they branch
		segmentLength = Math.Max(5, segmentLength - depth * 3);

		for (int step = 0; step < segmentLength; step++)
		{
			// Stop if we've gone too deep
            if (y > WorldConfig.TreeRootDepth) break;

            // Place root segment
            _rootSegments.Add(new RootSegment
            {
                X = (int)x,
                Y = (int)y,
                TileType = tileType,
                Thickness = thickness
            });

            // Advance position with drift and randomness
            x += dirX + (_rng.NextSingle() - 0.5f) * 0.8f;
            y += dirY + _rng.NextSingle() * 0.3f;

            // Roots tend to drift more horizontal as they grow (seeking)
            dirX += (_rng.NextSingle() - 0.5f) * 0.15f;
            dirX = Math.Clamp(dirX, -1.5f, 1.5f);

            // Occasionally wobble direction for organic feel
            if (_rng.Next(6) == 0)
                dirX = -dirX * 0.5f;

            // Sub-branch at random intervals
            if (step > 5 && _rng.Next(8) == 0 && depth < 4)
            {
                // Branch in a different direction
                float branchDirX = dirX + (_rng.NextSingle() - 0.5f) * 2.0f;
                float branchDirY = dirY + _rng.NextSingle() * 0.3f;

                // Thinner tile type for sub-branches
                TileType subType = GetThinnerRoot(tileType);
                int subThickness = Math.Max(1, thickness - 1);

                GrowRoot(x, y, branchDirX, branchDirY, subType, subThickness, depth + 1);
            }
        }

        // Place root tip at the end (where mycelium starts)
        TileType tipType = (depth >= 2) ? TileType.RootTip : GetThinnerRoot(tileType);

        // Fan out a few thin roots at the tip
        if (depth < 3)
        {
            for (int i = 0; i < 2 + _rng.Next(3); i++)
            {
                float tipDirX = dirX + (_rng.NextSingle() - 0.5f) * 2.0f;
                float tipDirY = dirY + _rng.NextSingle() * 0.5f;
                TileType fanType = GetThinnerRoot(tipType);
                GrowRoot(x, y, tipDirX, tipDirY, fanType, 1, depth + 2);
            }
        }

        // Final root tip marker
        _rootSegments.Add(new RootSegment
        {
            X = (int)x,
            Y = (int)y,
            TileType = TileType.RootTip,
            Thickness = 1
        });
        RootTipPositions.Add(((int)x, (int)y));
    }

    /// <summary>Get the next thinner root type.</summary>
    private static TileType GetThinnerRoot(TileType current)
    {
        return current switch
        {
            TileType.ThickRoot => TileType.MediumRoot,
            TileType.MediumRoot => TileType.ThinRoot,
            TileType.ThinRoot => TileType.RootTip,
            _ => TileType.RootTip
        };
    }
}
