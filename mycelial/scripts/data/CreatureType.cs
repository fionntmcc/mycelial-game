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

    /// <summary>Patrols an area. Attacks the tendril on contact. Drains vitality.</summary>
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
    Earthworm = 0,        // Wander. Slow. Common. Small vigor gain.
    Beetle = 1,           // Skittish. Medium speed. Decent vigor gain.
    Grub = 2,             // Burrowed. Stationary until disturbed. Good gain.

    // --- Root Maze (Prey + Threats) ---
    MoleRat = 10,         // Skittish. Fast. High vigor gain.
    RootBorer = 11,       // Wander. Eats through roots — can open paths for you.
    FungusGnat = 12,      // Grazer. Eats your mycelium. Pest — kill on sight.

    // --- Wet Dark (Prey + Threats) ---
    CaveFish = 20,        // Wander (in water-adjacent areas). Medium gain.
    BlindSalamander = 21, // Skittish. High gain. Rare.
    CaveSpider = 22,      // Ambush. Hides in walls, leaps when close. Drains vitality.

    // --- Bone Strata (Mostly Threats) ---
    BoneCrab = 30,        // Patrol. Armored. Drains lots of vitality on hit.
    WormColony = 31,      // Ambush. Swarm from walls. Very dangerous. High reward if killed.
    MarrowLeech = 32,     // Wander toward mycelium. Drains vitality passively when on your land.

    // --- Thermovents (Dangerous Prey) ---
    MagmaBeetle = 40,     // Patrol. Leaves hazard tiles behind it. Huge vigor gain.
    TubeWorm = 41,        // Burrowed near vents. Sprays acid when disturbed. Huge gain.

    // --- Mycelial Graveyard (Unique) ---
    MemorySlug = 50,      // Wander. Slow. Consuming it gives lore + massive vigor.
    FungalPredator = 51,  // Ambush. Evolved to kill networks like you. Very dangerous.
}

/// <summary>
/// Static configuration for each creature species.
/// </summary>
public readonly struct CreatureConfig
{
    public readonly CreatureSpecies Species;
    public readonly CreatureBehavior Behavior;
    public readonly float MoveSpeed;       // Seconds between AI direction changes (tick rate)
    public readonly int DetectRange;       // Tiles — how far it can "see" the tendril
    public readonly int FleeRange;         // Tiles — how far it runs before stopping
    public readonly float VigorOnConsume; // Vigor gained by tendril when consumed
    public readonly float DamageOnHit;     // Vitality drained from tendril on contact (threats only)
    public readonly float HitCooldown;     // Seconds between attacks (threats only)
    public readonly int Health;            // Hits to kill (for tough creatures)
    public readonly BiomeType[] Biomes;    // Which biomes this creature spawns in

    /// <summary>
    /// Movement speed in sub-cells per second.
    /// For reference, the tendril moves ~30–50 sub-cells/sec at full speed.
    /// </summary>
    public readonly float SubGridSpeed;

    /// <summary>
    /// Speed multiplier when fleeing (skittish) or charging (ambush).
    /// Applied on top of SubGridSpeed during those behaviors.
    /// </summary>
    public readonly float AlertSpeedMultiplier;

    public CreatureConfig(
        CreatureSpecies species, CreatureBehavior behavior,
        float moveSpeed, int detectRange, int fleeRange,
        float vigorOnConsume, float damageOnHit, float hitCooldown,
        int health,
        float subGridSpeed, float alertSpeedMultiplier,
        params BiomeType[] biomes)
    {
        Species = species;
        Behavior = behavior;
        MoveSpeed = moveSpeed;
        DetectRange = detectRange;
        FleeRange = fleeRange;
        VigorOnConsume = vigorOnConsume;
        DamageOnHit = damageOnHit;
        HitCooldown = hitCooldown;
        Health = health;
        SubGridSpeed = subGridSpeed;
        AlertSpeedMultiplier = alertSpeedMultiplier;
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
        //                                                                               subSpeed  alertMul

        // Earthworm — slow, dumb, everywhere. Your bread and butter.
        new(CreatureSpecies.Earthworm, CreatureBehavior.Wander,
            moveSpeed: 1.2f, detectRange: 0, fleeRange: 0,
            vigorOnConsume: 8f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 8f, alertSpeedMultiplier: 1f,
            BiomeType.Topsoil, BiomeType.Neutral, BiomeType.RootMaze),

        // Beetle — skittish, medium speed. Worth chasing.
        new(CreatureSpecies.Beetle, CreatureBehavior.Skittish,
            moveSpeed: 0.8f, detectRange: 6, fleeRange: 15,
            vigorOnConsume: 12f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 22f, alertSpeedMultiplier: 1.4f,
            BiomeType.Topsoil, BiomeType.Neutral),

        // Grub — burrowed. Stationary until disturbed, then flees.
        new(CreatureSpecies.Grub, CreatureBehavior.Burrowed,
            moveSpeed: 0.6f, detectRange: 3, fleeRange: 10,
            vigorOnConsume: 15f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 5f, alertSpeedMultiplier: 2.0f,
            BiomeType.Topsoil, BiomeType.Neutral, BiomeType.RootMaze),

        // ====== ROOT MAZE ======

        // Mole Rat — fast, high reward.
        new(CreatureSpecies.MoleRat, CreatureBehavior.Skittish,
            moveSpeed: 0.5f, detectRange: 8, fleeRange: 20,
            vigorOnConsume: 20f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 35f, alertSpeedMultiplier: 1.3f,
            BiomeType.RootMaze),

        // Root Borer — wanders, opens paths through roots.
        new(CreatureSpecies.RootBorer, CreatureBehavior.Wander,
            moveSpeed: 1.0f, detectRange: 0, fleeRange: 0,
            vigorOnConsume: 10f, damageOnHit: 0f, hitCooldown: 0f, health: 2,
            subGridSpeed: 12f, alertSpeedMultiplier: 1f,
            BiomeType.RootMaze),

        // Fungus Gnat — grazer pest. Eats your mycelium.
        new(CreatureSpecies.FungusGnat, CreatureBehavior.Grazer,
            moveSpeed: 0.4f, detectRange: 10, fleeRange: 0,
            vigorOnConsume: 6f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 18f, alertSpeedMultiplier: 1f,
            BiomeType.Topsoil, BiomeType.RootMaze),

        // ====== WET DARK ======

        // Cave Fish — wanders in water-adjacent areas.
        new(CreatureSpecies.CaveFish, CreatureBehavior.Wander,
            moveSpeed: 0.7f, detectRange: 0, fleeRange: 0,
            vigorOnConsume: 14f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 16f, alertSpeedMultiplier: 1f,
            BiomeType.WetDark),

        // Blind Salamander — rare, high reward, skittish.
        new(CreatureSpecies.BlindSalamander, CreatureBehavior.Skittish,
            moveSpeed: 0.6f, detectRange: 5, fleeRange: 18,
            vigorOnConsume: 25f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 28f, alertSpeedMultiplier: 1.5f,
            BiomeType.WetDark),

        // ====== THREATS ======

        // Cave Spider — ambush predator. Hides, leaps, hurts.
        new(CreatureSpecies.CaveSpider, CreatureBehavior.Ambush,
            moveSpeed: 0.3f, detectRange: 6, fleeRange: 0,
            vigorOnConsume: 18f, damageOnHit: 8f, hitCooldown: 1.5f, health: 2,
            subGridSpeed: 45f, alertSpeedMultiplier: 1.6f,
            BiomeType.WetDark, BiomeType.RootMaze),

        // Bone Crab — patrol. Armored. Drains vitality hard.
        new(CreatureSpecies.BoneCrab, CreatureBehavior.Patrol,
            moveSpeed: 0.5f, detectRange: 7, fleeRange: 0,
            vigorOnConsume: 30f, damageOnHit: 12f, hitCooldown: 2.0f, health: 3,
            subGridSpeed: 20f, alertSpeedMultiplier: 1.3f,
            BiomeType.BoneStrata),

        // Worm Colony — ambush swarm. Very dangerous. High reward.
        new(CreatureSpecies.WormColony, CreatureBehavior.Ambush,
            moveSpeed: 0.3f, detectRange: 5, fleeRange: 0,
            vigorOnConsume: 35f, damageOnHit: 15f, hitCooldown: 1.0f, health: 4,
            subGridSpeed: 30f, alertSpeedMultiplier: 1.4f,
            BiomeType.BoneStrata),

        // Marrow Leech — wanders toward mycelium. Passive vitality drain.
        new(CreatureSpecies.MarrowLeech, CreatureBehavior.Grazer,
            moveSpeed: 0.8f, detectRange: 12, fleeRange: 0,
            vigorOnConsume: 20f, damageOnHit: 3f, hitCooldown: 3.0f, health: 2,
            subGridSpeed: 14f, alertSpeedMultiplier: 1f,
            BiomeType.BoneStrata),

        // Magma Beetle — patrol. Leaves hazards. Huge reward but very tough.
        new(CreatureSpecies.MagmaBeetle, CreatureBehavior.Patrol,
            moveSpeed: 0.4f, detectRange: 8, fleeRange: 0,
            vigorOnConsume: 40f, damageOnHit: 10f, hitCooldown: 2.0f, health: 3,
            subGridSpeed: 25f, alertSpeedMultiplier: 1.2f,
            BiomeType.Thermovents),

        // Tube Worm — burrowed near vents. Sprays acid. Huge reward.
        new(CreatureSpecies.TubeWorm, CreatureBehavior.Burrowed,
            moveSpeed: 0.5f, detectRange: 4, fleeRange: 8,
            vigorOnConsume: 35f, damageOnHit: 0f, hitCooldown: 0f, health: 2,
            subGridSpeed: 10f, alertSpeedMultiplier: 1.5f,
            BiomeType.Thermovents),

        // Memory Slug — slow, massive vigor + lore. Harmless.
        new(CreatureSpecies.MemorySlug, CreatureBehavior.Wander,
            moveSpeed: 2.0f, detectRange: 0, fleeRange: 0,
            vigorOnConsume: 50f, damageOnHit: 0f, hitCooldown: 0f, health: 1,
            subGridSpeed: 4f, alertSpeedMultiplier: 1f,
            BiomeType.MycelialGraveyard),

        // Fungal Predator — evolved to kill fungi. Nightmare creature.
        new(CreatureSpecies.FungalPredator, CreatureBehavior.Ambush,
            moveSpeed: 0.1f, detectRange: 10, fleeRange: 0,
            vigorOnConsume: 60f, damageOnHit: 20f, hitCooldown: 1.5f, health: 5,
            subGridSpeed: 55f, alertSpeedMultiplier: 1.3f,
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
