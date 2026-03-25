namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Camera that follows the tendril head with smooth tracking, screen shake,
/// and look-ahead.
///
/// Three layered effects on top of the base follow:
///
///   1. LOOK-AHEAD — the camera leads slightly in the movement direction so the
///      player can see what's coming. Smoothly ramps up/down with speed.
///
///   2. MOVEMENT SHAKE — subtle organic tremor while burrowing. Driven by
///      Simplex noise (not random jitter) so it feels like vibration through
///      earth, not camera malfunction. Scales with speed.
///
///   3. COLLISION SHAKE — aggressive, directional. When the tendril hits a wall
///      the camera punches in the direction of impact and springs back.
///      Uses exponential decay with a high-frequency sine wobble.
///
/// All offsets are additive and combined each frame before pixel-snapping.
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

	// --- Base Follow ---
	[Export] public float FollowSpeed = 8.0f;
	[Export] public float ZoomSpeed = 0.1f;
	[Export] public float MinZoom = 0.2f;
	[Export] public float MaxZoom = 4.0f;
	[Export] public float DefaultZoom = 1.0f;

	// --- Look-Ahead ---
	/// <summary>Max pixels the camera leads ahead of the tendril in the movement direction.</summary>
	[Export] public float LookAheadDistance = 40.0f;

	/// <summary>How fast the look-ahead offset catches up to the target (per second).</summary>
	[Export] public float LookAheadSmoothing = 3.5f;

	// --- Movement Shake ---
	/// <summary>Max shake offset in pixels at full speed.</summary>
	[Export] public float MoveShakeIntensity = 1.2f;

	/// <summary>Noise sampling speed — higher = faster tremor.</summary>
	[Export] public float MoveShakeSpeed = 12.0f;

	// --- Collision Shake ---
	/// <summary>Max shake offset in pixels on a full-speed impact.</summary>
	[Export] public float CollisionShakeIntensity = 6.0f;

	/// <summary>How fast the collision shake decays (higher = shorter shake).</summary>
	[Export] public float CollisionShakeDecay = 10.0f;

	/// <summary>Oscillation frequency of the collision shake (Hz).</summary>
	[Export] public float CollisionShakeFrequency = 30.0f;

	/// <summary>How much of the collision shake is directional vs perpendicular (0–1).
	/// 1.0 = purely along the impact direction. 0.5 = equal both axes.</summary>
	[Export] public float CollisionDirectionalBias = 0.7f;

	// --- Pixel-Perfect Zoom Levels ---
	private static readonly float[] PixelPerfectZoomLevels = { 0.5f, 1.0f, 2.0f, 3.0f, 4.0f };

	// --- State ---
	private TendrilController _tendril;
	private Vector2 _targetPosition;
	private int _zoomLevelIndex = 1;

	// Look-ahead
	private Vector2 _lookAheadOffset;       // Current smoothed offset
	private Vector2 _lookAheadTarget;        // Where we want to be

	// Movement shake
	private FastNoiseLite _shakeNoise;
	private float _shakeTime;

	// Collision shake
	private Vector2 _collisionDir;           // Direction of last impact (normalized)
	private float _collisionMagnitude;       // Current shake magnitude (decays to 0)
	private float _collisionPhase;           // Oscillation phase

	// Zoom snap — when true, skip lerp and snap to target this frame
	private bool _snapToTargetNextFrame;

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

		// Initialize shake noise — Simplex gives smooth, organic variation
		_shakeNoise = new FastNoiseLite();
		_shakeNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
		_shakeNoise.Frequency = 0.8f;
		_shakeNoise.Seed = (int)(GD.Randi() % int.MaxValue);

		if (_tendril != null)
		{
			_targetPosition = _tendril.GetFocusPosition();
			GlobalPosition = _targetPosition;
		}
	}

	public override void _Process(double delta)
	{
		if (_tendril == null) return;

		float dt = (float)delta;

		// =====================================================================
		//  BASE TARGET — tendril head pixel position
		// =====================================================================
		_targetPosition = _tendril.GetFocusPosition();

		// =====================================================================
		//  LOOK-AHEAD — lead the camera in the movement direction
		// =====================================================================
		float speed = _tendril.CurrentSpeed;
		Vector2 moveDir = _tendril.LastMoveDirection;

		// Target offset scales with speed so it's zero when stopped
		_lookAheadTarget = moveDir * LookAheadDistance * speed;

		// Smooth interpolation so look-ahead doesn't jerk on direction changes
		_lookAheadOffset = _lookAheadOffset.Lerp(_lookAheadTarget, LookAheadSmoothing * dt);

		// =====================================================================
		//  MOVEMENT SHAKE — subtle noise-based tremor while burrowing
		// =====================================================================
		_shakeTime += dt * MoveShakeSpeed;

		// Sample 2D Simplex noise at offset positions for X and Y independently.
		// The 100-unit separation prevents correlation between axes.
		float shakeX = _shakeNoise.GetNoise2D(_shakeTime, 0f) * MoveShakeIntensity * speed;
		float shakeY = _shakeNoise.GetNoise2D(0f, _shakeTime + 100f) * MoveShakeIntensity * speed;
		Vector2 moveShakeOffset = new Vector2(shakeX, shakeY);

		// =====================================================================
		//  COLLISION SHAKE — directional punch on impact
		// =====================================================================
		Vector2 collisionShakeOffset = Vector2.Zero;

		// Check for new collision from the tendril
		Vector2 impulse = _tendril.CollisionImpulse;
		if (impulse.Length() > 0.05f)
		{
			// New impact — stronger impulse = bigger shake
			float newMagnitude = impulse.Length() * CollisionShakeIntensity;

			// Only override if this impact is stronger than the current decay
			if (newMagnitude > _collisionMagnitude * 0.5f)
			{
				_collisionDir = impulse.Normalized();
				_collisionMagnitude = newMagnitude;
				_collisionPhase = 0f;
			}
		}

		if (_collisionMagnitude > 0.01f)
		{
			_collisionPhase += dt * CollisionShakeFrequency * Mathf.Tau;

			// Exponential decay for a sharp initial punch that fades fast
			_collisionMagnitude *= Mathf.Exp(-CollisionShakeDecay * dt);

			// Oscillate along impact direction (primary) and perpendicular (secondary)
			float primaryOsc = Mathf.Sin(_collisionPhase);
			float secondaryOsc = Mathf.Cos(_collisionPhase * 1.3f); // Slightly different freq

			Vector2 perpDir = new Vector2(-_collisionDir.Y, _collisionDir.X);

			collisionShakeOffset =
				_collisionDir * primaryOsc * _collisionMagnitude * CollisionDirectionalBias
				+ perpDir * secondaryOsc * _collisionMagnitude * (1f - CollisionDirectionalBias);
		}
		else
		{
			_collisionMagnitude = 0f;
		}

		// =====================================================================
		//  COMBINE — base position + all offsets
		// =====================================================================
		Vector2 finalTarget = _targetPosition + _lookAheadOffset + moveShakeOffset + collisionShakeOffset;

		// On zoom change, snap directly to target so the tendril stays centered
		Vector2 next;
		if (_snapToTargetNextFrame)
		{
			next = finalTarget;
			_snapToTargetNextFrame = false;
		}
		else
		{
			next = GlobalPosition.Lerp(finalTarget, FollowSpeed * dt);
		}

		if (!PixelPerfectMode)
		{
			GlobalPosition = next;
			return;
		}

		// Pixel-perfect snapping — snap to nearest screen pixel
		float snapStep = 1.0f / Mathf.Max(Zoom.X, 0.0001f);

		// Only snap if the camera is close enough to the target that snapping
		// won't cause visible jumping. This prevents micro-stutter during smooth follow.
		float distToTarget = next.DistanceTo(finalTarget);
		if (distToTarget < snapStep * 2f)
		{
			GlobalPosition = new Vector2(
				Mathf.Snapped(next.X, snapStep),
				Mathf.Snapped(next.Y, snapStep)
			);
		}
		else
		{
			// Still catching up — don't snap yet, let lerp be smooth
			GlobalPosition = next;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Zoom with scroll wheel
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			if (PixelPerfectMode)
			{
				int prevIndex = _zoomLevelIndex;

				if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
					_zoomLevelIndex = System.Math.Min(_zoomLevelIndex + 1, PixelPerfectZoomLevels.Length - 1);
				else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
					_zoomLevelIndex = System.Math.Max(_zoomLevelIndex - 1, 0);

				if (_zoomLevelIndex != prevIndex)
				{
					float newZoomLevel = PixelPerfectZoomLevels[_zoomLevelIndex];
					Zoom = new Vector2(newZoomLevel, newZoomLevel);

					// Snap camera to tendril on zoom change so it stays centered
					_snapToTargetNextFrame = true;
				}
				return;
			}

			float newZoom = Zoom.X;

			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
				newZoom += ZoomSpeed;
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
				newZoom -= ZoomSpeed;

			float clampedZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
			if (!Mathf.IsEqualApprox(clampedZoom, Zoom.X))
			{
				Zoom = new Vector2(clampedZoom, clampedZoom);
				_snapToTargetNextFrame = true;
			}
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
