# Vitality + Vigor System — Full Design

## Overview

Two meters replace the single Hunger bar:

- **Vitality** = health. You die when this hits zero.
- **Vigor** = power/combo. Determines how good you feel to play.

Neither drains from movement. Movement is FREE. The game punishes
stagnation and rewards aggression.

---

## VITALITY (Health)

### What drains it:
- Enemy attacks (cave spider hits you: -15 vitality)
- Being disconnected from the network (-8/sec, escalating)
- Hazard tiles (lava, acid, toxic water: -5/sec while touching)
- Staying at zero vigor for too long (-2/sec after 5 seconds at zero)

### What restores it:
- Standing on your own territory: +3/sec
- Standing on the origin tree: +8/sec
- Consuming certain creatures (bonus vitality, not just vigor)
- Future: upgrades, items

### Behavior at low vitality:
- Below 30%: screen edge darkens (vignette), subtle heartbeat audio cue
- Below 15%: vignette intensifies, pulse visual on tendril
- Zero: death → retreat/respawn (existing system)

### Starting value: 100
### Max value: 100 (expandable via future upgrades)

### Key design point:
Vitality is BORING ON PURPOSE. It's the "don't die" meter. It should
rarely be the thing you're thinking about — until suddenly you're
disconnected and it's the ONLY thing you're thinking about. The swing
from "I forgot this existed" to "OH NO" is the drama.

---

## VIGOR (Combo / Power)

### What builds it:
- Consuming a creature: +15–40 vigor (scales with creature value)
- Claiming a new terrain tile: +1 vigor
- Infecting a tile: +2 vigor
- Slam kill (thrown creature kills another): +25 vigor bonus
- Harpoon catch: +5 vigor (just for grabbing, even before eating)

### What drains it:
- Passive decay: -4/sec baseline
- Decay PAUSES for 1.5 seconds after any vigor-building action
  (this is the "combo window" — keep hunting to keep the meter up)
- Being disconnected: decay doubles to -8/sec

### What vigor DOES (the fun part):

| Vigor Range | Name       | Effects |
|-------------|------------|---------|
| 0–15        | Withering  | Tendril moves at 60% speed. Blob shrinks to 60% radius. Harpoon range -30%. Roots don't spread. You feel WEAK. |
| 16–40       | Surviving  | Normal speed. Normal blob. Normal harpoon. Baseline everything. This is "fine." |
| 41–70       | Thriving   | 115% speed. Blob grows 15%. Harpoon range +20%. Root spread speed +30%. Passive corruption speed +20%. |
| 71–90       | Apex       | 130% speed. Blob grows 30%. Harpoon range +40%. Root spread speed +60%. Passive corruption speed +50%. Creature auto-steer radius doubles. |
| 91–100      | Unstoppable| 140% speed. Blob grows 40%. Harpoon +50% range. Max root spread. Nearby small creatures flee from you automatically (fear aura). Screen subtly pulses with power. |

### Starting value: 50 (Thriving — you start feeling good)
### Max value: 100
### Decay grace period: 1.5 seconds after last vigor gain

### Key design point:
Vigor should make you FEEL the difference. Going from Withering to
Apex should feel like going from a slug to a predator. The speed
change alone does most of this work — games feel fundamentally
different at different speeds. The blob size change is the visual
confirmation that you're growing more powerful.

The combo window (1.5s decay pause) is critical. It means that if
you're actively hunting — finding a creature every few seconds — your
vigor stays high or climbs. The moment you stop hunting (exploring an
empty cave, backtracking, being cautious), it starts to drop. This
creates a constant low-key pressure to KEEP MOVING, KEEP HUNTING
without the punishing "you moved so subtract food" feeling.

---

## NETWORK CONNECTION

### Definition:
The tendril head is "connected" if a path exists through sub-grid cells
(Trail, Root, Core, or Fresh state) from any cell adjacent to the head
back to any cell within the origin tree's terrain tiles.

### How to check:
Flood fill (BFS) from the head position through sub-grid cells.
If the fill reaches any cell whose terrain tile is in _treeTiles, connected.

### When to check:
- Every 1.0 seconds on a timer (cheap, not per-frame)
- Immediately when a grazer eats a cell (the cell-eat event triggers a check)
- Immediately on disconnect, check every 0.5 seconds (faster polling when critical)

### Performance:
BFS through the sub-grid dictionary. Worst case is the full trail length,
but you can early-exit the moment you find a tree tile. Average case is
much shorter because the head is usually close to connected territory.
Cap the BFS at ~5000 nodes to prevent frame spikes on very long trails;
if you hit the cap without finding the tree, assume disconnected.

### What happens on disconnect:
1. "DISCONNECTED" state flag sets immediately
2. Vitality drain starts: -8/sec, escalating by +2/sec every 3 seconds
   (so: -8, -10, -12, -14... increasingly urgent)
3. Vigor decay doubles
4. Visual: tendril color desaturates slightly, screen edge darkens
5. Audio: low warning drone, heartbeat
6. Fog of war: no new light emission from disconnected trail cells
   (existing light stays but doesn't refresh — it slowly dims)

### What happens on reconnect:
1. State clears immediately
2. Vitality drain stops
3. Vigor decay returns to normal
4. Visual snap-back: color returns, vignette fades
5. Audio: relief sound (a breath, a pulse of warmth)
6. Brief vigor bonus: +10 vigor for reconnecting (reward for surviving)

### Severing gameplay:
When the trail is severed (a gap in the sub-grid path):
- Everything "behind" the sever point (between the break and the tree)
  remains as territory — it's still claimed, still visible on fog of war
- Everything "ahead" of the sever point (between the break and the head)
  starts to decay — those cells slowly lose intensity and eventually vanish
  (this is the territory decay you wanted, but caused by severing, not time)
- The head is disconnected and bleeding vitality
- The player can reconnect by:
  a) Going back and filling the gap (if it's small)
  b) Routing around through alternate paths
  c) Reaching the tree by a completely different route

---

## INTEGRATION WITH EXISTING CODE

### TendrilController changes:

Replace Hunger field + all hunger logic with:

```csharp
// New fields
public float Vitality { get; private set; }
public float MaxVitality = 100f;
public float Vigor { get; private set; }
public float MaxVigor = 100f;
public bool IsConnected { get; private set; } = true;

// Vigor decay
private float _vigorDecayRate = 4f;
private float _vigorGracePeriod = 1.5f;
private float _vigorGraceTimer;

// Connection check
private float _connectionCheckInterval = 1.0f;
private float _connectionCheckTimer;
private float _disconnectTime; // How long we've been disconnected

// Vigor thresholds
public float SpeedMultiplier => GetVigorSpeedMultiplier();
public float BlobSizeMultiplier => GetVigorBlobMultiplier();
public float HarpoonRangeMultiplier => GetVigorHarpoonMultiplier();
```

### What to REMOVE:
- HungerPerMove, HungerPerHardMove, HungerOnCorrupted — movement is free
- All hunger cost calculations in TrySubMove() — just remove the cost block
- HungerRegenOnCorrupted — replaced by vitality regen on territory
- The retreat-on-zero-hunger trigger — replaced by retreat-on-zero-vitality
- DrainHunger/AddHunger — replaced by DamageVitality/AddVigor/etc

### What to KEEP (repurposed):
- TrySubMove() terrain passability checks — still can't go through walls
- ClaimTerrainTile() — now grants vigor instead of saving hunger
- The retreat/regen system — triggered by zero vitality instead of zero hunger
- CreatureManager consumption — now calls AddVigor instead of AddHunger

### TrySubMove() simplified:

```csharp
// OLD: complex hunger cost calculation
// NEW: movement is free, just claim and grant vigor

if (newTerrainTile)
{
    // Passability checks remain identical

    // Claim new territory → vigor reward
    if (!_claimedTiles.Contains(PackCoords(terrainX, terrainY)))
    {
        ClaimTerrainTile(terrainX, terrainY);
        AddVigor(1f); // Small vigor for new tiles
    }

    TrackTravelAndSpawnRoots();
}
```

### Vigor → movement speed integration:

In ProcessMovement(), multiply the effective move delay:

```csharp
float effectiveMoveDelay = Mathf.Max(0.005f, MoveDelay * delayMultiplier);
effectiveMoveDelay /= SpeedMultiplier; // Vigor makes you faster
```

### Vigor → blob size integration:

In PlaceBlob():

```csharp
int radius = (int)(BlobBaseRadius * BlobSizeMultiplier);
```

### Vigor → harpoon range:

TendrilHarpoon reads the multiplier:

```csharp
_targetRange = (int)(TapRange * _tendril.HarpoonRangeMultiplier);
```

---

## VIGOR TIER FUNCTIONS

```csharp
private float GetVigorSpeedMultiplier()
{
    if (Vigor <= 15) return 0.6f;
    if (Vigor <= 40) return 1.0f;
    if (Vigor <= 70) return 1.15f;
    if (Vigor <= 90) return 1.3f;
    return 1.4f;
}

private float GetVigorBlobMultiplier()
{
    if (Vigor <= 15) return 0.6f;
    if (Vigor <= 40) return 1.0f;
    if (Vigor <= 70) return 1.15f;
    if (Vigor <= 90) return 1.3f;
    return 1.4f;
}

private float GetVigorHarpoonMultiplier()
{
    if (Vigor <= 15) return 0.7f;
    if (Vigor <= 40) return 1.0f;
    if (Vigor <= 70) return 1.2f;
    if (Vigor <= 90) return 1.4f;
    return 1.5f;
}

private float GetVigorRootSpreadMultiplier()
{
    if (Vigor <= 15) return 0f;   // Roots don't spread when withering
    if (Vigor <= 40) return 1.0f;
    if (Vigor <= 70) return 1.3f;
    if (Vigor <= 90) return 1.6f;
    return 2.0f;
}
```

---

## CONNECTION CHECK (BFS)

```csharp
/// <summary>
/// Check if the head can trace a path through sub-grid cells to the tree.
/// Uses BFS with a node cap to prevent frame spikes.
/// </summary>
private bool CheckNetworkConnection()
{
    const int maxNodes = 5000;

    var queue = new Queue<long>();
    var visited = new HashSet<long>();

    // Start from cells adjacent to the head
    int scale = WorldConfig.SubGridScale;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            int sx = _subHeadX + dx;
            int sy = _subHeadY + dy;
            if (SubGrid.HasCell(sx, sy))
            {
                long key = SubGridData.PackCoords(sx, sy);
                queue.Enqueue(key);
                visited.Add(key);
            }
        }
    }

    while (queue.Count > 0 && visited.Count < maxNodes)
    {
        long key = queue.Dequeue();
        var (x, y) = SubGridData.UnpackCoords(key);

        // Check if this cell is on a tree tile
        var (tileX, tileY) = SubGridData.SubToTerrain(x, y);
        if (_treeTiles.Contains(PackCoords(tileX, tileY)))
            return true; // Connected!

        // Expand to neighbors
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                long nkey = SubGridData.PackCoords(nx, ny);
                if (visited.Contains(nkey)) continue;
                if (!SubGrid.HasCell(nx, ny)) continue;

                var cell = SubGrid.GetCell(nx, ny);
                if (cell.State == SubCellState.Empty) continue;

                visited.Add(nkey);
                queue.Enqueue(nkey);
            }
        }
    }

    return false; // Couldn't reach the tree
}
```

---

## HUD IMPLICATIONS

The player needs to see two things at a glance:

1. **Vitality bar** — simple health bar, red, top-left. Only draws attention
   when low. Boring by design.

2. **Vigor indicator** — this should be FELT more than read. Options:
   - The tendril's blob size IS the indicator (bigger = more vigor)
   - A subtle ring/glow around the head that grows with vigor
   - A small bar near the head that fills with a warm color
   - The screen saturation itself shifts (low vigor = muted, high = vivid)

   Recommendation: blob size + screen saturation. No explicit bar.
   The player learns "I feel slow and small = low vigor" and
   "I feel fast and big = high vigor" intuitively.

3. **Disconnected warning** — this should be ALARMING:
   - Screen edge vignette (dark, maybe reddish)
   - Tendril desaturates
   - Pulsing visual urgency
   - Later: audio heartbeat

---

## TUNING PHILOSOPHY

All numbers above are starting points. The feel depends on:

- **Vigor decay rate (4/sec)**: Too fast = exhausting, always scrambling.
  Too slow = no pressure. Test with 3–6 range.

- **Grace period (1.5s)**: This is the "combo window." Too short = impossible
  to maintain high vigor. Too long = no pressure between kills.
  Test with 1.0–2.5 range.

- **Speed multiplier range (0.6–1.4)**: Too narrow = vigor feels meaningless.
  Too wide = low vigor is unplayable. Test the low end especially.

- **Disconnect vitality drain (-8/sec)**: Too fast = instant death on sever,
  feels unfair. Too slow = no urgency. Should give ~12 seconds to reconnect
  at full vitality (100 / 8 = 12.5s). That's enough to feel urgent without
  being instant.

- **Creature vigor values**: These determine the pacing. If earthworms give
  too much vigor, the player never drops below Thriving. If they give too
  little, the player can't maintain even Surviving. Tune these relative to
  creature spawn density.

---

## MIGRATION CHECKLIST

1. Add Vitality + Vigor fields to TendrilController
2. Add vigor decay + grace timer to _Process
3. Add connection check on timer to _Process
4. Remove all hunger cost logic from TrySubMove
5. Add vigor gain on new tile claim
6. Add vitality regen on territory / tree
7. Replace DrainHunger with DamageVitality in CreatureManager
8. Replace AddHunger with AddVigor in CreatureManager + TendrilHarpoon
9. Wire vigor multipliers into movement speed, blob size, harpoon range
10. Add connection BFS
11. Update HUD signals (HungerChanged → VitalityChanged + VigorChanged)
12. Update retreat trigger: zero vitality instead of zero hunger
13. Test without enemies first — just vigor decay from exploring
14. Add enemies and tune creature vigor values
15. Test severing with a grazer and tune disconnect drain




1. Starting Vigor should be 30 (Surviving), not 50 (Thriving)

Starting at Thriving means the player immediately feels themselves getting weaker as vigor decays before they understand the system. Starting at Surviving (baseline) with easy earthworms/grubs near the origin tree teaches the loop naturally: eat → feel yourself speed up → understand the mechanic. The "aha" moment of crossing into Thriving for the first time is a reward you lose by starting there.

2. Root spread at Withering should be 0.3x, not 0x

At Withering, roots don't spread at all. But roots = territory = vitality regen = your recovery path. Zeroing out roots when the player is weakest creates a death spiral with no escape. A 0.3x multiplier keeps recovery possible (just painful), preserving the "scrambling parasite" fantasy without making Withering feel hopeless.

3. Fear aura radius at Unstoppable must be smaller than harpoon range

If small creatures flee at Unstoppable (91-100), and they flee outside harpoon reach, the player can't eat them, can't maintain vigor, and falls out of Unstoppable — a frustrating negative feedback loop at the moment you should feel most powerful. Suggestion: fear aura radius = 60% of TapRange (so ~30 sub-cells). Creatures flee but are still harponable.

4. Harpoon should be free to fire

Current HungerCost = 5f becomes irrelevant but needs an explicit decision. Making the harpoon free aligns with "reward aggression" — there's no cost to trying. The player is already paying vigor decay while not eating, so missing a shot is its own punishment (wasted time = vigor decay). No need for a double penalty.

5. DamageOnHit should target Vitality, not Vigor

The doc implies this but doesn't state it. Current CaveSpider.DamageOnHit = 8 should subtract from Vitality. If it subtracted from Vigor, combat would make the player weaker AND slower, creating a brutal death spiral. Combat should threaten your life (Vitality), not your power (Vigor). Killing the spider and eating it should boost Vigor — that's the reward loop.

6. TendrilInfection tile gains need retuning for Vigor

Currently TendrilInfection.cs awards per-tile hunger: Dirt=2, Roots=6, BoneMarrow=15, etc. These shouldn't map 1:1 to Vigor. A player infecting terrain already gets +2 vigor per tile claimed (from the design doc). The infection-specific gains should be rolled into that or made additive but smaller (+1 bonus for organic tiles). Otherwise infecting a BoneMarrow tile giving +15 vigor would be disproportionately powerful compared to eating a Grub (+15 from consumption).

7. Rush chain needs Vigor rewards

The rush chain (harpoon throw → slam → dash → consume stunned creatures) is mechanically rich but the doc only mentions "+25 vigor bonus" for slam kills. The full chain should reward at each stage:

Harpoon grab: +5 vigor
Throw-slam kill: +25 bonus
Rush-consumed stunned creature: +15 each (currently uses AddHunger)
Wall splat kill: +20 bonus
This makes aggressive combo play the fastest way to build Vigor, exactly matching the design intent.

8. Connection check should use Union-Find, not BFS (future optimization)

BFS capped at 5000 nodes is fine for v1. But as the network grows to thousands of cells, a Union-Find (disjoint set) structure would make connectivity checks O(α(n)) ≈ O(1) amortized. Worth noting for later — when a grazer eats a cell, you'd split the set and re-check. For now, BFS on a 1-second timer is correct and simple.

9. Grazer cell-eat event is missing

The doc says "immediately when a grazer eats a cell, trigger a connection check." The current CreatureManager.cs AIGrazer() erases cells but doesn't emit any event. You'll need a signal/callback like CellSevered(int subX, int subY) that TendrilController listens to for immediate connectivity checks.

10. Disconnected trail decay needs a new system

"Everything ahead of the sever point starts to decay" — SubGrid currently has no decay mechanic for disconnected cells. This needs: (a) identify which cells are disconnected (BFS from tree, anything not reached is disconnected), (b) gradually reduce intensity on disconnected cells, (c) remove cells at zero intensity, (d) unclaim terrain tiles when all their sub-cells are gone. This is a non-trivial new subsystem.

11. Disconnect vitality drain starting at -8/sec is aggressive

At 100 Vitality and -8/sec escalating by +2/sec every 3 seconds: you survive about 9 seconds. For a first-time player who doesn't understand reconnection yet, that's punishing. Suggestion: start at -5/sec, escalate by +2/sec every 4 seconds. That gives ~14 seconds — still urgent, but learnable.

12. Vigor → PassiveCorruption link is underspecified

PassiveCorruption.cs has SpreadsPerTick = 8 and SpreadInterval = 0.2s. The doc mentions corruption speed bonuses at higher vigor tiers but doesn't address Withering. Suggestion: at Withering, passive corruption stops entirely (reinforces "you're barely alive"). At Surviving, normal rate. At higher tiers, multiply SpreadsPerTick by the vigor root-spread multiplier.

13. Consider Vigor decay rate scaling with current Vigor

Flat -4/sec decay means dropping from 100→90 takes the same time as 20→10, but the impact is wildly different. Consider slight exponential decay: decayRate = 2 + (Vigor / 33) — at high vigor you decay at ~5/sec (harder to maintain peak), at low vigor you decay at ~2.5/sec (you won't hit zero as fast, reducing death spirals). This creates a natural equilibrium where casual play stabilizes around Surviving/Thriving.

Further Considerations
Vitality regen on territory (+3/sec) vs. vigor decay (-4/sec): A player standing still on territory heals Vitality but bleeds Vigor. This is correct — it prevents camping. But make sure there are always creatures within reach of the origin tree so a player at Withering with low Vitality has something to eat nearby. Consider spawning guaranteed grubs within the first 5-tile radius of the tree.

HUD for Vigor: "No explicit bar" is a bold choice. I'd recommend at minimum a tier-name text flash when transitioning ("THRIVING" fades in, stays 1.5s, fades out). The blob size difference alone may not register to players who haven't internalized what baseline looks like. Screen saturation is excellent for feel but poor for conscious decision-making.

Vigor tier transitions should have hysteresis: If a player hovers at 40-41 Vigor, they'll flicker between Surviving and Thriving every frame. Add a 2-point buffer: transition UP at the threshold, but transition DOWN at threshold-2. So you enter Thriving at 41 but don't drop back to Surviving until 39.

Want me to formalize this into an implementation plan?