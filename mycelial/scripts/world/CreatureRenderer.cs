namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Renders all creatures as sub-cell pixel art onto an Image texture.
///
/// Uses the same resolution, coordinate system, and rendering approach as
/// TendrilRenderer — creatures and the tendril share the same visual language.
///
/// SETUP:
///   1. Add a Sprite2D node as sibling of TendrilRenderer
///   2. Attach this script
///   3. Assign CreatureManagerPath, CameraPath, TendrilControllerPath
///   4. Texture Filter to "Nearest" in the inspector
///   5. Centered = false
/// </summary>
public partial class CreatureRenderer : Sprite2D
{
	[Export] public NodePath CreatureManagerPath { get; set; }
	[Export] public NodePath CameraPath { get; set; }
	[Export] public NodePath TendrilControllerPath { get; set; }

	// --- Visual Config ---
	[Export] public int Padding = 8;
	[Export] public Color DamageFlashColor = new(1f, 0.3f, 0.2f, 1f);
	[Export] public float DamageFlashDuration = 0.12f;

	// --- State ---
	private CreatureManager _creatureManager;
	private Camera2D _camera;
	private TendrilController _tendril;
	private Image _image;
	private ImageTexture _texture;
	private int _imgWidth;
	private int _imgHeight;
	private float _animTime;
	private float _lastZoom;

	public override void _Ready()
	{
		if (CreatureManagerPath != null)
			_creatureManager = GetNode<CreatureManager>(CreatureManagerPath);
		if (CameraPath != null)
			_camera = GetNode<Camera2D>(CameraPath);
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		if (_creatureManager == null || _camera == null)
		{
			GD.PrintErr("CreatureRenderer: Missing CreatureManager or Camera!");
			SetProcess(false);
			return;
		}

		TextureFilter = TextureFilterEnum.Nearest;
		Centered = false;

		int cellSize = WorldConfig.SubCellSize;
		Scale = new Vector2(cellSize, cellSize);

		ZAsRelative = false;
		ZIndex = 101;

		_lastZoom = _camera.Zoom.X;
		RebuildImage();
	}

	public override void _Process(double delta)
	{
		if (_creatureManager == null || _camera == null) return;

		_animTime += (float)delta;

		float currentZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(currentZoom, _lastZoom, 0.001f))
		{
			_lastZoom = currentZoom;
			RebuildImage();
		}

		PaintFrame();
	}

	// =========================================================================
	//  IMAGE MANAGEMENT
	// =========================================================================

	private void RebuildImage()
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

	// =========================================================================
	//  RENDERING
	// =========================================================================

	private void PaintFrame()
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 cameraPos = _camera.GlobalPosition;
		float zoom = _camera.Zoom.X;
		int cellSize = WorldConfig.SubCellSize;

		float halfW = (viewportSize.X / zoom) / 2f;
		float halfH = (viewportSize.Y / zoom) / 2f;

		int originSubX = (int)Mathf.Floor((cameraPos.X - halfW) / cellSize) - Padding;
		int originSubY = (int)Mathf.Floor((cameraPos.Y - halfH) / cellSize) - Padding;

		_image.Fill(Colors.Transparent);

		var creatures = _creatureManager.GetAllCreatures();

		for (int c = 0; c < creatures.Count; c++)
		{
			var creature = creatures[c];
			if (!creature.IsAlive || !creature.IsActive) continue;

			int creaturePx = creature.SubX - originSubX;
			int creaturePy = creature.SubY - originSubY;
			int bodyRadius = creature.Body?.Radius ?? 4;

			if (creaturePx + bodyRadius < 0 || creaturePx - bodyRadius >= _imgWidth) continue;
			if (creaturePy + bodyRadius < 0 || creaturePy - bodyRadius >= _imgHeight) continue;

			PaintCreature(creature, originSubX, originSubY);
		}

		_texture.Update(_image);
		GlobalPosition = new Vector2(originSubX * cellSize, originSubY * cellSize);
	}

	private void PaintCreature(Creature creature, int originSubX, int originSubY)
	{
		var bodySet = CreatureBodyRegistry.GetBodySet(creature.Species);
		bool isMoving = creature.Velocity.LengthSquared() > 0.5f;
		var body = isMoving ? bodySet.GetFrame(_animTime + creature.AnimTimeOffset)
							: bodySet.Idle;

		bool flipX = creature.Velocity.X < -0.5f;

		bool flashing = creature.DamageFlashTimer > 0;
		float flashLerp = flashing
			? Mathf.Clamp(creature.DamageFlashTimer / DamageFlashDuration, 0f, 1f)
			: 0f;

		for (int i = 0; i < body.Cells.Length; i++)
		{
			var (dx, dy) = body.Cells[i];
			if (flipX) dx = -dx;

			int px = (creature.SubX + dx) - originSubX;
			int py = (creature.SubY + dy) - originSubY;

			if (px < 0 || px >= _imgWidth || py < 0 || py >= _imgHeight)
				continue;

			Color color = body.Colors[i];

			if (flashing)
				color = color.Lerp(DamageFlashColor, flashLerp * 0.7f);

			_image.SetPixel(px, py, color);
		}
	}
}
