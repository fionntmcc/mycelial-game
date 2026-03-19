namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Camera that follows the tendril head with smooth tracking.
/// Scroll to zoom in/out. The camera smoothly lerps to the tendril position
/// so movement feels fluid, not jerky.
///
/// SETUP:
///   - Attach to a Camera2D node
///   - Assign TendrilControllerPath to your TendrilController node
///   - Enable "Current" on the Camera2D
/// </summary>
public partial class TendrilCamera : Camera2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }

	[Export] public float FollowSpeed = 8.0f;   // How fast camera catches up (higher = snappier)
	[Export] public float ZoomSpeed = 0.1f;
	[Export] public float MinZoom = 0.2f;
	[Export] public float MaxZoom = 4.0f;
	[Export] public float DefaultZoom = 1.0f;

	private TendrilController _tendril;
	private Vector2 _targetPosition;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		Zoom = new Vector2(DefaultZoom, DefaultZoom);
		MakeCurrent();

		if (_tendril != null)
		{
			// Start at tree center until tendril initializes
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

		// Target the tendril head position
		_targetPosition = _tendril.GetHeadPixelPosition();

		// Smooth follow
		GlobalPosition = GlobalPosition.Lerp(_targetPosition, FollowSpeed * (float)delta);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Zoom with scroll wheel
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			float newZoom = Zoom.X;

			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
				newZoom += ZoomSpeed;
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
				newZoom -= ZoomSpeed;

			newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
			Zoom = new Vector2(newZoom, newZoom);
		}
	}
}
