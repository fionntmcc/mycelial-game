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
///   - If fire is HELD → harpoon freezes, creature stays at tip
///     → Press L2 (or E on keyboard) to consume via retract
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
	[Export] public int CaveDetectRadius = 3;

	// --- Input ---
	[Export] public float TriggerDeadZone = 0.3f;
	[Export] public float StickDeadZone = 0.22f;
	[Export] public float GrabPulseDuration = 0.14f;

	// --- Throw Ability ---
	[ExportGroup("Throw Ability")]
	[Export] public bool ThrowUnlocked = false;
	[Export] public bool ThrowEnabled = true;

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

	// --- Rush Chain Config ---
	[Export] public bool EnableRushChain = true;
	[Export] public float RushStunDuration = 1.0f;
	[Export] public int RushConsumeRadius = 5;
	[Export] public int RushDamageToNonStunned = 2;
	[Export] public float RushKnockback = 75f;

	// --- State Machine ---
	private enum HarpoonState
	{
		Idle,
		Winding,      // Fire button held, deciding tap vs guided
		Guided,       // Extending with player steering
		Straight,     // Extending in a straight line
		Armed,        // Creature grabbed, fire held — waiting for aim + release
		Throwing,     // Creature launched as projectile
		Rushing,      // Chain-rush toward anchor wall after throw slam
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
	private Creature _pulseCreature;
	private int _pulseTipSubX;
	private int _pulseTipSubY;
	private float _grabPulseTimer;

	// Terrain tracking
	private int _lastCheckedTerrainX;
	private int _lastCheckedTerrainY;

	// Throw projectile state
	private float _projX, _projY;
	private int _projSubX, _projSubY;
	private Vector2 _throwDirection;
	private float _projDistanceTraveled;
	private float _projStepAccumulator;

	// Rush chain state
	private Vector2 _rushDirection;
	private readonly HashSet<Creature> _rushProcessed = new();

	// Input state
	private bool _wasSpaceHeld;
	private bool _wasTriggerHeld;
	private bool _wasEatKeyHeld;
	private bool _wasLeftTriggerHeld;

	// Armed throw tracking — requires new fire press+release cycle
	private bool _armedFireEngaged;

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

	/// <summary>Active pulse amount for grab feedback (0-1).</summary>
	public float GrabPulseStrength
	{
		get
		{
			if (_grabPulseTimer <= 0f || GrabPulseDuration <= 0f)
				return 0f;

			float progress = 1f - Mathf.Clamp(_grabPulseTimer / GrabPulseDuration, 0f, 1f);
			return Mathf.Sin(progress * Mathf.Pi);
		}
	}

	/// <summary>Creature that should receive the active grab pulse.</summary>
	public Creature PulseCreature => GrabPulseStrength > 0f ? _pulseCreature : null;

	/// <summary>Harpoon tip sub-grid position for the active grab pulse.</summary>
	public Vector2I PulseTipSubPosition => new(_pulseTipSubX, _pulseTipSubY);

	/// <summary>0–1 charge indicator for HUD.</summary>
	public float ChargePercent => _state == HarpoonState.Winding
		? Mathf.Clamp(_windTimer / GuidedModeThreshold, 0f, 1f) : 0f;

	/// <summary>True when throw branch is available to the player.</summary>
	public bool IsThrowAvailable => ThrowUnlocked && ThrowEnabled;

	/// <summary>True when the camera should track the harpoon tip (extending or retracting).</summary>
	public bool IsCameraTracking => _state == HarpoonState.Straight
		|| _state == HarpoonState.Guided
		|| _state == HarpoonState.Retracting;

	/// <summary>Get the harpoon tip position in world pixels.</summary>
	public Vector2 GetTipPixelPosition()
	{
		int cellSize = WorldConfig.SubCellSize;
		// During retraction the tip is the last remaining path element
		if (_harpoonPath.Count > 0)
		{
			var (tx, ty) = _harpoonPath[^1];
			return new Vector2(
				tx * cellSize + cellSize / 2f,
				ty * cellSize + cellSize / 2f
			);
		}
		return new Vector2(
			_currentSubX * cellSize + cellSize / 2f,
			_currentSubY * cellSize + cellSize / 2f
		);
	}

	/// <summary>The creature currently held by the harpoon (null when none).</summary>
	public Creature GrabbedCreature => _grabbedCreature;

	/// <summary>The live harpoon path for rush-follow tracking.</summary>
	public IReadOnlyList<(int SubX, int SubY)> HarpoonPath => _harpoonPath;

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
		if (_grabPulseTimer > 0f)
			_grabPulseTimer = Mathf.Max(0f, _grabPulseTimer - dt);

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
			case HarpoonState.Rushing:
				ProcessRushing(dt);
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
		_armedFireEngaged = false;

		_ddaX = _tendril.SubHeadX + 0.5f;
		_ddaY = _tendril.SubHeadY + 0.5f;
		_currentSubX = _tendril.SubHeadX;
		_currentSubY = _tendril.SubHeadY;

		var (tx, ty) = SubGridData.SubToTerrain(_currentSubX, _currentSubY);
		_lastCheckedTerrainX = tx;
		_lastCheckedTerrainY = ty;

		// Harpoon is free to fire — vigor decay while not eating is the cost
	}

	private void FireStraight()
	{
		_shotDirection = _tendril.LastMoveDirection;
		if (_shotDirection.LengthSquared() < 0.01f)
			_shotDirection = new Vector2(0, 1);
		_shotDirection = _shotDirection.Normalized();

		_targetRange = (int)(TapRange * _tendril.HarpoonRangeMultiplier);
		_stepsPerTick = TapSubStepsPerTick;

		InitializeShot();

		_state = HarpoonState.Straight;

		if (IsThrowAvailable)
			_tendril?.BeginRushFollow();

		EmitSignal(SignalName.HarpoonFired, _targetRange);
	}

	private void FireGuided()
	{
		_shotDirection = _tendril.LastMoveDirection;
		if (_shotDirection.LengthSquared() < 0.01f)
			_shotDirection = new Vector2(0, 1);
		_shotDirection = _shotDirection.Normalized();

		_targetRange = (int)(GuidedMaxRange * _tendril.HarpoonRangeMultiplier);
		_stepsPerTick = GuidedSubStepsPerTick;

		InitializeShot();

		_state = HarpoonState.Guided;

		if (IsThrowAvailable)
			_tendril?.BeginRushFollow();

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
			TriggerGrabPulse(hitCreature, newSubX, newSubY);
			EmitSignal(SignalName.CreatureGrabbed, hitCreature.SubX, hitCreature.SubY);

			if (!IsThrowAvailable)
			{
				GD.Print($"Harpoon grabbed {hitCreature.Species} — throw disabled, auto-consume.");
				StartRetract();
				return false;
			}

			// Throw is available — always enter Armed (player decides eat vs throw)
			_armedFireEngaged = false;
			_state = HarpoonState.Armed;
			GD.Print($"Harpoon grabbed {hitCreature.Species} — ARMED! Hold+release R2 to throw, L2/E to eat.");
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

		// Wait for the controller to arrive before allowing eat/throw
		if (_tendril != null && _tendril.IsRushFollowing)
			return;

		// Controller has arrived at the tip — player can now act

		// Eat: L2/E at any time (no need to hold fire)
		if (IsConsumeInputPressed())
		{
			GD.Print($"Harpoon: eat {_grabbedCreature.Species} — retracting.");
			StartRetract();
			return;
		}

		// Throw: requires a NEW fire press+release cycle
		// (prevents auto-throw when fire was held from the original shot)
		if (IsFireInputHeld())
			_armedFireEngaged = true;

		if (_armedFireEngaged && !IsFireInputHeld())
		{
			Vector2 aimDir = GetSteerInputVector();

			if (aimDir.LengthSquared() < 0.01f)
				aimDir = _shotDirection;
			else
				aimDir = aimDir.Normalized();

			// Controller stays at tip — committed to the throw
			_tendril?.ResolveRushHold();
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

			Creature projectileCreature = _grabbedCreature;

			// Rush chain gets first priority on throw-hit.
			if (EnableRushChain
				&& projectileCreature != null
				&& projectileCreature.IsAlive
				&& other.IsAlive)
			{
				_creatureManager.StunCreature(other, RushStunDuration);
				_creatureManager.StunCreature(projectileCreature, RushStunDuration);

				if (TryStartRushFromCollision(_projSubX, _projSubY, _throwDirection))
				{
					GD.Print("RUSH CHAIN! Momentum dash started.");
					return true;
				}

				GD.Print("Rush chain: no valid anchor found within search range.");
			}

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
				_tendril.AddVigor(targetConfig.VigorOnConsume + _tendril.VigorGainSlamKill);
				_particles?.SpawnBurst(other, _projSubX, _projSubY);
				GD.Print($"SLAM KILL! {_grabbedCreature.Species} → {other.Species}! +{targetConfig.VigorOnConsume + _tendril.VigorGainSlamKill} vigor");
			}
			else
			{
				GD.Print($"SLAM! {_grabbedCreature.Species} → {other.Species}! {other.Health} HP left");
			}

			EmitSignal(SignalName.CreatureSlammed, other.SubX, other.SubY);

			// Handle projectile creature when no rush chain starts
			if (projDied)
			{
				var projConfig = CreatureRegistry.GetConfig(_grabbedCreature.Species);
				_tendril.AddVigor(projConfig.VigorOnConsume * 0.5f);
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

		// WALL SPLAT — damage the thrown creature
		_particles?.SpawnBurst(_grabbedCreature, _projSubX, _projSubY);

		bool died = _creatureManager.DamageCreature(_grabbedCreature, SlamSelfDamage + WallSplatExtraDamage);

		if (died)
		{
			var config = CreatureRegistry.GetConfig(_grabbedCreature.Species);
			_tendril.AddVigor(config.VigorOnConsume * 0.25f);
			_creatureManager.KillCreatureExternal(_grabbedCreature);
			GD.Print($"SPLAT! {_grabbedCreature.Species} hit a wall!");
		}
		else
		{
			_creatureManager.StunCreature(_grabbedCreature, RushStunDuration);
			GD.Print($"{_grabbedCreature.Species} hit a wall — stunned!");
		}

		EmitSignal(SignalName.WallSplat, _projSubX, _projSubY);

		// Try rush chain toward a solid anchor behind the wall
		if (EnableRushChain && TryStartRushFromCollision(_projSubX, _projSubY, _throwDirection))
		{
			GD.Print("WALL RUSH! Momentum dash started.");
			return true;
		}

		FinishThrow();
		return true;
	}

	private void ThrowMiss()
	{
		if (_grabbedCreature != null && _grabbedCreature.IsAlive)
		{
			_creatureManager.StunCreature(_grabbedCreature, RushStunDuration);
			GD.Print($"Threw {_grabbedCreature.Species} — missed, stunned on landing.");
		}

		// Try rush even on a miss — if there's a wall ahead the tendril can rush to
		if (EnableRushChain && TryStartRushFromCollision(_projSubX, _projSubY, _throwDirection))
		{
			GD.Print("MISS RUSH! Momentum dash started.");
			return;
		}

		FinishThrow();
	}

	private bool TryStartRushFromCollision(int collisionSubX, int collisionSubY, Vector2 rushDirection)
	{
		if (_tendril == null) return false;
		if (rushDirection.LengthSquared() < 0.0001f) return false;

		// Calculate distance from controller to collision for impulse scaling
		int dx = collisionSubX - _tendril.SubHeadX;
		int dy = collisionSubY - _tendril.SubHeadY;
		float distance = Mathf.Sqrt(dx * dx + dy * dy);

		_rushDirection = rushDirection.Normalized();
		_rushProcessed.Clear();

		// Apply interactions at the collision point
		ApplyRushInteractionsAt(collisionSubX, collisionSubY);

		// Give the controller a momentum impulse
		_tendril.ApplyRushImpulse(_rushDirection, distance);

		_grabbedCreature = null;
		_state = HarpoonState.Rushing;
		return true;
	}

	private void ProcessRushing(float dt)
	{
		RetractLineStep(dt);

		// Movement is handled by the controller's momentum system.
		// Check for creature interactions at the controller's current position.
		if (_tendril != null)
			ApplyRushInteractionsAt(_tendril.SubHeadX, _tendril.SubHeadY);

		// Rush ends when the controller's dash is done
		if (_tendril == null || !_tendril.IsRushDashing)
		{
			FinishRush();
			return;
		}
	}

	private void ApplyRushInteractionsAt(int rushSubX, int rushSubY)
	{
		if (_creatureManager == null) return;

		foreach (var creature in _creatureManager.GetAllCreatures())
		{
			if (creature == null || !creature.IsAlive || !creature.IsActive) continue;
			if (_rushProcessed.Contains(creature)) continue;

			int radius = (creature.Body?.Radius ?? 3) + RushConsumeRadius;
			int dx = creature.SubX - rushSubX;
			int dy = creature.SubY - rushSubY;
			if (dx * dx + dy * dy > radius * radius)
				continue;

			if (creature.IsStunned)
			{
				var config = CreatureRegistry.GetConfig(creature.Species);
				_tendril.AddVigor(config.VigorOnConsume);
				_particles?.SpawnBurst(creature, rushSubX, rushSubY);
				_creatureManager.KillCreatureExternal(creature);
				_rushProcessed.Add(creature);
				continue;
			}

			bool died = _creatureManager.DamageCreature(creature, RushDamageToNonStunned);
			if (!died)
			{
				Vector2 knockDir = new Vector2(dx, dy);
				if (knockDir.LengthSquared() < 0.001f)
					knockDir = _rushDirection;
				else
					knockDir = knockDir.Normalized();

				_creatureManager.ApplyImpulse(creature, knockDir * RushKnockback);
			}

			_rushProcessed.Add(creature);
		}
	}

	private void FinishRush()
	{
		_rushProcessed.Clear();
		_tendril?.CleanupRushTrail();

		if (_harpoonPath.Count > 0)
			_state = HarpoonState.Retracting;
		else
			_state = HarpoonState.Idle;
	}

	private void FinishThrow()
	{
		_grabbedCreature = null;

		// Clean up rush trail and snap controller back to origin
		_tendril?.CleanupRushTrailAndReturn();

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
		// Smooth retract-follow when the controller has moved along the harpoon
		if (_tendril != null && (_tendril.IsRushFollowing || _tendril.IsRushHolding))
			_tendril.BeginRetractFollow();

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

			// Move controller backward along the harpoon path
			if (_tendril != null && _tendril.IsRetractFollowing && _harpoonPath.Count > 0)
			{
				var (tipX, tipY) = _harpoonPath[^1];
				_tendril.RetractFollowStep(tipX, tipY);
			}

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
				_tendril.AddVigor(config.VigorOnConsume + _tendril.VigorGainSlamKill);
				_particles?.SpawnBurst(other, projectileSubX, projectileSubY);
				GD.Print($"RETRACT SLAM! {_grabbedCreature.Species} killed {other.Species}!");
			}

			EmitSignal(SignalName.CreatureSlammed, other.SubX, other.SubY);

			if (projectileDied)
			{
				var projConfig = CreatureRegistry.GetConfig(_grabbedCreature.Species);
				_tendril.AddVigor(projConfig.VigorOnConsume);
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
			_tendril.AddVigor(config.VigorOnConsume + _tendril.VigorGainHarpoonCatch);
			_particles?.SpawnBurst(_grabbedCreature, _tendril.SubHeadX, _tendril.SubHeadY);
			_creatureManager?.KillCreatureExternal(_grabbedCreature);
			GD.Print($"Consumed {_grabbedCreature.Species}! +{config.VigorOnConsume + _tendril.VigorGainHarpoonCatch} vigor");
		}

		// Finish the controller's retract-follow if it was active
		_tendril?.FinishRetractFollow();

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
		// Cancel rush-follow or retract-follow if active
		_tendril?.CancelRushFollow();
		_tendril?.FinishRetractFollow();

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
		_pulseCreature = null;
		_grabPulseTimer = 0f;
		_rushProcessed.Clear();
		_state = HarpoonState.Idle;
	}

	private void TriggerGrabPulse(Creature creature, int tipSubX, int tipSubY)
	{
		_pulseCreature = creature;
		_pulseTipSubX = tipSubX;
		_pulseTipSubY = tipSubY;
		_grabPulseTimer = Mathf.Max(0f, GrabPulseDuration);
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

	private bool IsConsumeInputPressed()
	{
		bool eatKeyNow = Input.IsKeyPressed(Key.E);
		if (eatKeyNow && !_wasEatKeyHeld)
		{
			_wasEatKeyHeld = true;
			return true;
		}
		if (!eatKeyNow)
			_wasEatKeyHeld = false;

		float leftTrigger = GetL2Value();
		bool leftHeldNow = leftTrigger > TriggerDeadZone;
		if (leftHeldNow && !_wasLeftTriggerHeld)
		{
			_wasLeftTriggerHeld = true;
			return true;
		}
		if (!leftHeldNow)
			_wasLeftTriggerHeld = false;

		return false;
	}

	private float GetL2Value()
	{
		var joypads = Input.GetConnectedJoypads();
		if (joypads.Count == 0) return 0f;
		return Input.GetJoyAxis(joypads[0], JoyAxis.TriggerLeft);
	}
}
