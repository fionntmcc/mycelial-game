namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Fog of war driven by light propagation at sub-cell resolution.
///
/// The tendril body emits light that propagates through terrain with absorption.
/// The head stamps a clean circle of light after propagation so it stays round.
/// When the head touches an air pocket, raycast sight lines flood caves with light.
///
/// SETUP:
///   1. Change FogOfWar node from Node2D to Sprite2D
///   2. Attach this script
///   3. Assign TendrilControllerPath, CameraPath, ChunkManagerPath
/// </summary>
public partial class FogOfWar : Sprite2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath CameraPath { get; set; }
	[Export] public NodePath ChunkManagerPath { get; set; }

	// --- Light Emission ---
	[Export] public float CoreLightEmission = 1.0f;
	[Export] public float FreshLightEmission = 0.55f;
	[Export] public float TrailLightEmission = 0.25f;
	[Export] public float RootLightEmission = 0.15f;

	// --- Head Light Circle ---
	/// <summary>Radius of the clean circular light around the head, in sub-cells.</summary>
	[Export] public int HeadLightRadius = 12;

	// --- Cave Line-of-Sight ---
	/// <summary>How far sight extends into caves, in terrain tiles.</summary>
	[Export] public int CaveSightRadius = 28;

	/// <summary>Half-angle of the vision cone in degrees. 180 = full circle.</summary>
	[Export] public float ConeHalfAngleDeg = 75f;

	/// <summary>Radius of each ray stamp in sub-cells. Fills gaps between rays.</summary>
	[Export] public int RayStampRadius = 2;

	/// <summary>How close the head must be to air to trigger cave sight (terrain tiles).</summary>
	[Export] public int AirPocketContactRadius = 1;

	/// <summary>Number of rays to cast. More = smoother coverage. 64–128 is good.</summary>
	[Export] public int SightRayCount = 96;

	/// <summary>Light level stamped along cave sight lines (0–1).</summary>
	[Export] public float CaveSightLight = 0.85f;

	// --- Light Propagation ---
	[Export] public int PropagationPasses = 15;
	[Export] public float PropagationFactor = 0.92f;

	// --- Terrain Absorption ---
	[Export] public float AbsorptionAir = 0.01f;
	[Export] public float AbsorptionOrganic = 0.06f;
	[Export] public float AbsorptionHard = 0.15f;
	[Export] public float AbsorptionSolid = 0.45f;
	[Export] public float AbsorptionLiquid = 0.10f;

	// --- Fog Appearance ---
	[Export] public Color FogColor = new Color(0f, 0f, 0f, 1f);
	[Export(PropertyHint.Range, "0,1,0.01")] public float MaxFogAlpha = 1.0f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float LightCutoff = 0.02f;
	[Export] public int Padding = 4;

	private TendrilController _tendril;
	private Camera2D _camera;
	private ChunkManager _chunkManager;

	private Image _image;
	private ImageTexture _texture;
	private int _imgWidth;
	private int _imgHeight;
	private float _lastZoom;

	private float[] _lightMap;
	private float[] _lightMapBack;
	private float[] _absorptionMap;

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

		TextureFilter = TextureFilterEnum.Nearest;
		Centered = false;
		int cellSize = WorldConfig.SubCellSize;
		Scale = new Vector2(cellSize, cellSize);
		ZAsRelative = false;
		ZIndex = 5000;

		_lastZoom = _camera.Zoom.X;
		ReallocateBuffers();
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _camera == null || _chunkManager == null) return;

		float currentZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(currentZoom, _lastZoom, 0.001f))
		{
			_lastZoom = currentZoom;
			ReallocateBuffers();
		}

		PaintFrame();
	}

	private void ReallocateBuffers()
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		_imgWidth = (int)Mathf.Ceil(viewportSize.X / zoom / cellSize) + Padding * 2;
		_imgHeight = (int)Mathf.Ceil(viewportSize.Y / zoom / cellSize) + Padding * 2;

		int totalCells = _imgWidth * _imgHeight;
		_lightMap = new float[totalCells];
		_lightMapBack = new float[totalCells];
		_absorptionMap = new float[totalCells];

		_image = Image.CreateEmpty(_imgWidth, _imgHeight, false, Image.Format.Rgba8);

		if (_texture == null)
		{
			_texture = ImageTexture.CreateFromImage(_image);
			Texture = _texture;
		}
		else
		{
			_texture.Update(_image);
		}
	}

	private void PaintFrame()
	{
		int cellSize = WorldConfig.SubCellSize;
		int scale = WorldConfig.SubGridScale;
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 cameraPos = _camera.GlobalPosition;
		float zoom = _camera.Zoom.X;

		float halfW = (viewportSize.X / zoom) / 2f;
		float halfH = (viewportSize.Y / zoom) / 2f;

		int originSubX = (int)Mathf.Floor((cameraPos.X - halfW) / cellSize) - Padding;
		int originSubY = (int)Mathf.Floor((cameraPos.Y - halfH) / cellSize) - Padding;

		int totalCells = _imgWidth * _imgHeight;

		// --- Step 1: Clear light map ---
		System.Array.Clear(_lightMap, 0, totalCells);

		// --- Step 2: Build absorption map from terrain ---
		for (int py = 0; py < _imgHeight; py++)
		{
			for (int px = 0; px < _imgWidth; px++)
			{
				int subX = originSubX + px;
				int subY = originSubY + py;
				int tileX = FloorDiv(subX, scale);
				int tileY = FloorDiv(subY, scale);

				TileType tile = _chunkManager.GetTileAt(tileX, tileY);
				_absorptionMap[py * _imgWidth + px] = GetAbsorption(tile);
			}
		}

		// --- Step 3: Stamp light from trail/root cells (NOT core) ---
		SubGridData subGrid = _tendril.SubGrid;
		if (subGrid != null)
		{
			foreach (var (key, cell) in subGrid.Cells)
			{
				if (cell.State == SubCellState.Empty || cell.State == SubCellState.Core)
					continue;

				var (sx, sy) = SubGridData.UnpackCoords(key);
				int px = sx - originSubX;
				int py = sy - originSubY;

				if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
					continue;

				float emission = cell.State switch
				{
					SubCellState.Fresh => FreshLightEmission,
					SubCellState.Trail => TrailLightEmission,
					SubCellState.Root => RootLightEmission,
					_ => 0f,
				};

				int idx = py * _imgWidth + px;
				if (emission > _lightMap[idx])
					_lightMap[idx] = emission;
			}
		}

		// --- Step 4: Propagate light ---
		for (int pass = 0; pass < PropagationPasses; pass++)
		{
			System.Array.Copy(_lightMap, _lightMapBack, totalCells);

			for (int py = 1; py < _imgHeight - 1; py++)
			{
				for (int px = 1; px < _imgWidth - 1; px++)
				{
					int idx = py * _imgWidth + px;

					float maxNeighbor = _lightMapBack[idx - 1];
					float n = _lightMapBack[idx + 1];
					if (n > maxNeighbor) maxNeighbor = n;
					n = _lightMapBack[idx - _imgWidth];
					if (n > maxNeighbor) maxNeighbor = n;
					n = _lightMapBack[idx + _imgWidth];
					if (n > maxNeighbor) maxNeighbor = n;

					float propagated = maxNeighbor * PropagationFactor - _absorptionMap[idx];
					if (propagated < 0f) propagated = 0f;

					if (propagated > _lightMap[idx])
						_lightMap[idx] = propagated;
				}
			}
		}

		// --- Step 4b: Stamp head circle AFTER propagation so it stays clean ---
		int headSubX = _tendril.SubHeadX;
		int headSubY = _tendril.SubHeadY;

		for (int dy = -HeadLightRadius; dy <= HeadLightRadius; dy++)
		{
			for (int dx = -HeadLightRadius; dx <= HeadLightRadius; dx++)
			{
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				if (dist > HeadLightRadius) continue;

				int px = (headSubX + dx) - originSubX;
				int py = (headSubY + dy) - originSubY;
				if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
					continue;

				float falloff = 1f - (dist / HeadLightRadius);
				float emission = CoreLightEmission * falloff;

				int idx = py * _imgWidth + px;
				if (emission > _lightMap[idx])
					_lightMap[idx] = emission;
			}
		}

		// --- Step 4c: Directional cave line-of-sight at sub-cell resolution ---
		int headTileX = _tendril.HeadX;
		int headTileY = _tendril.HeadY;

		if (IsNearAir(headTileX, headTileY))
		{
			float angleStep = Mathf.Tau / SightRayCount;
			int maxSteps = CaveSightRadius * scale;

			// Cone direction from tendril movement
			Vector2 fwd = _tendril.LastMoveDirection;
			float fwdAngle = Mathf.Atan2(fwd.Y, fwd.X);
			float halfCone = Mathf.DegToRad(ConeHalfAngleDeg);

			for (int r = 0; r < SightRayCount; r++)
			{
				// Distribute rays within the cone
				float angle = fwdAngle - halfCone + (r / (float)(SightRayCount - 1)) * halfCone * 2f;

				float dirX = Mathf.Cos(angle);
				float dirY = Mathf.Sin(angle);

				for (int step = 1; step <= maxSteps; step++)
				{
					int subX = headSubX + Mathf.RoundToInt(dirX * step);
					int subY = headSubY + Mathf.RoundToInt(dirY * step);

					int tileX = FloorDiv(subX, scale);
					int tileY = FloorDiv(subY, scale);
					TileType tile = _chunkManager.GetTileAt(tileX, tileY);
					if (TileProperties.Is(tile, TileFlags.Solid))
						break;

					// Stamp a small kernel around the ray point to fill gaps
					float t = (float)step / maxSteps;
					float light = CaveSightLight * (1f - t * t);

					for (int ky = -RayStampRadius; ky <= RayStampRadius; ky++)
					{
						for (int kx = -RayStampRadius; kx <= RayStampRadius; kx++)
						{
							if (kx * kx + ky * ky > RayStampRadius * RayStampRadius)
								continue;

							int px = (subX + kx) - originSubX;
							int py = (subY + ky) - originSubY;

							if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
								continue;

							// Check the stamp pixel isn't inside solid terrain
							int stampTileX = FloorDiv(subX + kx, scale);
							int stampTileY = FloorDiv(subY + ky, scale);
							TileType stampTile = _chunkManager.GetTileAt(stampTileX, stampTileY);
							if (TileProperties.Is(stampTile, TileFlags.Solid))
								continue;

							int idx = py * _imgWidth + px;
							if (light > _lightMap[idx])
								_lightMap[idx] = light;
						}
					}
				}
			}
		}

		// --- Step 5: Convert light map to fog image ---
		float maxAlpha = Mathf.Clamp(MaxFogAlpha, 0f, 1f);

		for (int py = 0; py < _imgHeight; py++)
		{
			for (int px = 0; px < _imgWidth; px++)
			{
				float light = _lightMap[py * _imgWidth + px];

				if (light <= LightCutoff)
				{
					_image.SetPixel(px, py, new Color(FogColor.R, FogColor.G, FogColor.B, maxAlpha));
				}
				else
				{
					float alpha = maxAlpha * (1f - Mathf.Clamp(light, 0f, 1f));
					_image.SetPixel(px, py, new Color(FogColor.R, FogColor.G, FogColor.B, alpha));
				}
			}
		}

		_texture.Update(_image);
		GlobalPosition = new Vector2(originSubX * cellSize, originSubY * cellSize);
	}

	// =========================================================================
	//  HELPERS
	// =========================================================================

	private bool IsNearAir(int tileX, int tileY)
	{
		for (int dy = -AirPocketContactRadius; dy <= AirPocketContactRadius; dy++)
		{
			for (int dx = -AirPocketContactRadius; dx <= AirPocketContactRadius; dx++)
			{
				TileType tile = _chunkManager.GetTileAt(tileX + dx, tileY + dy);
				if (tile == TileType.Air)
					return true;
			}
		}
		return false;
	}

	private float GetAbsorption(TileType tile)
	{
		if (tile == TileType.Air)
			return AbsorptionAir;

		if (TileProperties.Is(tile, TileFlags.Liquid))
			return AbsorptionLiquid;

		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Breakable))
			return AbsorptionSolid;

		if (TileProperties.Is(tile, TileFlags.Solid) && !TileProperties.Is(tile, TileFlags.Organic))
			return AbsorptionHard;

		if (TileProperties.Is(tile, TileFlags.Organic))
			return AbsorptionOrganic;

		return AbsorptionHard;
	}

	private static int FloorDiv(int a, int b)
		=> a >= 0 ? a / b : (a - b + 1) / b;
}
