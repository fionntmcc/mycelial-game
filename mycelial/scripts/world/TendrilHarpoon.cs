namespace Mycorrhiza.World;

using Godot;
using System;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Harpoon-tendril with two firing modes and a hold-and-throw slam system.
///
/// FIRING:
///   TAP fire: Quick press-release → fast straight shot
///   HOLD fire: Hold past threshold → guided steerable shot
///
/// CATCHING + THROWING:
///   When the harpoon grabs a creature:
///   - If fire is RELEASED → normal retract → consume creature for hunger
///   - If fire is HELD → harpoon freezes, creature stays at tip
///     → Player aims with movement input
///     → Releasing fire LAUNCHES the creature as a projectile
///     → Projectile checks for creature slam and wall splat
///
/// This creates a natural decision: release early to eat, hold to weaponize.
///
/// SETUP:
///   - Add as sibling Node of TendrilController
///   - Assign TendrilControllerPath, ChunkManagerPath, CreatureManagerPath
///   - Optionally assign ParticlesPath for death/slam effects
///   - On TendrilController, assign HarpoonPath pointing to this node
/// </summary>
public partial class TendrilHarpoon : Node
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath CreatureManagerPath { get; set; }
	[Export] public NodePath ParticlesPath { get; set; }

	// --- Timing ---
	[Export] public float GuidedModeThreshold = 0.18f;

	// --- Tap Shot Config ---
	[Export] public int TapRange = 50;
	[Export] public float TapStepDelay = 0.003f;
	[Export] public int TapSubStepsPerTick = 5;

	// --- Guided Shot Config ---
	[Export] public int GuidedMaxRange = 180;
	[Export] public float GuidedStepDelay = 0.008f;
	[Export] public int GuidedSubStepsPerTick = 2;
	[Export] public float GuidedTurnRate = 4.5f;

	// --- Shape ---
	[Export(PropertyHint.Range, "0,6,1")] public int HarpoonThickness = 2;
	[Export(PropertyHint.Range, "0,2,0.1")] public float ShapeIrregularity = 0.8f;

	// --- Retract ---
	[Export] public float RetractStepDelay = 0.005f;
	[Export] public int RetractSubStepsPerTick = 6;

	// --- Cost ---
	[Export] public float HungerCost = 5f;
	[Export] public int CaveDetectRadius = 3;

	// --- Input ---
	[Export] public float TriggerDeadZone = 0.3f;
	[Export] public float StickDeadZone = 0.22f;

	// --- Slam / Throw Config ---

	/// <summary>Speed of a thrown creature in sub-cells per second.</summary>
	[Export] public float ThrowSpeed = 200f;

	/// <summary>Max range of a thrown creature in sub-cells.</summary>
	[Export] public int ThrowRange = 120;

	/// <summary>Damage dealt to the target creature on slam.</summary>
	[Export] public int SlamDamage = 3;

	/// <summary>Damage dealt to the thrown creature on impact.</summary>
	[Export] public int SlamSelfDamage = 1;

	/// <summary>Knockback applied to the target on slam.</summary>
	[Export] public float SlamKnockback = 80f;

	/// <summary>Damage dealt to the thrown creature on wall splat (on top of SlamSelfDamage).</summary>
	[Export] public int WallSplatExtraDamage = 1;

	// --- Retract Slam Config (existing slam-during-retract) ---
	[Export] public int RetractSlamDamageToTarget = 2;
	[Export] public int RetractSlamDamageToProjectile = 1;
	[Export] public float RetractSlamKnockbackForce = 60f;

	// --- State Machine ---
	private enum HarpoonState
	{
		Idle,
		Winding,      // Fire button held, deciding tap vs guided
		Guided,       // Extending with player steering
		Straight,     // Extending in a straight line
		Armed,        // Creature grabbed, fire held — waiting for aim + release
		Throwing,     // Creature launched as projectile
		Retracting,
	}

	private TendrilController _tendril;
	private ChunkManager _chunkManager;
	private CreatureManager _creatureManager;
	private CreatureParticles _particles;

	private HarpoonState _state = HarpoonState.Idle;
	private float _windTimer;
	private float _stepTimer;

	// DDA state
	private Vector2 _shotDirection;
	private float _ddaX, _ddaY;
	private int _currentSubX, _currentSubY;

	// Harpoon path
	private readonly List<(int SubX, int SubY)> _harpoonPath = new();
	private int _targetRange;
	private int _stepsPerTick;

	// Grabbed creature
	private Creature _grabbedCreature;

	// Terrain tracking
	private int _lastCheckedTerrainX;
	private int _lastCheckedTerrainY;

	// Throw projectile state
	private float _projX, _projY;
	private int _projSubX, _projSubY;
	private Vector2 _throwDirection;
	private float _projDistanceTraveled;
	private float _projStepAccumulator;

	// Input state
	private bool _wasSpaceHeld;
	private bool _wasTriggerHeld;

	// --- Signals ---
	[Signal] public delegate void ChargeStartedEventHandler();
	[Signal] public delegate void ChargeCancelledEventHandler();
	[Signal] public delegate void HarpoonFiredEventHandler(int range);
	[Signal] public delegate void CreatureGrabbedEventHandler(int creatureSubX, int creatureSubY);
	[Signal] public delegate void CreatureThrownEventHandler(float dirX, float dirY);
	[Signal] public delegate void HarpoonRetractedEventHandler(bool caughtSomething);
	[Signal] public delegate void CreatureSlammedEventHandler(int targetSubX, int targetSubY);
	[Signal] public delegate void WallSplatEventHandler(int subX, int subY);

	/// <summary>True when the harpoon is doing anything.</summary>
	public bool IsActive => _state != HarpoonState.Idle;

	/// <summary>True when armed and waiting for throw.</summary>
	public bool IsArmed => _state == HarpoonState.Armed;

	/// <summary>0–1 charge indicator for HUD.</summary>
	public float ChargePercent => _state == HarpoonState.Winding
		? Mathf.Clamp(_windTimer / GuidedModeThreshold, 0f, 1f) : 0f;

	public override void _Ready()
	{
		if (TendrilControllerPath != null)
			_tendril = GetNode<TendrilController>(TendrilControllerPath);
		if (ChunkManagerPath != null)
			_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
		if (CreatureManagerPath != null)
			_creatureManager = GetNode<CreatureManager>(CreatureManagerPath);
		if (ParticlesPath != null)
			_particles = GetNode<CreatureParticles>(ParticlesPath);

		if (_tendril == null || _chunkManager == null)
			GD.PrintErr("TendrilHarpoon: Missing TendrilController or ChunkManager!");
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _chunkManager == null) return;
		if (_tendril.IsRetreating || _tendril.IsRegenerating)
		{
			// If retreating, cancel everything
			if (_state != HarpoonState.Idle)
				CancelAndCleanup();
			return;
		}

		float dt = (float)delta;

		switch (_state)
		{
			case HarpoonState.Idle:
				ProcessIdle();
				break;
			case HarpoonState.Winding:
				ProcessWinding(dt);
				break;
			case HarpoonState.Straight:
				ProcessExtending(dt);
				break;
			case HarpoonState.Guided:
				ProcessGuided(dt);
				break;
			case HarpoonState.Armed:
				ProcessArmed(dt);
				break;
			case HarpoonState.Throwing:
				ProcessThrowing(dt);
				break;
			case HarpoonState.Retracting:
				ProcessRetracting(dt);
				break;
		}
	}

	// =========================================================================
	//  IDLE
	// =========================================================================

	private void ProcessIdle()
	{
		if (!IsFireInputPressed()) return;
		if (!IsNearCave()) return;
		if (_tendril.Hunger < HungerCost) return;

		_state = HarpoonState.Winding;
		_windTimer = 0f;
		EmitSignal(SignalName.ChargeStarted);
	}

	// =========================================================================
	//  WINDING
	// =========================================================================

	private void ProcessWinding(float dt)
	{
		_windTimer += dt;

		if (!IsFireInputHeld())
			FireStraight();
		else if (_windTimer >= GuidedModeThreshold)
			FireGuided();
	}

	// =========================================================================
	//  FIRE
	// =========================================================================

	private void InitializeShot()
	{
		_harpoonPath.Clear();
		_stepTimer = 0f;
		_grabbedCreature = null;

		_ddaX = _tendril.SubHeadX + 0.5f;
		_ddaY = _tendril.SubHeadY + 0.5f;
		_currentSubX = _tendril.SubHeadX;
		_currentSubY = _tendril.SubHeadY;

		var (tx, ty) = SubGridData.SubToTerrain(_currentSubX, _currentSubY);
		_lastCheckedTerrainX = tx;
		_lastCheckedTerrainY = ty;

		_tendril.DrainHunger(HungerCost);
	}

	private void FireStraight()
	{
		_shotDirection = _tendril.LastMoveDirection;
		if (_shotDirection.LengthSquared() < 0.01f)
			_shotDirection = new Vector2(0, 1);
		_shotDirection = _shotDirection.Normalized();

		_targetRange = TapRange;
		_stepsPerTick = TapSubStepsPerTick;

		InitializeShot();

		_state = HarpoonState.Straight;
		EmitSignal(SignalName.HarpoonFired, _targetRange);
	}

	private void FireGuided()
	{
		_shotDirection = _tendril.LastMoveDirection;
		if (_shotDirection.LengthSquared() < 0.01f)
			_shotDirection = new Vector2(0, 1);
		_shotDirection = _shotDirection.Normalized();

		_targetRange = GuidedMaxRange;
		_stepsPerTick = GuidedSubStepsPerTick;

		InitializeShot();

		_state = HarpoonState.Guided;
		EmitSignal(SignalName.HarpoonFired, _targetRange);
	}

	// =========================================================================
	//  STRAIGHT EXTENDING
	// =========================================================================

	private void ProcessExtending(float dt)
	{
		_stepTimer -= dt;
		if (_stepTimer > 0f) return;
		_stepTimer = TapStepDelay;

		for (int i = 0; i < _stepsPerTick; i++)
		{
			if (!ExtendOneStep())
				return;
		}
	}

	// =========================================================================
	//  GUIDED EXTENDING
	// =========================================================================

	private void ProcessGuided(float dt)
	{
		if (!IsFireInputHeld())
		{
			StartRetract();
			return;
		}

		Vector2 steerInput = GetSteerInputVector();
		if (steerInput.LengthSquared() > 0.01f)
		{
			Vector2 targetDir = steerInput.Normalized();
			float currentAngle = Mathf.Atan2(_shotDirection.Y, _shotDirection.X);
			float targetAngle = Mathf.Atan2(targetDir.Y, targetDir.X);
			float maxTurn = GuidedTurnRate * dt;
			float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Mathf.Clamp(maxTurn, 0f, 1f));
			_shotDirection = new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle));
		}

		_stepTimer -= dt;
		if (_stepTimer > 0f) return;
		_stepTimer = GuidedStepDelay;

		for (int i = 0; i < _stepsPerTick; i++)
		{
			if (!ExtendOneStep())
				return;
		}
	}

	// =========================================================================
	//  DDA STEP — shared by both extend modes
	// =========================================================================

	private bool ExtendOneStep()
	{
		_ddaX += _shotDirection.X;
		_ddaY += _shotDirection.Y;

		int newSubX = Mathf.FloorToInt(_ddaX);
		int newSubY = Mathf.FloorToInt(_ddaY);

		if (newSubX == _currentSubX && newSubY == _currentSubY)
			return true;

		_currentSubX = newSubX;
		_currentSubY = newSubY;

		// --- Creature hit check (every sub-step) ---
		Creature hitCreature = _creatureManager?.GetCreatureAtSubGrid(newSubX, newSubY);
		if (hitCreature != null)
		{
			PlaceHarpoonCell(newSubX, newSubY);
			_grabbedCreature = hitCreature;
			EmitSignal(SignalName.CreatureGrabbed, hitCreature.SubX, hitCreature.SubY);

			// KEY DECISION: is fire still held?
			if (IsFireInputHeld())
			{
				// Player is holding → enter Armed state, don't retract
				_state = HarpoonState.Armed;
				GD.Print($"Harpoon grabbed {hitCreature.Species} — ARMED! Aim and release to throw.");
			}
			else
			{
				// Player already released → normal retract + consume
				GD.Print($"Harpoon grabbed {hitCreature.Species} — retracting to consume.");
				StartRetract();
			}
			return false;
		}

		// --- Terrain check (tile boundary crossings) ---
		var (terrainX, terrainY) = SubGridData.SubToTerrain(newSubX, newSubY);
		bool newTerrainTile = (terrainX != _lastCheckedTerrainX || terrainY != _lastCheckedTerrainY);

		if (newTerrainTile)
		{
			_lastCheckedTerrainX = terrainX;
			_lastCheckedTerrainY = terrainY;

			TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
			if (tile != TileType.Air)
			{
				StartRetract();
				return false;
			}
		}

		if (_harpoonPath.Count >= _targetRange)
		{
			StartRetract();
			return false;
		}

		PlaceHarpoonCell(newSubX, newSubY);
		return true;
	}

	// =========================================================================
	//  ARMED — creature grabbed, fire held, waiting for aim + release
	// =========================================================================

	private void ProcessArmed(float dt)
	{
		if (_grabbedCreature == null || !_grabbedCreature.IsAlive)
		{
			// Creature died somehow — just retract
			StartRetract();
			return;
		}

		// Keep creature at the harpoon tip
		if (_harpoonPath.Count > 0)
		{
			var (tipX, tipY) = _harpoonPath[^1];
			_creatureManager?.ForceCreatureSubPosition(_grabbedCreature, tipX, tipY);
		}

		// Player releases fire → THROW in aimed direction
		if (!IsFireInputHeld())
		{
			// Get aim direction from movement input
			Vector2 aimDir = GetSteerInputVector();

			if (aimDir.LengthSquared() < 0.01f)
			{
				// No aim input — use the harpoon's current direction
				aimDir = _shotDirection;
			}
			else
			{
				aimDir = aimDir.Normalized();
			}

			LaunchCreature(aimDir);
			return;
		}
	}

	/// <summary>
	/// Launch the grabbed creature as a projectile and begin retracting the harpoon line.
	/// </summary>
	private void LaunchCreature(Vector2 direction)
	{
		if (_grabbedCreature == null) return;

		// Set up projectile from the harpoon tip position
		if (_harpoonPath.Count > 0)
		{
			var (tipX, tipY) = _harpoonPath[^1];
			_projX = tipX;
			_projY = tipY;
			_projSubX = tipX;
			_projSubY = tipY;
		}
		else
		{
			_projX = _grabbedCreature.SubX;
			_projY = _grabbedCreature.SubY;
			_projSubX = _grabbedCreature.SubX;
			_projSubY = _grabbedCreature.SubY;
		}

		_throwDirection = direction;
		_projDistanceTraveled = 0f;
		_projStepAccumulator = 0f;

		_state = HarpoonState.Throwing;

		// Start retracting the harpoon line in the background
		// (the line cleans up while the creature flies independently)

		EmitSignal(SignalName.CreatureThrown, direction.X, direction.Y);
		GD.Print($"THREW {_grabbedCreature.Species}! Dir: ({direction.X:F2},{direction.Y:F2})");
	}

	// =========================================================================
	//  THROWING — creature in flight as projectile
	// =========================================================================

	private void ProcessThrowing(float dt)
	{
		// Also retract the harpoon line while the creature flies
		RetractLineStep(dt);

		if (_grabbedCreature == null || !_grabbedCreature.IsAlive)
		{
			// Creature died during flight — finish up
			if (_harpoonPath.Count == 0)
			{
				_grabbedCreature = null;
				_state = HarpoonState.Idle;
			}
			return;
		}

		// Advance projectile
		_projStepAccumulator += ThrowSpeed * dt;

		int steps = 0;
		while (_projStepAccumulator >= 1f && steps < 12)
		{
			_projStepAccumulator -= 1f;
			steps++;

			_projX += _throwDirection.X;
			_projY += _throwDirection.Y;
			_projDistanceTraveled += 1f;

			int newSubX = (int)Mathf.Floor(_projX);
			int newSubY = (int)Mathf.Floor(_projY);

			if (newSubX == _projSubX && newSubY == _projSubY)
				continue;

			_projSubX = newSubX;
			_projSubY = newSubY;

			_creatureManager?.ForceCreatureSubPosition(_grabbedCreature, _projSubX, _projSubY);

			if (CheckThrowCreatureHit())
				return;

			if (CheckThrowTerrainHit())
				return;

			if (_projDistanceTraveled >= ThrowRange)
			{
				ThrowMiss();
				return;
			}
		}
	}

	private bool CheckThrowCreatureHit()
	{
		int projRadius = _grabbedCreature.Body?.Radius ?? 3;

		foreach (var other in _creatureManager.GetActiveCreatures())
		{
			if (other == _grabbedCreature) continue;

			int otherRadius = other.Body?.Radius ?? 3;
			int minDist = projRadius + otherRadius;

			int dx = _projSubX - other.SubX;
			int dy = _projSubY - other.SubY;

			if (dx * dx + dy * dy > minDist * minDist) continue;

			// SLAM HIT
			bool targetDied = _creatureManager.DamageCreature(other, SlamDamage);
			bool projDied = _creatureManager.DamageCreature(_grabbedCreature, SlamSelfDamage);

			// Knockback target
			if (other.IsAlive)
			{
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				Vector2 knockDir = dist > 0.01f
					? new Vector2(dx, dy) / dist
					: _throwDirection;
				_creatureManager.ApplyImpulse(other, knockDir * SlamKnockback);
			}

			// Particles
			_particles?.SpawnDamageHit(other, _projSubX, _projSubY);

			if (targetDied)
			{
				var targetConfig = CreatureRegistry.GetConfig(other.Species);
				_tendril.AddHunger(targetConfig.HungerOnConsume);
				_particles?.SpawnBurst(other, _projSubX, _projSubY);
				GD.Print($"SLAM KILL! {_grabbedCreature.Species} → {other.Species}! +{targetConfig.HungerOnConsume} hunger");
			}
			else
			{
				GD.Print($"SLAM! {_grabbedCreature.Species} → {other.Species}! {other.Health} HP left");
			}

			EmitSignal(SignalName.CreatureSlammed, other.SubX, other.SubY);

			// Handle projectile creature
			if (projDied)
			{
				var projConfig = CreatureRegistry.GetConfig(_grabbedCreature.Species);
				_tendril.AddHunger(projConfig.HungerOnConsume * 0.5f);
				_particles?.SpawnBurst(_grabbedCreature, _projSubX, _projSubY);
				_creatureManager.KillCreatureExternal(_grabbedCreature);
			}
			else
			{
				// Survives — bounces back slightly, resumes AI
				_creatureManager.ApplyImpulse(_grabbedCreature, _throwDirection * -30f);
			}

			FinishThrow();
			return true;
		}

		return false;
	}

	private bool CheckThrowTerrainHit()
	{
		var (terrainX, terrainY) = SubGridData.SubToTerrain(_projSubX, _projSubY);
		TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);

		if (tile == TileType.Air) return false;
		if (TileProperties.Is(tile, TileFlags.Organic) && !TileProperties.Is(tile, TileFlags.Solid))
			return false;

		// WALL SPLAT
		_particles?.SpawnBurst(_grabbedCreature, _projSubX, _projSubY);

		bool died = _creatureManager.DamageCreature(_grabbedCreature, SlamSelfDamage + WallSplatExtraDamage);

		if (died)
		{
			var config = CreatureRegistry.GetConfig(_grabbedCreature.Species);
			_tendril.AddHunger(config.HungerOnConsume * 0.25f);
			_creatureManager.KillCreatureExternal(_grabbedCreature);
			GD.Print($"SPLAT! {_grabbedCreature.Species} hit a wall!");
		}
		else
		{
			_creatureManager.ApplyImpulse(_grabbedCreature, _throwDirection * -20f);
			GD.Print($"{_grabbedCreature.Species} hit a wall but survived!");
		}

		EmitSignal(SignalName.WallSplat, _projSubX, _projSubY);

		FinishThrow();
		return true;
	}

	private void ThrowMiss()
	{
		if (_grabbedCreature != null && _grabbedCreature.IsAlive)
		{
			_creatureManager.ApplyImpulse(_grabbedCreature, _throwDirection * 15f);
			GD.Print($"Threw {_grabbedCreature.Species} — missed!");
		}

		FinishThrow();
	}

	private void FinishThrow()
	{
		_grabbedCreature = null;

		// If harpoon line is still retracting, let it finish
		if (_harpoonPath.Count > 0)
			_state = HarpoonState.Retracting;
		else
			_state = HarpoonState.Idle;
	}

	// =========================================================================
	//  HARPOON SHAPE
	// =========================================================================

	private void PlaceHarpoonCell(int subX, int subY)
	{
		_harpoonPath.Add((subX, subY));

		int r = HarpoonThickness;

		if (r <= 0)
		{
			_tendril.SubGrid.SetCell(subX, subY, SubCellState.Core, 0, 220);
			return;
		}

		for (int dy = -r; dy <= r; dy++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				if (dist > r) continue;

				float noise = Mathf.Sin((subX + dx) * 0.9f + (subY + dy) * 1.3f) * ShapeIrregularity;
				if (dist + noise > r) continue;

				int sx = subX + dx;
				int sy = subY + dy;

				float edgeFade = 1f - Mathf.Clamp(dist / r, 0f, 1f);
				byte intensity = (byte)(220 - (int)((1f - edgeFade) * 80));

				_tendril.SubGrid.SetCell(sx, sy, SubCellState.Core, 0, intensity);
			}
		}
	}

	private void ClearHarpoonCell(int subX, int subY)
	{
		int r = HarpoonThickness;

		if (r <= 0)
		{
			_tendril.SubGrid.ClearCell(subX, subY);
			return;
		}

		for (int dy = -r; dy <= r; dy++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				if (dx * dx + dy * dy <= r * r)
					_tendril.SubGrid.ClearCell(subX + dx, subY + dy);
			}
		}
	}

	// =========================================================================
	//  RETRACTING
	// =========================================================================

	private void StartRetract()
	{
		_state = HarpoonState.Retracting;
		_stepTimer = 0f;
	}

	private void ProcessRetracting(float dt)
	{
		_stepTimer -= dt;
		if (_stepTimer > 0f) return;
		_stepTimer = RetractStepDelay;

		for (int i = 0; i < RetractSubStepsPerTick; i++)
		{
			if (_harpoonPath.Count == 0)
			{
				FinishRetract();
				return;
			}

			var (rx, ry) = _harpoonPath[^1];
			_harpoonPath.RemoveAt(_harpoonPath.Count - 1);
			ClearHarpoonCell(rx, ry);

			// Drag creature along during retract
			if (_grabbedCreature != null && _grabbedCreature.IsAlive && _harpoonPath.Count > 0)
			{
				var (tipSubX, tipSubY) = _harpoonPath[^1];
				_creatureManager?.ForceCreatureSubPosition(_grabbedCreature, tipSubX, tipSubY);
				CheckRetractSlamCollision(tipSubX, tipSubY);
			}
		}
	}

	/// <summary>
	/// Check for slam during normal retract (creature dragged through another).
	/// </summary>
	private void CheckRetractSlamCollision(int projectileSubX, int projectileSubY)
	{
		if (_grabbedCreature == null || !_grabbedCreature.IsAlive) return;
		if (_creatureManager == null) return;

		int projectileRadius = _grabbedCreature.Body?.Radius ?? 3;

		foreach (var other in _creatureManager.GetActiveCreatures())
		{
			if (other == _grabbedCreature) continue;

			int otherRadius = other.Body?.Radius ?? 3;
			int minDist = projectileRadius + otherRadius;

			int sdx = projectileSubX - other.SubX;
			int sdy = projectileSubY - other.SubY;

			if (sdx * sdx + sdy * sdy > minDist * minDist) continue;

			bool targetDied = _creatureManager.DamageCreature(other, RetractSlamDamageToTarget);
			bool projectileDied = _creatureManager.DamageCreature(_grabbedCreature, RetractSlamDamageToProjectile);

			if (other.IsAlive)
			{
				float dist = Mathf.Sqrt(sdx * sdx + sdy * sdy);
				Vector2 knockDir = dist > 0.01f
					? new Vector2(sdx, sdy) / dist
					: new Vector2(0, -1);
				_creatureManager.ApplyImpulse(other, knockDir * -RetractSlamKnockbackForce);
			}

			_particles?.SpawnDamageHit(other, projectileSubX, projectileSubY);

			if (targetDied)
			{
				var config = CreatureRegistry.GetConfig(other.Species);
				_tendril.AddHunger(config.HungerOnConsume);
				_particles?.SpawnBurst(other, projectileSubX, projectileSubY);
				GD.Print($"RETRACT SLAM! {_grabbedCreature.Species} killed {other.Species}!");
			}

			EmitSignal(SignalName.CreatureSlammed, other.SubX, other.SubY);

			if (projectileDied)
			{
				var projConfig = CreatureRegistry.GetConfig(_grabbedCreature.Species);
				_tendril.AddHunger(projConfig.HungerOnConsume);
				_particles?.SpawnBurst(_grabbedCreature, projectileSubX, projectileSubY);
				_creatureManager.KillCreatureExternal(_grabbedCreature);
				_grabbedCreature = null;
				return;
			}
		}
	}

	/// <summary>
	/// Retract a few harpoon line cells. Used by ProcessThrowing to clean up the
	/// harpoon line while the creature flies independently.
	/// </summary>
	private void RetractLineStep(float dt)
	{
		_stepTimer -= dt;
		if (_stepTimer > 0f) return;
		_stepTimer = RetractStepDelay;

		for (int i = 0; i < RetractSubStepsPerTick; i++)
		{
			if (_harpoonPath.Count == 0) return;

			var (rx, ry) = _harpoonPath[^1];
			_harpoonPath.RemoveAt(_harpoonPath.Count - 1);
			ClearHarpoonCell(rx, ry);
		}
	}

	private void FinishRetract()
	{
		bool caught = _grabbedCreature != null && _grabbedCreature.IsAlive;

		if (caught)
		{
			// Normal retract with creature → consume it
			var config = CreatureRegistry.GetConfig(_grabbedCreature.Species);
			_tendril.AddHunger(config.HungerOnConsume);
			_particles?.SpawnBurst(_grabbedCreature, _tendril.SubHeadX, _tendril.SubHeadY);
			_creatureManager?.KillCreatureExternal(_grabbedCreature);
			GD.Print($"Consumed {_grabbedCreature.Species}! +{config.HungerOnConsume} hunger");
		}

		_grabbedCreature = null;
		_harpoonPath.Clear();
		_state = HarpoonState.Idle;
		EmitSignal(SignalName.HarpoonRetracted, caught);
	}

	/// <summary>
	/// Emergency cleanup — cancel everything and return to idle.
	/// Used when tendril retreats while harpoon is active.
	/// </summary>
	private void CancelAndCleanup()
	{
		// Clear all harpoon cells
		foreach (var (rx, ry) in _harpoonPath)
			ClearHarpoonCell(rx, ry);
		_harpoonPath.Clear();

		// Drop grabbed creature if any
		if (_grabbedCreature != null && _grabbedCreature.IsAlive)
		{
			// Just leave it where it is
			_creatureManager?.ApplyImpulse(_grabbedCreature, Vector2.Zero);
		}

		_grabbedCreature = null;
		_state = HarpoonState.Idle;
	}

	// =========================================================================
	//  CAVE DETECTION
	// =========================================================================

	private bool IsNearCave()
	{
		int hx = _tendril.HeadX;
		int hy = _tendril.HeadY;

		for (int dy = -CaveDetectRadius; dy <= CaveDetectRadius; dy++)
		{
			for (int dx = -CaveDetectRadius; dx <= CaveDetectRadius; dx++)
			{
				if (dx * dx + dy * dy > CaveDetectRadius * CaveDetectRadius) continue;
				if (_chunkManager.GetTileAt(hx + dx, hy + dy) == TileType.Air)
					return true;
			}
		}

		return false;
	}

	// =========================================================================
	//  INPUT
	// =========================================================================

	private Vector2 GetSteerInputVector()
	{
		int keyX = 0, keyY = 0;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) keyY -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) keyY += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) keyX -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) keyX += 1;

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
			if (stick.Length() < StickDeadZone)
				stick = Vector2.Zero;
			else if (stick.Length() > 0f)
			{
				float norm = (stick.Length() - StickDeadZone) / (1f - StickDeadZone);
				stick = stick.Normalized() * Mathf.Clamp(norm, 0f, 1f);
			}
		}

		Vector2 combined = keyboard + stick;
		if (combined.Length() > 1f) combined = combined.Normalized();
		return combined;
	}

	private bool IsFireInputPressed()
	{
		bool spaceNow = Input.IsKeyPressed(Key.Space);
		if (spaceNow && !_wasSpaceHeld)
		{
			_wasSpaceHeld = true;
			return true;
		}
		if (!spaceNow) _wasSpaceHeld = false;

		float trigger = GetR2Value();
		if (trigger > TriggerDeadZone && !_wasTriggerHeld)
		{
			_wasTriggerHeld = true;
			return true;
		}
		if (trigger <= TriggerDeadZone) _wasTriggerHeld = false;

		return false;
	}

	private bool IsFireInputHeld()
	{
		bool spaceHeld = Input.IsKeyPressed(Key.Space);
		if (!spaceHeld) _wasSpaceHeld = false;

		float trigger = GetR2Value();
		bool triggerHeld = trigger > TriggerDeadZone;
		if (!triggerHeld) _wasTriggerHeld = false;

		return spaceHeld || triggerHeld;
	}

	private float GetR2Value()
	{
		var joypads = Input.GetConnectedJoypads();
		if (joypads.Count == 0) return 0f;
		return Input.GetJoyAxis(joypads[0], JoyAxis.TriggerRight);
	}
}
