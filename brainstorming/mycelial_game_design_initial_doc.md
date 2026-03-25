# MYCORRHIZA — Game Design Concept Document

## Elevator Pitch

**You are the rot beneath the forest.** A sentient fungal network spreading through a dying woodland, growing tendrils into the earth to harvest strange organisms, hijacking creatures into your hivemind, spawning mutant fruiting bodies to fight for territory, and evolving your mycelium across runs to become something the forest has never seen. Infect. Spread. Consume. Evolve. A top-down atmospheric horror roguelite with FTL-style tactical invasion combat, Mewgenics-depth mutation breeding, Plague Inc-inspired contagion mechanics, and a "fishing into the deep" progression system — all wrapped in a Darkwood-inspired aesthetic of dread, darkness, and decay. And eventually, the humans in the forest will notice what you are. By then, you'll be ready for them.

---

## Core Fantasy

The player is not a person. They are the network — an ancient fungal consciousness awakening beneath a forest that is being consumed by something worse than them. You don't walk; you spread. You don't fight directly; you grow organisms that fight for you. You don't recruit allies; you *infect* them, hijack their bodies, and add them to your hivemind. You don't fish with a rod; you send tendrils deep underground, pulling up things that should have stayed buried — or seeding caves with spores to claim what lives down there.

The forest floor is your ocean. Depth is literal — the deeper your root network reaches, the stranger and more dangerous things you find. And something down there is sending things *up*.

Above ground, the forest is an ecosystem to consume. Insects. Animals. Rival fungi. And eventually — the humans who camp among the trees, who will notice the spreading rot, who will fight back with fire and steel. Until they too become part of you.

---

## Visual & Tonal Identity

### Aesthetic: Darkwood meets fungal biology

**Perspective:** The game uses two complementary 2D views:
- **Surface View (Top-Down):** Darkwood-style. Limited "awareness radius" defined by your mycelium network itself. You can only see where your hyphae have spread. Unexplored areas are shrouded in a fog of rich, dark soil tones. As you grow, your vision literally expands — but so does the surface area that hostile organisms can attack. This is where forest exploration, surface combat, human interactions, and territorial expansion happen
- **Depth View (Side-On Cross-Section):** Terraria-style. A vertical slice of the underground world showing your tendrils pushing through hundreds of meters of procedurally generated biomes. Caves, underground lakes, creature dens, and increasingly alien geology are all visible. The player switches between views freely — the other view doesn't pause, creating constant tension between surface and depth management

Both views share one screen. The switch should feel seamless — like turning your head from looking across the forest floor to looking down into the earth beneath your feet.

**Art style:** Dark, muted palette. Earthy browns, deep greens, bioluminescent blues and sickly yellows for your fungal growths. The forest above is rendered in oppressive detail — gnarled roots, rotting logs, pools of stagnant water. Everything feels damp and alive. Character portraits for NPCs (other organisms, ancient fungi, parasitic intelligences) should be grotesque but fascinating, like Darkwood's unsettling character art.

**Horror approach:** No jump scares. Atmospheric dread through sound design, limited information, and the creeping realization that the things you're pulling up from the deep are *aware* of you. The horror is in what you become as much as what you face.

**Sound:** Wet organic sounds. Creaking wood. The slow pulse of underground water. Distant rumbles from below. Your fruiting bodies make unsettling vocalizations when they fight. Music should be sparse and textural — think Darkwood's ambient score meets the wet biology of Hollow Knight's deeper areas.

---

## The Three Pillars of Gameplay

### PILLAR 1: "Deep Tending" — The Fishing-Into-Depth Mechanic

This is your primary resource gathering loop, inspired by your fishing concept and Dredge's progression-through-depth system.

**How it works:**

Each run, your mycelium hub sits at the surface level of a procedurally generated underground world. You can extend **tendrils** (root-like probes) downward into the earth. At the start, you can only reach shallow depths. But this world goes DEEP — hundreds of meters of procedurally generated underground biomes, each with distinct ecosystems, geology, creatures, and horrors. If you play well and invest in downward growth, your network can spread to absurd depths. The underground is not a fishing hole — it's a second world.

**The Underground Biomes:**

The underground is procedurally generated each run with the following biome layers. Exact depths vary per run, keeping exploration fresh, but the general order is consistent:

**LAYER 1: The Topsoil (0–15m)**
*"Where the dead things feed the living."*
- **Terrain:** Loose dark earth, root tangles from surface trees, decomposing leaf matter, small pockets of groundwater
- **Fauna:** Earthworms, beetle larvae, centipedes, mole crickets, burrowing spiders, nematodes. All mundane. All easy to harvest or infect
- **Resources:** Basic nutrients, common spore types, simple mutagens
- **Caves:** Shallow burrow networks. Mole tunnels. Ant colonies that can be hijacked wholesale for an early-game swarm army
- **Threat Level:** Minimal. This is your pantry and training ground
- **Mood:** Familiar. Damp. The sound of roots creaking and insects chittering. Feels like normal earth — for now

**LAYER 2: The Root Maze (15–60m)**
*"The trees know something is wrong. They're reaching deeper than they should."*
- **Terrain:** Dense root systems from ancient trees, some impossibly deep. Clay deposits. Underground streams. Pockets of stale air
- **Fauna:** Blind moles, root-boring beetles the size of your fist, cave crickets, pale salamanders, fungus gnats (ironic — they eat fungi, making them a genuine threat). Territorial burrowing snakes
- **Resources:** Rare mineral deposits, dormant seed pods containing ancient plant DNA (useful mutagens), crystallized tree sap with preservative properties
- **Caves:** Proper caverns begin appearing. Some are flooded. Some contain the remains of animal dens — bones, fur, the smell of something that lived here once. Occasionally you find root chambers where trees have grown hollow underground rooms. These make excellent network outposts
- **Threat Level:** Low to moderate. Things fight back here. Burrowing snakes can sever thin tendrils. Fungus gnats swarm and eat your hyphae if you grow too aggressively
- **Mood:** The first sense that you're leaving the normal world behind. Root systems twist in patterns that look almost deliberate. You find things preserved in clay that shouldn't be this deep

**LAYER 3: The Wet Dark (60–150m)**
*"Water remembers everything that has ever dissolved into it."*
- **Terrain:** Saturated stone. Underground rivers and lakes. Dripping limestone caverns. Clay that shifts and flows like it's alive. Bioluminescent mineral veins that pulse with a slow rhythm
- **Fauna:** Blind cave fish (excellent infection targets — they travel through water systems and spread your spores passively), cave crayfish, translucent cave spiders, albino bats in massive roost colonies (hundreds — infect one and it returns to the roost), cave-adapted frogs that hunt by vibration, colonies of cave millipedes that form living carpets across cavern floors
- **Resources:** Strange fossils (warm to the touch — something was alive when it was fossilized), mutagen compounds dissolved in the water (harvesting these contaminates the water system, which can be strategic or catastrophic), phosphorescent minerals for Lumina-class fruiting body upgrades
- **Caves:** Enormous cavern systems. Underground lakes you can't see the other side of. Waterfalls that roar in the darkness. Bat colonies that blot out your awareness when they move. Some caves have their own weather — fog, dripping condensation, air currents that carry spores unpredictably
- **Threat Level:** Moderate. The bat colonies are the first serious infection opportunity — or the first serious threat if they swarm your tendrils. Cave fish carry diseases that can infect YOUR network if you're not careful. The water itself is both highway and hazard — Hydrophilic strains thrive here, others struggle
- **Mood:** Beautiful and unsettling. The bioluminescence makes everything feel like a fever dream. Sounds carry strangely — you hear things from very far away but can't tell direction. The first time you find a fossil that's warm, you realize the underground is not empty geology. Something is generating heat below

**LAYER 4: The Bone Strata (150–300m)**
*"Not all of this is rock."*
- **Terrain:** The geology shifts. Mixed with the stone are layers of calcite that, on closer inspection, are compressed bone. Ribs the size of tunnels. Teeth embedded in cliff faces. Whatever died here was enormous, and there were many of them. The stone itself is organic — calcium carbonate from millions of years of accumulated shells, skeletons, and carapaces
- **Fauna:** This is where things get strange. Cave organisms here have been living in proximity to the bone strata for generations, and they've changed. Insects with exoskeletons made of bone. Fish with too many jaws. Eyeless mammals that navigate by tasting the air with tongues covered in sensory papillae. Predatory worm colonies that hunt as a collective — each worm is small but they share a primitive nervous system through chemical signals (sound familiar?). Territorial "bone crabs" that build shells from the fossilized remains around them
- **Resources:** Ancient biological compounds unavailable anywhere else — prion-like proteins that enable radical mutations, calcium-based armor compounds, neural tissue preserved in bone marrow (when processed, this grants your network new cognitive abilities — faster hivemind response, more simultaneous direct-control slots)
- **Caves:** The caverns here aren't geological — they're anatomical. You're exploring inside the ribcage of something that died so long ago the earth grew around it. Some "caves" are hollow skulls. Some "tunnels" are ossified blood vessels. The scale is incomprehensible — whatever these things were, they make whales look like minnows
- **Threat Level:** High. The bone crabs are armored and aggressive. The collective worm colonies can overwhelm tendrils through sheer numbers. Some organisms here have a primitive immune response to fungal infection — they've been fighting fungi for millennia. Parasitic Takeover attempts fail more often, and failed attempts trigger more violent responses
- **Mood:** Awe and creeping dread. The realization that these bones go on FOREVER. That the entire underground world you've been exploring is built on a graveyard of titans. And the deeper question: what killed them?

**LAYER 5: The Thermovents (300–500m)**
*"The earth has a pulse. You can feel it now."*
- **Terrain:** Volcanic activity. Thermal vents pumping superheated water and mineral-rich steam. Basalt columns. Obsidian formations. Rivers of water so hot they glow. Pockets of toxic gas. The temperature fluctuates wildly — scalding near vents, freezing in dead zones between them
- **Fauna:** Extremophile organisms unlike anything on the surface. Thermophilic bacteria mats that form living terrain (your mycelium can merge with these if you have the right affinity). Armored tube worms clustering around vents. Magma salamanders — amphibians adapted to near-boiling water, their skin thick and heat-radiating. Swarms of heat-loving insects that build hives from solidified mineral deposits. Apex predators here are "thermal lurkers" — organisms that lie dormant in cooling lava until something disturbs them, then erupt with terrifying speed
- **Resources:** Heat-forged mutagens (the most potent in the game — these enable mutations that are impossible at shallower depths). Rare mineral compounds that supercharge Galvanic and Thermophilic affinity upgrades. Geothermal energy that can be harnessed to accelerate your network's growth rate across ALL depths if you establish a tendril connection back to the surface
- **Caves:** Lava tubes — long, smooth tunnels carved by ancient magma flows. Some are still active. Some contain pockets of ancient atmosphere with bizarre gas compositions. Vent chambers where the heat is so intense your tendrils take damage just existing there — you need heat-resistant upgrades or Thermophilic affinity to survive. Crystal grottos formed by mineral deposition over millions of years, containing compounds found nowhere else
- **Threat Level:** Very high. The environment itself is hostile. Non-Thermophilic networks need significant upgrades to even survive here, let alone spread. Thermal lurkers are mini-boss tier enemies. The tube worm colonies are passive until disturbed, then defend their vent territories with chemical sprays that dissolve organic matter
- **Mood:** Hellish beauty. The glow of thermal vents in underground darkness. The sound of the earth breathing. Everything here feels primal and dangerous. Thermophilic strains feel at home. Everything else feels like it's trespassing

**LAYER 6: The Mycelial Graveyard (500–800m)**
*"You are not the first network to reach this deep. You are not the hundredth."*
- **Terrain:** The stone here is threaded with fossilized mycelium — ancient fungal networks that grew this deep long before you and died. Their remains have partially mineralized, creating a labyrinth of petrified hyphae the width of tunnels. In some places, the old mycelium isn't fully dead — it twitches. It responds to your presence. It remembers
- **Fauna:** Organisms here have co-evolved with ancient fungi for so long they're essentially hybrid creatures — part animal, part fungus, existing in a state that blurs the line between infected and symbiotic. Mycelial centipedes that are 50% fungal tissue. Spore-breathing moths that pollinate underground fungal forests (yes — there are underground fungal forests down here, glowing in the dark, and they are not yours). Parasitic entities that specifically prey on fungal networks — they have evolved to eat YOU. "Memory slugs" — organisms that consumed ancient mycelium and somehow retained fragments of those networks' experiences. If you absorb one, you get flashes of what the old networks saw before they died
- **Resources:** Ancient spore banks — genetic material from extinct fungal species with mutations that no longer occur naturally. These are the most valuable breeding resources in the game. Fossilized network nodes that, when absorbed, grant permanent upgrades to your own network architecture. And the memories — fragmented, disturbing glimpses of what killed the old networks
- **Caves:** The fossilized mycelium creates organic cathedral-like spaces. Underground fungal forests that glow with their own bioluminescence — some of these are neutral, some are remnants of old networks that will treat you as an invader. Hollow chambers where ancient networks stored their most valuable spores, now guarded by organisms that have been protecting this dead treasure for millennia
- **Threat Level:** Extreme. The fungal predators here have literally evolved to destroy networks like yours. The semi-living old mycelium can interfere with your growth, creating dead zones where your hyphae can't spread. Some memory slugs carry corrupted data that can introduce bugs into your hivemind — infected creatures start behaving erratically or turn hostile
- **Mood:** Deeply unsettling recognition. You're looking at what you could become — what you WILL become if you fail. The old networks were vast, powerful, and they still died. The memory fragments hint at something deeper that consumed them. And the fossilized mycelium occasionally pulses in patterns that look almost like it's trying to communicate

**LAYER 7: The Deep Rot (800–1200m)**
*"Down here, the distinction between alive and dead loses all meaning."*
- **Terrain:** Reality gets soft. The stone is warm and yielding. Tunnel walls contract rhythmically, like breathing. Gravity feels inconsistent — your tendrils grow in directions that don't make geometric sense. The geology contains structures that look engineered but on a scale and with a logic that isn't human or even terrestrial. Crystalline formations that hum at frequencies your network can feel but not interpret
- **Fauna:** "Organisms" is a generous word. Things down here exist in states between living and mineral. Entities made of compressed sediment that move with intention. Crystalline structures that respond to stimuli. Flesh-stone hybrid creatures that might be organs of something larger — you find what appears to be an eye the size of a room, embedded in the cave wall, and it tracks your tendril as it passes. Creatures from upper layers that wandered down and were transformed — you find a bat colony that has fused into a single organism, still echolocating, its hundred mouths opening and closing in sync
- **Resources:** Compounds that break the rules of your mutation system — mutagens that allow combinations that shouldn't be possible, creating fruiting bodies with abilities from multiple base types simultaneously. Neural-analogue crystals that expand your hivemind's processing capacity. And occasionally, messages. Chemical signals in the stone that translate into something like language. They say: "deeper"
- **Caves:** It's hard to tell what's a cave and what's an interior space of something alive. Chambers pulse. Corridors redirect themselves between visits. You map an area and the map is wrong the next cycle. Some spaces are clearly artificial — carved with precision by something with intelligence and purpose. Others are organic cavities in what you're increasingly certain is a living thing you're moving through
- **Threat Level:** Extreme. Everything here can damage your network in ways that surface threats can't. Infections go both ways — things down here can infect YOUR mycelium, corrupting sections of your network and turning them against you. Some entities are so alien that your parasitic takeover literally doesn't work — your spores can't find biological processes to hijack
- **Mood:** Cosmic horror. The scale shift from "I'm a fungus in a forest" to "I'm a fungus inside something that makes the forest look like a skin cell." The memory slugs' warnings make sense now. This is what killed the old networks. Not a creature. Not a disease. A place. A living geology that digests anything that grows too deep

**LAYER 8: The Below (1200m+)**
*[REDACTED — endgame content]*
- What is known: the stone stops. Something else begins. Your tendrils, if they survive long enough, report sensations your network has no framework to interpret. The old networks' memories end here — not because they stopped recording, but because what they recorded doesn't translate into anything your consciousness can process
- What is suggested: The Below is not empty. It is aware. It has been aware of you since you first sent a tendril past the Topsoil. It has been patient. It is no longer patient
- What the player experiences: To be determined through development. The mystery IS the design. The Below should be the thing players talk about, theorize about, argue about. It should not be fully explained. Ever

**The Tendril Mechanic:**

Sending a tendril down works like this:

1. **You choose a "bore point"** on your territory map — this is where the tendril enters the earth
2. **The tendril extends in real-time** — you watch it push through procedurally generated underground terrain, hitting rocks, pockets of water, existing root systems
3. **When it contacts something**, you get a notification — a vibration, a visual pulse through your network. You can choose to **absorb** it (pull it up) or **retract** (leave it alone)
4. **Deeper = better rewards but higher risk.** At shallow depths, things come up easily. At deeper levels, organisms resist extraction, can damage or sever your tendril, or — worst case — follow the tendril back UP to your network

**Parasitic Takeover — Seeding the Depths:**

Tendrils don't just pull things up. When your tendril reaches an underground cave, tunnel, or nest, you can **project spores** into that space — infecting creatures rather than harvesting them. This turns Deep Tending from a one-way pantry into a two-way colonization system.

How takeover works:

1. **Your tendril reaches a cave or underground chamber.** The cave is revealed on your underground map — you can see the creatures inside
2. **You fire a spore payload** from the tendril tip into the chamber. This costs biomass and a spore from your Spore Bank
3. **The spore attempts to infect a target creature.** Success depends on your tendril's **Parasitic Potency** upgrade level vs. the creature's resistance. Small cave insects = easy. A blind cave predator = much harder. Something from The Below = you'll need serious upgrades
4. **If successful, the creature joins your hivemind.** You get two options:
   - **Direct Control:** You pilot the creature yourself. Useful for scouting deeper caves, fighting threats underground before they reach your network, or harvesting resources in dangerous areas. But you can only directly control one creature at a time, and your attention is split from surface operations
   - **Autonomous Hivemind:** The creature acts on its own with broad behavioral directives you set (patrol, harvest, defend, scout deeper). It's less precise but frees you up. Hivemind creatures gain mutations over time from your fungal infection — they become stranger, stronger, and less recognizable as what they once were

5. **Failed takeover attempts alert the target** and everything nearby. A botched infection deep in the cave system can trigger a retaliatory swarm heading UP your tendril toward your network

**Why this matters strategically:**

- A hijacked blind cave fish in the Wet Dark can travel through water systems and scout connected caverns you haven't reached with tendrils yet
- Hijacked bat colonies in the Wet Dark become massive infection vectors — hundreds of bats returning to roost, each carrying your spores
- Hivemind creatures stationed in cave chambers act as early-warning systems — they'll fight anything that tries to climb your tendrils
- Some creatures have unique abilities (echolocation, bioluminescence, burrowing, heat resistance) that your fruiting bodies can't replicate. Hijacking them gives you capabilities you can't breed
- In late-game, seeding the Deep Rot or The Below with hijacked organisms may be the only way to gather intelligence about what's down there without sending your core network into lethal territory
- A fully colonized underground biome — tendrils threaded through every cave, hijacked creatures patrolling every tunnel — is one of the most satisfying achievements in the game. You're not just growing down. You're *claiming* the earth

**The Quota System (from your fishing concept):**

Your fungal network has a biological imperative — a **Hunger** meter that depletes each cycle (day). You need to harvest enough biomass from tending to feed your network and your fruiting bodies. If Hunger hits zero:

- Your weakest fruiting bodies begin to wither and die
- Your territory contracts (outer hyphae die off, shrinking your vision)
- Eventually, your core starves and the run ends

The Hunger quota scales each cycle. Early cycles are forgiving. By mid-run, you're forced to go deeper than is safe. By late-run, you NEED things from the Deep Rot to survive, and those things don't come quietly.

**Upgrade progression (fishing rod equivalent):**

- **Tendril Thickness** — thicker tendrils resist severing and can pull up larger organisms
- **Tendril Length** — how deep you can reach
- **Sensory Nodes** — lets you "feel" what's at a depth before you reach it (like sonar)
- **Barbs** — tendrils that grip prey so they can't escape during extraction
- **Lure Glands** — secretions that attract specific organism types at specific depths

**The "Creepy Finds" Escalation:**

The underground biomes are designed so that each layer feels like a meaningful escalation in strangeness. The Topsoil is mundane. The Root Maze is unsettling. The Wet Dark is beautiful and wrong. The Bone Strata is awe-inspiring and dreadful. The Thermovents are hellish. The Mycelial Graveyard is a mirror. The Deep Rot breaks reality. And The Below is unknowable.

Each biome has unique "discovery events" — one-time finds that tell the story of what happened underground:

- In the Root Maze, you find root systems growing in geometric patterns that no tree would produce naturally
- In the Wet Dark, fossils that are warm to the touch. Something was alive when it was fossilized — or it still is
- In the Bone Strata, a jawbone with teeth that match no known species, and the teeth are growing
- In the Mycelial Graveyard, memory slugs that show you what the old networks saw before they died — fragmentary, terrifying images of something rising
- In the Deep Rot, things that *talk* to your network. Things that remember being something else. Things that know your name — a name you didn't know you had

The player should feel a constant pull between "I need to go deeper for resources" and "I am genuinely unsettled by what I'm finding down here." That tension is the game.

---

### PILLAR 2: "Sporefront" — Tactical Invasion Combat

This is where FTL's crew combat meets Mewgenics' chaotic mutation system, reframed through fungal biology.

**The Setup:**

Your territory will periodically be invaded by hostile organisms — forest creatures corrupted by whatever is happening to the woods, rival fungal networks, parasitic colonies, and things from the deep. Conversely, YOU can invade other organism colonies to steal their biomass and territory.

**Your Units: Fruiting Bodies**

Instead of crew members, you grow **fruiting bodies** — specialized fungal organisms that you deploy for combat. Each one is grown from spores you've collected (from Deep Tending or from previous combat), and each one comes with randomized traits.

A fruiting body has:

- **A Base Type** (equivalent to Mewgenics' classes): Sporecaster (ranged), Strangler (melee/grapple), Bloater (tank/explosive), Mycotoxin (debuffer/poison), Lumina (support/healer), Rhizomorph (fast scout/flanker)
- **Mutations** (random at growth, inherited from parent spores): These are the Mewgenics-style randomized abilities. Could be anything — extra limb tendrils, bioluminescent flash (stuns), spore cloud on death, parasitic attachment, acid secretion, camouflage, tremorsense, etc.
- **Disorders/Quirks** (negative traits that come with mutations): Photosensitive (weaker in lit areas), Unstable (might explode prematurely), Cannibal (attacks your own units if hungry), Brittle (one-hit kills it but it shatters into damaging fragments)

**Combat Flow — FTL-Style but Faster:**

Combat plays out on a **grid map** representing the invasion target (an enemy nest, a corrupted grove, a rival network's territory). Unlike FTL's pausable real-time, this is **real-time with a slow-down button** (think Superhot-lite — time moves faster when you give commands, slower when you're planning).

1. **Deployment Phase** (5 seconds): You choose which fruiting bodies to send and where to place them at the entry points
2. **Invasion Phase**: Your fruiting bodies push into the target. You give broad commands — move to room/zone, attack target, use ability, retreat — but they have **autonomous behaviors** based on their mutations and quirks. A Cannibal unit might turn on an ally. An Unstable unit might charge a group of enemies and detonate
3. **The enemy fights back**: Each enemy colony has its own "rooms" (nest chambers, defensive nodes, the core). Enemies have their own random mutations. A single enemy type you've fought before might have completely different abilities this time
4. **Systemic interactions**: This is the Mewgenics magic. Your acid-secreting unit melts through a wall, flooding the next chamber with caustic fluid. Your Sporecaster releases a cloud that your Lumina accidentally ignites with its bioluminescence, creating a flash-fire. An enemy's water-based defense interacts with your Mycotoxin's poison to create a toxic flood that damages everyone. The terrain itself matters — wood rots faster under your influence, stone doesn't, water carries your spores but also dilutes your toxins

**How this differs from FTL (addressing your critiques):**

| FTL | Mycorrhiza |
|-----|-----------|
| Pausable real-time, can be slow | Real-time with slow-mo, always pressure |
| Crew are broadly similar (stat differences) | Every fruiting body is wildly different due to mutations |
| Predictable enemy crews | Enemy mutations are random every encounter |
| Boarding is methodical — target systems, retreat to medbay | Invasion is chaotic — your units have autonomous behaviors you can't fully control |
| Rewards for killing crew = more scrap | Rewards for killing = you ABSORB their biomass and can inherit their mutations into your spore pool |

**The FTL boarding fantasy, preserved:**

The core of what you love about FTL boarding is here — you're sending YOUR organisms into THEIR territory, fighting room by room, trying to neutralize threats and steal everything. But it's faster, more chaotic, and every single encounter feels different because of the mutation system.

**Post-combat:**

- Surviving fruiting bodies return with experience, new mutations (from absorbing enemy biomass), and possibly injuries/new disorders
- Dead fruiting bodies drop spores that go into your **Spore Bank** — these carry a random subset of the dead unit's traits and can be used to grow new fruiting bodies (the Mewgenics breeding loop)
- You can choose to **colonize** the defeated territory, expanding your network (more vision, more bore points for tending, but also more territory to defend)

---

### PILLAR 3: "The Breeding Rot" — Mutation & Evolution Meta-Game

This is the Mewgenics-inspired between-combat progression system that ties everything together.

**The Spore Bank:**

Every fruiting body that lives or dies contributes to your **Spore Bank** — a genetic library of traits, mutations, and base types. Between combat encounters, you manage this bank:

- **Crossbreeding**: Combine two spore profiles to create a new fruiting body. The offspring inherits traits from both parents randomly (like Mewgenics' cat breeding). A Strangler's grapple tendrils + a Bloater's explosive sacs might create a unit that grabs enemies and detonates, killing itself and the target
- **Mutagen Injection**: Use rare compounds from Deep Tending to force new random mutations onto a spore. High risk — could give incredible abilities or crippling disorders
- **Selective Pruning**: Sacrifice fruiting bodies to remove unwanted traits from their spore line. Cruel but necessary
- **The Compost Heap**: Throw unwanted spores and dead fruiting bodies here. Over time, the compost generates random "wild" spores with completely unpredictable trait combinations — your lottery ticket system

**Why this works (learning from Mewgenics' strengths and addressing its weaknesses):**

Mewgenics' breeding is incredible but the turn-based combat can feel slow. Here, the breeding feeds directly into fast-paced real-time combat, so you immediately FEEL the impact of a great breed. That parasitic-exploding-acid-spraying monstrosity you just created? You're deploying it in the next invasion and watching the chaos unfold in real time.

The randomness is the point — like you said about Mewgenics, every "cat" (here, every fruiting body) feels completely unique. But the real-time combat ensures there's always tension and risk on the line, addressing your critique about Mewgenics' pacing.

---

### PILLAR 4: "Strain Evolution" — The Elemental Affinity Skill Tree

Each run, your fungal network develops along an **evolutionary path** — a skill tree that defines your strain's fundamental nature. This isn't just stat bonuses; it changes how your network interacts with the world at a systemic level.

**The Core Affinities:**

At key moments during a run (after major victories, after reaching new depth layers, after consuming enough biomass), you're offered **Strain Mutations** — permanent upgrades for the current run that push you toward an elemental identity.

**Hydrophilic (Wet Affinity):**
- Your mycelium spreads 50% faster through waterlogged soil, marshes, and along underground water channels
- You spread 20% slower in dry, arid, or rocky terrain
- Your tendrils can travel through water pockets instantly, reaching distant cave systems
- Fruiting bodies gain water-based mutation bonuses — spore clouds travel further in humidity, toxins dissolve into water systems to spread passively
- You're more proficient at taking over aquatic and amphibious organisms (cave fish, salamanders, water insects)
- **Vulnerability:** Fire is devastating. Burning attacks deal double damage to your network, and you can't spread through scorched ground at all

**Thermophilic (Heat Affinity):**
- Your mycelium thrives in warm, dry terrain — volcanic soil, sun-baked clearings, near underground thermal vents
- You spread 20% slower in cold, wet environments
- Your fruiting bodies run hotter — melee attacks inflict burn damage, and dead units leave behind smoldering patches that damage enemies
- Deep Tending near thermal vents yields rare heat-forged mutagens unavailable to other strains
- Better at infecting warm-blooded creatures (mammals, birds — and eventually, humans)
- **Vulnerability:** Water-based attacks extinguish your thermal advantage and can flood your hyphae

**Galvanic (Electric Affinity):**
- Your mycelium conducts bioelectric impulses — your network communicates faster, meaning hivemind creatures respond more quickly and your awareness radius updates in real-time instead of with a slight delay
- You spread along mineral-rich veins and metallic deposits in stone
- Fruiting bodies can discharge electric pulses — stunning enemies, short-circuiting rival networks, and chaining damage through wet terrain or metal objects
- Your tendrils act as lightning rods underground, attracting and absorbing static charges that power devastating abilities
- **Vulnerability:** Your conductivity works both ways. Enemy electric attacks chain through your network, potentially damaging distant parts of your territory

**Lithic (Stone Affinity):**
- Your mycelium can slowly penetrate and grow through rock, opening paths other strains can't reach
- You spread slower on the surface but your underground network is incredibly resilient — harder to sever, harder to burn out
- Fruiting bodies are dense and armored. They hit harder in melee and take more punishment, but move slower
- Your tendrils can bore through stone layers that block other strains, giving you access to deeper caves earlier
- **Vulnerability:** You're slow. Enemies that use hit-and-run tactics or that spread faster than you can overwhelm your territory before you can respond

**How affinities interact with the map:**

Each procedurally generated forest has a terrain composition — some runs spawn you in a boggy wetland (Hydrophilic paradise), others in a rocky highland (Lithic advantage). Part of the strategic depth is adapting your strain evolution to the terrain you're given, or doubling down on your strengths and finding ways to mitigate the weaknesses.

Affinities aren't locked choices — they're weighted. You can take mostly Hydrophilic mutations but grab one or two Galvanic ones for the electric stun ability. But the deeper you go into one affinity, the stronger its bonuses AND vulnerabilities become. A fully Thermophilic network is terrifyingly powerful in dry terrain but a single rainstorm event could be catastrophic.

---

### PILLAR 5: "The Spreading Sickness" — Contagion & Infection System

Inspired by Plague Inc, this system governs how your fungal infection spreads beyond your direct mycelium network — through the air, through water, through the bodies of living things.

**Infection as a Mechanic:**

Everything in the forest is a potential host. Your network doesn't just grow through soil — it produces **spores** that can infect living organisms. Infected creatures don't immediately join your hivemind (that requires the stronger Parasitic Takeover from Deep Tending). Instead, infection is subtler and more insidious:

**Infection Stages:**

1. **Exposure:** A creature passes through your territory, breathes your spores, or eats contaminated food. They don't know anything is wrong yet
2. **Incubation:** The infection takes hold internally. The creature behaves normally but is now a carrier — spreading spores to other creatures it contacts. You can see infected creatures on your map with a subtle visual indicator
3. **Symptomatic:** The creature begins behaving erratically. Insects swarm toward your network instead of away. Animals become docile near your territory. Their bodies begin showing fungal growths
4. **Thrall:** Full infection. The creature is now functionally part of your network. It can be issued commands, fights on your behalf, and its body is visibly overtaken by fungal growth. When it dies, it bursts into a spore cloud that can infect everything nearby

**Contagion Upgrades (Plague Inc-style skill tree):**

These upgrades are purchased with biomass and mutagen, separate from Strain Evolution:

*Transmission Path:*
- **Airborne Spores** — Your infection spreads through wind. Creatures downwind of your territory have a passive chance of exposure each cycle
- **Waterborne** — Your spores survive in water. Any creature that drinks from a contaminated stream or pond gets exposed
- **Contact Transmission** — Infected creatures spread the infection to anything they physically touch
- **Vector Insects** — Infected insects actively seek out and bite uninfected creatures, injecting spores. Turns your hijacked bugs into delivery systems
- **Soil Saturation** — Your mycelium secretes infectious compounds into the soil itself. Anything that burrows or walks on bare earth in your territory gets exposed over time

*Symptom Path:*
- **Docility** — Symptomatic creatures become passive near your territory, making them easier to absorb or ignore
- **Aggression** — Symptomatic creatures become hostile to uninfected creatures, causing chaos among enemy factions
- **Explosive Death** — When an infected creature dies, the spore burst radius and potency is dramatically increased
- **Fungal Armor** — Thralls grow hardened fungal plates, making them tankier in combat
- **Mycelial Link** — Thralls generate a small mycelium patch wherever they stand, extending your network's awareness without direct growth
- **Contagious Corpse** — Dead thralls don't just burst — their bodies remain as persistent infection zones, slowly converting the soil around them

*Resistance Path:*
- **Immune Evasion** — Your infection is harder for organisms to fight off. Faster progression from Exposure to Thrall
- **Dormancy** — If a host's immune system starts winning, the infection goes dormant instead of dying. It reactivates when the host is weakened
- **Adaptation** — Each time your infection fails against a species, it mutates slightly. The next attempt against that species has a higher success rate

**The Strategic Layer:**

The contagion system means you're not just growing outward through soil — you're seeding the forest with invisible infections that pay off cycles later. An animal you infected on Cycle 3 might not become a Thrall until Cycle 7, but when it does, it's deep in enemy territory and suddenly you have an agent behind their lines.

This also creates emergent narrative moments. A deer infected early in the run wanders into a human camp. The humans don't notice. Three cycles later, half the camp is coughing. Two cycles after that...

---

## THE HUMAN FACTION — "The Wardens"

Humans are the final and most dangerous surface-level faction. They don't appear immediately — they're a mid-to-late game escalation that fundamentally changes how you play.

**The Escalation Timeline:**

**Cycles 1–5 — Ignorance:**
Humans exist in the forest as background elements. You can see their campfires through the canopy, hear distant conversation, find their trash and abandoned gear. They're hikers, campers, forest rangers. They don't know you exist. Their camps are visible on your map but they're not a threat or a target — yet.

**Cycles 6–10 — Suspicion:**
As your network expands and the forest visibly changes (trees dying, animal behavior shifts, patches of bioluminescent growth), humans start investigating. You'll see:
- Rangers marking infected trees with paint
- Research teams setting up equipment near your territory
- Dogs brought in that can detect your mycelium underground (early-warning for the player that humans are getting close)
- Controlled burns at the edges of your territory — small but targeted

**Cycles 11–15 — Active Containment:**
The humans now understand something is very wrong. They escalate:
- **Firebreaks** — They burn corridors through the forest, severing sections of your mycelium network. If they cut off a section from your core, that section dies (losing all fruiting bodies and bore points in it)
- **Chemical Agents** — Fungicide sprayed from backpacks or small vehicles. Kills surface-level mycelium and weakens your fruiting bodies in the affected area
- **Quarantine Zones** — Humans set up barriers and checkpoints. Infected animals that wander near are killed and burned, removing them from your infection pool
- **Armed Response** — If your fruiting bodies are spotted, humans shoot them. They're smart, they use ranged weapons, they retreat and regroup, they call for backup. They don't fight like insects or rival fungi — they fight like *people*

**Cycles 16+ — Total War (or Total Infection):**

This is where the player's earlier choices pay off. If you've invested in the Contagion system, you have options:

**The Infection Route:**
- Humans who've been exposed to your spores begin showing symptoms. The camp's cohesion breaks down
- Symptomatic humans make mistakes — leaving gates open, dropping equipment, wandering away from groups
- Thrall humans are the most powerful units in the game. They retain their intelligence, tool use, and social knowledge but serve your network. A Thrall ranger can sabotage containment efforts from inside. A Thrall researcher can disable fungicide equipment
- The horror of this should not be understated from a narrative perspective. These were people. The player made them into this. The game doesn't comment on it — it just lets you see what you've done

**The Brute Force Route:**
- If you've invested in combat mutations and Strain Evolution instead of Contagion, you can fight the humans directly with overwhelming force
- Fruiting bodies bred for anti-human combat — acid sprayers to melt equipment, Bloaters to destroy structures, Rhizomorphs fast enough to chase fleeing humans
- This is harder and costlier than infection but faster and more controllable

**The Avoidance Route:**
- It's possible to play around humans — spreading underground, keeping surface presence minimal, growing through cave systems and waterways to expand without triggering escalation
- This is the stealth approach. It's slower but avoids the resource drain of fighting or infecting an intelligent, organized enemy
- Some players will find this the most terrifying route — the tension of knowing they're RIGHT THERE, looking for you, and you're hiding just below their feet

**What Humans Drop / Provide:**

- **Equipment** — Human tools and materials are foreign to your biology but can be repurposed. Metal becomes armor plating for fruiting bodies. Chemical compounds become new mutagen types. Batteries provide Galvanic energy
- **Knowledge** — Infected or Thrall humans give your network access to information it couldn't have otherwise. Map data. Awareness of human patrol routes. Understanding of fire and how to resist it
- **The Radio** — Late-game, if humans manage to radio for outside help, a timer begins. External forces will arrive in X cycles. This creates a hard deadline for the endgame — either you've consumed enough to survive what's coming, or the military burns the entire forest

---

## The Run Structure

A single run of Mycorrhiza plays out over **cycles** (equivalent to days):

### Each Cycle:

1. **Hunger Check** — Do you have enough biomass? If not, things start dying
2. **Deep Tending Phase** — Send tendrils down, harvest resources, pull up spores and mutagens. Manage risk vs. reward of going deeper
3. **Event** — Something happens. An invasion force approaches. A rival network sends an emissary. Something crawls up from the Deep Rot uninvited. A strange NPC organism offers a trade. These are procedurally selected from a large pool
4. **Combat/Exploration** — Either defend your territory, invade a target, or explore a new area of the forest floor
5. **Breeding Phase** — Manage your Spore Bank, grow new fruiting bodies, crossbreed, evolve
6. **Expansion** — Choose to grow your network into new territory (more resources but more vulnerability) or consolidate (safer but slower growth)

### Run Progression:

- **Early Cycles (1–5):** Learning the forest. Shallow tending. Weak enemies. Building your first generation of fruiting bodies. Humans are distant background noise — campfire smoke through the trees
- **Mid Cycles (6–12):** Hunger pressure increases. You're forced deeper. Enemies mutate and get stranger. You start encountering other intelligent fungal networks with their own agendas. Humans begin investigating the changes in the forest — rangers appear at your territory edges
- **Late Cycles (13–18):** The Deep Rot is yielding things that change the game entirely. Powerful mutations but devastating risks. Boss-tier organisms. The forest above is visibly dying. Humans escalate to active containment — firebreaks, fungicide, armed response. Your contagion investments either pay off now or you're fighting a two-front war
- **Endgame (19+):** The Below opens. Whatever is down there is now aware of you. Humans are either consumed, contained, or calling for reinforcements. The final cycles are a desperate three-way tension between your network, The Below, and humanity's last stand

### Meta-Progression (Between Runs):

When a run ends (death or victory), you keep:

- **The Evolutionary Record** — A permanent unlock tree of mutations you've discovered. Once discovered, these can appear in future runs
- **Ancestral Spores** — You can save a small number of spore profiles to start future runs with. These degrade slightly each generation (preventing god-tier snowballing) but give you a head start
- **Forest Memory** — The procedural generation remembers broad strokes of your previous runs. A territory you colonized might still show fungal scarring. An enemy you defeated might have evolved in response
- **New Starting Configurations** — Unlock different "origin" types for your fungal network, each with different starting mutations and playstyles (like FTL's ship unlocks)

---

## NPCs & The World

### The Dying Forest

The forest is not just a backdrop — it's an ecosystem in collapse. Trees are rotting from the inside. Animals are behaving strangely. The soil chemistry is changing. Your fungal network is part of this ecosystem, and your choices affect it.

**Key NPC types:**

- **The Old Growth** — Ancient tree-root networks that communicate slowly. They remember what the forest was before. Some are allies. Some think you're part of the problem
- **The Hive Colonies** — Insect colonies with their own mutations and agendas. Can be traded with, absorbed, or warred against. Excellent early targets for infection — insect vectors spread your contagion efficiently
- **Rival Networks** — Other fungal intelligences. Some are cooperative (forming mycorrhizal partnerships for mutual benefit). Some are parasitic and will try to consume you. Their "personality" is randomized each run. Note: rival networks have their OWN strain affinities — a Thermophilic rival in a dry sector is a serious threat to a Hydrophilic player
- **The Deep Speakers** — Organisms from below the Root Layer that have been changed by proximity to The Below. They offer powerful mutagens and forbidden knowledge in exchange for... things you might not want to give
- **The Rot Surgeon** — A recurring NPC. A parasitic intelligence that claims to be a doctor. It can perform "operations" on your fruiting bodies — adding mutations, removing disorders, splicing traits between units. Its prices are steep and its motives are unclear. Sometimes the operations go wrong in interesting ways. (This is your Doctor Sturgeon analog — a creepy, transactional character who is simultaneously your best resource and your biggest threat)
- **The Wardens (Humans)** — Detailed in the Human Faction section above. The most intelligent and adaptable enemy in the game. They escalate their response based on your actions, coordinate with each other, use tools and fire, and are the only faction that can call for outside help. But they're also the most valuable hosts — a Thrall human is worth a dozen hijacked cave beetles

---

## Technical Scope & Feasibility

### Why this is achievable for a solo/small team:

- **Fully 2D with dual views** — Top-down for surface (Darkwood-style) and side-on cross-section for underground (Terraria-style). Both are well-understood rendering approaches with extensive Godot documentation. No 3D camera management, no 3D asset pipeline, no 3D lighting complexity
- **The dual-view system is simpler than it sounds** — Terraria already proved that massive procedurally generated 2D worlds with multiple biomes are achievable by small teams (Terraria was originally built by one programmer). The surface top-down view is a separate, smaller map. The two views share game state but render independently
- **Procedural generation is core** — Maps, biomes, mutations, enemy compositions, events. The underground biome system generates massive replayability. Each run produces a different geological profile — different cave layouts, different biome depths, different creature distributions. You build the generation rules once and get infinite worlds
- **Art style favors suggestion over detail** — Darkwood's pixel-adjacent style with limited animation for the surface. Terraria-style tile-based rendering for the underground. Fungal organisms and cave creatures are inherently abstract and forgiving to animate — a pulsing mass of hyphae doesn't need the animation fidelity of a human character
- **Systemic interactions are the content** — Like Mewgenics' 1050 abilities creating emergent gameplay, the mutation system generates novelty automatically. The underground biomes provide visual and mechanical variety through terrain and creature differences, not through hand-crafted content
- **Sound design carries the horror** — Cheaper than high-fidelity visuals and often more effective. Each underground biome should have a distinct soundscape that tells the player where they are even before they look

### Recommended engine: Godot

- Excellent 2D performance
- GDScript for rapid prototyping, C# or GDExtension for performance-critical systems (the mutation/genetics engine)
- Free, open-source, no royalties
- Growing community with strong 2D-focused documentation

### Estimated scope:

- **Prototype (3–4 months):** Core loop — tendril mechanic with basic parasitic takeover, basic combat with 3 unit types and ~10 mutations, one enemy type, Topsoil and Root Maze biomes only, one affinity. No humans yet. Prove the dual-view (top-down + side-on) works and feels good
- **Vertical slice (6–8 months):** Full cycle loop, breeding system, 6 unit types, ~50 mutations, 3 enemy factions, underground biomes through The Wet Dark (3 biomes), basic infection/contagion system, basic meta-progression. Humans as scripted events (not full faction yet)
- **Early Access (12–18 months):** Underground biomes through The Thermovents (5 biomes), full mutation pool (200+), all base types, 4 strain affinities, full contagion system, 5+ enemy factions, human faction with full escalation timeline, NPC interactions, meta-progression, 3+ starting configurations
- **Early Access updates (18–24 months):** Add The Mycelial Graveyard and Deep Rot biomes, endgame content, expanded mutation pool, balancing passes based on player data
- **Full release (24–30 months):** The Below, The Radio mechanic, full story threads, final polish, balancing, potential co-op framework. The longer timeline reflects the expanded underground scope — this is now a bigger game, and it's worth taking the time to get each biome right

---

## Market Positioning

Based on what's performing on Steam right now:

- **Genre tags:** Roguelite, Horror, Strategy, Tactical, Indie, Colony Sim
- **Price point:** $14.99–$17.99 — the expanded scope justifies a slightly higher ceiling
- **Streamer appeal:** VERY HIGH — the mutation system creates inherently shareable moments. "Look at this absolute monstrosity I bred." The infection of humans creates dark, memorable narrative moments that generate discussion. The systemic chaos creates clips. The "what did I just pull up from the deep" moments are perfect for reaction content
- **Comparable titles for marketing:** "If Darkwood, FTL, Mewgenics, and Plague Inc merged into a fungal hivemind"
- **Co-op potential (post-launch):** Two players managing different sectors of the same network, sharing spore banks, coordinating invasions. One player could focus on surface contagion while the other manages Deep Tending. Given the co-op trend data, this could be a significant post-launch driver

---

## Open Questions for Development

1. **Pacing dial:** How fast should real-time combat actually be? Needs extensive playtesting. Too fast = chaotic mess. Too slow = loses the energy advantage over Mewgenics/FTL
2. **Mutation pool depth vs. balance:** More mutations = more emergent fun but harder to balance. Start with 30 well-tested mutations and expand from there
3. **The Below:** What IS down there? This should be developed organically during production. The mystery is the hook — don't over-define it early
4. **Narrative delivery:** Darkwood uses environmental storytelling and sparse NPC dialogue. This should too. No cutscenes. No exposition dumps. The story is in the soil
5. **Moral dimension:** You're a fungal parasite consuming a dying forest and eventually infecting humans. The game should not moralize — it should present the consequences and let the player feel whatever they feel. The first time you see a Thrall human stumbling through the trees, fungal growths splitting their skin, acting on your commands — that should be unsettling even though YOU did it. That dissonance is the horror
6. **Affinity balance:** Four affinities is a lot of systemic interaction to test. Consider launching with two (Hydrophilic and Thermophilic as the clearest opposites) and adding Galvanic and Lithic in Early Access updates
7. **Human faction timing:** When exactly humans escalate should be tuned to the player's aggression level, not just cycle count. A player who spreads aggressively and infects everything should trigger humans earlier. A stealthy underground player might not see serious human response until much later. This rewards different playstyles
8. **Contagion vs. combat investment:** These two paths need to feel equally viable. If infection is always better than fighting, combat mutations feel wasted. If fighting is always better, the entire contagion system is a trap. The terrain, human faction behavior, and rival network compositions should all push different runs toward different strategies
9. **The Radio:** Is the external military response a good mechanic or does it feel like an arbitrary timer? Playtest this extensively. It might work better as one of several possible endgame triggers rather than a guaranteed one                                         