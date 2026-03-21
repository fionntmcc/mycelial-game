namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Camera that follows the tendril head with smooth tracking.
/// Scroll to zoom in/out. The camera smoothly lerps to the tendril position
/// so movement feels fluid, not jerky.
///
/// UPDATED: Now tracks the continuous pixel position from TendrilHead
/// instead of the old tile-based position. This gives smoother camera
/// movement that matches the fluid tendril physics.
///
/// SETUP:
///   - Attach to a Camera2D node
///   - Assign TendrilControllerPath to your TendrilController node
///   - Enable "Current" on the Camera2D
/// </summary>
public partial class TendrilCamera : Camera2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public bool PixelPerfectMode = true;

	[Export] public float FollowSpeed = 8.0f;
	[Export] public float ZoomSpeed = 0.1f;
	[Export] public float MinZoom = 0.2f;
	[Export] public float MaxZoom = 4.0f;
	[Export] public float DefaultZoom = 1.0f;

	private static readonly float[] PixelPerfectZoomLevels = { 0.5f, 1.0f, 2.0f, 3.0f, 4.0f };

	private TendrilController _tendril;
	private Vector2 _targetPosition;
	private int _zoomLevelIndex = 1;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		if (PixelPerfectMode)
			TextureFilter = TextureFilterEnum.Nearest;

		_zoomLevelIndex = FindNearestZoomIndex(Mathf.Clamp(DefaultZoom, MinZoom, MaxZoom));
		float startZoom = PixelPerfectMode
			? PixelPerfectZoomLevels[_zoomLevelIndex]
			: DefaultZoom;

		Zoom = new Vector2(startZoom, startZoom);
		MakeCurrent();

		if (_tendril != null)
		{
			_targetPosition = new Vector2(
				WorldConfig.TreeWorldX * WorldConfig.TileSize,
				0
			);
			GlobalPosition = _targetPosition;
		}
	}

	public override void _Process(double delta)
	{
		if (_tendril == null) return;

		// Track the continuous pixel position (smooth, not tile-snapped)
		_targetPosition = _tendril.GetHeadPixelPosition();

		// Smooth follow
		Vector2 next = GlobalPosition.Lerp(_targetPosition, FollowSpeed * (float)delta);

		if (!PixelPerfectMode)
		{
			GlobalPosition = next;
			return;
		}

		float snapStep = 1.0f / Mathf.Max(Zoom.X, 0.0001f);
		GlobalPosition = new Vector2(
			Mathf.Snapped(next.X, snapStep),
			Mathf.Snapped(next.Y, snapStep)
		);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			if (PixelPerfectMode)
			{
				if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
					_zoomLevelIndex = System.Math.Min(_zoomLevelIndex + 1, PixelPerfectZoomLevels.Length - 1);
				else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
					_zoomLevelIndex = System.Math.Max(_zoomLevelIndex - 1, 0);

				float newZoomLevel = PixelPerfectZoomLevels[_zoomLevelIndex];
				Zoom = new Vector2(newZoomLevel, newZoomLevel);
				return;
			}

			float newZoom = Zoom.X;

			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
				newZoom += ZoomSpeed;
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
				newZoom -= ZoomSpeed;

			newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
			Zoom = new Vector2(newZoom, newZoom);
		}
	}

	private int FindNearestZoomIndex(float targetZoom)
	{
		int best = 0;
		float bestDiff = float.MaxValue;

		for (int i = 0; i < PixelPerfectZoomLevels.Length; i++)
		{
			float z = PixelPerfectZoomLevels[i];
			if (z < MinZoom || z > MaxZoom) continue;

			float diff = Mathf.Abs(targetZoom - z);
			if (diff < bestDiff)
			{
				bestDiff = diff;
				best = i;
			}
		}

		return best;
	}
}
