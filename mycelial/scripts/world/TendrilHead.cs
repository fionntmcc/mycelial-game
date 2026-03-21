namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Continuous-space steering physics for the tendril head.
///
/// The head lives in pixel-space (float coordinates), completely decoupled from the
/// tile grid. Player input applies steering torque to rotate the heading — it does NOT
/// set velocity directly. This creates arcing, organic movement.
///
/// Tiles exert drag/resistance as the head passes through them.
/// Solid tiles use circle-vs-AABB collision with wall-sliding (no hard stops).
///
/// COORDINATE SYSTEM:
///   HeadPosition is in world pixels.
///   To get the tile coordinate: tileX = FloorToInt(HeadPosition.X / TileSize)
///
/// SETUP:
///   - Add as a child of TendrilController
///   - Initialize() with ChunkManager reference and start position
///   - Call Update(delta) from _Process each frame
///   - Read HeadPosition, Heading, Speed for rendering/game logic
/// </summary>
public partial class TendrilHead : Node2D
{
	// =========================================================================
	//  CONFIGURATION — Tune these in the inspector with the debug overlay
	// =========================================================================

	[ExportGroup("Core Physics")]

	/// <summary>Forward thrust force (pixels/sec²). Higher = faster acceleration.</summary>
	[Export] public float Thrust = 480f;

	/// <summary>Base drag coefficient. Higher = more deceleration when not thrusting.</summary>
	[Export] public float BaseDrag = 3.5f;

	/// <summary>Max turn rate in radians/sec. Lower = wider arcs, more commitment to direction.</summary>
	[Export] public float MaxTurnRate = 5.0f;

	/// <summary>Max speed in pixels/sec.</summary>
	[Export] public float MaxSpeed = 360f;

	/// <summary>Below this speed (px/sec), the head stops completely.</summary>
	[Export] public float DeadZoneSpeed = 10f;

	/// <summary>Radius of the collision circle in pixels.</summary>
	[Export] public float CollisionRadius = 6f;

	/// <summary>Friction when sliding along walls. 0 = ice, 1 = glue.</summary>
	[Export] public float WallSlideFriction = 0.12f;

	/// <summary>Momentum retained on steep wall impact (0-1).</summary>
	[Export] public float WallBounceRetention = 0.55f;

	[ExportGroup("Terrain Resistance")]

	/// <summary>Drag multiplier for soft soil (dirt, leaf litter).</summary>
	[Export] public float SoftSoilDrag = 1.0f;

	/// <summary>Drag multiplier for dense material (clay, gravel).</summary>
	[Export] public float DenseMaterialDrag = 2.8f;

	/// <summary>Drag multiplier for wet/saturated tiles. Below 1.0 = speed boost.</summary>
	[Export] public float WetTileDrag = 0.55f;

	/// <summary>Drag multiplier for already-infected (player-owned) tiles.</summary>
	[Export] public float InfectedTileDrag = 0.65f;

	/// <summary>Drag multiplier for sand.</summary>
	[Export] public float SandDrag = 1.6f;

	[ExportGroup("Controller")]
	[Export] public float ControllerDeadZone = 0.22f;

	// =========================================================================
	//  PUBLIC STATE
	// =========================================================================

	/// <summary>Current position in world pixel space.</summary>
	public Vector2 HeadPosition { get; private set; }

	/// <summary>Current heading angle in radians.</summary>
	public float Heading { get; private set; }

	/// <summary>Current heading as a unit vector.</summary>
	public Vector2 HeadingVector => new(Mathf.Cos(Heading), Mathf.Sin(Heading));

	/// <summary>Current velocity in pixels/sec.</summary>
	public Vector2 Velocity { get; private set; }

	/// <summary>Current speed magnitude in pixels/sec.</summary>
	public float Speed => Velocity.Length();

	/// <summary>The tile coordinate the head is currently in.</summary>
	public (int X, int Y) CurrentTile => (
		Mathf.FloorToInt(HeadPosition.X / WorldConfig.TileSize),
		Mathf.FloorToInt(HeadPosition.Y / WorldConfig.TileSize)
	);

	/// <summary>True if the head entered a new tile this frame.</summary>
	public bool EnteredNewTile { get; private set; }

	/// <summary>The tile we were in last frame.</summary>
	public (int X, int Y) PreviousTile { get; private set; }

	/// <summary>True if the head is currently sliding along a solid surface.</summary>
	public bool IsSlidingAlongWall { get; private set; }

	/// <summary>Normal of the wall we're sliding against.</summary>
	public Vector2 WallNormal { get; private set; }

	// =========================================================================
	//  INTERNAL
	// =========================================================================

	private ChunkManager _chunkManager;
	private (int X, int Y) _lastTile;

	// =========================================================================
	//  INITIALIZATION
	// =========================================================================

	/// <summary>
	/// Initialize the head at a world pixel position.
	/// </summary>
	public void Initialize(ChunkManager chunkManager, Vector2 startPosition, float startHeading = Mathf.Pi * 0.5f)
	{
		_chunkManager = chunkManager;
		HeadPosition = startPosition;
		Heading = startHeading; // Default: pointing down (+Y)
		Velocity = Vector2.Zero;
		_lastTile = CurrentTile;
		PreviousTile = _lastTile;
		EnteredNewTile = false;
	}

	/// <summary>
	/// Teleport the head (e.g. respawn at root tip). Resets velocity.
	/// </summary>
	public void Teleport(Vector2 position, float heading)
	{
		HeadPosition = position;
		Heading = heading;
		Velocity = Vector2.Zero;
		_lastTile = CurrentTile;
		PreviousTile = _lastTile;
		EnteredNewTile = false;
	}

	// =========================================================================
	//  PHYSICS UPDATE — Call from TendrilController._Process
	// =========================================================================

	/// <summary>
	/// Run one frame of steering physics. Returns the raw input vector.
	/// </summary>
	public Vector2 Update(float delta)
	{
		Vector2 input = GetSteeringInput();

		// --- 1. Steering torque ---
		if (input.LengthSquared() > 0.01f)
		{
			float targetHeading = Mathf.Atan2(input.Y, input.X);
			float angleDiff = Mathf.Wrap(targetHeading - Heading, -Mathf.Pi, Mathf.Pi);

			// Turn rate scales with input magnitude (gentle tilt = gentle curve)
			float maxTurn = MaxTurnRate * delta * Mathf.Clamp(input.Length(), 0f, 1f);
			float turnAmount = Mathf.Clamp(angleDiff, -maxTurn, maxTurn);
			Heading += turnAmount;
		}

		// --- 2. Thrust along heading ---
		float thrustMagnitude = input.LengthSquared() > 0.01f ? Mathf.Clamp(input.Length(), 0f, 1f) : 0f;
		Vector2 thrustForce = HeadingVector * (Thrust * thrustMagnitude);

		// --- 3. Terrain drag ---
		float terrainDrag = GetTerrainDrag(CurrentTile.X, CurrentTile.Y);
		float effectiveDrag = BaseDrag * terrainDrag;

		// --- 4. Integrate velocity (semi-implicit Euler) ---
		Velocity += thrustForce * delta;
		Velocity -= Velocity * (effectiveDrag * delta);

		// Clamp max speed
		if (Velocity.LengthSquared() > MaxSpeed * MaxSpeed)
			Velocity = Velocity.Normalized() * MaxSpeed;

		// Dead zone
		if (Velocity.LengthSquared() < DeadZoneSpeed * DeadZoneSpeed && thrustMagnitude < 0.01f)
			Velocity = Vector2.Zero;

		// --- 5. Move with collision ---
		PreviousTile = _lastTile;
		Vector2 desiredMove = Velocity * delta;

		if (desiredMove.LengthSquared() > 0.01f)
			HeadPosition = MoveWithCollision(HeadPosition, desiredMove);

		// --- 6. Detect tile transitions ---
		var newTile = CurrentTile;
		EnteredNewTile = (newTile.X != _lastTile.X || newTile.Y != _lastTile.Y);
		_lastTile = newTile;

		// Sync Godot node transform
		Position = HeadPosition;

		return input;
	}

	// =========================================================================
	//  COLLISION — Circle vs Tile Grid with Wall-Sliding
	// =========================================================================

	private Vector2 MoveWithCollision(Vector2 from, Vector2 move)
	{
		IsSlidingAlongWall = false;
		WallNormal = Vector2.Zero;

		Vector2 target = from + move;

		for (int iteration = 0; iteration < 4; iteration++)
		{
			Vector2 pushOut = GetTileCollisionPushOut(target);
			if (pushOut.LengthSquared() < 0.001f)
				break;

			target += pushOut;
			Vector2 normal = pushOut.Normalized();

			// Wall-slide: strip velocity component going into the wall
			float velIntoWall = Velocity.Dot(normal);
			if (velIntoWall < 0)
			{
				Velocity -= normal * velIntoWall;
				Velocity *= (1f - WallSlideFriction);

				// More energy loss for head-on impacts, less for glancing
				float impactAngle = Mathf.Abs(velIntoWall) / (Speed + 0.001f);
				Velocity *= Mathf.Lerp(1f, WallBounceRetention, impactAngle);

				IsSlidingAlongWall = true;
				WallNormal = normal;
			}
		}

		return target;
	}

	private Vector2 GetTileCollisionPushOut(Vector2 position)
	{
		Vector2 totalPush = Vector2.Zero;
		int ts = WorldConfig.TileSize;

		int centerTileX = Mathf.FloorToInt(position.X / ts);
		int centerTileY = Mathf.FloorToInt(position.Y / ts);

		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				int tx = centerTileX + dx;
				int ty = centerTileY + dy;

				if (!IsTileSolid(tx, ty)) continue;

				// AABB of this tile in pixel space
				float tileLeft = tx * ts;
				float tileTop = ty * ts;
				float tileRight = tileLeft + ts;
				float tileBottom = tileTop + ts;

				// Closest point on AABB to circle center
				float closestX = Mathf.Clamp(position.X, tileLeft, tileRight);
				float closestY = Mathf.Clamp(position.Y, tileTop, tileBottom);

				Vector2 diff = position - new Vector2(closestX, closestY);
				float distSq = diff.LengthSquared();

				if (distSq < CollisionRadius * CollisionRadius && distSq > 0.0001f)
				{
					float dist = Mathf.Sqrt(distSq);
					float overlap = CollisionRadius - dist;
					totalPush += (diff / dist) * overlap;
				}
				else if (distSq < 0.0001f)
				{
					// Exactly on the edge — push away from tile center
					Vector2 tileCenter = new(tileLeft + ts * 0.5f, tileTop + ts * 0.5f);
					Vector2 away = (position - tileCenter).Normalized();
					totalPush += away * CollisionRadius;
				}
			}
		}

		return totalPush;
	}

	/// <summary>
	/// Whether a tile is impassable. Air is also impassable — the tendril burrows
	/// through material, it can't fly through open space.
	/// </summary>
	private bool IsTileSolid(int tileX, int tileY)
	{
		if (_chunkManager == null) return true;

		TileType tile = _chunkManager.GetTileAt(tileX, tileY);

		if (tile == TileType.Air) return true;
		if (tile == TileType.Stone) return true;
		if (tile == TileType.Water) return true;
		if (tile == TileType.Lava) return true;
		if (tile == TileType.ToxicWater) return true;
		if (tile == TileType.AcidPool) return true;
		if (tile == TileType.Wood) return true;
		if (tile == TileType.Obsidian) return true;
		if (tile == TileType.Basalt) return true;

		return false;
	}

	// =========================================================================
	//  TERRAIN RESISTANCE
	// =========================================================================

	private float GetTerrainDrag(int tileX, int tileY)
	{
		if (_chunkManager == null) return 1f;

		TileType tile = _chunkManager.GetTileAt(tileX, tileY);

		// Player-owned tiles: speed boost (retracing network is fast)
		if (TileProperties.Is(tile, TileFlags.PlayerOwned))
			return InfectedTileDrag;

		return tile switch
		{
			TileType.Dirt => SoftSoilDrag,
			TileType.Leaf => SoftSoilDrag * 0.85f,
			TileType.InfectedDirt => InfectedTileDrag,

			TileType.Clay => DenseMaterialDrag,
			TileType.Gravel => DenseMaterialDrag * 0.8f,

			TileType.Sand => SandDrag,

			TileType.Roots => SoftSoilDrag * 1.3f,
			TileType.RootTip => SoftSoilDrag * 1.1f,

			TileType.BioluminescentVein => WetTileDrag,
			TileType.BoneMarrow => SoftSoilDrag * 0.8f,
			TileType.CrystalGrotte => DenseMaterialDrag,

			_ when IsGrassTile(tile) => SoftSoilDrag * 1.1f,

			_ => SoftSoilDrag,
		};
	}

	private static bool IsGrassTile(TileType tile)
	{
		int id = (int)tile;
		return (id >= 16 && id <= 23) || (id >= 32 && id <= 35);
	}

	// =========================================================================
	//  INPUT
	// =========================================================================

	private Vector2 GetSteeringInput()
	{
		int keyX = 0, keyY = 0;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) keyY -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) keyY += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) keyX -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) keyX += 1;

		Vector2 keyboard = new(keyX, keyY);
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
				float norm = Mathf.Clamp((len - ControllerDeadZone) / (1f - ControllerDeadZone), 0f, 1f);
				stick = stick.Normalized() * norm;
			}
		}

		Vector2 combined = keyboard + stick;
		return combined.LengthSquared() > 1f ? combined.Normalized() : combined;
	}

	// =========================================================================
	//  UTILITY
	// =========================================================================

	/// <summary>Convert tile grid position to pixel center of that tile.</summary>
	public static Vector2 TileToPixel(int tileX, int tileY)
	{
		return new Vector2(
			(tileX + 0.5f) * WorldConfig.TileSize,
			(tileY + 0.5f) * WorldConfig.TileSize
		);
	}

	/// <summary>Convert pixel position to tile coordinates.</summary>
	public static (int X, int Y) PixelToTile(Vector2 pixel)
	{
		return (
			Mathf.FloorToInt(pixel.X / WorldConfig.TileSize),
			Mathf.FloorToInt(pixel.Y / WorldConfig.TileSize)
		);
	}
}
