namespace Mycorrhiza.World;

using Godot;
using System;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// A single living creature in the world.
/// Stored as pure data — no Godot nodes. Rendered by CreatureManager.
/// </summary>
public class Creature
{
    public CreatureSpecies Species;
    public int X, Y;                // World tile position
    public int Health;
    public CreatureBehavior Behavior;
    public float MoveTimer;         // Countdown to next move
    public float AttackCooldown;    // Countdown to next attack
    public bool IsActive;           // False = dormant (burrowed/ambush)
    public bool IsAlive;
    public int PatrolOriginX, PatrolOriginY; // For patrol behavior
    public int FleeTargetX, FleeTargetY;     // Where it's running to
    public bool IsFleeing;

    // Visual
    public int TileAtPosition;      // What tile was here before creature spawned (to restore on move)
}

/// <summary>
/// Manages all creatures in the world. Handles spawning, AI updates,
/// rendering (as tile overlays), and interaction with the tendril.
///
/// Creatures are stored as data, not Godot nodes. They occupy tiles in the
/// world and are rendered by temporarily placing creature tiles.
///
/// SETUP:
///   - Add as child of World
///   - Assign ChunkManagerPath and TendrilControllerPath
/// </summary>
public partial class CreatureManager : Node2D
{
    [Export] public NodePath ChunkManagerPath { get; set; }
    [Export] public NodePath TendrilControllerPath { get; set; }

    // --- Spawn Config ---
    [Export] public float SpawnCheckInterval = 2.0f;  // How often to check for new spawns
    [Export] public int MaxCreatures = 60;             // Global creature cap
    [Export] public int SpawnRadius = 40;              // Spawn within this radius of tendril head
    [Export] public int DespawnRadius = 80;            // Remove creatures beyond this distance
    [Export] public int MinSpawnDistance = 8;           // Don't spawn right on top of the player

    private ChunkManager _chunkManager;
    private TendrilController _tendril;
    private readonly List<Creature> _creatures = new();
    private float _spawnTimer;
    private readonly Random _rng = new();

    // Creature tile IDs — add these to your tileset
    // They use TileType values 144+
    private const int CreatureTileBase = 144;

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

        // Spawn new creatures periodically
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

            UpdateCreature(creature, dt);
            CheckTendrilInteraction(creature);
        }
    }

    // =========================================================================
    //  SPAWNING
    // =========================================================================

    private void TrySpawnCreatures()
    {
        if (_creatures.Count >= MaxCreatures) return;

        int headX = _tendril.HeadX;
        int headY = _tendril.HeadY;

        // Determine biome at tendril position to know what can spawn
        BiomeType currentBiome = _chunkManager.GetBiomeAt(headX, headY);
        var validSpecies = CreatureRegistry.GetForBiome(currentBiome);
        if (validSpecies.Count == 0) return;

        // Also check nearby biomes (creatures from adjacent biomes can wander in)
        BiomeType nearbyBiome = _chunkManager.GetBiomeAt(headX + 30, headY);
        if (nearbyBiome != currentBiome)
        {
            var nearbySpecies = CreatureRegistry.GetForBiome(nearbyBiome);
            foreach (var s in nearbySpecies)
            {
                if (_rng.Next(3) == 0) // 33% chance to include nearby biome creatures
                    validSpecies.Add(s);
            }
        }

        // Try to spawn 1-3 creatures per check
        int toSpawn = 1 + _rng.Next(3);

        for (int i = 0; i < toSpawn; i++)
        {
            if (_creatures.Count >= MaxCreatures) break;

            // Pick a random species
            var config = validSpecies[_rng.Next(validSpecies.Count)];

            // Find a valid spawn position
            if (TryFindSpawnPosition(headX, headY, out int sx, out int sy))
            {
                SpawnCreature(config, sx, sy);
            }
        }
    }

    private bool TryFindSpawnPosition(int centerX, int centerY, out int x, out int y)
    {
        // Try several random positions
        for (int attempt = 0; attempt < 15; attempt++)
        {
            x = centerX + _rng.Next(-SpawnRadius, SpawnRadius + 1);
            y = centerY + _rng.Next(-SpawnRadius, SpawnRadius + 1);

            // Enforce minimum distance from tendril
            int dx = x - _tendril.HeadX;
            int dy = y - _tendril.HeadY;
            if (dx * dx + dy * dy < MinSpawnDistance * MinSpawnDistance) continue;

            // Must be in a cave (air tile) or on organic ground
            TileType tile = _chunkManager.GetTileAt(x, y);
            if (tile == TileType.Air) return true;

            // Some creatures burrow in soil
            if (TileProperties.Is(tile, TileFlags.Organic) && !TileProperties.Is(tile, TileFlags.PlayerOwned))
                return true;
        }

        x = 0; y = 0;
        return false;
    }

    private void SpawnCreature(CreatureConfig config, int x, int y)
    {
        var creature = new Creature
        {
            Species = config.Species,
            X = x,
            Y = y,
            Health = config.Health,
            Behavior = config.Behavior,
            MoveTimer = config.MoveSpeed * (0.5f + _rng.NextSingle()), // Stagger initial timers
            AttackCooldown = 0,
            IsActive = config.Behavior != CreatureBehavior.Burrowed
                    && config.Behavior != CreatureBehavior.Ambush,
            IsAlive = true,
            PatrolOriginX = x,
            PatrolOriginY = y,
            IsFleeing = false,
        };

        // Store what tile was here so we can restore it when creature moves
        creature.TileAtPosition = (int)_chunkManager.GetTileAt(x, y);

        _creatures.Add(creature);

        // Place creature tile (only if active/visible)
        if (creature.IsActive)
            PlaceCreatureTile(creature);
    }

    private void DespawnDistantCreatures()
    {
        int headX = _tendril.HeadX;
        int headY = _tendril.HeadY;

        for (int i = _creatures.Count - 1; i >= 0; i--)
        {
            var c = _creatures[i];
            int dx = c.X - headX;
            int dy = c.Y - headY;
            if (dx * dx + dy * dy > DespawnRadius * DespawnRadius)
            {
                // Restore tile and remove
                RestoreCreatureTile(c);
                _creatures.RemoveAt(i);
            }
        }
    }

    // =========================================================================
    //  AI UPDATE
    // =========================================================================

    private void UpdateCreature(Creature creature, float dt)
    {
        var config = CreatureRegistry.GetConfig(creature.Species);

        // Attack cooldown
        if (creature.AttackCooldown > 0)
            creature.AttackCooldown -= dt;

        // Movement timer
        creature.MoveTimer -= dt;
        if (creature.MoveTimer > 0) return;
        creature.MoveTimer = config.MoveSpeed;

        int headX = _tendril.HeadX;
        int headY = _tendril.HeadY;
        int dx = headX - creature.X;
        int dy = headY - creature.Y;
        int distSq = dx * dx + dy * dy;
        int dist = (int)MathF.Sqrt(distSq);

        switch (creature.Behavior)
        {
            case CreatureBehavior.Wander:
                MoveRandom(creature);
                break;

            case CreatureBehavior.Skittish:
                if (dist <= config.DetectRange)
                {
                    creature.IsFleeing = true;
                    MoveAwayFrom(creature, headX, headY);
                }
                else
                {
                    creature.IsFleeing = false;
                    MoveRandom(creature);
                }
                break;

            case CreatureBehavior.Burrowed:
                if (!creature.IsActive)
                {
                    // Dormant — check if tendril is close enough to wake up
                    if (dist <= config.DetectRange)
                    {
                        creature.IsActive = true;
                        creature.IsFleeing = true;
                        PlaceCreatureTile(creature);
                    }
                }
                else
                {
                    // Awake and fleeing
                    MoveAwayFrom(creature, headX, headY);
                    if (dist > config.FleeRange)
                    {
                        // Escaped — re-burrow
                        creature.IsActive = false;
                        creature.IsFleeing = false;
                        RestoreCreatureTile(creature);
                    }
                }
                break;

            case CreatureBehavior.Patrol:
                if (dist <= config.DetectRange)
                {
                    // Chase the tendril
                    MoveToward(creature, headX, headY);
                }
                else
                {
                    // Patrol around origin point
                    int patrolDx = creature.X - creature.PatrolOriginX;
                    int patrolDy = creature.Y - creature.PatrolOriginY;
                    if (patrolDx * patrolDx + patrolDy * patrolDy > 100)
                    {
                        // Too far from origin, head back
                        MoveToward(creature, creature.PatrolOriginX, creature.PatrolOriginY);
                    }
                    else
                    {
                        MoveRandom(creature);
                    }
                }
                break;

            case CreatureBehavior.Ambush:
                if (!creature.IsActive)
                {
                    if (dist <= config.DetectRange)
                    {
                        creature.IsActive = true;
                        PlaceCreatureTile(creature);
                    }
                }
                else
                {
                    // Active — chase the tendril
                    MoveToward(creature, headX, headY);

                    // Give up if tendril gets too far
                    if (dist > config.DetectRange * 3)
                    {
                        creature.IsActive = false;
                        RestoreCreatureTile(creature);
                    }
                }
                break;

            case CreatureBehavior.Grazer:
                // Seek out mycelium tiles and eat them
                if (_tendril.IsOnTerritory(creature.X, creature.Y))
                {
                    // Eat the mycelium tile we're standing on
                    _chunkManager.SetTileAt(creature.X, creature.Y, TileType.Air);
                    _tendril.ClaimedTiles.Remove(TendrilController.PackCoords(creature.X, creature.Y));
                    creature.TileAtPosition = (int)TileType.Air;
                }

                // Move toward nearest mycelium
                if (dist <= config.DetectRange)
                {
                    // If close to tendril head, move toward territory
                    MoveTowardMycelium(creature);
                }
                else
                {
                    MoveRandom(creature);
                }
                break;
        }
    }

    // =========================================================================
    //  MOVEMENT HELPERS
    // =========================================================================

    private void MoveRandom(Creature creature)
    {
        int dir = _rng.Next(4);
        int nx = creature.X + (dir == 0 ? -1 : dir == 1 ? 1 : 0);
        int ny = creature.Y + (dir == 2 ? -1 : dir == 3 ? 1 : 0);
        TryMoveCreature(creature, nx, ny);
    }

    private void MoveToward(Creature creature, int targetX, int targetY)
    {
        int dx = targetX - creature.X;
        int dy = targetY - creature.Y;

        // Move in the axis with the greater distance (with some randomness)
        int nx = creature.X;
        int ny = creature.Y;

        if (Math.Abs(dx) >= Math.Abs(dy) || (_rng.Next(3) == 0 && dy != 0))
        {
            if (Math.Abs(dy) > Math.Abs(dx) || _rng.Next(3) == 0)
                ny += Math.Sign(dy);
            else
                nx += Math.Sign(dx);
        }
        else
        {
            nx += Math.Sign(dx);
        }

        if (!TryMoveCreature(creature, nx, ny))
        {
            // Blocked — try the other axis
            nx = creature.X;
            ny = creature.Y;
            if (dx != 0) nx += Math.Sign(dx);
            else if (dy != 0) ny += Math.Sign(dy);
            TryMoveCreature(creature, nx, ny);
        }
    }

    private void MoveAwayFrom(Creature creature, int threatX, int threatY)
    {
        int dx = creature.X - threatX;
        int dy = creature.Y - threatY;

        int nx = creature.X + Math.Sign(dx);
        int ny = creature.Y + Math.Sign(dy);

        // Try primary direction first
        if (!TryMoveCreature(creature, nx, creature.Y))
        {
            if (!TryMoveCreature(creature, creature.X, ny))
            {
                // Cornered — try random
                MoveRandom(creature);
            }
        }
    }

    private void MoveTowardMycelium(Creature creature)
    {
        // Simple: check 4 neighbors, move toward any that has mycelium
        int[] offsets = { -1, 0, 1, 0, 0, -1, 0, 1 };
        for (int i = 0; i < 8; i += 2)
        {
            int nx = creature.X + offsets[i];
            int ny = creature.Y + offsets[i + 1];
            TileType t = _chunkManager.GetTileAt(nx, ny);
            if (TileProperties.IsMycelium(t))
            {
                TryMoveCreature(creature, nx, ny);
                return;
            }
        }
        // No mycelium nearby — wander
        MoveRandom(creature);
    }

    private bool TryMoveCreature(Creature creature, int newX, int newY)
    {
        TileType target = _chunkManager.GetTileAt(newX, newY);

        // Creatures can move through air, organic tiles, and mycelium
        bool canMove = target == TileType.Air
            || TileProperties.Is(target, TileFlags.Organic)
            || TileProperties.Is(target, TileFlags.PlayerOwned);

        // Can't walk into unbreakable solids or liquids
        if (TileProperties.Is(target, TileFlags.Solid) && !TileProperties.Is(target, TileFlags.Organic)
            && !TileProperties.Is(target, TileFlags.PlayerOwned))
            canMove = false;
        if (TileProperties.Is(target, TileFlags.Liquid))
            canMove = false;

        if (!canMove) return false;

        // Check for other creatures at target
        foreach (var other in _creatures)
        {
            if (other != creature && other.IsAlive && other.X == newX && other.Y == newY)
                return false;
        }

        // Restore old tile
        RestoreCreatureTile(creature);

        // Move
        creature.X = newX;
        creature.Y = newY;
        creature.TileAtPosition = (int)target;

        // Place creature at new position
        if (creature.IsActive)
            PlaceCreatureTile(creature);

        return true;
    }

    // =========================================================================
    //  TENDRIL INTERACTION
    // =========================================================================

    private void CheckTendrilInteraction(Creature creature)
    {
        if (!creature.IsAlive || !creature.IsActive) return;

        // Check if tendril head overlaps this creature
        if (!_tendril.OverlapsHead(creature.X, creature.Y)) return;

        var config = CreatureRegistry.GetConfig(creature.Species);

        bool isThreat = config.DamageOnHit > 0;

        if (isThreat)
        {
            // Threat creature — damages the tendril
            if (creature.AttackCooldown <= 0)
            {
                _tendril.DrainHunger(config.DamageOnHit);
                creature.AttackCooldown = config.HitCooldown;

                // Tendril damages the creature back (one hit per contact)
                creature.Health--;
                if (creature.Health <= 0)
                {
                    // Killed the threat — bonus hunger!
                    _tendril.AddHunger(config.HungerOnConsume);
                    KillCreature(creature);
                    GD.Print($"Killed {creature.Species}! +{config.HungerOnConsume} hunger");
                }
                else
                {
                    GD.Print($"{creature.Species} hit you! -{config.DamageOnHit} hunger. HP: {creature.Health}");
                }
            }
        }
        else
        {
            // Prey creature — consumed immediately
            _tendril.AddHunger(config.HungerOnConsume);
            KillCreature(creature);
            GD.Print($"Consumed {creature.Species}! +{config.HungerOnConsume} hunger");
        }
    }

    private void KillCreature(Creature creature)
    {
        creature.IsAlive = false;
        RestoreCreatureTile(creature);
    }

    // =========================================================================
    //  TILE MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Place a creature's visual tile at its position.
    /// Creatures are rendered as special tile types in the tileset.
    /// </summary>
    private void PlaceCreatureTile(Creature creature)
    {
        TileType tileType = GetCreatureTileType(creature.Species);
        _chunkManager.SetTileAt(creature.X, creature.Y, tileType);
    }

    private static TileType GetCreatureTileType(CreatureSpecies species)
    {
        return species switch
        {
            CreatureSpecies.Earthworm => TileType.CreatureEarthworm,
            CreatureSpecies.Beetle => TileType.CreatureBeetle,
            CreatureSpecies.Grub => TileType.CreatureGrub,
            CreatureSpecies.MoleRat => TileType.CreatureMoleRat,
            CreatureSpecies.RootBorer => TileType.CreatureRootBorer,
            CreatureSpecies.FungusGnat => TileType.CreatureFungusGnat,
            CreatureSpecies.CaveFish => TileType.CreatureCaveFish,
            CreatureSpecies.BlindSalamander => TileType.CreatureBlindSalamander,
            CreatureSpecies.CaveSpider => TileType.CreatureCaveSpider,
            CreatureSpecies.BoneCrab => TileType.CreatureBoneCrab,
            CreatureSpecies.WormColony => TileType.CreatureWormColony,
            CreatureSpecies.MarrowLeech => TileType.CreatureMarrowLeech,
            CreatureSpecies.MagmaBeetle => TileType.CreatureMagmaBeetle,
            CreatureSpecies.TubeWorm => TileType.CreatureTubeWorm,
            CreatureSpecies.MemorySlug => TileType.CreatureMemorySlug,
            CreatureSpecies.FungalPredator => TileType.CreatureFungalPredator,
            _ => TileType.CreatureEarthworm,
        };
    }

    /// <summary>
    /// Restore the tile that was at a creature's position before it was there.
    /// </summary>
    private void RestoreCreatureTile(Creature creature)
    {
        _chunkManager.SetTileAt(creature.X, creature.Y, (TileType)creature.TileAtPosition);
    }
}
