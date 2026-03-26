namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Handles player input, momentum physics, creature auto-steering, and
/// sub-grid stepping.  Extracted from TendrilController so all movement
/// tuning lives in one place.
/// </summary>
public partial class TendrilMovement : Node
{
	// --- Movement Config ---
	[Export] public float MoveDelay = 0.08f;
	[Export] public float MomentumAcceleration = 8.0f;
	[Export] public float MomentumSteering = 14.0f;
	[Export] public float MomentumTurnAroundBrake = 10.0f;
	[Export] public float MomentumReverseLockThreshold = -0.55f;
	[Export] public float MomentumDrag = 3.2f;
	[Export] public float MomentumDeadZone = 0.08f;
	[Export] public float ControllerDeadZone = 0.22f;
	[Export] public float MinMoveDelayMultiplier = 0.58f;
	[Export] public float MaxMoveDelayMultiplier = 1.0f;
	[Export] public float BlockedMomentumDamping = 0.45f;
	[Export] public int MaxSubStepsPerFrame = 8;

	// --- Creature Auto-Steer ---
	/// <summary>Radius in terrain tiles to scan for creatures to steer toward.</summary>
	[Export] public int CreatureSteerRadius = 6;

	/// <summary>How strongly the tendril steers toward nearby creatures (0–1).</summary>
	[Export] public float CreatureSteerStrength = 0.15f;

	// ---- State ----
	private TendrilController _controller;
	private CreatureManager _creatureManager;

	/// <summary>Current momentum vector (direction × speed 0–1).</summary>
	public Vector2 Momentum;

	/// <summary>Fractional sub-cell offset accumulated between discrete steps.</summary>
	public Vector2 MoveAccumulator;

	/// <summary>Continuous movement direction (not grid-locked).</summary>
	public Vector2 LastMoveDir = new Vector2(0, 1);

	/// <summary>Normalized speed 0–1 for camera shake scaling.</summary>
	public float CurrentSpeed => Mathf.Clamp(Momentum.Length(), 0f, 1f);

	// =========================================================================
	//  LIFECYCLE
	// =========================================================================

	public void Initialize(TendrilController controller, CreatureManager creatureManager)
	{
		_controller = controller;
		_creatureManager = creatureManager;
	}

	/// <summary>Zero out both momentum and the fractional accumulator.</summary>
	public void ClearMomentum()
	{
		Momentum = Vector2.Zero;
		MoveAccumulator = Vector2.Zero;
	}

	// =========================================================================
	//  PROCESS — called once per frame by TendrilController
	// =========================================================================

	public void Process(float dt)
	{
		Vector2 input = GetInputVector();

		if (input != Vector2.Zero)
		{
			Vector2 inputDir = input.Normalized();

			if (Momentum == Vector2.Zero)
			{
				Momentum = Momentum.MoveToward(input, MomentumAcceleration * dt);
			}
			else
			{
				Vector2 momentumDir = Momentum.Normalized();
				float alignment = momentumDir.Dot(inputDir);

				if (alignment <= MomentumReverseLockThreshold)
				{
					Momentum = Momentum.MoveToward(Vector2.Zero, MomentumTurnAroundBrake * dt);

					if (Momentum.Length() <= MomentumDeadZone * 1.25f)
						Momentum = Momentum.MoveToward(input, MomentumAcceleration * dt);
				}
				else
				{
					float fromAngle = Mathf.Atan2(momentumDir.Y, momentumDir.X);
					float toAngle = Mathf.Atan2(inputDir.Y, inputDir.X);
					float steerT = Mathf.Clamp(MomentumSteering * dt, 0f, 1f);
					float steeredAngle = Mathf.LerpAngle(fromAngle, toAngle, steerT);
					Vector2 steeredDir = new Vector2(Mathf.Cos(steeredAngle), Mathf.Sin(steeredAngle));

					float targetMagnitude = input.Length();
					float nextMagnitude = Mathf.MoveToward(Momentum.Length(), targetMagnitude, MomentumAcceleration * dt);
					Momentum = steeredDir * nextMagnitude;
				}
			}
		}
		else
		{
			Momentum = Momentum.MoveToward(Vector2.Zero, MomentumDrag * dt);
		}

		// --- Creature Auto-Steer (sub-grid precision) ---
		if (_creatureManager != null && Momentum.Length() > MomentumDeadZone && CreatureSteerStrength > 0f)
		{
			int steerRadiusSub = CreatureSteerRadius * WorldConfig.SubGridScale;
			var nearest = _creatureManager.GetNearestCreatureSubPosition(
				_controller.SubHeadX, _controller.SubHeadY, steerRadiusSub);
			if (nearest.HasValue)
			{
				Vector2 creatureSubPos = new Vector2(nearest.Value.SubX, nearest.Value.SubY);
				Vector2 headPos = new Vector2(_controller.SubHeadX, _controller.SubHeadY);
				Vector2 toCreature = (creatureSubPos - headPos).Normalized();

				float steerAmount = CreatureSteerStrength * Momentum.Length();
				Momentum += toCreature * steerAmount * dt;

				if (Momentum.Length() > 1f)
					Momentum = Momentum.Normalized() * 1f;
			}
		}

		float speed = Mathf.Clamp(Momentum.Length(), 0f, 1f);
		if (speed <= MomentumDeadZone)
		{
			if (input == Vector2.Zero)
				MoveAccumulator = Vector2.Zero;
			return;
		}

		if (Momentum.Length() > MomentumDeadZone)
			LastMoveDir = Momentum.Normalized();

		float delayMultiplier = Mathf.Lerp(MaxMoveDelayMultiplier, MinMoveDelayMultiplier, speed);
		float effectiveMoveDelay = Mathf.Max(0.005f, MoveDelay * delayMultiplier);
		effectiveMoveDelay /= _controller.SpeedMultiplier;
		float stepsPerSecond = 1.0f / effectiveMoveDelay;
		MoveAccumulator += Momentum * stepsPerSecond * dt;

		int iterations = 0;
		while ((Mathf.Abs(MoveAccumulator.X) >= 1f || Mathf.Abs(MoveAccumulator.Y) >= 1f) && iterations < MaxSubStepsPerFrame)
		{
			iterations++;

			int stepX = Mathf.Abs(MoveAccumulator.X) >= 1f ? System.Math.Sign(MoveAccumulator.X) : 0;
			int stepY = Mathf.Abs(MoveAccumulator.Y) >= 1f ? System.Math.Sign(MoveAccumulator.Y) : 0;

			bool moved = false;

			if (stepX != 0 || stepY != 0)
			{
				moved = _controller.TrySubMoveStep(
					_controller.SubHeadX + stepX, _controller.SubHeadY + stepY);
				if (moved)
				{
					if (stepX != 0) MoveAccumulator.X -= stepX;
					if (stepY != 0) MoveAccumulator.Y -= stepY;
					continue;
				}
			}

			if (stepX != 0)
			{
				moved = _controller.TrySubMoveStep(_controller.SubHeadX + stepX, _controller.SubHeadY);
				if (moved)
				{
					MoveAccumulator.X -= stepX;
					continue;
				}
			}

			if (stepY != 0)
			{
				moved = _controller.TrySubMoveStep(_controller.SubHeadX, _controller.SubHeadY + stepY);
				if (moved)
				{
					MoveAccumulator.Y -= stepY;
					continue;
				}
			}

			// Fully blocked — record collision for camera shake
			Vector2 blockedDir = new Vector2(stepX, stepY).Normalized();
			float impactForce = Momentum.Length();
			_controller.CollisionImpulse = blockedDir * impactForce;

			MoveAccumulator *= 0.35f;
			Momentum *= BlockedMomentumDamping;
			break;
		}
	}

	// =========================================================================
	//  INPUT
	// =========================================================================

	private Vector2 GetInputVector()
	{
		int keyX = 0;
		int keyY = 0;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			keyY -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			keyY += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			keyX -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			keyX += 1;

		Vector2 keyboard = new Vector2(keyX, keyY);
		if (keyboard != Vector2.Zero)
			keyboard = keyboard.Normalized();

		Vector2 stick = Vector2.Zero;
		var joypads = Input.GetConnectedJoypads();
		if (joypads.Count > 0)
		{
			int joyId = joypads[0];
			stick = new Vector2(
				Input.GetJoyAxis(joyId, JoyAxis.LeftX),
				Input.GetJoyAxis(joyId, JoyAxis.LeftY)
			);

			float len = stick.Length();
			if (len < ControllerDeadZone)
			{
				stick = Vector2.Zero;
			}
			else if (len > 0f)
			{
				float normalizedLen = (len - ControllerDeadZone) / (1f - ControllerDeadZone);
				normalizedLen = Mathf.Clamp(normalizedLen, 0f, 1f);
				stick = stick.Normalized() * normalizedLen;
			}
		}

		Vector2 combined = keyboard + stick;
		if (combined.Length() > 1f)
			combined = combined.Normalized();

		return combined;
	}
}
