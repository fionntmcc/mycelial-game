namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Harpoon-tendril shooting mechanic on the sub-grid.
///
/// When the tendril head is near a cave (adjacent air tiles), the player can
/// hold fire to charge, then release to launch a harpoon tendril through the
/// air in the exact facing direction (not snapped to 8 directions).
///
/// The harpoon traces a line through sub-grid cells using DDA (Digital
/// Differential Analyzer), placing Core sub-cells along its path. When it
/// crosses into a new terrain tile, it checks for creatures and wall collisions.
///
/// On creature hit: grabs it, retracts along the path, delivers it to the head.
/// On wall hit or max range: retracts empty.
/// Retraction clears the sub-cells that were placed.
///
/// Input: Spacebar (keyboard) or R2 / right trigger (controller).
///
/// SETUP:
///   - Add as sibling Node of TendrilController in the scene
///   - Assign TendrilControllerPath, ChunkManagerPath, CreatureManagerPath
/// </summary>
public partial class TendrilHarpoon : Node
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath CreatureManagerPath { get; set; }

	// --- Charge Config ---
	[Export] public float ChargeTimeMax = 0.6f;
	[Export] public float MinChargeFraction = 0.15f;

	// --- Shot Config ---
	/// <summary>Max sub-grid cells the harpoon can travel at full charge.</summary>
	[Export] public int MaxRange = 120;
	/// <summary>Min sub-grid cells at minimum charge.</summary>
	[Export] public int MinRange = 24;
	/// <summary>Seconds between each sub-cell step when extending.</summary>
	[Export] public float ShootStepDelay = 0.005f;
	/// <summary>Seconds between each sub-cell step when retracting.</summary>
	[Export] public float RetractStepDelay = 0.008f;
	/// <summary>Hunger cost to fire.</summary>
	[Export] public float HungerCost = 5f;
	/// <summary>How close (terrain tiles) to air to allow firing.</summary>
	[Export] public int CaveDetectRadius = 3;
	/// <summary>Sub-cells to step per tick (for fast extension).</summary>
	[Export] public int SubStepsPerTick = 3;

	// --- Controller dead zone for R2 trigger ---
	[Export] public float TriggerDeadZone = 0.3f;

	// --- State Machine ---
	private enum HarpoonState
	{
		Idle,
		Charging,
		Shooting,
		Retracting,
	}

	private TendrilController _tendril;
	private ChunkManager _chunkManager;
	private CreatureManager _creatureManager;

	private HarpoonState _state = HarpoonState.Idle;
	private float _chargeTimer;
	private float _stepTimer;

	// DDA line-tracing state
	private Vector2 _shotDirection;     // Exact normalized direction (not grid-snapped)
	private float _ddaX, _ddaY;         // Fractional position along the line
	private int _currentSubX, _currentSubY; // Current integer sub-grid position

	// Path of sub-cells placed (for retraction and cleanup)
	private readonly List<(int SubX, int SubY)> _harpoonPath = new();
	private int _targetRange;

	// Grabbed creature (null if miss)
	private Creature _grabbedCreature;

	// Track which terrain tile we last checked (avoid redundant lookups)
	private int _lastCheckedTerrainX;
	private int _lastCheckedTerrainY;

	// Input edge-detection state
	private bool _wasSpaceHeld;
	private bool _wasTriggerHeld;

	// --- Signals ---
	[Signal] public delegate void ChargeStartedEventHandler();
	[Signal] public delegate void ChargeCancelledEventHandler();
	[Signal] public delegate void HarpoonFiredEventHandler(int range);
	[Signal] public delegate void CreatureGrabbedEventHandler(int creatureX, int creatureY);
	[Signal] public delegate void HarpoonRetractedEventHandler(bool caughtSomething);

	public bool IsActive => _state != HarpoonState.Idle;
	public float ChargePercent => _state == HarpoonState.Charging
		? Mathf.Clamp(_chargeTimer / ChargeTimeMax, 0f, 1f) : 0f;

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
			case HarpoonState.Charging:
				ProcessCharging(dt);
				break;
			case HarpoonState.Shooting:
				ProcessShooting(dt);
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

		_state = HarpoonState.Charging;
		_chargeTimer = 0f;
		EmitSignal(SignalName.ChargeStarted);
	}

	// =========================================================================
	//  CHARGING
	// =========================================================================

	private void ProcessCharging(float dt)
	{
		_chargeTimer += dt;

		if (!IsFireInputHeld())
		{
			float fraction = Mathf.Clamp(_chargeTimer / ChargeTimeMax, 0f, 1f);

			if (fraction >= MinChargeFraction)
				Fire(fraction);
			else
			{
				_state = HarpoonState.Idle;
				_chargeTimer = 0f;
				EmitSignal(SignalName.ChargeCancelled);
			}
		}
		else if (_chargeTimer >= ChargeTimeMax)
		{
			Fire(1.0f);
		}
	}

	// =========================================================================
	//  FIRE — initialize DDA line trace from head in exact direction
	// =========================================================================

	private void Fire(float chargeFraction)
	{
		// Use the exact continuous direction — no grid snapping
		_shotDirection = _tendril.LastMoveDirection;
		if (_shotDirection.LengthSquared() < 0.01f)
			_shotDirection = new Vector2(0, 1); // Fallback to down

		_shotDirection = _shotDirection.Normalized();

		_targetRange = (int)Mathf.Lerp(MinRange, MaxRange, chargeFraction);
		_harpoonPath.Clear();
		_stepTimer = 0f;
		_grabbedCreature = null;

		// Initialize DDA from the tendril head sub-grid position
		_ddaX = _tendril.SubHeadX + 0.5f;
		_ddaY = _tendril.SubHeadY + 0.5f;
		_currentSubX = _tendril.SubHeadX;
		_currentSubY = _tendril.SubHeadY;

		// Track initial terrain tile
		var (tx, ty) = SubGridData.SubToTerrain(_currentSubX, _currentSubY);
		_lastCheckedTerrainX = tx;
		_lastCheckedTerrainY = ty;

		_tendril.DrainHunger(HungerCost);

		_state = HarpoonState.Shooting;
		EmitSignal(SignalName.HarpoonFired, _targetRange);
		GD.Print($"Harpoon fired! Direction: ({_shotDirection.X:F2},{_shotDirection.Y:F2}), Range: {_targetRange} sub-cells");
	}

	// =========================================================================
	//  SHOOTING — DDA line trace through sub-grid
	// =========================================================================

	private void ProcessShooting(float dt)
	{
		_stepTimer -= dt;
		if (_stepTimer > 0f) return;
		_stepTimer = ShootStepDelay;

		// Take multiple sub-steps per tick for fast extension
		for (int i = 0; i < SubStepsPerTick; i++)
		{
			if (!ExtendOneStep())
				return; // Hit something or max range — already started retract
		}
	}

	/// <summary>
	/// Advance the DDA by one sub-cell step.
	/// Returns false if the harpoon should stop (hit wall, creature, or max range).
	/// </summary>
	private bool ExtendOneStep()
	{
		// Advance fractional position
		_ddaX += _shotDirection.X;
		_ddaY += _shotDirection.Y;

		// Determine new integer sub-cell
		int newSubX = Mathf.FloorToInt(_ddaX);
		int newSubY = Mathf.FloorToInt(_ddaY);

		// Skip if we haven't moved to a new sub-cell
		if (newSubX == _currentSubX && newSubY == _currentSubY)
			return true;

		_currentSubX = newSubX;
		_currentSubY = newSubY;

		// Check terrain tile at this sub-cell
		var (terrainX, terrainY) = SubGridData.SubToTerrain(newSubX, newSubY);

		bool newTerrainTile = (terrainX != _lastCheckedTerrainX || terrainY != _lastCheckedTerrainY);

		if (newTerrainTile)
		{
			_lastCheckedTerrainX = terrainX;
			_lastCheckedTerrainY = terrainY;

			// Check for creature hit
			Creature hitCreature = _creatureManager?.GetCreatureAt(terrainX, terrainY);
			if (hitCreature != null)
			{
				// Place this cell, then grab and retract
				PlaceHarpoonCell(newSubX, newSubY);
				_grabbedCreature = hitCreature;
				GD.Print($"Harpoon grabbed {hitCreature.Species} at terrain ({terrainX},{terrainY})!");
				EmitSignal(SignalName.CreatureGrabbed, terrainX, terrainY);
				StartRetract();
				return false;
			}

			// Harpoon can only travel through air
			TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);
			if (tile != TileType.Air)
			{
				GD.Print($"Harpoon hit {tile} at terrain ({terrainX},{terrainY}), retracting.");
				StartRetract();
				return false;
			}
		}

		// Max range reached
		if (_harpoonPath.Count >= _targetRange)
		{
			GD.Print("Harpoon reached max range, retracting.");
			StartRetract();
			return false;
		}

		// Place sub-cell
		PlaceHarpoonCell(newSubX, newSubY);
		return true;
	}

	private void PlaceHarpoonCell(int subX, int subY)
	{
		_harpoonPath.Add((subX, subY));
		_tendril.SubGrid.SetCell(subX, subY, SubCellState.Core, 0, 220);
	}

	// =========================================================================
	//  RETRACTING — remove sub-cells from tip back toward head
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

		// Retract multiple steps per tick
		for (int i = 0; i < SubStepsPerTick; i++)
		{
			if (_harpoonPath.Count == 0)
			{
				FinishRetract();
				return;
			}

			// Remove the furthest cell (retract from tip toward head)
			var (rx, ry) = _harpoonPath[^1];
			_harpoonPath.RemoveAt(_harpoonPath.Count - 1);

			// Clear the sub-cell
			_tendril.SubGrid.ClearCell(rx, ry);

			// If dragging a creature, move it along the retracting tip
			if (_grabbedCreature != null && _grabbedCreature.IsAlive && _harpoonPath.Count > 0)
			{
				// Move creature to the terrain tile at the current retract tip
				var (tipSubX, tipSubY) = _harpoonPath[^1];
				var (terrainX, terrainY) = SubGridData.SubToTerrain(tipSubX, tipSubY);
				_creatureManager?.ForceCreaturePosition(_grabbedCreature, terrainX, terrainY);
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
	//  CAVE DETECTION — checks terrain tiles near the head for air
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
	//  INPUT — Spacebar + R2 trigger with edge detection
	// =========================================================================

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
