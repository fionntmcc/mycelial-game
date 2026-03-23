namespace Mycorrhiza.World;

using Godot;
using System;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// A single living creature in the world.
/// Now lives on the sub-grid (4px resolution) alongside the tendril.
///
/// Position truth is (SubX, SubY) in sub-grid coordinates.
/// Terrain-tile coordinates are derived for gameplay checks (biome, spawn, despawn).
/// </summary>
public class Creature
{
	public CreatureSpecies Species;
	public bool IsAlive;
	public bool IsActive;          // False = dormant (burrowed/ambush), invisible
	public int Health;
	public CreatureBehavior Behavior;

	// === Sub-Grid Position (source of truth) ===
	public int SubX, SubY;
	public Vector2 Velocity;           // Current movement direction × speed
	public Vector2 MoveAccumulator;    // Fractional sub-cell accumulation

	// === Derived terrain coords (for biome checks, despawn distance, etc.) ===
	public int TileX => SubGridData.SubToTerrain(SubX, SubY).TerrainX;
	public int TileY => SubGridData.SubToTerrain(SubX, SubY).TerrainY;

	// === AI State ===
	public float MoveTimer;            // Countdown to next direction change
	public float AttackCooldown;
	public int PatrolOriginSubX, PatrolOriginSubY;
	public bool IsFleeing;

	// === Visual ===
	public CreatureBody Body;
	public float AnimTimeOffset;       // Per-creature offset so they don't all animate in sync
    public float DamageFlashTimer;     // Counts down from DamageFlashDuration on hit
}

/// <summary>
/// Manages all creatures in the world. Handles spawning, sub-grid AI movement,
/// pixel-perfect collision with the tendril, and creature-to-creature interaction.
///
/// Creatures are no longer rendered as tile overlays. They are pure data objects
/// painted by CreatureRenderer onto the same image layer as the tendril.
///
/// SETUP:
///   - Add as child of World (Node2D)
///   - Assign ChunkManagerPath and TendrilControllerPath
///   - Add a CreatureRenderer Sprite2D and point it at this node
/// </summary>
public partial class CreatureManager : Node2D
{
    [Export] public NodePath ChunkManagerPath { get; set; }
    [Export] public NodePath TendrilControllerPath { get; set; }

    // --- Spawn Config ---
    [Export] public float SpawnCheckInterval = 2.0f;
    [Export] public int MaxCreatures = 60;
    [Export] public int SpawnRadius = 40;           // In terrain tiles
    [Export] public int DespawnRadius = 80;          // In terrain tiles
    [Export] public int MinSpawnDistance = 8;         // In terrain tiles

    // --- Collision Config ---
    /// <summary>Radius around tendril head for "head overlap" consumption checks (sub-cells).</summary>
    [Export] public int HeadCollisionRadius = 5;

    /// <summary>How long the damage flash lasts (seconds).</summary>
    [Export] public float DamageFlashDuration = 0.12f;

    // --- State ---
    private ChunkManager _chunkManager;
    private TendrilController _tendril;
    private readonly List<Creature> _creatures = new();
    private float _spawnTimer;
    private readonly Random _rng = new();

    // =========================================================================
    //  LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        if (ChunkManagerPath != null)
            _chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
        if (TendrilControllerPath != null)
            _tendril = GetNode<TendrilController>(TendrilControllerPath);

        if (_chunkManager == null || _tendril == null)
            GD.PrintErr("CreatureManager: Missing ChunkManager or TendrilController!");
    }

    public override void _Process(double delta)
    {
        if (_chunkManager == null || _tendril == null) return;
        if (_tendril.IsRetreating || _tendril.IsRegenerating) return;

        float dt = (float)delta;

        // Spawn/despawn on timer
        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            _spawnTimer = SpawnCheckInterval;
            TrySpawnCreatures();
            DespawnDistantCreatures();
        }

        // Update all creatures
        for (int i = _creatures.Count - 1; i >= 0; i--)
        {
            var creature = _creatures[i];
            if (!creature.IsAlive)
            {
                _creatures.RemoveAt(i);
                continue;
            }

            // Tick damage flash
            if (creature.DamageFlashTimer > 0)
                creature.DamageFlashTimer -= dt;

            UpdateCreatureAI(creature, dt);
            CheckTendrilCollision(creature);
        }
    }

    // =========================================================================
    //  PUBLIC API (for CreatureRenderer, TendrilHarpoon, TendrilController)
    // =========================================================================

    /// <summary>Get all creatures (for rendering).</summary>
    public List<Creature> GetAllCreatures() => _creatures;

    /// <summary>Get all active, living creatures (convenience).</summary>
    public IEnumerable<Creature> GetActiveCreatures()
    {
        foreach (var c in _creatures)
        {
            if (c.IsAlive && c.IsActive) yield return c;
        }
    }

    /// <summary>
    /// Find a creature whose body overlaps a specific sub-grid position.
    /// Used by harpoon hit detection — replaces old GetCreatureAt(tileX, tileY).
    /// </summary>
    public Creature GetCreatureAtSubGrid(int subX, int subY)
    {
        foreach (var creature in _creatures)
        {
            if (!creature.IsAlive || !creature.IsActive) continue;

            // Broad phase: bounding radius check
            int dx = creature.SubX - subX;
            int dy = creature.SubY - subY;
            int r = creature.Body?.Radius ?? 4;
            if (dx * dx + dy * dy > (r + 1) * (r + 1)) continue;

            // Narrow phase: check body cells
            if (creature.Body == null) continue;
            foreach (var (cdx, cdy) in creature.Body.Cells)
            {
                if (creature.SubX + cdx == subX && creature.SubY + cdy == subY)
                    return creature;
            }
        }
        return null;
    }

    /// <summary>
    /// Find the nearest living, active creature within a radius of a sub-grid position.
	/// Returns the creature's sub-grid position, or null if none found.
	/// Used by TendrilController for auto-steering toward prey.
	/// </summary>
	public (int SubX, int SubY)? GetNearestCreatureSubPosition(int centerSubX, int centerSubY, int radiusSub)
	{
		int bestDistSq = radiusSub * radiusSub;
		(int, int)? best = null;

		foreach (var creature in _creatures)
		{
			if (!creature.IsAlive || !creature.IsActive) continue;

			int dx = creature.SubX - centerSubX;
			int dy = creature.SubY - centerSubY;
			int distSq = dx * dx + dy * dy;

			if (distSq < bestDistSq)
			{
				bestDistSq = distSq;
				best = (creature.SubX, creature.SubY);
			}
		}

		return best;
	}

	/// <summary>
	/// Find the nearest creature object (not just position) for the harpoon to grab.
	/// </summary>
	public Creature GetNearestCreature(int subX, int subY, int radiusSub)
	{
		int bestDistSq = radiusSub * radiusSub;
		Creature best = null;

		foreach (var creature in _creatures)
		{
			if (!creature.IsAlive || !creature.IsActive) continue;

			int dx = creature.SubX - subX;
			int dy = creature.SubY - subY;
			int distSq = dx * dx + dy * dy;

			if (distSq < bestDistSq)
			{
				bestDistSq = distSq;
				best = creature;
			}
		}

		return best;
	}

	/// <summary>Kill a creature from an external source (harpoon delivery).</summary>
	public void KillCreatureExternal(Creature creature)
	{
		if (creature == null || !creature.IsAlive) return;
		creature.IsAlive = false;
	}

	/// <summary>
	/// Forcibly move a creature to a new sub-grid position (harpoon drag).
	/// </summary>
	public void ForceCreatureSubPosition(Creature creature, int newSubX, int newSubY)
	{
		if (creature == null || !creature.IsAlive) return;
		creature.SubX = newSubX;
		creature.SubY = newSubY;
	}

	/// <summary>
	/// Apply a velocity impulse to a creature (knockback, slam, etc.).
	/// The creature's normal AI will reassert after MoveTimer expires.
    /// </summary>
    public void ApplyImpulse(Creature creature, Vector2 impulse)
    {
        if (creature == null || !creature.IsAlive) return;
        creature.Velocity = impulse;
        creature.MoveAccumulator = Vector2.Zero;
        creature.MoveTimer = 0.3f; // Override AI for a brief moment
    }

    /// <summary>
    /// Deal damage to a creature. Returns true if the creature died.
    /// Triggers damage flash.
    /// </summary>
    public bool DamageCreature(Creature creature, int damage)
    {
        if (creature == null || !creature.IsAlive) return false;

        creature.Health -= damage;
        creature.DamageFlashTimer = DamageFlashDuration;

        if (creature.Health <= 0)
        {
            creature.IsAlive = false;
            return true;
        }
        return false;
    }

    // =========================================================================
    //  SPAWNING
    // =========================================================================

    private void TrySpawnCreatures()
    {
        if (_creatures.Count >= MaxCreatures) return;

        int headTileX = _tendril.HeadX;
        int headTileY = _tendril.HeadY;

        BiomeType currentBiome = _chunkManager.GetBiomeAt(headTileX, headTileY);
        var validSpecies = CreatureRegistry.GetForBiome(currentBiome);
        if (validSpecies.Count == 0) return;

        // Also pull in creatures from nearby biomes
        BiomeType nearbyBiome = _chunkManager.GetBiomeAt(headTileX + 30, headTileY);
        if (nearbyBiome != currentBiome)
        {
            var nearbySpecies = CreatureRegistry.GetForBiome(nearbyBiome);
            foreach (var s in nearbySpecies)
            {
                if (_rng.Next(3) == 0)
                    validSpecies.Add(s);
            }
        }

        int toSpawn = 1 + _rng.Next(3);
        for (int i = 0; i < toSpawn; i++)
        {
            if (_creatures.Count >= MaxCreatures) break;

            var config = validSpecies[_rng.Next(validSpecies.Count)];

            if (TryFindSpawnPosition(headTileX, headTileY, out int tileX, out int tileY))
            {
                SpawnCreature(config, tileX, tileY);
            }
        }
    }

    private bool TryFindSpawnPosition(int centerX, int centerY, out int x, out int y)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            x = centerX + _rng.Next(-SpawnRadius, SpawnRadius + 1);
            y = centerY + _rng.Next(-SpawnRadius, SpawnRadius + 1);

            int dx = x - _tendril.HeadX;
            int dy = y - _tendril.HeadY;
            if (dx * dx + dy * dy < MinSpawnDistance * MinSpawnDistance) continue;

            TileType tile = _chunkManager.GetTileAt(x, y);
            if (tile == TileType.Air) return true;
            if (TileProperties.Is(tile, TileFlags.Organic) && !TileProperties.Is(tile, TileFlags.PlayerOwned))
                return true;
        }

        x = 0; y = 0;
        return false;
    }

    private void SpawnCreature(CreatureConfig config, int tileX, int tileY)
    {
        int scale = WorldConfig.SubGridScale;
        int subX = tileX * scale + scale / 2;
        int subY = tileY * scale + scale / 2;

        var creature = new Creature
        {
            Species = config.Species,
            SubX = subX,
            SubY = subY,
            Health = config.Health,
            Behavior = config.Behavior,
            MoveTimer = config.MoveSpeed * (0.5f + _rng.NextSingle()),
            AttackCooldown = 0,
            IsActive = config.Behavior != CreatureBehavior.Burrowed
                    && config.Behavior != CreatureBehavior.Ambush,
            IsAlive = true,
            PatrolOriginSubX = subX,
            PatrolOriginSubY = subY,
            IsFleeing = false,
            Velocity = Vector2.Zero,
            MoveAccumulator = Vector2.Zero,
            Body = CreatureBodyRegistry.GetDefaultBody(config.Species),
            AnimTimeOffset = _rng.NextSingle() * 10f, // Desync animations
            DamageFlashTimer = 0f,
        };

        _creatures.Add(creature);
    }

    private void DespawnDistantCreatures()
    {
        int headTileX = _tendril.HeadX;
        int headTileY = _tendril.HeadY;

        for (int i = _creatures.Count - 1; i >= 0; i--)
        {
            var c = _creatures[i];
            int dx = c.TileX - headTileX;
            int dy = c.TileY - headTileY;
            if (dx * dx + dy * dy > DespawnRadius * DespawnRadius)
            {
                _creatures.RemoveAt(i);
            }
        }
    }

    // =========================================================================
    //  AI UPDATE
    // =========================================================================

    private void UpdateCreatureAI(Creature creature, float dt)
    {
        var config = CreatureRegistry.GetConfig(creature.Species);

        if (creature.AttackCooldown > 0)
            creature.AttackCooldown -= dt;

        int headSubX = _tendril.SubHeadX;
        int headSubY = _tendril.SubHeadY;
        float dx = headSubX - creature.SubX;
        float dy = headSubY - creature.SubY;
        float distSub = MathF.Sqrt(dx * dx + dy * dy);

        // Convert detect/flee ranges to sub-grid units
        int scale = WorldConfig.SubGridScale;
        float detectRangeSub = config.DetectRange * scale;
        float fleeRangeSub = config.FleeRange * scale;

        switch (creature.Behavior)
        {
            case CreatureBehavior.Wander:
                AIWander(creature, config, dt);
                break;

            case CreatureBehavior.Skittish:
                if (distSub <= detectRangeSub)
                {
                    creature.IsFleeing = true;
                    AIFlee(creature, config, headSubX, headSubY, dt);
                }
                else
                {
                    creature.IsFleeing = false;
                    AIWander(creature, config, dt);
                }
                break;

            case CreatureBehavior.Burrowed:
                if (!creature.IsActive)
                {
                    if (distSub <= detectRangeSub)
                    {
                        creature.IsActive = true;
                        creature.IsFleeing = true;
                    }
                }
                else
                {
                    AIFlee(creature, config, headSubX, headSubY, dt);
                    if (distSub > fleeRangeSub)
                    {
                        creature.IsActive = false;
                        creature.IsFleeing = false;
                    }
                }
                break;

            case CreatureBehavior.Patrol:
                if (distSub <= detectRangeSub)
                {
                    AIChase(creature, config, headSubX, headSubY, dt);
                }
                else
                {
                    // Patrol around origin
                    float patrolDx = creature.SubX - creature.PatrolOriginSubX;
                    float patrolDy = creature.SubY - creature.PatrolOriginSubY;
                    float patrolDist = MathF.Sqrt(patrolDx * patrolDx + patrolDy * patrolDy);
                    if (patrolDist > 10 * scale)
                    {
                        AIChase(creature, config, creature.PatrolOriginSubX, creature.PatrolOriginSubY, dt);
                    }
                    else
                    {
                        AIWander(creature, config, dt);
                    }
                }
                break;

            case CreatureBehavior.Ambush:
                if (!creature.IsActive)
                {
                    if (distSub <= detectRangeSub)
                    {
                        creature.IsActive = true;
                    }
                }
                else
                {
                    AIChase(creature, config, headSubX, headSubY, dt);

                    // Give up if tendril gets very far
                    if (distSub > detectRangeSub * 3)
                    {
                        creature.IsActive = false;
                    }
                }
                break;

            case CreatureBehavior.Grazer:
                AIGrazer(creature, config, dt);
                break;
        }
    }

    // =========================================================================
    //  AI BEHAVIORS
    // =========================================================================

    private void AIWander(Creature creature, CreatureConfig config, float dt)
    {
        creature.MoveTimer -= dt;
        if (creature.MoveTimer <= 0)
        {
            // Pick a new random direction
            float angle = _rng.NextSingle() * MathF.Tau;
            creature.Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * config.SubGridSpeed;
            creature.MoveTimer = 0.5f + _rng.NextSingle() * 2.0f;
        }

        StepCreature(creature, dt);
    }

    private void AIFlee(Creature creature, CreatureConfig config,
                        int threatSubX, int threatSubY, float dt)
    {
        float fdx = creature.SubX - threatSubX;
        float fdy = creature.SubY - threatSubY;
        float dist = MathF.Sqrt(fdx * fdx + fdy * fdy);

        if (dist > 0.01f)
        {
            Vector2 away = new Vector2(fdx, fdy) / dist;

			// Add jitter so flee path isn't a perfect straight line
			float jitter = (_rng.NextSingle() - 0.5f) * 0.5f;
			away = away.Rotated(jitter);

			float speed = config.SubGridSpeed * config.AlertSpeedMultiplier;
			creature.Velocity = away * speed;
		}

		StepCreature(creature, dt);
	}

	private void AIChase(Creature creature, CreatureConfig config,
						 int targetSubX, int targetSubY, float dt)
	{
		float cdx = targetSubX - creature.SubX;
		float cdy = targetSubY - creature.SubY;
		float dist = MathF.Sqrt(cdx * cdx + cdy * cdy);

		if (dist > 0.01f)
		{
			Vector2 toward = new Vector2(cdx, cdy) / dist;
			float speed = config.SubGridSpeed * config.AlertSpeedMultiplier;
			creature.Velocity = toward * speed;
		}

		StepCreature(creature, dt);
	}

	private void AIGrazer(Creature creature, CreatureConfig config, float dt)
	{
		// Eat sub-grid cells under and around the creature
		SubGridData subGrid = _tendril.SubGrid;
		int eatRadius = (creature.Body?.Radius ?? 2) + 1;

		bool ateAnything = false;
		for (int edy = -eatRadius; edy <= eatRadius; edy++)
		{
			for (int edx = -eatRadius; edx <= eatRadius; edx++)
			{
				int sx = creature.SubX + edx;
				int sy = creature.SubY + edy;
				if (subGrid.HasCell(sx, sy))
				{
					var cell = subGrid.GetCell(sx, sy);
					// Don't eat the tendril head (Core/Fresh) — only Trail/Root
                    if (cell.State == SubCellState.Trail || cell.State == SubCellState.Root)
                    {
                        subGrid.ClearCell(sx, sy);
                        ateAnything = true;
                    }
                }
            }
        }

        // Also unclaim terrain tile if all sub-cells cleared
        if (ateAnything)
        {
            var (tileX, tileY) = SubGridData.SubToTerrain(creature.SubX, creature.SubY);
            if (!subGrid.HasCellsInTerrainTile(tileX, tileY))
            {
                _tendril.ClaimedTiles.Remove(TendrilController.PackCoords(tileX, tileY));
            }
        }

        // Move toward nearest tendril territory
        (int, int)? nearestTerritory = FindNearestTendrilCell(creature.SubX, creature.SubY, 20);
        if (nearestTerritory.HasValue)
        {
            float gdx = nearestTerritory.Value.Item1 - creature.SubX;
            float gdy = nearestTerritory.Value.Item2 - creature.SubY;
            float dist = MathF.Sqrt(gdx * gdx + gdy * gdy);
            if (dist > 0.01f)
            {
                creature.Velocity = new Vector2(gdx, gdy) / dist * config.SubGridSpeed;
            }
        }
        else
        {
            AIWander(creature, config, dt);
            return;
        }

        StepCreature(creature, dt);
    }

    /// <summary>
    /// Find the nearest tendril sub-cell within a search radius.
    /// Used by grazers to seek mycelium.
    /// </summary>
    private (int, int)? FindNearestTendrilCell(int subX, int subY, int radius)
    {
        SubGridData subGrid = _tendril.SubGrid;
        int bestDistSq = radius * radius;
        (int, int)? best = null;

        // Spiral outward — check increasing rings
        for (int r = 1; r <= radius; r++)
        {
            for (int d = -r; d <= r; d++)
            {
                // Check 4 edges of the ring
                CheckTendrilCell(subGrid, subX + d, subY - r, subX, subY, ref bestDistSq, ref best);
                CheckTendrilCell(subGrid, subX + d, subY + r, subX, subY, ref bestDistSq, ref best);
                CheckTendrilCell(subGrid, subX - r, subY + d, subX, subY, ref bestDistSq, ref best);
                CheckTendrilCell(subGrid, subX + r, subY + d, subX, subY, ref bestDistSq, ref best);
            }

            // If we found something in this ring, no need to search further
            if (best.HasValue) return best;
        }

        return best;
    }

    private static void CheckTendrilCell(SubGridData subGrid, int sx, int sy,
        int originX, int originY, ref int bestDistSq, ref (int, int)? best)
    {
        if (!subGrid.HasCell(sx, sy)) return;
        var cell = subGrid.GetCell(sx, sy);
        if (cell.State != SubCellState.Trail && cell.State != SubCellState.Root) return;

        int dx = sx - originX;
        int dy = sy - originY;
        int distSq = dx * dx + dy * dy;
        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            best = (sx, sy);
        }
    }

    // =========================================================================
    //  SUB-GRID MOVEMENT
    // =========================================================================

    /// <summary>
    /// Step a creature through sub-grid coordinates using its current velocity.
    /// Same accumulator pattern as the tendril — smooth 4px steps.
    /// Includes terrain collision and creature-creature avoidance.
    /// </summary>
    private void StepCreature(Creature creature, float dt)
    {
        creature.MoveAccumulator += creature.Velocity * dt;

        int iterations = 0;
        while ((MathF.Abs(creature.MoveAccumulator.X) >= 1f
            || MathF.Abs(creature.MoveAccumulator.Y) >= 1f) && iterations < 8)
        {
            iterations++;

            int stepX = MathF.Abs(creature.MoveAccumulator.X) >= 1f
                ? Math.Sign(creature.MoveAccumulator.X) : 0;
            int stepY = MathF.Abs(creature.MoveAccumulator.Y) >= 1f
                ? Math.Sign(creature.MoveAccumulator.Y) : 0;

            int newSubX = creature.SubX + stepX;
            int newSubY = creature.SubY + stepY;

            // Try full diagonal step first
            if (!CanCreatureOccupy(creature, newSubX, newSubY))
            {
                // Try sliding along X
                if (stepX != 0 && CanCreatureOccupy(creature, creature.SubX + stepX, creature.SubY))
                {
                    newSubX = creature.SubX + stepX;
                    newSubY = creature.SubY;
                    stepY = 0;
                }
                // Try sliding along Y
                else if (stepY != 0 && CanCreatureOccupy(creature, creature.SubX, creature.SubY + stepY))
                {
                    newSubX = creature.SubX;
                    newSubY = creature.SubY + stepY;
                    stepX = 0;
                }
                else
                {
                    // Fully blocked — zero out and stop
                    creature.MoveAccumulator = Vector2.Zero;
                    creature.Velocity = Vector2.Zero;
                    break;
                }
            }

            if (stepX != 0) creature.MoveAccumulator -= new Vector2(stepX, 0);
            if (stepY != 0) creature.MoveAccumulator -= new Vector2(0, stepY);

            creature.SubX = newSubX;
            creature.SubY = newSubY;
        }
    }

    /// <summary>
	/// Check if ALL cells of a creature's body can exist at a given sub-grid position.
	/// Checks terrain passability per-cell and basic creature-creature avoidance.
	/// </summary>
	private bool CanCreatureOccupy(Creature creature, int subX, int subY)
	{
		var body = creature.Body;
		if (body == null) return true;

		// Check terrain for each body cell
		foreach (var (dx, dy) in body.Cells)
		{
			int cellSubX = subX + dx;
			int cellSubY = subY + dy;

			var (terrainX, terrainY) = SubGridData.SubToTerrain(cellSubX, cellSubY);
			TileType tile = _chunkManager.GetTileAt(terrainX, terrainY);

			if (tile == TileType.Air) continue;
			if (TileProperties.Is(tile, TileFlags.Organic)) continue;
			if (TileProperties.Is(tile, TileFlags.PlayerOwned)) continue;

			// Blocked by solid non-organic terrain
			return false;
		}

		// Basic creature-creature avoidance (broad phase only — keeps them from stacking)
		int selfRadius = body.Radius;
		foreach (var other in _creatures)
		{
			if (other == creature || !other.IsAlive || !other.IsActive) continue;

			int odx = subX - other.SubX;
			int ody = subY - other.SubY;
			int minDist = selfRadius + (other.Body?.Radius ?? 2);

			if (odx * odx + ody * ody < minDist * minDist)
				return false;
		}

		return true;
	}

	// =========================================================================
	//  TENDRIL COLLISION
	// =========================================================================

	/// <summary>
	/// Check if a creature overlaps the tendril head and resolve the interaction.
	/// Uses pixel-perfect sub-cell collision against the tendril's core blob.
    /// </summary>
    private void CheckTendrilCollision(Creature creature)
    {
        if (!creature.IsAlive || !creature.IsActive) return;

        // Broad phase: is the creature even close to the tendril head?
        int headSubX = _tendril.SubHeadX;
        int headSubY = _tendril.SubHeadY;
        int bodyRadius = creature.Body?.Radius ?? 3;
        int checkDist = bodyRadius + HeadCollisionRadius;

        int hdx = creature.SubX - headSubX;
        int hdy = creature.SubY - headSubY;
        if (hdx * hdx + hdy * hdy > checkDist * checkDist) return;

        // Narrow phase: does any creature body cell overlap a tendril core/fresh cell?
        bool overlaps = CreatureOverlapsTendrilHead(creature, headSubX, headSubY);
        if (!overlaps) return;

        var config = CreatureRegistry.GetConfig(creature.Species);
        bool isThreat = config.DamageOnHit > 0;

        if (isThreat)
        {
            if (creature.AttackCooldown <= 0)
            {
                _tendril.DrainHunger(config.DamageOnHit);
                creature.AttackCooldown = config.HitCooldown;
                creature.DamageFlashTimer = DamageFlashDuration;

                creature.Health--;
                if (creature.Health <= 0)
                {
                    _tendril.AddHunger(config.HungerOnConsume);
                    creature.IsAlive = false;
                    GD.Print($"Killed {creature.Species}! +{config.HungerOnConsume} hunger");
                }
                else
                {
                    // Knockback: push creature away from tendril head
                    if (hdx * hdx + hdy * hdy > 0)
                    {
                        Vector2 knockback = new Vector2(hdx, hdy).Normalized() * 40f;
                        ApplyImpulse(creature, knockback);
                    }

                    GD.Print($"{creature.Species} hit you! -{config.DamageOnHit} hunger. HP: {creature.Health}");
                }
            }
        }
        else
        {
            // Prey — consumed on contact
            _tendril.AddHunger(config.HungerOnConsume);
            creature.IsAlive = false;
            GD.Print($"Consumed {creature.Species}! +{config.HungerOnConsume} hunger");
        }
    }

    /// <summary>
    /// Pixel-perfect check: does any creature body cell overlap the tendril head area?
    /// Checks against actual sub-grid cells (Core and Fresh states = the head blob).
    /// </summary>
    private bool CreatureOverlapsTendrilHead(Creature creature, int headSubX, int headSubY)
    {
        var body = creature.Body;
        if (body == null) return false;

        SubGridData subGrid = _tendril.SubGrid;

        foreach (var (dx, dy) in body.Cells)
        {
            int cellX = creature.SubX + dx;
            int cellY = creature.SubY + dy;

            // Check if this cell is within the tendril head radius
            int hdx = cellX - headSubX;
            int hdy = cellY - headSubY;
            if (hdx * hdx + hdy * hdy > HeadCollisionRadius * HeadCollisionRadius) continue;

			// Check if there's actually a tendril cell here (Core or Fresh = the active head)
			if (subGrid.HasCell(cellX, cellY))
			{
				var cell = subGrid.GetCell(cellX, cellY);
				if (cell.State == SubCellState.Core || cell.State == SubCellState.Fresh)
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Check if any of a creature's body cells overlap any tendril sub-cell.
    /// Used for territory overlap checks (grazers, passive effects).
    /// More expensive than head-only check but needed for some behaviors.
    /// </summary>
    public bool CreatureOverlapsTendrilTerritory(Creature creature)
    {
        if (creature.Body == null) return false;

        SubGridData subGrid = _tendril.SubGrid;

        foreach (var (dx, dy) in creature.Body.Cells)
        {
            if (subGrid.HasCell(creature.SubX + dx, creature.SubY + dy))
                return true;
        }

        return false;
    }

    // =========================================================================
    //  BACKWARD COMPAT (bridge methods during migration)
    // =========================================================================

    /// <summary>
    /// Find the nearest creature position in TERRAIN-TILE coords.
	/// Bridge for TendrilController auto-steer until it's updated to use sub-grid.
	/// </summary>
	public (int X, int Y)? GetNearestCreaturePosition(int centerTileX, int centerTileY, int radiusTiles)
	{
		int scale = WorldConfig.SubGridScale;
		var result = GetNearestCreatureSubPosition(
			centerTileX * scale + scale / 2,
			centerTileY * scale + scale / 2,
			radiusTiles * scale);

		if (result.HasValue)
		{
			var (subX, subY) = result.Value;
			return SubGridData.SubToTerrain(subX, subY);
		}
		return null;
	}

	/// <summary>
	/// Find a creature at terrain-tile coords (old API).
	/// Bridge for systems not yet updated.
	/// </summary>
	public Creature GetCreatureAt(int worldX, int worldY)
	{
		int scale = WorldConfig.SubGridScale;
		// Check all sub-cells within this terrain tile
		for (int dy = 0; dy < scale; dy++)
		{
			for (int dx = 0; dx < scale; dx++)
			{
				var creature = GetCreatureAtSubGrid(worldX * scale + dx, worldY * scale + dy);
				if (creature != null) return creature;
			}
		}
		return null;
	}
}
