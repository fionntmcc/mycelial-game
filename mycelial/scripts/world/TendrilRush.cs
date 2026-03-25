namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Rush state machine for the tendril — follow, hold, dash, and retract.
///
/// Owns all rush-related configuration, state flags, and process methods.
/// This component exists solely to serve the harpoon system: when the tendril
/// fires a harpoon and grabs a creature, the rush system creeps the tendril
/// along the harpoon path, holds at the tip, then dashes through air on throw.
///
/// Uses a back-reference to TendrilController for position/blob manipulation
/// since rush inherently operates as a mode of the controller's movement.
///
/// STATE MACHINE:
///   Idle → BeginFollow → (IsFollowing) → (IsHolding) → ResolveHold
///        → ApplyImpulse → (IsDashing) → EndDash → Idle
///   OR:  (IsFollowing/IsHolding) → BeginRetractFollow → (IsRetractFollowing)
///        → RetractFollowStep... → FinishRetractFollow → Idle
/// </summary>
public partial class TendrilRush : Node
{
	// =========================================================================
	//  CONFIG
	// =========================================================================

	/// <summary>Delay between rush-follow animation steps.</summary>
	[Export] public float RushFollowStepDelay = 0.003f;

	/// <summary>Number of sub-steps per tick during rush-follow.</summary>
	[Export] public int RushFollowSubStepsPerTick = 5;

	/// <summary>Base impulse applied on rush dash (sub-cells/s).</summary>
	[Export] public float RushDashBaseImpulse = 180f;

	/// <summary>Additional impulse per sub-cell of distance to collision.</summary>
	[Export] public float RushDashDistanceScale = 1.5f;

	/// <summary>Drag applied during rush dash (decelerates over time).</summary>
	[Export] public float RushDashDrag = 1.8f;

	/// <summary>Minimum speed to maintain during rush dash. Below this the rush ends.</summary>
	[Export] public float RushDashMinSpeed = 40f;

	/// <summary>Max sub-steps per frame during rush dash (higher = smoother at speed).</summary>
	[Export] public int RushDashMaxSubSteps = 24;

	// =========================================================================
	//  STATE
	// =========================================================================

	/// <summary>True when creeping along the harpoon path toward the tip.</summary>
	public bool IsFollowing { get; private set; }

	/// <summary>True when holding at the harpoon tip, waiting for eat/throw.</summary>
	public bool IsHolding { get; private set; }

	/// <summary>True when dashing through air after a throw collision.</summary>
	public bool IsDashing { get; private set; }

	/// <summary>True when following the harpoon backward during retraction.</summary>
	public bool IsRetractFollowing { get; private set; }

	/// <summary>True when any rush state is active.</summary>
	public bool IsAnyActive => IsFollowing || IsHolding || IsDashing || IsRetractFollowing;

	// Rush-follow state
	private int _followIndex;
	private float _followTimer;
	private int _originSubX;
	private int _originSubY;
	private readonly HashSet<(int X, int Y)> _followTrailCells = new();

	// Rush dash state
	private Vector2 _dashVelocity;
	private float _dashAccumulator;

	// References
	private TendrilController _controller;
	private TendrilHarpoon _harpoon;

	// =========================================================================
	//  INITIALIZATION
	// =========================================================================

	/// <summary>
	/// Wire up references. Called once from TendrilController.Initialize.
	/// </summary>
	public void Initialize(TendrilController controller, TendrilHarpoon harpoon)
	{
		_controller = controller;
		_harpoon = harpoon;
	}

	// =========================================================================
	//  RUSH FOLLOW — creep along harpoon path to tip
	// =========================================================================

	/// <summary>
	/// Begin creeping along the given path toward the harpoon tip.
	/// Called by TendrilHarpoon when a creature is grabbed with throw unlocked.
	/// </summary>
	public void BeginFollow()
	{
		if (_controller.IsRetreating || _controller.IsRegenerating) return;
		if (_harpoon == null) return;

		_originSubX = _controller.SubHeadX;
		_originSubY = _controller.SubHeadY;
		_followIndex = 0;
		_followTimer = 0f;
		_followTrailCells.Clear();
		IsFollowing = true;
		IsHolding = false;
		_controller.ClearMomentum();
	}

	/// <summary>
	/// Process rush-follow: step along the harpoon path each tick.
	/// Called from TendrilController._Process when IsFollowing is true.
	/// </summary>
	public void ProcessFollow(float dt)
	{
		if (_harpoon == null)
		{
			CancelFollow();
			return;
		}

		var path = _harpoon.HarpoonPath;

		// Harpoon fully retracted while we were following — cancel
		if (!_harpoon.IsActive && path.Count == 0)
		{
			CancelFollow();
			return;
		}

		// Already at or past the end of the current path
		if (_followIndex >= path.Count)
		{
			if (_harpoon.IsArmed)
			{
				IsFollowing = false;
				IsHolding = true;
			}
			return;
		}

		_followTimer -= dt;
		if (_followTimer > 0f) return;
		_followTimer = RushFollowStepDelay;

		for (int i = 0; i < RushFollowSubStepsPerTick; i++)
		{
			if (_followIndex >= path.Count)
			{
				if (_harpoon.IsArmed)
				{
					IsFollowing = false;
					IsHolding = true;
				}
				return;
			}

			var (targetSubX, targetSubY) = path[_followIndex];
			_followIndex++;

			foreach (var cell in _controller.CoreCells)
				_followTrailCells.Add(cell);

			_controller.RushMoveToSub(targetSubX, targetSubY);
		}
	}

	/// <summary>
	/// Resolve rush hold — the controller stays at the tip.
	/// Called when the player throws (commits to the position).
	/// </summary>
	public void ResolveHold()
	{
		IsFollowing = false;
		IsHolding = false;
		// Trail cells are kept for visual continuity during throw
	}

	/// <summary>
	/// Clean up trail cells deposited during rush-follow.
	/// Does NOT snap back — use when controller is already at final position.
	/// </summary>
	public void CleanupTrail()
	{
		foreach (var (cx, cy) in _followTrailCells)
			_controller.SubGrid.ClearCell(cx, cy);
		_followTrailCells.Clear();
	}

	/// <summary>
	/// Clean up trail cells and snap controller back to pre-rush origin.
	/// </summary>
	public void CleanupTrailAndReturn()
	{
		CleanupTrail();
		_controller.RushMoveToSub(_originSubX, _originSubY);
	}

	/// <summary>
	/// Cancel rush follow and snap back to the origin position.
	/// Called on eat, retreat, or any cancellation.
	/// </summary>
	public void CancelFollow()
	{
		if (!IsFollowing && !IsHolding) return;

		IsFollowing = false;
		IsHolding = false;

		foreach (var (cx, cy) in _followTrailCells)
			_controller.SubGrid.ClearCell(cx, cy);
		_followTrailCells.Clear();

		_controller.RushMoveToSub(_originSubX, _originSubY);
	}

	// =========================================================================
	//  RETRACT FOLLOW — walk backward along harpoon path
	// =========================================================================

	/// <summary>
	/// Begin following the harpoon backward during retraction.
	/// </summary>
	public void BeginRetractFollow()
	{
		foreach (var cell in _controller.CoreCells)
			_followTrailCells.Add(cell);

		IsFollowing = false;
		IsHolding = false;
		IsRetractFollowing = true;
		_controller.ClearMomentum();
	}

	/// <summary>
	/// Move the controller one step backward during harpoon retraction.
	/// </summary>
	public void RetractFollowStep(int targetSubX, int targetSubY)
	{
		if (!IsRetractFollowing) return;
		if (_controller.SubHeadX == targetSubX && _controller.SubHeadY == targetSubY) return;

		// Clear old core cells
		foreach (var (x, y) in _controller.CoreCells)
			_controller.SubGrid.ClearCell(x, y);

		SweepClearTrail(_controller.SubHeadX, _controller.SubHeadY);

		_controller.SetSubHeadDirect(targetSubX, targetSubY);
		_controller.PlaceBlobAndEmitMoved();
	}

	/// <summary>
	/// Finish retract-follow: clean up remaining rush trail, restore origin, re-place blob.
	/// </summary>
	public void FinishRetractFollow()
	{
		if (!IsRetractFollowing) return;
		IsRetractFollowing = false;

		// Clear the current blob
		foreach (var (x, y) in _controller.CoreCells)
			_controller.SubGrid.ClearCell(x, y);

		// Clean up remaining trail cells
		foreach (var (cx, cy) in _followTrailCells)
			_controller.SubGrid.ClearCell(cx, cy);
		_followTrailCells.Clear();

		// Restore to pre-rush origin
		_controller.SetSubHeadDirect(_originSubX, _originSubY);
		_controller.PlaceBlobAndEmitMoved();
	}

	/// <summary>
	/// Clear rush-follow trail cells within blob radius of a position.
	/// </summary>
	private void SweepClearTrail(int centerSubX, int centerSubY)
	{
		int clearRadius = _controller.BlobBaseRadius + (int)_controller.BlobNoiseAmplitude + 2;
		int clearRadiusSq = clearRadius * clearRadius;

		for (int dy = -clearRadius; dy <= clearRadius; dy++)
		{
			for (int dx = -clearRadius; dx <= clearRadius; dx++)
			{
				if (dx * dx + dy * dy > clearRadiusSq) continue;

				var pos = (centerSubX + dx, centerSubY + dy);
				if (_followTrailCells.Remove(pos))
					_controller.SubGrid.ClearCell(pos.Item1, pos.Item2);
			}
		}
	}

	// =========================================================================
	//  RUSH DASH — momentum charge through air after throw collision
	// =========================================================================

	/// <summary>
	/// Apply a momentum impulse. The controller will fly through air tiles
	/// and land on the first traversible solid block.
	/// </summary>
	public void ApplyImpulse(Vector2 direction, float distance)
	{
		if (_controller.IsRetreating || _controller.IsRegenerating) return;
		if (direction.LengthSquared() < 0.0001f) return;

		float impulse = RushDashBaseImpulse + distance * RushDashDistanceScale;
		_dashVelocity = direction.Normalized() * impulse;
		_dashAccumulator = 0f;
		IsDashing = true;
		_controller.SetLastMoveDir(direction.Normalized());
	}

	/// <summary>
	/// Process the rush dash each frame — step through sub-cells using TrySubMove.
	/// Called from TendrilController._Process when IsDashing is true.
	/// </summary>
	public void ProcessDash(float dt)
	{
		float speed = _dashVelocity.Length();
		if (speed <= RushDashMinSpeed)
		{
			EndDash();
			return;
		}

		_dashVelocity *= Mathf.Max(0f, 1f - RushDashDrag * dt);

		_dashAccumulator += _dashVelocity.Length() * dt;

		int steps = 0;
		while (_dashAccumulator >= 1f && steps < RushDashMaxSubSteps)
		{
			_dashAccumulator -= 1f;
			steps++;

			Vector2 dir = _dashVelocity.Normalized();
			int headX = _controller.SubHeadX;
			int headY = _controller.SubHeadY;

			int nextX = headX + (Mathf.Abs(dir.X) >= Mathf.Abs(dir.Y) ? System.Math.Sign(dir.X) : 0);
			int nextY = headY + (Mathf.Abs(dir.Y) > Mathf.Abs(dir.X) ? System.Math.Sign(dir.Y) : 0);

			if (Mathf.Abs(dir.X) > 0.3f && Mathf.Abs(dir.Y) > 0.3f)
			{
				nextX = headX + System.Math.Sign(dir.X);
				nextY = headY + System.Math.Sign(dir.Y);
			}

			bool moved = _controller.TrySubMoveStep(nextX, nextY);

			if (!moved)
			{
				if (_controller.TrySubMoveStep(headX + System.Math.Sign(dir.X), headY))
					moved = true;
				else if (_controller.TrySubMoveStep(headX, headY + System.Math.Sign(dir.Y)))
					moved = true;
			}

			if (!moved)
			{
				EndDash();
				return;
			}

			// IsDashing may have been cleared by TrySubMove landing on solid ground
			if (!IsDashing)
				return;
		}
	}

	/// <summary>
	/// Called by TendrilController.TrySubMove when dash lands on solid ground.
	/// </summary>
	public void NotifyDashLanded()
	{
		IsDashing = false;
	}

	private void EndDash()
	{
		IsDashing = false;
		_dashVelocity = Vector2.Zero;
		_dashAccumulator = 0f;
		_controller.ClearMomentum();
	}

	/// <summary>
	/// Force-cancel the retract-following flag without full cleanup.
	/// Used by StartRetreat when everything is about to be rebuilt anyway.
	/// </summary>
	public void ForceEndRetract()
	{
		IsRetractFollowing = false;
	}
}
