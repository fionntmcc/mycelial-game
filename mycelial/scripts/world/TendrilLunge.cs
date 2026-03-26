namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Hold-to-charge, release-to-lunge momentum ability.
/// L2 (controller) or Left Shift (keyboard) to charge while moving normally.
/// Release to lunge — sigmoidal speed boost that ramps up, peaks, and returns.
/// Costs vitality for the duration of the lunge. Damages creatures on contact.
/// Only active during normal movement (not harpoon, retreat, or regen).
/// </summary>
public partial class TendrilLunge : Node
{
	// --- Charge Config ---
	[Export] public float MaxChargeTime = 1.5f;
	[Export] public float MinChargeThreshold = 0.15f;

	// --- Lunge Config ---
	[Export] public float MinPeakBoost = 0.6f;
	[Export] public float MaxPeakBoost = 1.8f;
	[Export] public float MinLungeDuration = 0.5f;
	[Export] public float MaxLungeDuration = 0.9f;

	// --- Cost ---
	[Export] public float VitalityDrainPerSecond = 10f;

	// --- Cooldown ---
	[Export] public float CooldownTime = 1.0f;

	// --- Combat ---
	[Export] public int LungeDamage = 2;
	[Export] public float LungeKnockback = 80f;
	[Export] public int LungeHitRadius = 5;
	[Export] public float VigorGainLungeKill = 15f;

	// --- Input ---
	[Export] public float TriggerDeadZone = 0.3f;

	// =========================================================================
	//  STATE
	// =========================================================================

	private enum LungeState { Idle, Charging, Lunging, Cooldown }
	private LungeState _state = LungeState.Idle;

	private TendrilController _controller;
	private TendrilHarpoon _harpoon;
	private CreatureManager _creatureManager;

	private float _chargeTime;
	private float _chargeAmount;

	private float _lungeTimer;
	private float _lungeDuration;
	private float _peakBoost;

	private float _cooldownTimer;

	private bool _wasL2Held;

	private readonly HashSet<Creature> _hitCreatures = new();

	// --- Public read-only state ---

	/// <summary>Additive speed boost (0 when not lunging). Consumed by TendrilMovement.</summary>
	public float CurrentBoost { get; private set; }

	/// <summary>True during the active lunge phase.</summary>
	public bool IsLunging => _state == LungeState.Lunging;

	/// <summary>True while holding L2 to charge.</summary>
	public bool IsCharging => _state == LungeState.Charging;

	/// <summary>Charge progress 0–1 for HUD/visual feedback.</summary>
	public float ChargePercent => _state == LungeState.Charging
		? Mathf.Clamp(_chargeTime / MaxChargeTime, 0f, 1f)
		: 0f;

	// =========================================================================
	//  LIFECYCLE
	// =========================================================================

	public void Initialize(TendrilController controller, TendrilHarpoon harpoon, CreatureManager creatureManager)
	{
		_controller = controller;
		_harpoon = harpoon;
		_creatureManager = creatureManager;
	}

	public void Process(float dt)
	{
		// Cancel if controller entered retreat/regen
		if (_controller.IsRetreating || _controller.IsRegenerating)
		{
			Cancel();
			return;
		}

		switch (_state)
		{
			case LungeState.Idle:
				ProcessIdle();
				break;
			case LungeState.Charging:
				ProcessCharging(dt);
				break;
			case LungeState.Lunging:
				ProcessLunging(dt);
				break;
			case LungeState.Cooldown:
				ProcessCooldown(dt);
				break;
		}
	}

	/// <summary>Force-cancel the lunge (e.g. on death/retreat).</summary>
	public void Cancel()
	{
		_state = LungeState.Idle;
		CurrentBoost = 0f;
		_chargeTime = 0f;
		_hitCreatures.Clear();
	}

	// =========================================================================
	//  STATE MACHINE
	// =========================================================================

	private void ProcessIdle()
	{
		CurrentBoost = 0f;

		// Don't allow charge during harpoon
		if (_harpoon != null && _harpoon.IsActive)
			return;

		bool l2Held = IsL2Held();
		if (l2Held && !_wasL2Held)
		{
			_state = LungeState.Charging;
			_chargeTime = 0f;
		}
		_wasL2Held = l2Held;
	}

	private void ProcessCharging(float dt)
	{
		// Cancel if harpoon activates mid-charge
		if (_harpoon != null && _harpoon.IsActive)
		{
			Cancel();
			return;
		}

		bool l2Held = IsL2Held();
		_chargeTime += dt;

		// Cap charge time
		if (_chargeTime > MaxChargeTime)
			_chargeTime = MaxChargeTime;

		if (!l2Held)
		{
			// Released — check minimum threshold
			if (_chargeTime >= MinChargeThreshold)
			{
				BeginLunge();
			}
			else
			{
				// Too short — cancel
				_state = LungeState.Idle;
				_chargeTime = 0f;
			}
		}

		_wasL2Held = l2Held;
	}

	private void ProcessLunging(float dt)
	{
		_lungeTimer += dt;

		if (_lungeTimer >= _lungeDuration)
		{
			// Lunge complete
			CurrentBoost = 0f;
			_hitCreatures.Clear();
			_cooldownTimer = 0f;
			_state = LungeState.Cooldown;
			return;
		}

		// Sigmoidal boost: sin²(π * t / duration)
		float t = _lungeTimer / _lungeDuration;
		float sinVal = Mathf.Sin(Mathf.Pi * t);
		CurrentBoost = _peakBoost * sinVal * sinVal;

		// Vitality drain
		_controller.Vitals.DamageVitality(VitalityDrainPerSecond * dt);

		// Creature collision
		if (_creatureManager != null)
			CheckCreatureCollisions();
	}

	private void ProcessCooldown(float dt)
	{
		CurrentBoost = 0f;
		_cooldownTimer += dt;
		if (_cooldownTimer >= CooldownTime)
		{
			_state = LungeState.Idle;
			_wasL2Held = IsL2Held(); // Prevent immediate re-trigger if still holding
		}
	}

	// =========================================================================
	//  LUNGE ACTIVATION
	// =========================================================================

	private void BeginLunge()
	{
		_chargeAmount = Mathf.Clamp(_chargeTime / MaxChargeTime, 0f, 1f);
		_peakBoost = Mathf.Lerp(MinPeakBoost, MaxPeakBoost, _chargeAmount);
		_lungeDuration = Mathf.Lerp(MinLungeDuration, MaxLungeDuration, _chargeAmount);
		_lungeTimer = 0f;
		_hitCreatures.Clear();
		_state = LungeState.Lunging;

		GD.Print($"Lunge! charge={_chargeAmount:F2} peak={_peakBoost:F2} duration={_lungeDuration:F2}s");
	}

	// =========================================================================
	//  CREATURE COLLISION
	// =========================================================================

	private void CheckCreatureCollisions()
	{
		int headSubX = _controller.SubHeadX;
		int headSubY = _controller.SubHeadY;

		foreach (var creature in _creatureManager.GetAllCreatures())
		{
			if (creature == null || !creature.IsAlive || !creature.IsActive) continue;
			if (_hitCreatures.Contains(creature)) continue;

			int radius = (creature.Body?.Radius ?? 3) + LungeHitRadius;
			int dx = creature.SubX - headSubX;
			int dy = creature.SubY - headSubY;
			if (dx * dx + dy * dy > radius * radius) continue;

			// Hit!
			bool died = _creatureManager.DamageCreature(creature, LungeDamage);
			if (died)
			{
				var config = CreatureRegistry.GetConfig(creature.Species);
				_controller.Vitals.AddVigor(config.VigorOnConsume + VigorGainLungeKill);
				_creatureManager.KillCreatureExternal(creature);
			}
			else
			{
				Vector2 knockDir = new Vector2(dx, dy);
				if (knockDir.LengthSquared() < 0.001f)
					knockDir = _controller.LastMoveDirection;
				else
					knockDir = knockDir.Normalized();

				_creatureManager.ApplyImpulse(creature, knockDir * LungeKnockback);
			}

			_hitCreatures.Add(creature);
		}
	}

	// =========================================================================
	//  INPUT
	// =========================================================================

	private bool IsL2Held()
	{
		// Keyboard: Left Shift
		if (Input.IsKeyPressed(Key.Shift))
			return true;

		// Controller: Left Trigger
		var joypads = Input.GetConnectedJoypads();
		if (joypads.Count > 0)
		{
			float l2 = Input.GetJoyAxis(joypads[0], JoyAxis.TriggerLeft);
			if (l2 > TriggerDeadZone)
				return true;
		}

		return false;
	}
}
