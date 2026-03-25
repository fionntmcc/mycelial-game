# My Thoughts So Far - 2 days of development

I have made a lot of progress on the game already and I am extremely happy with a lot of it. I will break it down here for future development directions.

## What I have worked in so far - Likes and Dislikes:

### Game Concept:
- I love the rough game idea. I love the idea of a dark forest with strange stuff buried underneath, biomes and creatures that get stranger and more dangerous as the player explores deeper and further.
- I love the idea of playing as a mycelial infection in this terrain, small and brittle and weak at first, growing to become a force to be reckoned with and scaling to match the enemies it encounters underneath and on the surface (perhaps humans too).
- I think the theme and ideas translate really well and easily into a compelling art style and character.
- It turns out that I'm pretty good at pixel art and will be able to steer the art direction into something cool.

### Already Partially developed elements:
- The main one is I love the TendrilController. It already feels fun to control, fine control yet still irregular in shape, it feels organic and not robotic. I like the branching roots that spread slowly into the terrain. The movement already feels on par / maybe better than some indie games I've played, and it feels very unique. I like this a lot and it's definitely my game's strongest aspect at the moment. 
- I also love the TendrilHarpoon. I like how the player can steer it and it looks organic. It's already pretty fun to pick up small creatures with and eat them. This obviously needs to be fleshed out and updated in many ways, with unlockable upgrades that allow for catching larger enemies, longer harpoon, perhaps infection instead of consumption, etc. But these mechanics require other mechanics to be already developed.
- I like the concept of a procedurally generated world. I am not super happy at the moment because it feels very empty and random. So many rules will have to be written to allow for unique biomes, difficulty balancing, unique properties of blocks and new blocks, infection design and balancing, mob spawn placements etc.
- FogOfWar that goes around the tendril - adds so much mystery! This is massive.
- Camera - panning forward as the player moves, slight shake on movement, intense shake when colliding. This can be improved to react to attacks from enemies, perhaps a dimming and HUD overlay of blood / gore etc when the player's HP / hunger is low.
- I think (very subjective, you can tell me if I am wrong, and be honest) that the mix of game genres is very good. Horror and 2d with fun movement and hopefully good combat, spooky theme and setting. I think it will be a roguelike / lite also, and these are doing very well as of March 2026.

## What I think could be very promising but hasn't been implemented at all yet:
- Upgrade system: perhaps based on consuming enemies, infection milestones. Smaller upgrades at first, which ramp up to extremely interesting and powerful upgrades that have downsides to them. This makes playthroughs feel fresh and choices are impactful.
- Infection scale on blocks: Blocks react more dynamically to infection in interesting ways.
- Biome uniqueness: Water/wet-based biomes, hotter biomes, foliage-based biomes.
- Different ways of going about progressing: Perhaps infection, perhaps scorched-earth, etc. etc. Need more ideas for this.
- Humans being a major threat to you in the late game perhaps.
- "Area of control" or something similar, where you have a zone in which gameplay is standard, but if you venture further out of your depth, perhaps your tendril starts losing health much quicker, becomes brittle etc. This can be baked into the tendril itself, or the tendril requires upgrades to, for instance, venture into stony dirt. Deeper soil has much more cutthroat enemies that pose immediate and immense threats, etc. etc.

## What I think is extremely half-baked at the moment:
- Hunger system is the major one. I really am not sure what is the best way to balance this. Might get rid of it but i need to find a way to make this super compelling.
- Should 1 death mean game over? I am not sure what to do here.
- World generation. I need more variety and uniqueness.
- Combat: I really need to work on this. There is basically no combat currently. I feel like I need a good idea of what direction to steer this. Even though the harpoon feels really good in its immature state.
- Enemy style: I definitely think the creatures should exist on the same or cloned sub-pixel layer as the tendril, for easier sprite creation.
- Enemy sprites: This hasn't even been started yet. I need to figure out sizes of animals, if there will be underground bosses, etc.

## Ideas in the Game Design doc that I like:
- Plague Inc. inspiration: this is great, not much to be said. Fun infection strategies and strategy stemming from game systems rather than hard-coded. Upgrade systems like a skill tree, perhaps a mutation currency for buying upgrades mixed with collectibles (fossils, human items like pocketwatches, boots, socks, artifacts etc.) that you can find in the soil, maybe with sidequests associated with them, steering your best strategy and keeping gameplay fresh.
- Terraria-style 2d world. Not much more to be said there.
- Maybe Mewgenics-style disorders that hinder your tendril with a debuff, but perhaps improve the tendril in a certain way.

## Ideas in the Game Design doc that I don't think I like so far:
- Mewgenics-style breeding: far too ambitious I think! This could take so long and doesn't really make a whole lot of sense.
- FTL-style combat: Doesn't really make much sense. However FTL-style hard decisions could be really cool. I will think about this. Also character sprites in FTL are so cool, and could work well in a pixel art game like this.




# COOL IDEAS

Combat is the most important thing to figure out next, because it's what turns "fun to move around" into "fun to play." And you already have the seed of it — the harpoon. Rather than designing a separate combat system, I'd lean into the harpoon as your primary combat tool and build outward from there. Grab a small creature, slam it into another one. Harpoon a rock and fling it. Harpoon an enemy and reel yourself toward it to infect it. Harpoon a larger creature and get dragged. The tendril itself could be a weapon too — wrapping around things, constricting, leaving toxic residue. This keeps combat feeling organic to movement rather than bolted on.


The hunger system — your instinct that it's not working is worth listening to. Pure hunger timers feel punishing and rarely fun. Consider reframing it as growth momentum instead. Your mycelium naturally wants to expand. If you're actively consuming and infecting, you're thriving — your tendril gets faster, tougher, your corruption spreads further. If you stagnate, you start to wither. Same mechanical purpose (forces the player forward and into danger) but it feels like a reward for aggression rather than a punishment for existing. Think of it less like a food meter and more like a combo meter that decays.


Death and run structure — I'd say yes to permadeath, but with short runs. A 20-40 minute run where death is real creates tension that matches the horror tone perfectly. The roguelite meta-progression can be unlocking new starting mutations, new tendril types, new biome knowledge. Each death teaches you something about the world. This also solves your "fresh playthroughs" goal naturally.



On your promising-but-unbuilt ideas:
The area of control concept is fantastic and fits perfectly. Your mycelium network is your safe zone. Venturing beyond it into unclaimed soil should feel tense and dangerous — you're exposed, fragile, far from your root network. This creates a natural push-pull: expand carefully to grow your safe zone, or rush ahead and risk getting cut off. If an enemy severs your tendril from the network, that could be an instant death or a frantic race to reconnect.
The upgrade system should lean into the "you are what you eat" fantasy. Consume a beetle with hard chitin? Your tendril gains armor. Eat something bioluminescent? You can see further in the dark. Consume something toxic? Your infection becomes poisonous. This makes every creature in the world both a threat and a potential upgrade, which gives exploration real stakes beyond just "what's down there."
What I'd build in the next 2 weeks, in order:
First, get 2-3 basic enemy types working on the sub-grid — something small and passive (grubs), something that flees (centipedes), and something aggressive that charges you (a burrowing beetle). Just movement AI and collision with your tendril. Second, make the harpoon kill/consume them with a satisfying effect. Third, add one upgrade that changes how you play when you consume enough of something. Fourth, add one enemy that can actually kill you, and a death/restart flow. That gives you a complete loop: explore, find creatures, hunt them, grow stronger, encounter something dangerous, survive or die, try again.
That's your Steam Next Fest demo in miniature. Everything else — biomes, humans, deeper upgrades, infection strategy — layers on top of that loop once it's proven.
Want me to help you design the enemy AI system or the upgrade/mutation architecture in detail?



# HUNGER REDESIGN

This is the right problem to solve now — the hunger system is the core loop and if it doesn't feel good, nothing built on top of it will either. Let me think through this properly.
The fundamental issue with the current hunger system is that movement costs resources. That means the most fun thing in your game (moving around) is the thing that punishes you. That's backwards. The game should punish not moving.
Here's a proposal that I think fits your snake.io-like tendril, your fast-paced goals, and the "connected to the network" concept:
Replace hunger with two systems: Vitality and Vigor.
Vitality is your health. It doesn't drain from movement at all. It only drops when enemies hit you or when you're severed from your network. It regens slowly when you're on your own territory. This is the "am I alive" meter — simple, legible, not annoying.
Vigor is the combo meter. It's the entire game feel. Vigor builds when you consume creatures, claim new tiles, and infect terrain. It decays passively over time. High vigor makes you faster, your blob bigger, your harpoon stronger, your roots spread further. Low vigor makes you sluggish and small. Zero vigor doesn't kill you — but you're a tiny, slow, vulnerable worm barely able to defend yourself.
The magic is that vigor makes the game feel different moment to moment. At high vigor you're an aggressive predator tearing through the soil. At low vigor you're a desperate parasite scrambling for scraps. Same player, same controls, totally different vibe. And the way to stay powerful is to keep hunting — which keeps pushing you into danger.
The network connection is where it gets interesting. Your spline trail is your nervous system. As long as your head can trace a path back through your sub-grid cells to the origin tree, you're connected. Connected = vitality regens on your territory, vigor decay is normal. Disconnected = vitality drains fast (like a countdown), vigor plummets, you're on borrowed time. Reconnect or die.
This makes grazers genuinely terrifying without them dealing direct damage — they eat your trail, and if they chew through the right spot, you're suddenly disconnected and bleeding out. The cave spider is scary because it hits hard. The fungus gnat is scary because it quietly severs your lifeline while you're not looking. Different kinds of threats, both meaningful.
What counts as "connected" — this is the key design question you raised. Here's the non-arbitrary answer: your sub-grid trail cells form a graph. If any Trail or Root cell adjacent to your head blob can trace a path through other Trail/Root cells back to any cell within the origin tree's tiles, you're connected. You don't need to check this every frame — check it every second or so, or whenever a grazer eats a cell. It's a flood fill from the head through your claimed cells. If it reaches the tree, you're connected.
The snake.io parallel is exact: your trail IS your body. If something cuts through the middle, the disconnected half dies and becomes available territory for enemies. The head survives on a timer (disconnected vitality drain) and you're racing to either reconnect or reach the tree by a different path.
Here's what the moment-to-moment gameplay looks like with this system:

You push out from the tree, consuming grubs and beetles. Vigor climbs. You're moving fast, blob is big, feeling powerful.
You stop to explore a cave. No creatures to eat. Vigor decays. You feel yourself slowing down. Tension builds.
A cave spider attacks. Your vitality drops. You fight it off with the harpoon, slam it into a wall. Consume it — vigor spikes back up.
Meanwhile a fungus gnat has been quietly eating your trail behind you. You don't notice.
The gnat chews through a narrow point. Suddenly "DISCONNECTED" — vitality starts plummeting. Your screen darkens at the edges. You can see on the fog of war where the break is.
You have maybe 10-15 seconds. Do you race back to seal the break? Or push forward to the tree by a different route? Or kill the gnat and hope there's enough trail left to reconnect?

That's a game. That's the tension. And none of it comes from "you moved 10 tiles so subtract 8 hunger."