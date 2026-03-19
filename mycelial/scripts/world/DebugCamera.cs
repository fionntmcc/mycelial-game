namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Simple fly camera for testing world generation.
/// Attach to a Camera2D node. WASD to move, scroll to zoom.
/// 
/// Delete this when you implement proper game camera controls.
/// </summary>
public partial class DebugCamera : Camera2D
{
	[Export] public float MoveSpeed = 500.0f;
	[Export] public float ZoomSpeed = 0.1f;
	[Export] public float MinZoom = 0.1f;
	[Export] public float MaxZoom = 4.0f;

	public override void _Ready()
	{
		// Start the camera at the Origin Tree (center of world, slightly above surface)
		GlobalPosition = new Vector2(
			WorldConfig.TreeWorldX * WorldConfig.TileSize,
			-WorldConfig.TreeTrunkHeight * WorldConfig.TileSize / 2.0f // Above ground, looking at trunk
		);
		Zoom = new Vector2(0.5f, 0.5f); // Zoomed out to see the whole tree
	}

	public override void _Process(double delta)
	{
		// --- Movement (WASD) ---
		Vector2 velocity = Vector2.Zero;
		float dt = (float)delta;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			velocity.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			velocity.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			velocity.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			velocity.X += 1;

		// Speed boost with Shift
		float speed = MoveSpeed;
		if (Input.IsKeyPressed(Key.Shift))
			speed *= 3.0f;

		// Scale speed by zoom level (move faster when zoomed out)
		speed /= Zoom.X;

		if (velocity.LengthSquared() > 0)
		{
			velocity = velocity.Normalized();
			GlobalPosition += velocity * speed * dt;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// --- Zoom (scroll wheel) ---
		if (@event is InputEventMouseButton mouseEvent)
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

	/// <summary>
	/// Draw debug info overlay.
	/// </summary>
	public override void _Draw()
	{
		// This runs in camera-local space. Not ideal for HUD.
		// Use a CanvasLayer + Label node for proper HUD instead.
		// This is just for quick testing.
	}
}
