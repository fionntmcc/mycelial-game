namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Renders the tendril sub-grid as a single ImageTexture on a Sprite2D,
/// with an organic shader driven by HSV pulse waves and movement reactivity.
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

	// --- Cell Colors (purple palette) ---
	[Export] public Color CoreColor      = new Color(0.62f, 0.58f, 0.68f, 0.95f);
	[Export] public Color CorePulseColor = new Color(0.78f, 0.70f, 0.85f, 1.0f);
	[Export] public Color FreshColor     = new Color(0.49f, 0.46f, 0.53f, 0.90f);
	[Export] public Color TrailColor     = new Color(0.41f, 0.35f, 0.46f, 0.85f);
	[Export] public Color RootColor      = new Color(0.25f, 0.18f, 0.30f, 0.72f);

	// --- Spline Aura ---
	/// <summary>Path to TendrilSplineRenderer for spline-based aura lighting.</summary>
	[Export] public NodePath SplineRendererPath { get; set; }

	/// <summary>Extra glow radius beyond the spline width (in sub-cells).</summary>
	[Export] public int AuraSpread = 3;

	/// <summary>Glow intensity falloff power. Higher = tighter glow around the spline.</summary>
	[Export(PropertyHint.Range, "0.5,4,0.1")] public float AuraFalloffPower = 1.5f;

	/// <summary>Skip every Nth spline point for aura (1 = every point, 2 = every other, etc).
	/// Higher = cheaper but less uniform glow.</summary>
	[Export(PropertyHint.Range, "1,8,1")] public int AuraSplineStride = 2;

	[Export] public Color AuraColor = new Color(0.35f, 0.22f, 0.44f, 0.22f);

	// --- Pulse ---
	[Export] public float PulseSpeed = 4.5f;
	[Export] public float PulseIntensity = 0.35f;
	[Export] public int Padding = 8;

	// --- Per-Pixel Variation ---
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float PixelVariation = 0.12f;

	// --- Shader: HSV Pulse Wave ---
	[Export(PropertyHint.Range, "0,8,0.1")] public float HueWaveSpeed = 3.0f;
	[Export(PropertyHint.Range, "5,200,1")] public float HueWaveLength = 30.0f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HueShiftRange = 0.15f;
	[Export(PropertyHint.Range, "0,0.8,0.01")] public float ValShiftRange = 0.40f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float SatShiftRange = 0.15f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float IdlePulseStrength = 0.35f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float MovePulseBoost = 0.65f;

	// --- Shader: Writhing ---
	[Export(PropertyHint.Range, "0,5,0.1")] public float WritheSpeed = 1.2f;
	[Export(PropertyHint.Range, "0,20,0.5")] public float WritheStrength = 5.0f;
	[Export(PropertyHint.Range, "0.5,40,0.5")] public float WritheScale = 10.0f;

	// --- Shader: Color Mottling ---
	[Export(PropertyHint.Range, "0,1,0.01")] public float MottleStrength = 0.35f;
	[Export(PropertyHint.Range, "0.5,30,0.5")] public float MottleScale = 5.0f;

	// --- Shader: Edge Wobble ---
	[Export(PropertyHint.Range, "0,5,0.1")] public float EdgeWobbleSpeed = 1.5f;
	[Export(PropertyHint.Range, "0,10,0.5")] public float EdgeWobbleStrength = 3.0f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float EdgeWobbleScale = 7.0f;

	// --- Shader: Veins ---
	[Export(PropertyHint.Range, "0,0.6,0.01")] public float VeinStrength = 0.2f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float VeinScale = 4.0f;
	[Export(PropertyHint.Range, "0,3,0.1")] public float VeinSpeed = 0.3f;

	// --- Movement Reactivity ---
	[Export(PropertyHint.Range, "0,1,0.05")] public float MovementReactivity = 0.75f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float IdleActivityFloor = 0.15f;
	[Export(PropertyHint.Range, "1,15,0.5")] public float SpeedSmoothing = 4.0f;

	private TendrilController _tendril;
	private TendrilHarpoon _harpoon;
	private Camera2D _camera;
	private TendrilSplineRenderer _splineRenderer;
	private float _pulseTimer;
	private ShaderMaterial _shaderMat;

	private Image _image;
	private ImageTexture _texture;
	private int _imgWidth;
	private int _imgHeight;
	private float _lastZoom;

	private float _smoothedSpeed;
	private Vector2 _smoothedDir;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);
		if (CameraPath != null)
			_camera = GetNode<Camera2D>(CameraPath);
		if (SplineRendererPath != null)
			_splineRenderer = GetNode<TendrilSplineRenderer>(SplineRendererPath);
		_harpoon = _tendril?.Harpoon;

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

		_smoothedSpeed = 0f;
		_smoothedDir = Vector2.Down;

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

		_shaderMat.SetShaderParameter("tex_size", new Vector2(_imgWidth, _imgHeight));

		// HSV pulse wave
		_shaderMat.SetShaderParameter("hue_wave_speed", HueWaveSpeed);
		_shaderMat.SetShaderParameter("hue_wave_length", HueWaveLength);
		_shaderMat.SetShaderParameter("hue_shift_range", HueShiftRange);
		_shaderMat.SetShaderParameter("val_shift_range", ValShiftRange);
		_shaderMat.SetShaderParameter("sat_shift_range", SatShiftRange);
		_shaderMat.SetShaderParameter("idle_pulse_strength", IdlePulseStrength);
		_shaderMat.SetShaderParameter("move_pulse_boost", MovePulseBoost);

		// Writhing
		_shaderMat.SetShaderParameter("writhe_speed", WritheSpeed);
		_shaderMat.SetShaderParameter("writhe_strength", WritheStrength);
		_shaderMat.SetShaderParameter("writhe_scale", WritheScale);

		// Mottling
		_shaderMat.SetShaderParameter("mottle_strength", MottleStrength);
		_shaderMat.SetShaderParameter("mottle_scale", MottleScale);

		// Edge wobble
		_shaderMat.SetShaderParameter("edge_wobble_speed", EdgeWobbleSpeed);
		_shaderMat.SetShaderParameter("edge_wobble_strength", EdgeWobbleStrength);
		_shaderMat.SetShaderParameter("edge_wobble_scale", EdgeWobbleScale);

		// Veins
		_shaderMat.SetShaderParameter("vein_strength", VeinStrength);
		_shaderMat.SetShaderParameter("vein_scale", VeinScale);
		_shaderMat.SetShaderParameter("vein_speed", VeinSpeed);

		// Movement
		_shaderMat.SetShaderParameter("move_speed", _smoothedSpeed);
		_shaderMat.SetShaderParameter("move_direction", _smoothedDir);
		_shaderMat.SetShaderParameter("activity_floor", IdleActivityFloor);
		_shaderMat.SetShaderParameter("move_reactivity", MovementReactivity);
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _camera == null) return;

		float dt = (float)delta;

		// Smooth movement data
		float rawSpeed = _tendril.CurrentSpeed;
		_smoothedSpeed = Mathf.Lerp(_smoothedSpeed, rawSpeed, SpeedSmoothing * dt);

		Vector2 rawDir = _tendril.LastMoveDirection;
		if (rawDir.LengthSquared() > 0.01f)
		{
			float fromAngle = Mathf.Atan2(_smoothedDir.Y, _smoothedDir.X);
			float toAngle = Mathf.Atan2(rawDir.Y, rawDir.X);
			float smoothed = Mathf.LerpAngle(fromAngle, toAngle, SpeedSmoothing * 0.5f * dt);
			_smoothedDir = new Vector2(Mathf.Cos(smoothed), Mathf.Sin(smoothed));
		}

		_pulseTimer += dt * PulseSpeed;

		float currentZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(currentZoom, _lastZoom, 0.001f))
		{
			_lastZoom = currentZoom;
			ReallocateImage();
		}

		PaintFrame();
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
		if (_harpoon == null)
			_harpoon = _tendril?.Harpoon;

		// --- Pass 1: Spline-based aura glow ---
		if (AuraSpread > 0 && _splineRenderer != null)
		{
			var splinePoints = _splineRenderer.InterpolatedPoints;

			for (int i = 0; i < splinePoints.Count; i += AuraSplineStride)
			{
				var sp = splinePoints[i];

				// Convert world-pixel position to image coordinates
				float subXf = sp.Position.X / cellSize;
				float subYf = sp.Position.Y / cellSize;
				int px = (int)Mathf.Floor(subXf) - originSubX;
				int py = (int)Mathf.Floor(subYf) - originSubY;

				// Glow radius = half the spline width (in sub-cells) + extra spread
				float widthInSubCells = sp.Width / cellSize;
				int glowRadius = (int)Mathf.Ceil(widthInSubCells * 0.5f) + AuraSpread;

				for (int ay = -glowRadius; ay <= glowRadius; ay++)
				{
					for (int ax = -glowRadius; ax <= glowRadius; ax++)
					{
						int apx = px + ax;
						int apy = py + ay;
						if (apx < 0 || apx >= _imgWidth || apy < 0 || apy >= _imgHeight)
							continue;

						float dist = Mathf.Sqrt(ax * ax + ay * ay);
						if (dist > glowRadius) continue;

						float normalizedDist = dist / glowRadius;
						float fade = Mathf.Pow(1f - normalizedDist, AuraFalloffPower);

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

		// Split-second grab feedback at the harpoon tip.
		if (_harpoon != null)
		{
			float grabPulse = _harpoon.GrabPulseStrength;
			if (grabPulse > 0f)
			{
				Vector2I tip = _harpoon.PulseTipSubPosition;
				int tipPx = tip.X - originSubX;
				int tipPy = tip.Y - originSubY;

				if (tipPx >= 0 && tipPx < _imgWidth && tipPy >= 0 && tipPy < _imgHeight)
				{
					int radius = 2;
					for (int oy = -radius; oy <= radius; oy++)
					{
						for (int ox = -radius; ox <= radius; ox++)
						{
							int px = tipPx + ox;
							int py = tipPy + oy;
							if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
								continue;

							float dist = Mathf.Sqrt(ox * ox + oy * oy);
							if (dist > radius)
								continue;

							float falloff = 1f - (dist / radius);
							Color overlay = new Color(
								CorePulseColor.R,
								CorePulseColor.G,
								CorePulseColor.B,
								0.45f * grabPulse * falloff);

							Color current = _image.GetPixel(px, py);
							_image.SetPixel(px, py, current.Blend(overlay));
						}
					}
				}
			}
		}

		_texture.Update(_image);

		GlobalPosition = new Vector2(originSubX * cellSize, originSubY * cellSize);
	}

	// =========================================================================
	//  PER-PIXEL COLOR
	// =========================================================================

	private static float CellHash(int sx, int sy)
	{
		unchecked
		{
			int h = sx * 73856093 ^ sy * 19349663;
			h = (h ^ (h >> 13)) * 1274126177;
			return (float)((uint)h % 10000u) / 10000f;
		}
	}

	private Color GetCellColor(SubCell cell, float pulse, int sx, int sy)
	{
		float hash = CellHash(sx, sy);
		float variation = (hash - 0.5f) * 2f * PixelVariation;

		float hash2 = CellHash(sx + 7919, sy + 6271);
		float hueShift = (hash2 - 0.5f) * PixelVariation * 0.6f;

		switch (cell.State)
		{
			case SubCellState.Core:
			{
				float t = pulse * PulseIntensity * (cell.Intensity / 255f);
				Color c = CoreColor.Lerp(CorePulseColor, t);
				return new Color(
					Mathf.Clamp(c.R + variation + hueShift, 0f, 1f),
					Mathf.Clamp(c.G + variation * 0.5f, 0f, 1f),
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
					Mathf.Clamp(TrailColor.G + v * 0.03f + variation * 0.4f, 0f, 1f),
					Mathf.Clamp(TrailColor.B + variation * 0.5f - hueShift, 0f, 1f),
					TrailColor.A
				);
			}

			case SubCellState.Root:
				return new Color(
					Mathf.Clamp(RootColor.R + variation + hueShift, 0f, 1f),
					Mathf.Clamp(RootColor.G + variation * 0.4f, 0f, 1f),
					Mathf.Clamp(RootColor.B + variation * 0.6f - hueShift, 0f, 1f),
					RootColor.A
				);

			default:
				return Colors.Transparent;
		}
	}
}
