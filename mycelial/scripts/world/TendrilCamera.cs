namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Camera that follows the tendril with smooth tracking, screenshake, and dynamic zoom.
///
/// UPDATED with:
///   - Screenshake on wall impacts (proportional to impact force)
///   - Speed-based zoom drift (zooms out slightly when moving fast)
///   - Subtle look-ahead (camera drifts in the direction of movement)
///   - Impact freeze-frame (tiny pause on hard wall hits for punch)
///
/// SETUP:
///   - Attach to a Camera2D node
///   - Assign TendrilControllerPath
///   - Enable "Current" on the Camera2D
/// </summary>
public partial class TendrilCamera : Camera2D
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public bool PixelPerfectMode = true;

	[ExportGroup("Follow")]
	[Export] public float FollowSpeed = 8.0f;

	/// <summary>How far ahead of the tendril the camera looks (pixels).</summary>
	[Export] public float LookAheadDistance = 24f;

	/// <summary>How fast the look-ahead adjusts to direction changes.</summary>
	[Export] public float LookAheadSmoothing = 4.0f;

	[ExportGroup("Zoom")]
	[Export] public float ZoomSpeed = 0.1f;
	[Export] public float MinZoom = 0.2f;
	[Export] public float MaxZoom = 4.0f;
	[Export] public float DefaultZoom = 1.0f;

	/// <summary>How much the camera zooms out at max speed (additive).</summary>
	[Export] public float SpeedZoomOutAmount = 0.15f;

	/// <summary>Speed (px/sec) at which max zoom-out is reached.</summary>
	[Export] public float SpeedZoomOutMaxSpeed = 350f;

	/// <summary>How fast zoom adjusts to speed changes.</summary>
	[Export] public float ZoomDriftSmoothing = 3.0f;

	[ExportGroup("Screenshake")]

	/// <summary>Enable screenshake on wall impacts.</summary>
	[Export] public bool EnableScreenshake = true;

	/// <summary>Max shake offset in pixels for the hardest impacts.</summary>
	[Export] public float MaxShakeAmplitude = 4.0f;

	/// <summary>How fast shake decays (higher = shorter shakes).</summary>
	[Export] public float ShakeDecayRate = 12.0f;

	/// <summary>Frequency of shake oscillation in Hz.</summary>
	[Export] public float ShakeFrequency = 30.0f;

	/// <summary>Min speed delta to trigger screenshake (filters out gentle slides).</summary>
	[Export] public float ShakeMinImpactSpeed = 80.0f;

	[ExportGroup("Impact Freeze")]

	/// <summary>Enable tiny time-freeze on hard wall impacts (adds punch).</summary>
	[Export] public bool EnableImpactFreeze = true;

	/// <summary>Duration of freeze in seconds (very short — 0.03-0.06 feels good).</summary>
	[Export] public float ImpactFreezeDuration = 0.04f;

	/// <summary>Min impact speed to trigger freeze (only hard hits).</summary>
	[Export] public float ImpactFreezeMinSpeed = 150f;

	// =========================================================================
	//  INTERNAL STATE
	// =========================================================================

	private static readonly float[] PixelPerfectZoomLevels = { 0.5f, 1.0f, 2.0f, 3.0f, 4.0f };

	private TendrilController _tendril;
	private TendrilHead _head;

	private Vector2 _targetPosition;
	private int _zoomLevelIndex = 1;

	// Screenshake
	private float _shakeIntensity;
	private float _shakeTimer;
	private Vector2 _shakeOffset;

	// Speed zoom
	private float _currentZoomDrift;
	private float _baseZoom;

	// Look-ahead
	private Vector2 _lookAheadOffset;

	// Impact freeze
	private float _freezeTimer;

	// Previous frame speed for detecting impacts
	private float _prevSpeed;
	private bool _prevSliding;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);

		if (PixelPerfectMode)
			TextureFilter = TextureFilterEnum.Nearest;

		_zoomLevelIndex = FindNearestZoomIndex(Mathf.Clamp(DefaultZoom, MinZoom, MaxZoom));
		_baseZoom = PixelPerfectMode
			? PixelPerfectZoomLevels[_zoomLevelIndex]
			: DefaultZoom;

		Zoom = new Vector2(_baseZoom, _baseZoom);
		MakeCurrent();

		if (_tendril != null)
		{
			_targetPosition = new Vector2(
				WorldConfig.TreeWorldX * WorldConfig.TileSize, 0
			);
			GlobalPosition = _targetPosition;
		}
	}

	public override void _Process(double delta)
	{
		if (_tendril == null) return;
		float dt = (float)delta;

		// Try to find TendrilHead if we haven't yet
		_head ??= _tendril.GetNodeOrNull<TendrilHead>("TendrilHead");

		// --- Impact freeze ---
		if (_freezeTimer > 0)
		{
			_freezeTimer -= dt;
			// During freeze, don't update camera position (creates a brief "hit" feel)
			// Still decay shake so it's ready when freeze ends
			UpdateShake(dt);
			ApplyShakeOffset();
			return;
		}

		// --- Detect wall impacts ---
		if (_head != null && EnableScreenshake)
			DetectWallImpacts();

		// --- Target position with look-ahead ---
		Vector2 headPos = _tendril.GetHeadPixelPosition();

		if (_head != null && _head.Speed > 20f)
		{
			Vector2 moveDir = _head.Velocity.Normalized();
			Vector2 targetLookAhead = moveDir * LookAheadDistance;
			_lookAheadOffset = _lookAheadOffset.Lerp(targetLookAhead, LookAheadSmoothing * dt);
		}
		else
		{
			_lookAheadOffset = _lookAheadOffset.Lerp(Vector2.Zero, LookAheadSmoothing * dt);
		}

		_targetPosition = headPos + _lookAheadOffset;

		// --- Smooth follow ---
		Vector2 next = GlobalPosition.Lerp(_targetPosition, FollowSpeed * dt);

		// --- Speed-based zoom drift ---
		float currentSpeed = _head?.Speed ?? 0f;
		float targetZoomDrift = 0f;
		if (currentSpeed > 10f)
		{
			float speedT = Mathf.Clamp(currentSpeed / SpeedZoomOutMaxSpeed, 0f, 1f);
			speedT = speedT * speedT; // Ease-in so gentle movement barely zooms
			targetZoomDrift = -SpeedZoomOutAmount * speedT; // Negative = zoom out
		}
		_currentZoomDrift = Mathf.Lerp(_currentZoomDrift, targetZoomDrift, ZoomDriftSmoothing * dt);

		float effectiveZoom = _baseZoom + _currentZoomDrift;
		effectiveZoom = Mathf.Clamp(effectiveZoom, MinZoom, MaxZoom);
		Zoom = new Vector2(effectiveZoom, effectiveZoom);

		// --- Screenshake ---
		UpdateShake(dt);
		next += _shakeOffset;

		// --- Pixel-perfect snapping ---
		if (PixelPerfectMode)
		{
			float snapStep = 1.0f / Mathf.Max(effectiveZoom, 0.0001f);
			next = new Vector2(
				Mathf.Snapped(next.X, snapStep),
				Mathf.Snapped(next.Y, snapStep)
			);
		}

		GlobalPosition = next;

		// Cache for next frame
		_prevSpeed = currentSpeed;
		_prevSliding = _head?.IsSlidingAlongWall ?? false;
	}

	// =========================================================================
	//  SCREENSHAKE
	// =========================================================================

	private void DetectWallImpacts()
	{
		if (_head == null) return;

		float currentSpeed = _head.Speed;
		bool currentlySliding = _head.IsSlidingAlongWall;

		// Trigger shake when: just started sliding AND lost significant speed
		if (currentlySliding && !_prevSliding)
		{
			float speedLost = _prevSpeed - currentSpeed;
			if (speedLost > ShakeMinImpactSpeed)
			{
				float impactForce = Mathf.Clamp(speedLost / 300f, 0f, 1f);
				TriggerShake(impactForce);

				// Impact freeze for hard hits
				if (EnableImpactFreeze && speedLost > ImpactFreezeMinSpeed)
				{
					_freezeTimer = ImpactFreezeDuration;
				}
			}
		}
	}

	/// <summary>
	/// Trigger screenshake. Intensity 0-1, where 1 = MaxShakeAmplitude.
	/// Can be called externally (e.g., creature damage, explosions).
	/// </summary>
	public void TriggerShake(float intensity)
	{
		// Only override if this shake is stronger than the current one
		float newIntensity = Mathf.Clamp(intensity, 0f, 1f);
		if (newIntensity > _shakeIntensity)
		{
			_shakeIntensity = newIntensity;
			_shakeTimer = 0f;
		}
	}

	private void UpdateShake(float dt)
	{
		if (_shakeIntensity <= 0.01f)
		{
			_shakeIntensity = 0f;
			_shakeOffset = Vector2.Zero;
			return;
		}

		_shakeTimer += dt;

		// Decaying sinusoidal shake
		float decay = Mathf.Exp(-ShakeDecayRate * _shakeTimer);
		float amplitude = MaxShakeAmplitude * _shakeIntensity * decay;

		float angle = _shakeTimer * ShakeFrequency * Mathf.Tau;
		_shakeOffset = new Vector2(
			Mathf.Sin(angle) * amplitude,
			Mathf.Cos(angle * 1.3f) * amplitude * 0.7f // Slightly different Y frequency
		);

		// Kill shake when it's imperceptible
		if (amplitude < 0.1f)
		{
			_shakeIntensity = 0f;
			_shakeOffset = Vector2.Zero;
		}
	}

	private void ApplyShakeOffset()
	{
		// Used during freeze frames to keep shake visible
		if (_shakeOffset.LengthSquared() > 0.01f)
		{
			Vector2 pos = GlobalPosition + _shakeOffset;
			if (PixelPerfectMode)
			{
				float snapStep = 1.0f / Mathf.Max(Zoom.X, 0.0001f);
				pos = new Vector2(
					Mathf.Snapped(pos.X, snapStep),
					Mathf.Snapped(pos.Y, snapStep)
				);
			}
			GlobalPosition = pos;
		}
	}

	// =========================================================================
	//  ZOOM CONTROLS
	// =========================================================================

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

				_baseZoom = PixelPerfectZoomLevels[_zoomLevelIndex];
				return;
			}

			float newZoom = _baseZoom;
			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
				newZoom += ZoomSpeed;
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
				newZoom -= ZoomSpeed;

			_baseZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
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
			if (diff < bestDiff) { bestDiff = diff; best = i; }
		}
		return best;
	}
}
