namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Harpoon-tendril with two firing modes on the sub-grid.
///
/// TAP fire: Quick press-release fires a fast, straight, unguided shot.
///   - Uses facing direction at time of fire
///   - Fast extension speed, short-medium range
///   - Good for sniping visible creatures
///
/// HOLD fire: Keep holding past a brief charge-up, then the tendril begins
///   extending and you STEER it with movement input (WASD / left stick).
///   - Slower extension speed, longer max range
///   - Turn radius based on speed — feels like piloting a living thing
///   - Release fire button to stop extending → retract begins
///   - The main tendril head is FROZEN while steering
///
/// Both modes: harpoon traces through air on the sub-grid using DDA.
/// Grabs the first creature it hits and drags it back on retract.
///
/// SETUP:
///   - Add as sibling Node of TendrilController
///   - Assign TendrilControllerPath, ChunkManagerPath, CreatureManagerPath
///   - On TendrilController, assign HarpoonPath pointing to this node
/// </summary>
public partial class TendrilHarpoon : Node
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath CreatureManagerPath { get; set; }

	// --- Timing ---
	/// <summary>Hold duration before guided mode activates (seconds).</summary>
	[Export] public float GuidedModeThreshold = 0.18f;

	// --- Tap Shot Config ---
	[Export] public int TapRange = 50;
	[Export] public float TapStepDelay = 0.003f;
	[Export] public int TapSubStepsPerTick = 5;

	// --- Guided Shot Config ---
	[Export] public int GuidedMaxRange = 180;
	[Export] public float GuidedStepDelay = 0.008f;
	[Export] public int GuidedSubStepsPerTick = 2;
	/// <summary>How fast the guided tendril can turn (radians/second).</summary>
	[Export] public float GuidedTurnRate = 4.5f;

	// --- Shape ---
	/// <summary>Radius of the harpoon tendril in sub-cells. 0 = single pixel, 2 = wormy, 3+ = fat.</summary>
	[Export(PropertyHint.Range, "0,6,1")] public int HarpoonThickness = 2;
	/// <summary>How much the edge wobbles (0 = perfect circle, higher = more organic).</summary>
	[Export(PropertyHint.Range, "0,2,0.1")] public float ShapeIrregularity = 0.8f;

	// --- Shared Config ---
	[Export] public float RetractStepDelay = 0.005f;
	[Export] public int RetractSubStepsPerTick = 6;
	[Export] public float HungerCost = 5f;
	[Export] public int CaveDetectRadius = 3;
	[Export] public float TriggerDeadZone = 0.3f;
	[Export] public float StickDeadZone = 0.22f;

	// --- State Machine ---
	private enum HarpoonState
	{
		Idle,
		Winding,      // Fire button held, waiting to see if tap or guided
		Guided,       // Extending with player steering
		Straight,     // Extending in a straight line (tap shot)
		Retracting,
	}

	private TendrilController _tendril;
	private ChunkManager _chunkManager;
	private CreatureManager _creatureManager;

	private HarpoonState _state = HarpoonState.Idle;
	private float _windTimer;
	private float _stepTimer;

	// DDA line-tracing state
	private Vector2 _shotDirection;
	private float _ddaX, _ddaY;
	private int _currentSubX, _currentSubY;

	// Path of sub-cells placed (center points — blob is stamped around each)
	private readonly List<(int SubX, int SubY)> _harpoonPath = new();
	private int _targetRange;
	private int _stepsPerTick;

	// Grabbed creature
	private Creature _grabbedCreature;

	// Terrain tile tracking
	private int _lastCheckedTerrainX;
	private int _lastCheckedTerrainY;

	// Input state
	private bool _wasSpaceHeld;
	private bool _wasTriggerHeld;

	// --- Signals ---
	[Signal] public delegate void ChargeStartedEventHandler();
	[Signal] public delegate void ChargeCancelledEventHandler();
	[Signal] public delegate void HarpoonFiredEventHandler(int range);
	[Signal] public delegate void CreatureGrabbedEventHandler(int creatureX, int creatureY);
	[Signal] public delegate void HarpoonRetractedEventHandler(bool caughtSomething);

	/// <summary>True when the harpoon is doing anything (TendrilController checks this to freeze).</summary>
	public bool IsActive => _state != HarpoonState.Idle;

	/// <summary>0–1 charge indicator for HUD display.</summary>
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

		if (_tendril == null || _chunkManager == null)
			GD.PrintErr("TendrilHarpoon: Missing TendrilController or ChunkManager!");
	}

	public override void _Process(double delta)
	{
		if (_tendril == null || _chunkManager == null) return;
		if (_tendril.IsRetreating || _tendril.IsRegenerating) return;

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
	//  WINDING — waiting to see if this is a tap or a hold
	// =========================================================================

	private void ProcessWinding(float dt)
	{
		_windTimer += dt;

		if (!IsFireInputHeld())
		{
			FireStraight();
		}
		else if (_windTimer >= GuidedModeThreshold)
		{
			FireGuided();
		}
	}

	// =========================================================================
	//  FIRE — initialize DDA from head position
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
		GD.Print($"Harpoon TAP! Dir: ({_shotDirection.X:F2},{_shotDirection.Y:F2}), Range: {_targetRange}");
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
		GD.Print($"Harpoon GUIDED! Dir: ({_shotDirection.X:F2},{_shotDirection.Y:F2}), Range: {_targetRange}");
	}

	// =========================================================================
	//  STRAIGHT EXTENDING — fast, no steering
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
	//  GUIDED EXTENDING — slower, player steers with movement input
	// =========================================================================

	private void ProcessGuided(float dt)
	{
		if (!IsFireInputHeld())
		{
			GD.Print("Guided shot released, retracting.");
			StartRetract();
			return;
		}

		// Steer: rotate _shotDirection toward movement input
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
	//  DDA STEP — shared by both modes
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

		// Check terrain when crossing tile boundaries
		var (terrainX, terrainY) = SubGridData.SubToTerrain(newSubX, newSubY);
		bool newTerrainTile = (terrainX != _lastCheckedTerrainX || terrainY != _lastCheckedTerrainY);

		if (newTerrainTile)
		{
			_lastCheckedTerrainX = terrainX;
			_lastCheckedTerrainY = terrainY;

			// Creature hit?
			Creature hitCreature = _creatureManager?.GetCreatureAt(terrainX, terrainY);
			if (hitCreature != null)
			{
				PlaceHarpoonCell(newSubX, newSubY);
				_grabbedCreature = hitCreature;
				GD.Print($"Harpoon grabbed {hitCreature.Species} at ({terrainX},{terrainY})!");
				EmitSignal(SignalName.CreatureGrabbed, terrainX, terrainY);
				StartRetract();
				return false;
			}

			// Wall hit? (harpoon only travels through air)
			TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
			if (tile != TileType.Air)
			{
				GD.Print($"Harpoon hit {tile} at ({terrainX},{terrainY}), retracting.");
				StartRetract();
				return false;
			}
		}

		// Max range?
		if (_harpoonPath.Count >= _targetRange)
		{
			GD.Print("Harpoon max range, retracting.");
			StartRetract();
			return false;
		}

		PlaceHarpoonCell(newSubX, newSubY);
		return true;
	}

	// =========================================================================
	//  HARPOON SHAPE — organic blob stamped at each path point
	// =========================================================================

	/// <summary>
	/// Stamp an organic blob of sub-cells around a center point.
	/// HarpoonThickness controls radius, ShapeIrregularity controls edge wobble.
	/// At thickness 0, places a single sub-cell.
	/// </summary>
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

				// Irregular edge — sine-based noise seeded by world position
				float noise = Mathf.Sin((subX + dx) * 0.9f + (subY + dy) * 1.3f) * ShapeIrregularity;
				if (dist + noise > r) continue;

				int sx = subX + dx;
				int sy = subY + dy;

				// Intensity falls off toward edge for fleshy gradient
				float edgeFade = 1f - Mathf.Clamp(dist / r, 0f, 1f);
				byte intensity = (byte)(220 - (int)((1f - edgeFade) * 80));

				_tendril.SubGrid.SetCell(sx, sy, SubCellState.Core, 0, intensity);
			}
		}
	}

	/// <summary>
	/// Clear the blob footprint at a path point. Must match PlaceHarpoonCell radius.
	/// </summary>
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

			// Drag creature along
			if (_grabbedCreature != null && _grabbedCreature.IsAlive && _harpoonPath.Count > 0)
			{
				var (tipSubX, tipSubY) = _harpoonPath[^1];
				var (tx, ty) = SubGridData.SubToTerrain(tipSubX, tipSubY);
				_creatureManager?.ForceCreaturePosition(_grabbedCreature, tx, ty);
			}
		}
	}

	private void FinishRetract()
	{
		bool caught = _grabbedCreature != null && _grabbedCreature.IsAlive;

		if (caught)
		{
			var config = CreatureRegistry.GetConfig(_grabbedCreature.Species);
			_tendril.AddHunger(config.HungerOnConsume);
			_creatureManager?.KillCreatureExternal(_grabbedCreature);
			GD.Print($"Harpoon delivered {_grabbedCreature.Species}! +{config.HungerOnConsume} hunger");
		}
		else
		{
			GD.Print("Harpoon retracted empty.");
		}

		_grabbedCreature = null;
		_harpoonPath.Clear();
		_state = HarpoonState.Idle;
		EmitSignal(SignalName.HarpoonRetracted, caught);
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
