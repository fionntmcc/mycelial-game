namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Light-propagation fog of war.
///
/// Instead of radius checks and line-of-sight raycasts, this system models
/// light as a value that floods outward from the tendril, losing strength
/// as it passes through tiles. Each tile type absorbs a different amount:
///
///   - Air:       barely absorbs — light floods through caves naturally
///   - Dirt/soil: moderate absorption — light bleeds a few tiles into earth
///   - Stone:     heavy absorption — light barely penetrates
///   - Mycelium:  very low absorption — your network is translucent
///   - Water:     moderate absorption with a faint glow
///
/// Light sources:
///   - Tendril head:  strong emitter (full light)
///   - Tendril body:  medium emitter (fades along trail length)
///   - Claimed tiles: very faint ambient glow
///   - Emissive tiles: bioluminescent veins, lava, etc. emit their own light
///
/// The result: when you burrow near a cave, light naturally spills through
/// the opening and illuminates the interior. No special cases needed. Deep
/// caves far from the tendril stay dark. Your trail behind you glows faintly.
///
/// ALGORITHM:
///   1. Allocate a flat light-level array covering the viewport
///   2. Seed all light sources into the array
///   3. BFS flood-fill: each tile propagates light to neighbors minus absorption
///   4. Draw fog rects with alpha = 1 - lightLevel
///
/// PERFORMANCE:
///   The BFS is bounded by the viewport size plus padding. With a typical
///   viewport of ~80x50 tiles, the array is ~4000 entries. The BFS touches
///   each tile at most once. This is faster than the old system's per-tile
///   radius checks + LOS raycasts.
///
/// SETUP: Same as before — assign TendrilController, Camera, ChunkManager.
/// </summary>
public partial class FogOfWar : Node2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath CameraPath { get; set; }
	[Export] public NodePath ChunkManagerPath { get; set; }

	// =========================================================================
	//  LIGHT SOURCE STRENGTHS
	// =========================================================================

	[ExportGroup("Light Sources")]

	/// <summary>Light emitted by the tendril head (max value).</summary>
	[Export(PropertyHint.Range, "0.5,2.0,0.05")] public float HeadLightStrength = 1.0f;

	/// <summary>Light emitted by tendril body at the base (oldest point).</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float BodyLightBase = 0.15f;

	/// <summary>Light emitted by tendril body near the head (newest point).</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float BodyLightNearHead = 0.5f;

	/// <summary>Faint ambient glow from claimed territory tiles.</summary>
	[Export(PropertyHint.Range, "0.0,0.5,0.01")] public float TerritoryGlow = 0.08f;

	/// <summary>Light emitted by emissive tiles (bioluminescent veins, lava, etc.).</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float EmissiveTileLight = 0.4f;

	/// <summary>How many spline body points to sample as light sources.</summary>
	[Export(PropertyHint.Range, "2,60,1")] public int BodyLightSamples = 30;

	/// <summary>Sample every Nth rendered spline point.</summary>
	[Export(PropertyHint.Range, "1,12,1")] public int BodySampleInterval = 5;

	// =========================================================================
	//  LIGHT ABSORPTION PER TILE TYPE
	// =========================================================================

	[ExportGroup("Light Absorption")]

	/// <summary>How much light air absorbs per tile (very low = caves flood with light).</summary>
	[Export(PropertyHint.Range, "0.005,0.1,0.005")] public float AirAbsorption = 0.025f;

	/// <summary>How much light soft soil absorbs (dirt, leaf, sand).</summary>
	[Export(PropertyHint.Range, "0.05,0.4,0.01")] public float SoilAbsorption = 0.13f;

	/// <summary>How much light dense material absorbs (stone, clay, gravel).</summary>
	[Export(PropertyHint.Range, "0.1,0.6,0.01")] public float StoneAbsorption = 0.30f;

	/// <summary>How much light player-owned mycelium absorbs (very low = network glows).</summary>
	[Export(PropertyHint.Range, "0.005,0.15,0.005")] public float MyceliumAbsorption = 0.02f;

	/// <summary>How much light water/liquid absorbs.</summary>
	[Export(PropertyHint.Range, "0.02,0.3,0.01")] public float WaterAbsorption = 0.06f;

	/// <summary>How much light wood/roots absorb.</summary>
	[Export(PropertyHint.Range, "0.05,0.3,0.01")] public float WoodAbsorption = 0.18f;

	// =========================================================================
	//  FOG APPEARANCE
	// =========================================================================

	[ExportGroup("Fog Appearance")]
	[Export] public Color FogColor = new(0f, 0f, 0f, 1f);

	/// <summary>Light level below which fog is fully opaque.</summary>
	[Export(PropertyHint.Range, "0.0,0.1,0.005")] public float DarknessThreshold = 0.01f;

	/// <summary>Extra tiles beyond viewport to compute light for (prevents pop-in).</summary>
	[Export(PropertyHint.Range, "2,12,1")] public int ViewportPadding = 4;

	// =========================================================================
	//  INTERNAL STATE
	// =========================================================================

	private TendrilController _tendril;
	private Camera2D _camera;
	private ChunkManager _chunkManager;
	private TendrilSplineRenderer _splineRenderer;

	// Light level grid — reused each frame to avoid allocation
	private float[] _lightGrid;
	private int _gridWidth;
	private int _gridHeight;
	private int _gridOriginX; // World tile coord of grid[0,0]
	private int _gridOriginY;

	// BFS queue — reused each frame
	private readonly Queue<(int x, int y, float light)> _bfsQueue = new();

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);
		if (CameraPath != null)
			_camera = GetNode<Camera2D>(CameraPath);
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);

		if (_tendril == null || _camera == null || _chunkManager == null)
		{
			GD.PrintErr("FogOfWar: Missing TendrilController, Camera2D, or ChunkManager.");
			SetProcess(false);
			return;
		}

		ZAsRelative = false;
		ZIndex = 5000;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_splineRenderer ??= _tendril.GetNodeOrNull<TendrilSplineRenderer>("TendrilSplineRenderer");
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_tendril == null || _camera == null || _chunkManager == null)
			return;

		// --- 1. Calculate viewport tile bounds ---
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float zoom = Mathf.Max(0.0001f, _camera.Zoom.X);
		Vector2 cameraPos = _camera.GlobalPosition;

		float halfW = (viewportSize.X / zoom) * 0.5f;
		float halfH = (viewportSize.Y / zoom) * 0.5f;

		int minX = Mathf.FloorToInt((cameraPos.X - halfW) / WorldConfig.TileSize) - ViewportPadding;
		int minY = Mathf.FloorToInt((cameraPos.Y - halfH) / WorldConfig.TileSize) - ViewportPadding;
		int maxX = Mathf.CeilToInt((cameraPos.X + halfW) / WorldConfig.TileSize) + ViewportPadding;
		int maxY = Mathf.CeilToInt((cameraPos.Y + halfH) / WorldConfig.TileSize) + ViewportPadding;

		int width = maxX - minX + 1;
		int height = maxY - minY + 1;

		// --- 2. Allocate/resize light grid ---
		int gridSize = width * height;
		if (_lightGrid == null || _lightGrid.Length < gridSize)
			_lightGrid = new float[gridSize];
		else
			System.Array.Clear(_lightGrid, 0, gridSize);

		_gridWidth = width;
		_gridHeight = height;
		_gridOriginX = minX;
		_gridOriginY = minY;

		// --- 3. Seed light sources & propagate ---
		_bfsQueue.Clear();

		SeedHeadLight();
		SeedBodyLight();
		SeedTerritoryGlow(minX, minY, maxX, maxY);
		SeedEmissiveTiles(minX, minY, maxX, maxY);

		PropagateLightBFS();

		// --- 4. Draw fog based on light levels ---
		DrawFogFromLightGrid(minX, minY, maxX, maxY);
	}

	// =========================================================================
	//  LIGHT SEEDING
	// =========================================================================

	/// <summary>Seed the tendril head as the primary light source.</summary>
	private void SeedHeadLight()
	{
		int hx = _tendril.HeadX;
		int hy = _tendril.HeadY;
		SeedLight(hx, hy, HeadLightStrength);
	}

	/// <summary>Seed light along the tendril spline body, fading from head to base.</summary>
	private void SeedBodyLight()
	{
		if (_splineRenderer == null) return;

		var line = _splineRenderer.GetNodeOrNull<Line2D>("Line2D");
		if (line == null || line.GetPointCount() < 2) return;

		int pointCount = line.GetPointCount();
		int interval = System.Math.Max(1, BodySampleInterval);
		int sampled = 0;

		for (int i = pointCount - 1; i >= 0 && sampled < BodyLightSamples; i -= interval)
		{
			// t=0 at head (newest), t=1 at base (oldest)
			float t = 1f - ((float)i / (pointCount - 1));
			float strength = Mathf.Lerp(BodyLightNearHead, BodyLightBase, t);

			if (strength < DarknessThreshold) continue;

			Vector2 point = line.GetPointPosition(i);
			Vector2 worldPoint = _splineRenderer.GlobalTransform * point;

			int tx = Mathf.FloorToInt(worldPoint.X / WorldConfig.TileSize);
			int ty = Mathf.FloorToInt(worldPoint.Y / WorldConfig.TileSize);

			SeedLight(tx, ty, strength);
			sampled++;
		}
	}

	/// <summary>Seed faint glow from all claimed territory tiles visible in viewport.</summary>
	private void SeedTerritoryGlow(int minX, int minY, int maxX, int maxY)
	{
		if (TerritoryGlow < DarknessThreshold) return;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				if (_tendril.IsOnTerritory(x, y))
					SeedLight(x, y, TerritoryGlow);
			}
		}
	}

	/// <summary>Seed light from emissive tiles (bioluminescent veins, lava, etc.).</summary>
	private void SeedEmissiveTiles(int minX, int minY, int maxX, int maxY)
	{
		if (EmissiveTileLight < DarknessThreshold) return;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				TileType tile = _chunkManager.GetTileAt(x, y);
				if (TileProperties.Is(tile, TileFlags.Emissive))
				{
					// Different emissive tiles could have different strengths
					float strength = tile switch
					{
						TileType.Lava => EmissiveTileLight * 1.2f,
						TileType.BioluminescentVein => EmissiveTileLight,
						TileType.ThermalVent => EmissiveTileLight * 0.8f,
						TileType.LivingFossil => EmissiveTileLight * 0.5f,
						TileType.MyceliumCore => EmissiveTileLight * 0.6f,
						TileType.RootTip => EmissiveTileLight * 0.3f,
						_ => EmissiveTileLight * 0.4f,
					};

					SeedLight(x, y, strength);
				}
			}
		}
	}

	/// <summary>
	/// Seed a single light source. If the tile already has more light, this is a no-op.
	/// Adds to BFS queue for propagation.
	/// </summary>
	private void SeedLight(int worldX, int worldY, float strength)
	{
		int lx = worldX - _gridOriginX;
		int ly = worldY - _gridOriginY;

		if (lx < 0 || lx >= _gridWidth || ly < 0 || ly >= _gridHeight)
			return;

		int idx = ly * _gridWidth + lx;

		// Only seed if this is brighter than existing light
		if (strength <= _lightGrid[idx])
			return;

		_lightGrid[idx] = strength;
		_bfsQueue.Enqueue((worldX, worldY, strength));
	}

	// =========================================================================
	//  LIGHT PROPAGATION — BFS Flood Fill
	// =========================================================================

	// Cardinal neighbor offsets
	private static readonly (int dx, int dy)[] Neighbors = { (-1, 0), (1, 0), (0, -1), (0, 1) };

	/// <summary>
	/// BFS propagation: each tile passes light to its neighbors, minus the
	/// neighbor's absorption cost. Light only flows if it would increase
	/// the neighbor's current level.
	/// </summary>
	private void PropagateLightBFS()
	{
		while (_bfsQueue.Count > 0)
		{
			var (wx, wy, currentLight) = _bfsQueue.Dequeue();

			foreach (var (dx, dy) in Neighbors)
			{
				int nx = wx + dx;
				int ny = wy + dy;

				int lx = nx - _gridOriginX;
				int ly = ny - _gridOriginY;

				if (lx < 0 || lx >= _gridWidth || ly < 0 || ly >= _gridHeight)
					continue;

				// How much light this neighbor tile absorbs
				float absorption = GetTileAbsorption(nx, ny);

				float newLight = currentLight - absorption;
				if (newLight <= DarknessThreshold)
					continue;

				int idx = ly * _gridWidth + lx;

				// Only propagate if we'd increase this tile's light
				if (newLight <= _lightGrid[idx])
					continue;

				_lightGrid[idx] = newLight;
				_bfsQueue.Enqueue((nx, ny, newLight));
			}
		}
	}

	// =========================================================================
	//  TILE ABSORPTION — How much light each tile type consumes
	// =========================================================================

	/// <summary>
	/// Get how much light a tile absorbs when light passes through it.
	/// Low values = translucent (air, mycelium). High values = opaque (stone).
	/// </summary>
	private float GetTileAbsorption(int worldX, int worldY)
	{
		TileType tile = _chunkManager.GetTileAt(worldX, worldY);

		// Air — barely absorbs. Caves flood with light.
		if (tile == TileType.Air)
			return AirAbsorption;

		// Player-owned mycelium — your network is almost transparent to light
		if (TileProperties.Is(tile, TileFlags.PlayerOwned))
			return MyceliumAbsorption;

		// Liquids
		if (TileProperties.Is(tile, TileFlags.Liquid))
			return WaterAbsorption;

		// Specific tile types
		return tile switch
		{
			// Soft soil — moderate
			TileType.Dirt => SoilAbsorption,
			TileType.Sand => SoilAbsorption,
			TileType.Leaf => SoilAbsorption * 0.7f,
			TileType.InfectedDirt => SoilAbsorption * 0.8f,

			// Dense material — heavy
			TileType.Stone => StoneAbsorption,
			TileType.Clay => StoneAbsorption * 0.85f,
			TileType.Gravel => StoneAbsorption * 0.75f,
			TileType.Obsidian => StoneAbsorption * 1.3f,
			TileType.Basalt => StoneAbsorption * 1.1f,

			// Wood/roots — moderate-heavy
			TileType.Wood => WoodAbsorption,
			TileType.Roots => WoodAbsorption * 0.9f,
			TileType.RootTip => WoodAbsorption * 0.7f,

			// Biome accents — varies
			TileType.BioluminescentVein => AirAbsorption, // Glows, barely absorbs
			TileType.BoneMarrow => SoilAbsorption * 0.6f,
			TileType.FossilRib => StoneAbsorption * 0.7f,
			TileType.Boneite => StoneAbsorption * 0.8f,
			TileType.CrystalGrotte => SoilAbsorption * 0.5f, // Semi-transparent crystals
			TileType.PetrifiedMycelium => SoilAbsorption * 0.4f,
			TileType.LivingFossil => SoilAbsorption * 0.3f,
			TileType.AncientSporeNode => SoilAbsorption * 0.5f,
			TileType.ThermalVent => AirAbsorption * 2f,

			// Grass (solid but organic) — moderate
			_ when TileProperties.IsGrass(tile) => SoilAbsorption * 1.1f,
			_ when TileProperties.IsInfectedGrass(tile) => MyceliumAbsorption * 1.5f,

			// Default solid — treat as soil
			_ => TileProperties.Is(tile, TileFlags.Solid) ? SoilAbsorption : AirAbsorption,
		};
	}

	// =========================================================================
	//  FOG RENDERING
	// =========================================================================

	/// <summary>
	/// Draw fog rectangles based on computed light levels.
	/// Fog alpha = 1 - lightLevel, clamped to [0, 1].
	/// Tiles at or above 1.0 light are fully visible (no fog drawn).
	/// </summary>
	private void DrawFogFromLightGrid(int minX, int minY, int maxX, int maxY)
	{
		int ts = WorldConfig.TileSize;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				int lx = x - _gridOriginX;
				int ly = y - _gridOriginY;

				if (lx < 0 || lx >= _gridWidth || ly < 0 || ly >= _gridHeight)
					continue;

				float light = _lightGrid[ly * _gridWidth + lx];

				// Fully lit — skip drawing
				if (light >= 1.0f)
					continue;

				// Below threshold — fully dark
				float alpha;
				if (light <= DarknessThreshold)
				{
					alpha = 1.0f;
				}
				else
				{
					// Map light (threshold..1.0) to alpha (1.0..0.0)
					alpha = 1.0f - Mathf.Clamp(light, 0f, 1f);
				}

				DrawRect(
					new Rect2(x * ts, y * ts, ts, ts),
					new Color(FogColor.R, FogColor.G, FogColor.B, alpha),
					true
				);
			}
		}
	}
}
