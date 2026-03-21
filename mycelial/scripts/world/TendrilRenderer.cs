namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Renders the tendril sub-grid as a single ImageTexture on a Sprite2D.
///
/// Each frame:
///   1. Compute the visible sub-grid rectangle from camera position/zoom
///   2. Clear a small Image to transparent
///   3. Paint each visible sub-cell as one pixel (Image scales up via Nearest filter)
///   4. Upload to ImageTexture — one GPU call
///   5. Position the Sprite2D in world space so pixels align with sub-cells
///
/// At zoom 1.0 with a 1920×1080 viewport and 4px sub-cells, the image is only
/// ~500×280 pixels. Repainting from scratch every frame is faster than any
/// dirty-tracking approach at this size.
///
/// SETUP (Godot Editor):
///   1. Add a Sprite2D node as a child of your World scene
///   2. Attach this script to it
///   3. Set TendrilControllerPath and CameraPath in the inspector
///   4. Place it AFTER ChunkManager, BEFORE FogOfWar in the scene tree
///
///   The script configures all Sprite2D properties in _Ready automatically.
///   You don't need to assign a texture — it creates one at runtime.
/// </summary>
public partial class TendrilRenderer : Sprite2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath CameraPath { get; set; }

	// --- Visual Config ---

	/// <summary>Core blob base color — vivid fungal purple-pink.</summary>
	[Export] public Color CoreColor = new Color(0.72f, 0.22f, 0.55f, 0.95f);

	/// <summary>Core pulse highlight color — hot pink on pulse peaks.</summary>
	[Export] public Color CorePulseColor = new Color(0.90f, 0.30f, 0.65f, 1.0f);

	/// <summary>Fresh trail color — muted plum, still alive-looking.</summary>
	[Export] public Color FreshColor = new Color(0.40f, 0.15f, 0.35f, 0.88f);

	/// <summary>Settled trail color — deep bruised purple-brown.</summary>
	[Export] public Color TrailColor = new Color(0.22f, 0.10f, 0.20f, 0.82f);

	/// <summary>Root tendril color — near-black purple threads.</summary>
	[Export] public Color RootColor = new Color(0.15f, 0.06f, 0.14f, 0.70f);
	
	/// <summary>Aura glow radius in sub-cells around trail/root cells.</summary>
	[Export] public int AuraRadius = 2;

	/// <summary>Aura base color — faint bioluminescent bleed.</summary>
	[Export] public Color AuraColor = new Color(0.55f, 0.15f, 0.45f, 0.25f);

	/// <summary>Speed of the core pulse animation (radians per second).</summary>
	[Export] public float PulseSpeed = 4.5f;

	/// <summary>How much the pulse affects color (0 = none, 1 = full swing).</summary>
	[Export] public float PulseIntensity = 0.35f;

	/// <summary>Extra sub-cells of padding around the viewport edge.</summary>
	[Export] public int Padding = 8;

	private TendrilController _tendril;
	private Camera2D _camera;
	private float _pulseTimer;

	// Image buffer
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

		// --- Configure the Sprite2D programmatically ---
		// Nearest-neighbor so each image pixel stays a crisp block
		TextureFilter = TextureFilterEnum.Nearest;

		// Not centered — GlobalPosition = top-left corner in world space
		Centered = false;

		// Each image pixel represents one sub-cell (4 world pixels wide/tall)
		int cellSize = WorldConfig.SubCellSize;
		Scale = new Vector2(cellSize, cellSize);

		// Render between terrain (Z ~0) and fog of war (Z 5000)
		ZAsRelative = false;
		ZIndex = 100;

		// Create initial image and texture
		_lastZoom = _camera.Zoom.X;
		ReallocateImage();
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _camera == null) return;

		_pulseTimer += (float)delta * PulseSpeed;

		// Reallocate if zoom changed (viewport covers a different world area)
		float currentZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(currentZoom, _lastZoom, 0.001f))
		{
			_lastZoom = currentZoom;
			ReallocateImage();
		}

		PaintFrame();
	}

	/// <summary>
	/// Allocate (or re-allocate) the Image and ImageTexture to match
	/// the current viewport size and zoom level.
	/// </summary>
	private void ReallocateImage()
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		// How many sub-cells fit on screen, plus padding on each side
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

	/// <summary>
	/// Clear the image, paint all visible sub-cells, upload to GPU, reposition sprite.
	/// </summary>
	private void PaintFrame()
	{
		SubGridData subGrid = _tendril.SubGrid;
		if (subGrid == null) return;

		// Work out which sub-grid rectangle is visible
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 cameraPos = _camera.GlobalPosition;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		float halfW = (viewportSize.X / zoom) / 2f;
		float halfH = (viewportSize.Y / zoom) / 2f;

		// Origin = top-left of visible region in sub-grid coordinates
		int originSubX = (int)Mathf.Floor((cameraPos.X - halfW) / cellSize) - Padding;
		int originSubY = (int)Mathf.Floor((cameraPos.Y - halfH) / cellSize) - Padding;

		// Wipe to transparent
		_image.Fill(Colors.Transparent);

		// Pulse value for core animation (0..1 sine wave)
		float pulse = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;

		// --- Pass 1: Aura glow around trail and root cells ---
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

						// Fade alpha with distance
						float fade = 1f - (dist / AuraRadius);
						Color glow = new Color(AuraColor.R, AuraColor.G, AuraColor.B, AuraColor.A * fade);

						// Blend with existing pixel (max alpha so auras overlap softly)
						Color existing = _image.GetPixel(apx, apy);
						if (glow.A > existing.A)
							_image.SetPixel(apx, apy, glow);
					}
				}
			}
		}

		// --- Pass 2: Actual cells on top ---
		foreach (var (key, cell) in subGrid.Cells)
		{
			if (cell.State == SubCellState.Empty) continue;

			var (sx, sy) = SubGridData.UnpackCoords(key);
			int px = sx - originSubX;
			int py = sy - originSubY;

			if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
				continue;

			_image.SetPixel(px, py, GetCellColor(cell, pulse));
		}

		// One GPU upload
		_texture.Update(_image);

		// Snap sprite so image pixel (0,0) lines up with world sub-cell (originSubX, originSubY)
		GlobalPosition = new Vector2(originSubX * cellSize, originSubY * cellSize);
	}

	/// <summary>
	/// Pick the draw color for a sub-cell based on its state and the current pulse phase.
	/// </summary>
	private Color GetCellColor(SubCell cell, float pulse)
	{
		switch (cell.State)
		{
			case SubCellState.Core:
			{
				// Pulse between base and highlight; intensity varies across the blob surface
				float t = pulse * PulseIntensity * (cell.Intensity / 255f);
				return CoreColor.Lerp(CorePulseColor, t);
			}

			case SubCellState.Fresh:
			{
				// Gradually fade from fresh toward trail color as age increases
				float ageFade = Mathf.Clamp(cell.Age / 60f, 0f, 1f);
				return FreshColor.Lerp(TrailColor, ageFade * 0.6f);
			}

			case SubCellState.Trail:
			{
				// Slight intensity variation for organic visual texture
				float v = cell.Intensity / 255f * 0.15f;
				return new Color(
					TrailColor.R + v * 0.05f,
					TrailColor.G + v * 0.03f,
					TrailColor.B,
					TrailColor.A
				);
			}

			case SubCellState.Root:
				return RootColor;

			default:
				return Colors.Transparent;
		}
	}
}
