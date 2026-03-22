namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Renders the tendril sub-grid as a single ImageTexture on a Sprite2D,
/// with an organic shader for writhing, mottling, veins, edge wobble, and pulse waves.
///
/// SETUP (Godot Editor):
///   1. Add a Sprite2D node as a child of your World scene
///   2. Attach this script to it
///   3. Set TendrilControllerPath and CameraPath in the inspector
///   4. Place the shader file at res://shaders/tendril_organic.gdshader
/// </summary>
public partial class TendrilRenderer : Sprite2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath CameraPath { get; set; }

	// --- Shader Path ---
	[Export] public string ShaderPath = "res://shaders/tendril_organic.gdshader";

	// --- Cell Colors (data-side, painted to the Image) ---
	// NOTE: Default colors bumped up from scene overrides to give the shader
	//       more headroom. The scene .tscn values were extremely dark (Core ~0.16,
	//       Trail ~0.015) which made multiplicative shader effects invisible.
	//       If you prefer darker tones, keep them above ~0.15 per channel minimum.

	[Export] public Color CoreColor = new Color(0.72f, 0.22f, 0.55f, 0.95f);
	[Export] public Color CorePulseColor = new Color(0.90f, 0.30f, 0.65f, 1.0f);
	[Export] public Color FreshColor = new Color(0.40f, 0.15f, 0.35f, 0.88f);
	[Export] public Color TrailColor = new Color(0.22f, 0.10f, 0.20f, 0.82f);
	[Export] public Color RootColor = new Color(0.15f, 0.06f, 0.14f, 0.70f);
	[Export] public int AuraRadius = 2;
	[Export] public Color AuraColor = new Color(0.55f, 0.15f, 0.45f, 0.25f);
	[Export] public float PulseSpeed = 4.5f;
	[Export] public float PulseIntensity = 0.35f;
	[Export] public int Padding = 8;

	// --- Per-Pixel Variation (NEW) ---
	/// <summary>
	/// How much per-pixel brightness scatter to add (0–1).
	/// This gives the shader's UV-distortion effects visible texture to work with.
	/// Without this, adjacent pixels are identical and writhing/wobble are invisible.
	/// </summary>
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float PixelVariation = 0.15f;

	// --- Shader: Writhing ---
	[Export(PropertyHint.Range, "0,5,0.1")] public float WritheSpeed = 1.2f;
	[Export(PropertyHint.Range, "0,20,0.5")] public float WritheStrength = 5.0f;
	[Export(PropertyHint.Range, "0.5,40,0.5")] public float WritheScale = 10.0f;

	// --- Shader: Color Mottling ---
	[Export(PropertyHint.Range, "0,1,0.01")] public float MottleStrength = 0.35f;
	[Export(PropertyHint.Range, "0.5,30,0.5")] public float MottleScale = 5.0f;

	// --- Shader: Pulse Wave ---
	[Export(PropertyHint.Range, "0,10,0.1")] public float ShaderPulseSpeed = 2.0f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ShaderPulseStrength = 0.4f;
	[Export(PropertyHint.Range, "5,200,1")] public float ShaderPulseWavelength = 40.0f;

	// --- Shader: Edge Wobble ---
	[Export(PropertyHint.Range, "0,5,0.1")] public float EdgeWobbleSpeed = 1.5f;
	[Export(PropertyHint.Range, "0,10,0.5")] public float EdgeWobbleStrength = 3.0f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float EdgeWobbleScale = 7.0f;

	// --- Shader: Veins ---
	[Export(PropertyHint.Range, "0,0.6,0.01")] public float VeinStrength = 0.2f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float VeinScale = 4.0f;
	[Export(PropertyHint.Range, "0,3,0.1")] public float VeinSpeed = 0.3f;

	private TendrilController _tendril;
	private Camera2D _camera;
	private float _pulseTimer;
	private ShaderMaterial _shaderMat;

	private Image _image;
	private ImageTexture _texture;
	private int _imgWidth;
	private int _imgHeight;
	private float _lastZoom;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);
		if (CameraPath != null)
			_camera = GetNode<Camera2D>(CameraPath);

		if (_tendril == null || _camera == null)
		{
			GD.PrintErr("TendrilRenderer: Missing TendrilController or Camera2D path.");
			SetProcess(false);
			return;
		}

		TextureFilter = TextureFilterEnum.Nearest;
		Centered = false;

		int cellSize = WorldConfig.SubCellSize;
		Scale = new Vector2(cellSize, cellSize);

		ZAsRelative = false;
		ZIndex = 100;

		SetupShader();

		_lastZoom = _camera.Zoom.X;
		ReallocateImage();
	}

	private void SetupShader()
	{
		var shader = GD.Load<Shader>(ShaderPath);
		if (shader == null)
		{
			GD.PrintErr($"TendrilRenderer: Could not load shader at '{ShaderPath}'. " +
						"Visuals will work but without organic effects.");
			return;
		}

		_shaderMat = new ShaderMaterial();
		_shaderMat.Shader = shader;
		Material = _shaderMat;

		SyncShaderUniforms();
		GD.Print("TendrilRenderer: Organic shader loaded.");
	}

	public void SyncShaderUniforms()
	{
		if (_shaderMat == null) return;

		// Pass actual image dimensions — shader can't reliably use textureSize()
		_shaderMat.SetShaderParameter("tex_size", new Vector2(_imgWidth, _imgHeight));

		_shaderMat.SetShaderParameter("writhe_speed", WritheSpeed);
		_shaderMat.SetShaderParameter("writhe_strength", WritheStrength);
		_shaderMat.SetShaderParameter("writhe_scale", WritheScale);
		_shaderMat.SetShaderParameter("mottle_strength", MottleStrength);
		_shaderMat.SetShaderParameter("mottle_scale", MottleScale);
		_shaderMat.SetShaderParameter("pulse_speed", ShaderPulseSpeed);
		_shaderMat.SetShaderParameter("pulse_strength", ShaderPulseStrength);
		_shaderMat.SetShaderParameter("pulse_wavelength", ShaderPulseWavelength);
		_shaderMat.SetShaderParameter("edge_wobble_speed", EdgeWobbleSpeed);
		_shaderMat.SetShaderParameter("edge_wobble_strength", EdgeWobbleStrength);
		_shaderMat.SetShaderParameter("edge_wobble_scale", EdgeWobbleScale);
		_shaderMat.SetShaderParameter("vein_strength", VeinStrength);
		_shaderMat.SetShaderParameter("vein_scale", VeinScale);
		_shaderMat.SetShaderParameter("vein_speed", VeinSpeed);
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _camera == null) return;

		_pulseTimer += (float)delta * PulseSpeed;

		float currentZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(currentZoom, _lastZoom, 0.001f))
		{
			_lastZoom = currentZoom;
			ReallocateImage();
		}

		PaintFrame();

		// Sync every frame so inspector tweaks are live
		SyncShaderUniforms();
	}

	private void ReallocateImage()
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		_imgWidth = (int)Mathf.Ceil(viewportSize.X / zoom / cellSize) + Padding * 2;
		_imgHeight = (int)Mathf.Ceil(viewportSize.Y / zoom / cellSize) + Padding * 2;

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
		SubGridData subGrid = _tendril.SubGrid;
		if (subGrid == null) return;

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 cameraPos = _camera.GlobalPosition;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		float halfW = (viewportSize.X / zoom) / 2f;
		float halfH = (viewportSize.Y / zoom) / 2f;

		int originSubX = (int)Mathf.Floor((cameraPos.X - halfW) / cellSize) - Padding;
		int originSubY = (int)Mathf.Floor((cameraPos.Y - halfH) / cellSize) - Padding;

		_image.Fill(Colors.Transparent);

		float pulse = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;

		// --- Pass 1: Aura glow ---
		if (AuraRadius > 0)
		{
			foreach (var (key, cell) in subGrid.Cells)
			{
				if (cell.State != SubCellState.Trail && cell.State != SubCellState.Root)
					continue;

				var (sx, sy) = SubGridData.UnpackCoords(key);
				int px = sx - originSubX;
				int py = sy - originSubY;

				for (int ay = -AuraRadius; ay <= AuraRadius; ay++)
				{
					for (int ax = -AuraRadius; ax <= AuraRadius; ax++)
					{
						int apx = px + ax;
						int apy = py + ay;
						if (apx < 0 || apx >= _imgWidth || apy < 0 || apy >= _imgHeight)
							continue;

						float dist = Mathf.Sqrt(ax * ax + ay * ay);
						if (dist > AuraRadius) continue;

						float fade = 1f - (dist / AuraRadius);
						Color glow = new Color(AuraColor.R, AuraColor.G, AuraColor.B, AuraColor.A * fade);

						Color existing = _image.GetPixel(apx, apy);
						if (glow.A > existing.A)
							_image.SetPixel(apx, apy, glow);
					}
				}
			}
		}

		// --- Pass 2: Actual cells ---
		foreach (var (key, cell) in subGrid.Cells)
		{
			if (cell.State == SubCellState.Empty) continue;

			var (sx, sy) = SubGridData.UnpackCoords(key);
			int px = sx - originSubX;
			int py = sy - originSubY;

			if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
				continue;

			_image.SetPixel(px, py, GetCellColor(cell, pulse, sx, sy));
		}

		_texture.Update(_image);

		GlobalPosition = new Vector2(originSubX * cellSize, originSubY * cellSize);
	}

	/// <summary>
	/// Deterministic per-pixel hash. Returns a value in [0, 1) that is unique
	/// per sub-grid coordinate but stable across frames, so the texture doesn't
	/// shimmer on its own — only the shader's UV distortion reveals the variation.
	/// </summary>
	private static float CellHash(int sx, int sy)
	{
		// Large primes for spatial hashing — avoids visible grid patterns
		unchecked
		{
			int h = sx * 73856093 ^ sy * 19349663;
			h = (h ^ (h >> 13)) * 1274126177;
			return (float)((uint)h % 10000u) / 10000f;
		}
	}

	private Color GetCellColor(SubCell cell, float pulse, int sx, int sy)
	{
		// Per-pixel variation so adjacent texels differ and shader UV-distortion
		// actually has visible texture to work with.
		float hash = CellHash(sx, sy);
		float variation = (hash - 0.5f) * 2f * PixelVariation; // ±PixelVariation

		// Secondary hash for slight hue shifts (prevents uniform grey scatter)
		float hash2 = CellHash(sx + 7919, sy + 6271);
		float hueShift = (hash2 - 0.5f) * PixelVariation * 0.5f;

		switch (cell.State)
		{
			case SubCellState.Core:
			{
				float t = pulse * PulseIntensity * (cell.Intensity / 255f);
				Color c = CoreColor.Lerp(CorePulseColor, t);
				return new Color(
					Mathf.Clamp(c.R + variation + hueShift, 0f, 1f),
					Mathf.Clamp(c.G + variation * 0.6f, 0f, 1f),
					Mathf.Clamp(c.B + variation - hueShift, 0f, 1f),
					c.A
				);
			}

			case SubCellState.Fresh:
			{
				float ageFade = Mathf.Clamp(cell.Age / 60f, 0f, 1f);
				Color c = FreshColor.Lerp(TrailColor, ageFade * 0.6f);
				return new Color(
					Mathf.Clamp(c.R + variation + hueShift, 0f, 1f),
					Mathf.Clamp(c.G + variation * 0.5f, 0f, 1f),
					Mathf.Clamp(c.B + variation - hueShift, 0f, 1f),
					c.A
				);
			}

			case SubCellState.Trail:
			{
				float v = cell.Intensity / 255f * 0.15f;
				return new Color(
					Mathf.Clamp(TrailColor.R + v * 0.05f + variation + hueShift, 0f, 1f),
					Mathf.Clamp(TrailColor.G + v * 0.03f + variation * 0.5f, 0f, 1f),
					Mathf.Clamp(TrailColor.B + variation * 0.4f - hueShift, 0f, 1f),
					TrailColor.A
				);
			}

			case SubCellState.Root:
				return new Color(
					Mathf.Clamp(RootColor.R + variation + hueShift, 0f, 1f),
					Mathf.Clamp(RootColor.G + variation * 0.5f, 0f, 1f),
					Mathf.Clamp(RootColor.B + variation - hueShift, 0f, 1f),
					RootColor.A
				);

			default:
				return Colors.Transparent;
		}
	}
}
