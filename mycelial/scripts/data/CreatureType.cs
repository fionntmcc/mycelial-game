namespace Mycorrhiza.Data;

/// <summary>
/// Creature behavior types — determines how the creature moves and reacts.
/// </summary>
public enum CreatureBehavior : byte
{
    /// <summary>Wanders randomly. Doesn't react to the tendril. Easy prey.</summary>
    Wander,

    /// <summary>Wanders until tendril gets close, then flees. Worth chasing.</summary>
    Skittish,

    /// <summary>Stationary until disturbed, then flees fast. Hard to catch.</summary>
    Burrowed,

    /// <summary>Patrols an area. Attacks the tendril on contact. Drains hunger.</summary>
    Patrol,

    /// <summary>Dormant until tendril enters range, then chases aggressively.</summary>
    Ambush,

    /// <summary>Actively seeks out mycelium territory and eats it. Destroys your land.</summary>
    Grazer,
}

/// <summary>
/// Species identifier for each creature type.
/// </summary>
public enum CreatureSpecies : byte
{
    // --- Topsoil / Neutral (Prey) ---
    Earthworm = 0,        // Wander. Slow. Common. Small hunger gain.
    Beetle = 1,           // Skittish. Medium speed. Decent hunger gain.
    Grub = 2,             // Burrowed. Stationary until disturbed. Good gain.

    // --- Root Maze (Prey + Threats) ---
    MoleRat = 10,         // Skittish. Fast. High hunger gain.
    RootBorer = 11,       // Wander. Eats through roots — can open paths for you.
    FungusGnat = 12,      // Grazer. Eats your mycelium. Pest — kill on sight.

    // --- Wet Dark (Prey + Threats) ---
    CaveFish = 20,        // Wander (in water-adjacent areas). Medium gain.
    BlindSalamander = 21, // Skittish. High gain. Rare.
    CaveSpider = 22,      // Ambush. Hides in walls, leaps when close. Drains hunger.

    // --- Bone Strata (Mostly Threats) ---
    BoneCrab = 30,        // Patrol. Armored. Drains lots of hunger on hit.
    WormColony = 31,      // Ambush. Swarm from walls. Very dangerous. High reward if killed.
    MarrowLeech = 32,     // Wander toward mycelium. Drains hunger passively when on your land.

    // --- Thermovents (Dangerous Prey) ---
    MagmaBeetle = 40,     // Patrol. Leaves hazard tiles behind it. Huge hunger gain.
    TubeWorm = 41,        // Burrowed near vents. Sprays acid when disturbed. Huge gain.

    // --- Mycelial Graveyard (Unique) ---
    MemorySlug = 50,      // Wander. Slow. Consuming it gives lore + massive hunger.
    FungalPredator = 51,  // Ambush. Evolved to kill networks like you. Very dangerous.
}

/// <summary>
/// Static configuration for each creature species.
/// </summary>
public readonly struct CreatureConfig
{
    public readonly CreatureSpecies Species;
    public readonly CreatureBehavior Behavior;
    public readonly float MoveSpeed;       // Seconds between moves (lower = faster)
    public readonly int DetectRange;       // Tiles — how far it can "see" the tendril
    public readonly int FleeRange;         // Tiles — how far it runs before stopping
    public readonly float HungerOnConsume; // Hunger gained by tendril when consumed
    public readonly float DamageOnHit;     // Hunger drained from tendril on contact (threats only)
    public readonly float HitCooldown;     // Seconds between attacks (threats only)
    public readonly int Health;            // Hits to kill (for tough creatures)
    public readonly BiomeType[] Biomes;    // Which biomes this creature spawns in

    public CreatureConfig(
        CreatureSpecies species, CreatureBehavior behavior,
        float moveSpeed, int detectRange, int fleeRange,
        float hungerOnConsume, float damageOnHit, float hitCooldown,
        int health, params BiomeType[] biomes)
    {
        Species = species;
        Behavior = behavior;
        MoveSpeed = moveSpeed;
        DetectRange = detectRange;
        FleeRange = fleeRange;
        HungerOnConsume = hungerOnConsume;
        DamageOnHit = damageOnHit;
        HitCooldown = hitCooldown;
        Health = health;
        Biomes = biomes;
    }
}

/// <summary>
/// Registry of all creature configurations.
/// </summary>
public static class CreatureRegistry
{
    public static readonly CreatureConfig[] All = new CreatureConfig[]
    {
        // ====== PREY ======

        // Earthworm — slow, dumb, everywhere. Your bread and butter.
        new(CreatureSpecies.Earthworm, CreatureBehavior.Wander,
            moveSpeed: 0.8f, detectRange: 0, fleeRange: 0,
            hungerOnConsume: 8f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.Neutral, BiomeType.Topsoil, BiomeType.RootMaze),

        // Beetle — skittish, worth chasing
        new(CreatureSpecies.Beetle, CreatureBehavior.Skittish,
            moveSpeed: 0.3f, detectRange: 6, fleeRange: 12,
            hungerOnConsume: 12f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.Neutral, BiomeType.Topsoil, BiomeType.RootMaze),

        // Grub — sits still in soil, pops out when you get close, runs
        new(CreatureSpecies.Grub, CreatureBehavior.Burrowed,
            moveSpeed: 0.5f, detectRange: 3, fleeRange: 8,
            hungerOnConsume: 15f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.Topsoil, BiomeType.RootMaze),

        // Mole Rat — fast, high value. The prize catch of early game.
        new(CreatureSpecies.MoleRat, CreatureBehavior.Skittish,
            moveSpeed: 0.15f, detectRange: 8, fleeRange: 20,
            hungerOnConsume: 25f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.RootMaze, BiomeType.Topsoil),

        // Root Borer — wanders, eats roots (opens paths)
        new(CreatureSpecies.RootBorer, CreatureBehavior.Wander,
            moveSpeed: 0.6f, detectRange: 0, fleeRange: 0,
            hungerOnConsume: 10f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.RootMaze),

        // Cave Fish — wanders near water. Decent gain.
        new(CreatureSpecies.CaveFish, CreatureBehavior.Wander,
            moveSpeed: 0.4f, detectRange: 0, fleeRange: 0,
            hungerOnConsume: 14f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.WetDark),

        // Blind Salamander — rare, skittish, high value
        new(CreatureSpecies.BlindSalamander, CreatureBehavior.Skittish,
            moveSpeed: 0.25f, detectRange: 5, fleeRange: 15,
            hungerOnConsume: 30f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.WetDark),

        // Memory Slug — slow, wandering, lore creature. Massive hunger.
        new(CreatureSpecies.MemorySlug, CreatureBehavior.Wander,
            moveSpeed: 1.2f, detectRange: 0, fleeRange: 0,
            hungerOnConsume: 50f, damageOnHit: 0, hitCooldown: 0, health: 1,
            BiomeType.MycelialGraveyard),

        // ====== THREATS ======

        // Fungus Gnat — grazer. Eats your mycelium. Annoying pest.
        new(CreatureSpecies.FungusGnat, CreatureBehavior.Grazer,
            moveSpeed: 0.4f, detectRange: 15, fleeRange: 0,
            hungerOnConsume: 6f, damageOnHit: 2f, hitCooldown: 1.5f, health: 1,
            BiomeType.RootMaze, BiomeType.Topsoil),

        // Cave Spider — ambush predator. Hides until you're close, then strikes.
        new(CreatureSpecies.CaveSpider, CreatureBehavior.Ambush,
            moveSpeed: 0.12f, detectRange: 5, fleeRange: 0,
            hungerOnConsume: 18f, damageOnHit: 8f, hitCooldown: 2.0f, health: 2,
            BiomeType.WetDark, BiomeType.RootMaze),

        // Bone Crab — patrols cave edges. Armored. Hurts a lot.
        new(CreatureSpecies.BoneCrab, CreatureBehavior.Patrol,
            moveSpeed: 0.35f, detectRange: 7, fleeRange: 0,
            hungerOnConsume: 20f, damageOnHit: 12f, hitCooldown: 2.5f, health: 3,
            BiomeType.BoneStrata),

        // Worm Colony — ambush swarm. Very dangerous. High reward.
        new(CreatureSpecies.WormColony, CreatureBehavior.Ambush,
            moveSpeed: 0.2f, detectRange: 4, fleeRange: 0,
            hungerOnConsume: 35f, damageOnHit: 15f, hitCooldown: 1.0f, health: 4,
            BiomeType.BoneStrata),

        // Marrow Leech — seeks mycelium and drains it
        new(CreatureSpecies.MarrowLeech, CreatureBehavior.Grazer,
            moveSpeed: 0.5f, detectRange: 20, fleeRange: 0,
            hungerOnConsume: 12f, damageOnHit: 3f, hitCooldown: 1.0f, health: 2,
            BiomeType.BoneStrata),

        // Magma Beetle — patrols thermovents. Very tough.
        new(CreatureSpecies.MagmaBeetle, CreatureBehavior.Patrol,
            moveSpeed: 0.4f, detectRange: 8, fleeRange: 0,
            hungerOnConsume: 40f, damageOnHit: 10f, hitCooldown: 2.0f, health: 3,
            BiomeType.Thermovents),

        // Fungal Predator — evolved to kill fungi. Nightmare creature.
        new(CreatureSpecies.FungalPredator, CreatureBehavior.Ambush,
            moveSpeed: 0.1f, detectRange: 10, fleeRange: 0,
            hungerOnConsume: 60f, damageOnHit: 20f, hitCooldown: 1.5f, health: 5,
            BiomeType.MycelialGraveyard),
    };

    /// <summary>Get the config for a species.</summary>
    public static CreatureConfig GetConfig(CreatureSpecies species)
    {
        foreach (var c in All)
        {
            if (c.Species == species) return c;
        }
        return All[0]; // Fallback to earthworm
    }

    /// <summary>Get all creature configs that can spawn in a given biome.</summary>
    public static System.Collections.Generic.List<CreatureConfig> GetForBiome(BiomeType biome)
    {
        var result = new System.Collections.Generic.List<CreatureConfig>();
        foreach (var c in All)
        {
            foreach (var b in c.Biomes)
            {
                if (b == biome) { result.Add(c); break; }
            }
        }
        return result;
    }
}
