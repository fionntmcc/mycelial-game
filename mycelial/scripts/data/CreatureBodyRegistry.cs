namespace Mycorrhiza.Data;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Body shape templates for every creature species.
///
/// Each creature is a tiny pixel-art sprite defined as sub-cell offsets.
/// At 4px per sub-cell, these are deliberately small — recognizable by shape
/// and color rather than detail, matching the tendril's visual language.
///
/// COLOR PHILOSOPHY:
///   - Prey: earthy, muted (browns, pinks, pale yellows) — they belong in the soil
///   - Threats: high-contrast accent colors (red highlights, dark shells) — danger is visible
///   - Deep biome creatures: increasingly alien palettes (blues, purples, greens)
///
/// BODY DESIGN RULES:
///   - Origin (0,0) is always the visual center of the creature
///   - Bodies should be roughly symmetrical for clean horizontal flipping
///   - Radius must cover the farthest cell from origin
///   - Larger creatures have more cells but stay compact (no sprawling shapes)
/// </summary>
public static class CreatureBodyRegistry
{
    private static readonly Dictionary<CreatureSpecies, CreatureBodySet> _bodies = new();

    static CreatureBodyRegistry()
    {
        Register(CreatureSpecies.Earthworm, BuildEarthworm());
        Register(CreatureSpecies.Beetle, BuildBeetle());
        Register(CreatureSpecies.Grub, BuildGrub());
        Register(CreatureSpecies.MoleRat, BuildMoleRat());
        Register(CreatureSpecies.RootBorer, BuildRootBorer());
        Register(CreatureSpecies.FungusGnat, BuildFungusGnat());
        Register(CreatureSpecies.CaveFish, BuildCaveFish());
        Register(CreatureSpecies.BlindSalamander, BuildBlindSalamander());
        Register(CreatureSpecies.CaveSpider, BuildCaveSpider());
        Register(CreatureSpecies.BoneCrab, BuildBoneCrab());
        Register(CreatureSpecies.WormColony, BuildWormColony());
        Register(CreatureSpecies.MarrowLeech, BuildMarrowLeech());
        Register(CreatureSpecies.MagmaBeetle, BuildMagmaBeetle());
        Register(CreatureSpecies.TubeWorm, BuildTubeWorm());
        Register(CreatureSpecies.MemorySlug, BuildMemorySlug());
        Register(CreatureSpecies.FungalPredator, BuildFungalPredator());
    }

    private static void Register(CreatureSpecies species, CreatureBodySet bodySet)
    {
        _bodies[species] = bodySet;
    }

    /// <summary>Get the full body set (with animation frames) for a species.</summary>
    public static CreatureBodySet GetBodySet(CreatureSpecies species)
    {
        if (_bodies.TryGetValue(species, out var set)) return set;
        return _bodies[CreatureSpecies.Grub]; // Fallback: tiny blob
    }

    /// <summary>Get the idle body for a species (shortcut).</summary>
    public static CreatureBody GetDefaultBody(CreatureSpecies species)
    {
        return GetBodySet(species).Idle;
    }

    // =========================================================================
    //  COLOR HELPERS
    // =========================================================================

    private static Color C(float r, float g, float b) => new(r, g, b, 1f);
    private static Color C(float r, float g, float b, float a) => new(r, g, b, a);

    // Shared palettes
    private static readonly Color WormPink = C(0.72f, 0.50f, 0.42f);
    private static readonly Color WormDark = C(0.55f, 0.36f, 0.30f);
    private static readonly Color WormLight = C(0.80f, 0.58f, 0.48f);
    private static readonly Color ShellDark = C(0.25f, 0.18f, 0.12f);
    private static readonly Color ShellMid = C(0.38f, 0.28f, 0.18f);
    private static readonly Color ShellHighlight = C(0.50f, 0.38f, 0.25f);
    private static readonly Color GrubPale = C(0.85f, 0.80f, 0.65f);
    private static readonly Color GrubMid = C(0.78f, 0.72f, 0.58f);
    private static readonly Color FurBrown = C(0.45f, 0.32f, 0.22f);
    private static readonly Color FurLight = C(0.58f, 0.45f, 0.32f);
    private static readonly Color CavePale = C(0.70f, 0.75f, 0.80f);
    private static readonly Color CaveBlue = C(0.45f, 0.55f, 0.70f);
    private static readonly Color BoneWhite = C(0.85f, 0.82f, 0.75f);
    private static readonly Color BoneYellow = C(0.78f, 0.72f, 0.58f);
    private static readonly Color ThreatRed = C(0.75f, 0.20f, 0.15f);
    private static readonly Color ThreatDark = C(0.35f, 0.10f, 0.08f);
    private static readonly Color MagmaOrange = C(0.90f, 0.45f, 0.10f);
    private static readonly Color MagmaYellow = C(0.95f, 0.70f, 0.20f);
    private static readonly Color SporeGreen = C(0.40f, 0.65f, 0.30f);
    private static readonly Color SporeGlow = C(0.55f, 0.80f, 0.40f);
    private static readonly Color GhostBlue = C(0.55f, 0.60f, 0.78f);
    private static readonly Color GhostPale = C(0.75f, 0.78f, 0.88f);
    private static readonly Color PredatorPurple = C(0.50f, 0.18f, 0.45f);
    private static readonly Color PredatorGlow = C(0.72f, 0.25f, 0.60f);

    // =========================================================================
    //  TOPSOIL / NEUTRAL CREATURES
    // =========================================================================

    /// <summary>
    /// Earthworm — horizontal wormy line. 7 cells long, 3 cells tall at thickest.
    /// Wiggles when moving (3-frame animation shifting the curve).
    ///
    ///  Frame 0 (straight):
    ///      . X .
    ///  X X X o X X X
    ///      . X .
    /// </summary>
    private static CreatureBodySet BuildEarthworm()
    {
        var frame0 = new CreatureBody(
            new (int, int)[] {
                                     (0, -1),
                (-3, 0), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (3, 0),
                                     (0,  1),
            },
            new Color[] {
                                   WormLight,
                WormDark, WormPink, WormLight, WormPink, WormLight, WormPink, WormDark,
                                   WormLight,
            },
            radius: 4
        );

        // Frame 1: slight upward curve on left side
        var frame1 = new CreatureBody(
            new (int, int)[] {
                         (-2, -1), (-1, -1),
                (-3, 0),                     (0, 0), (1, 0), (2, 0), (3, 0),
                                                              (2,  1),
            },
            new Color[] {
                         WormPink, WormLight,
                WormDark,                    WormPink, WormLight, WormPink, WormDark,
                                                                 WormLight,
            },
            radius: 4
        );

        // Frame 2: slight downward curve on left side
        var frame2 = new CreatureBody(
            new (int, int)[] {
                                                              (2, -1),
                (-3, 0),                     (0, 0), (1, 0), (2, 0), (3, 0),
                         (-2,  1), (-1,  1),
            },
            new Color[] {
                                                              WormLight,
                WormDark,                    WormPink, WormLight, WormPink, WormDark,
                         WormPink, WormLight,
            },
            radius: 4
        );

        return new CreatureBodySet(new[] { frame0, frame1, frame0, frame2 }, 0.18f);
    }

    /// <summary>
    /// Beetle — compact oval with dark shell. 5 wide, 4 tall.
    ///
    ///    X X X
    ///  X X o X X
    ///  X X X X X
    ///    X X X
    /// </summary>
    private static CreatureBodySet BuildBeetle()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                         (-1, -1), (0, -1), (1, -1),
                (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0),
                (-2, 1), (-1,  1), (0,  1), (1,  1), (2, 1),
                         (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                            ShellMid,      ShellHighlight, ShellMid,
                ShellDark,  ShellMid,      ShellHighlight, ShellMid,      ShellDark,
                ShellDark,  ShellMid,      ShellMid,       ShellMid,      ShellDark,
                            ShellDark,     ShellMid,       ShellDark,
            },
            radius: 3
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Grub — tiny 3×2 pale blob. Barely moves. Easy prey.
    ///
    ///  X X
    ///  X o X
    ///  X X
    /// </summary>
    private static CreatureBodySet BuildGrub()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                (-1, -1), (0, -1),
                (-1,  0), (0,  0), (1, 0),
                (-1,  1), (0,  1),
            },
            new Color[] {
                GrubMid,  GrubPale,
                GrubPale, GrubPale, GrubMid,
                GrubMid,  GrubPale,
            },
            radius: 2
        );

        return new CreatureBodySet(body);
    }

    // =========================================================================
    //  ROOT MAZE CREATURES
    // =========================================================================

    /// <summary>
    /// Mole Rat — chunky 5×4 body. Fast and skittish. Brown fur.
    ///
    ///    X X X
    ///  X X o X X
    ///  X X X X X
    ///  . X X X .
    /// </summary>
    private static CreatureBodySet BuildMoleRat()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                         (-1, -1), (0, -1), (1, -1),
                (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0),
                (-2, 1), (-1,  1), (0,  1), (1,  1), (2, 1),
                         (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                            FurLight,  FurBrown,  FurLight,
                FurBrown,   FurLight,  FurBrown,  FurLight,  FurBrown,
                FurBrown,   FurBrown,  FurLight,  FurBrown,  FurBrown,
                            FurBrown,  FurBrown,  FurBrown,
            },
            radius: 3
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Root Borer — elongated 6×3 insect with mandibles at front.
    ///
    ///  X . X X X .
    ///  X X o X X X
    ///  X . X X X .
    /// </summary>
    private static CreatureBodySet BuildRootBorer()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                (-3, -1),          (-1, -1), (0, -1), (1, -1),
                (-3,  0), (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0),
                (-3,  1),          (-1,  1), (0,  1), (1,  1),
            },
            new Color[] {
                ShellDark,              ShellMid,      ShellHighlight, ShellMid,
                ShellDark, ShellMid,    ShellHighlight, ShellMid,      ShellHighlight, ShellMid,
                ShellDark,              ShellMid,      ShellHighlight, ShellMid,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Fungus Gnat — tiny 3×3 flying pest with translucent wings.
    ///
    ///  . X .
    ///  X o X
    ///  . X .
    /// </summary>
    private static CreatureBodySet BuildFungusGnat()
    {
        Color wing = C(0.60f, 0.55f, 0.45f, 0.7f);
        Color gnatBody = C(0.30f, 0.25f, 0.18f);

        var frame0 = new CreatureBody(
            new (int, int)[] {
                         (0, -1),
                (-1, 0), (0,  0), (1, 0),
                         (0,  1),
            },
            new Color[] {
                      wing,
                wing, gnatBody, wing,
                      wing,
            },
            radius: 2
        );

        // Frame 1: wings spread wider
        var frame1 = new CreatureBody(
            new (int, int)[] {
                (-1, -1),          (1, -1),
                         (0,  0),
                (-1,  1),          (1,  1),
            },
            new Color[] {
                wing,              wing,
                      gnatBody,
                wing,              wing,
            },
            radius: 2
        );

        return new CreatureBodySet(new[] { frame0, frame1 }, 0.10f);
    }

    // =========================================================================
    //  WET DARK CREATURES
    // =========================================================================

    /// <summary>
    /// Cave Fish — streamlined 5×3 with pale coloring.
    ///
    ///    X X X
    ///  X X o X X
    ///    X X X
    /// </summary>
    private static CreatureBodySet BuildCaveFish()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                         (-1, -1), (0, -1), (1, -1),
                (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0),
                         (-1,  1), (0,  1), (1,  1),
            },
            new Color[] {
                         CavePale, CavePale,  CaveBlue,
                CaveBlue, CavePale, CavePale,  CavePale, CaveBlue,
                         CaveBlue, CavePale,  CaveBlue,
            },
            radius: 3
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Blind Salamander — elongated 7×3 with stubby limbs.
    ///
    ///  X . X X X . X
    ///  . X X o X X .
    ///  X . X X X . X
    /// </summary>
    private static CreatureBodySet BuildBlindSalamander()
    {
        Color salPale = C(0.82f, 0.78f, 0.75f);
        Color salPink = C(0.85f, 0.65f, 0.62f);

        var body = new CreatureBody(
            new (int, int)[] {
                (-3, -1),          (-1, -1), (0, -1), (1, -1),          (3, -1),
                         (-2,  0), (-1,  0), (0,  0), (1,  0), (2, 0),
                (-3,  1),          (-1,  1), (0,  1), (1,  1),          (3,  1),
            },
            new Color[] {
                salPale,            salPink,  salPale,  salPink,          salPale,
                         salPink,   salPale,  salPink,  salPale, salPink,
                salPale,            salPink,  salPale,  salPink,          salPale,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Cave Spider — 7×7 star shape with long legs. Terrifying.
    ///
    ///  X . . . . . X
    ///  . X . . . X .
    ///  . . X X X . .
    ///  . . X o X . .
    ///  . . X X X . .
    ///  . X . . . X .
    ///  X . . . . . X
    /// </summary>
    private static CreatureBodySet BuildCaveSpider()
    {
        Color leg = C(0.30f, 0.28f, 0.25f);
        Color body = C(0.22f, 0.18f, 0.15f);
        Color eye = ThreatRed;

        var shape = new CreatureBody(
            new (int, int)[] {
                // Legs (diagonal)
                (-3, -3),                                        (3, -3),
                         (-2, -2),                      (2, -2),
                // Body
                                   (-1, -1), (0, -1), (1, -1),
                                   (-1,  0), (0,  0), (1,  0),
                                   (-1,  1), (0,  1), (1,  1),
                // Legs (diagonal)
                         (-2,  2),                      (2,  2),
                (-3,  3),                                        (3,  3),
            },
            new Color[] {
                leg,                                              leg,
                      leg,                               leg,
                              body,     eye,      body,
                              body,     body,     body,
                              body,     body,     body,
                      leg,                               leg,
                leg,                                              leg,
            },
            radius: 4
        );

        return new CreatureBodySet(shape);
    }

    // =========================================================================
    //  BONE STRATA CREATURES
    // =========================================================================

    /// <summary>
    /// Bone Crab — wide 7×5 armored body with claw extensions.
    ///
    ///  X . . X . . X
    ///  X . X X X . X
    ///  . X X o X X .
    ///  . X X X X X .
    ///  . . X X X . .
    /// </summary>
    private static CreatureBodySet BuildBoneCrab()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                (-3, -2),          (0, -2),          (3, -2),
                (-3, -1),  (-1, -1), (0, -1), (1, -1),  (3, -1),
                   (-2, 0), (-1,  0), (0,  0), (1,  0), (2,  0),
                   (-2, 1), (-1,  1), (0,  1), (1,  1), (2,  1),
                            (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                BoneWhite,          BoneYellow,          BoneWhite,
                BoneWhite,  BoneYellow, BoneWhite, BoneYellow,  BoneWhite,
                   BoneYellow, BoneWhite, BoneYellow, BoneWhite, BoneYellow,
                   BoneYellow, BoneWhite, BoneWhite,  BoneWhite, BoneYellow,
                               BoneYellow, BoneWhite, BoneYellow,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Worm Colony — amorphous 5×5 blob of writhing worms. Dangerous swarm.
    ///
    ///  . X X X .
    ///  X X X X X
    ///  X X o X X
    ///  X X X X X
    ///  . X X X .
    /// </summary>
    private static CreatureBodySet BuildWormColony()
    {
        Color worm1 = C(0.60f, 0.38f, 0.30f);
        Color worm2 = C(0.50f, 0.30f, 0.25f);
        Color worm3 = C(0.68f, 0.45f, 0.35f);

        var frame0 = new CreatureBody(
            new (int, int)[] {
                         (-1, -2), (0, -2), (1, -2),
                (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1),
                (-2,  0), (-1,  0), (0,  0), (1,  0), (2,  0),
                (-2,  1), (-1,  1), (0,  1), (1,  1), (2,  1),
                         (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                      worm2, worm1, worm3,
                worm1, worm3, worm2, worm1, worm2,
                worm3, worm1, worm2, worm3, worm1,
                worm2, worm3, worm1, worm2, worm3,
                      worm1, worm3, worm2,
            },
            radius: 3
        );

        // Frame 1: shift colors to simulate writhing
        var frame1 = new CreatureBody(
            frame0.Cells, // Same shape
            new Color[] {
                      worm3, worm2, worm1,
                worm2, worm1, worm3, worm2, worm3,
                worm1, worm3, worm1, worm2, worm3,
                worm3, worm1, worm2, worm3, worm1,
                      worm2, worm1, worm3,
            },
            radius: 3
        );

        return new CreatureBodySet(new[] { frame0, frame1 }, 0.12f);
    }

    /// <summary>
    /// Marrow Leech — thin elongated 6×2 parasite.
    ///
    ///  X X X o X X
    ///  . X X X X .
    /// </summary>
    private static CreatureBodySet BuildMarrowLeech()
    {
        Color leech = C(0.50f, 0.15f, 0.12f);
        Color leechLight = C(0.65f, 0.25f, 0.20f);

        var body = new CreatureBody(
            new (int, int)[] {
                (-3, 0), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0),
                         (-2, 1), (-1, 1), (0, 1), (1, 1),
            },
            new Color[] {
                leech, leechLight, leech, leechLight, leech, leechLight,
                       leech,     leechLight, leech, leechLight,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    // =========================================================================
    //  THERMOVENT CREATURES
    // =========================================================================

    /// <summary>
    /// Magma Beetle — like the beetle but with glowing orange highlights.
    /// Same silhouette as regular beetle but ANGRY colors.
    /// </summary>
    private static CreatureBodySet BuildMagmaBeetle()
    {
        Color shell = C(0.20f, 0.12f, 0.08f);
        Color crack = MagmaOrange;
        Color glow = MagmaYellow;

        var body = new CreatureBody(
            new (int, int)[] {
                         (-1, -1), (0, -1), (1, -1),
                (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0),
                (-2, 1), (-1,  1), (0,  1), (1,  1), (2, 1),
                         (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                         shell,  crack,    shell,
                shell,   crack,  glow,     crack,   shell,
                shell,   shell,  crack,    shell,   shell,
                         shell,  shell,    shell,
            },
            radius: 3
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Tube Worm — tall vertical 3×7. Burrowed, springs out.
    ///
    ///    X
    ///  X X X
    ///  X o X
    ///  X X X
    ///  X X X
    ///  X X X
    ///    X
    /// </summary>
    private static CreatureBodySet BuildTubeWorm()
    {
        Color tube = C(0.70f, 0.55f, 0.40f);
        Color tubeDark = C(0.50f, 0.38f, 0.28f);
        Color tip = ThreatRed;

        var body = new CreatureBody(
            new (int, int)[] {
                         (0, -3),
                (-1, -2), (0, -2), (1, -2),
                (-1, -1), (0, -1), (1, -1),
                (-1,  0), (0,  0), (1,  0),
                (-1,  1), (0,  1), (1,  1),
                (-1,  2), (0,  2), (1,  2),
                         (0,  3),
            },
            new Color[] {
                         tip,
                tip,     tip,     tip,
                tube,    tubeDark, tube,
                tubeDark, tube,   tubeDark,
                tube,    tubeDark, tube,
                tubeDark, tube,   tubeDark,
                         tubeDark,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    // =========================================================================
    //  MYCELIAL GRAVEYARD CREATURES
    // =========================================================================

    /// <summary>
    /// Memory Slug — large 7×5 translucent blob. Slow, carries lore.
    /// Ghostly blue-white palette. Feels ancient and fragile.
    /// </summary>
    private static CreatureBodySet BuildMemorySlug()
    {
        var body = new CreatureBody(
            new (int, int)[] {
                                  (-1, -2), (0, -2), (1, -2),
                         (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1),
                (-3, 0), (-2,  0), (-1,  0), (0,  0), (1,  0), (2,  0), (3, 0),
                         (-2,  1), (-1,  1), (0,  1), (1,  1), (2,  1),
                                   (-1,  2), (0,  2), (1,  2),
            },
            new Color[] {
                                  GhostBlue, GhostPale, GhostBlue,
                         GhostBlue, GhostPale, GhostPale, GhostPale, GhostBlue,
                GhostBlue, GhostPale, GhostPale, GhostPale, GhostPale, GhostPale, GhostBlue,
                         GhostBlue, GhostPale, GhostPale, GhostPale, GhostBlue,
                                   GhostBlue, GhostBlue, GhostBlue,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }

    /// <summary>
    /// Fungal Predator — large 7×7 aggressive shape with glowing purple.
    /// Star-like with reaching tendrils. The anti-you.
    ///
    ///  X . . X . . X
    ///  . X . X . X .
    ///  . . X X X . .
    ///  X X X o X X X
    ///  . . X X X . .
    ///  . X . X . X .
    ///  X . . X . . X
    /// </summary>
    private static CreatureBodySet BuildFungalPredator()
    {
        Color t = PredatorPurple;  // Tendril
        Color g = PredatorGlow;    // Glow accent
        Color c = C(0.35f, 0.12f, 0.30f); // Core dark

        var body = new CreatureBody(
            new (int, int)[] {
                (-3, -3),          (0, -3),          (3, -3),
                     (-2, -2),     (0, -2),     (2, -2),
                          (-1, -1), (0, -1), (1, -1),
                (-3, 0), (-2, 0), (-1,  0), (0,  0), (1,  0), (2, 0), (3, 0),
                          (-1,  1), (0,  1), (1,  1),
                     (-2,  2),     (0,  2),     (2,  2),
                (-3,  3),          (0,  3),          (3,  3),
            },
            new Color[] {
                t,             g,             t,
                    t,         g,         t,
                        g,     c,     g,
                t, g, c,       g,     c, g, t,
                        g,     c,     g,
                    t,         g,         t,
                t,             g,             t,
            },
            radius: 4
        );

        return new CreatureBodySet(body);
    }
}
