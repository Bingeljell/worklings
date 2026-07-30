# Sprite Generation Prompts

Ready-to-use prompts for generating the new art the [dungeon system](dungeons.md) needs.
First-pass / janky is fine — the goal is to match the **existing style** closely enough
to drop into the game, then iterate (and eventually regenerate cleanly through the
planned 3D→2D pipeline).

Two sets:
- **A. Workling combat poses** — new poses that extend the shared [twelve-frame pose contract](characters.md), generated once **per family**.
- **B. Cache Warren foes** — the dungeon-specific creatures.

## How to assemble a prompt

Every prompt = **[Global style block]** + **[the specific subject/pose clause]**. Paste the
global block first, then the subject clause. For a Workling pose, drop in that family's
**character anchor** so the creature stays itself across every pose.

### Global style block (prepend to everything)

```
Detailed high-resolution pixel-art creature sprite in the style of a modern indie RPG:
painterly shading built from clean pixel clusters with subtle dithering, crisp readable
silhouette, warm expressive character. Single creature, full body, centered, 3/4
front-side view facing left. Transparent background, no ground, no shadow, no text, no
border, no frame. Consistent soft light from the upper-left with a gentle rim light.
Square composition.
```

### Technical / delivery notes

- The app crops sprites from a **4-column grid of 256×256 px cells** (`WildkinPetView.WorklingSpriteFrame`; existing sheets are 1024×768, twelve frames). Generate large (e.g. 1024²) and downscale to a clean 256² cell.
- Deliver either individual transparent PNGs (preferred for iteration) or an assembled sheet matching the existing 4-column layout. The five core combat poses add **row 3** (a 4×4 sheet); the two optional poses add row 4.
- **Match each family's existing palette exactly** — pull colors from `assets/worklings-<family>-spritesheet.png`.
- Keep proportions and accessories identical to the anchor across all poses (same bell, same key, same ember orb, etc.).

## Family character anchors

Insert the matching line into every Workling pose prompt.

- **Wildkin (moss-fox):** `a small fox cub with warm tan-and-orange fur, large pointed ears and a huge bushy tail both formed of fern-green leafy foliage, patches of green moss along its back, a small golden bell on a brown collar, bright green eyes.`
- **Elemental (ember-newt):** `a small charcoal-black salamander with glowing molten-orange lava cracks and speckles across its skin, bright orange flame-frill fins along its neck like fiery gills, a glowing ember orb at the tip of its tail, large expressive amber eyes, faint floating ember sparks.`
- **Relicborn (keyback pangolin):** `a small pangolin armored in overlapping cream-and-gold scales with glowing cyan seams, a brass wind-up key set into a runed escutcheon plate on its back, a soft brown underbelly and clawed limbs, a curled scaly tail, calm blue eyes.`

## A. Workling combat poses

Generate each for all three families. The pose must read at a glance and be visibly
**distinct from the existing twelve** (idle, blink, walk×4, happy, cared-for, hungry,
sleepy, sad, wary).

| Pose | Read | Pose clause (append after the anchor) |
| --- | --- | --- |
| **Strike** | landing an attack | `mid-lunge attacking pose, body stretched forward, striking with a front paw/claw, dynamic motion, fierce determined expression, a small impact spark at the strike point.` |
| **Hurt** | taking a hit | `recoiling backward from a blow, head turned aside, wincing in pain, off-balance with one limb raised, a small burst of impact sparks against its body.` |
| **Low-HP** | on the ropes | `battle-worn and staggering, hunched low and breathing hard, weakened trembling stance, scuffed and weary but still standing, exhausted-yet-determined expression. Clearly a combat exhaustion, not sleeping.` |
| **Victory** | fight won | `triumphant celebratory pose, chest up and one paw raised high, bright joyful confident expression, an energetic little hop, a sparkle or two. More dynamic and heroic than a calm happy idle.` |
| **Downed** | knocked out | `collapsed and knocked out, lying on its side/back with limbs splayed, eyes shut or dizzy swirls, a small puff of dust. Defeated, not curled up asleep.` |
| **Brace** *(opt.)* | defending | `defensive bracing crouch, hunkered low behind a raised guard (paw/tail/scaled back), bracing for impact, focused eyes.` |
| **Signature** *(opt.)* | class special | `charging a powerful signature move, dynamic heroic stance wreathed in a glowing elemental aura, building energy, intense expression.` For the aura, use the family element: Wildkin → swirling green nature/leaf glow; Elemental → flaring orange fire; Relicborn → radiant cyan rune-light. |

### Fully-assembled example (Wildkin · Strike)

```
Detailed high-resolution pixel-art creature sprite in the style of a modern indie RPG:
painterly shading built from clean pixel clusters with subtle dithering, crisp readable
silhouette, warm expressive character. Single creature, full body, centered, 3/4
front-side view facing left. Transparent background, no ground, no shadow, no text, no
border, no frame. Consistent soft light from the upper-left with a gentle rim light.
Square composition. Subject: a small fox cub with warm tan-and-orange fur, large pointed
ears and a huge bushy tail both formed of fern-green leafy foliage, patches of green moss
along its back, a small golden bell on a brown collar, bright green eyes — in a mid-lunge
attacking pose, body stretched forward, striking with a front paw, dynamic motion, fierce
determined expression, a small impact spark at the strike point.
```

Swap the anchor for the other two families; swap the pose clause for the other poses.

## B. Cache Warren foes

The first delve's bestiary — a fantasy underworld that dual-codes to work-chaos (see the
[dungeon setting](dungeons.md#a-worked-encounter-the-flicker)). First pass: **one base /
idle sprite each** (attack and hurt poses come later). Foes can run a darker,
cooler dungeon palette than the warm companion sprites, but keep the same pixel-art
rendering style. Scale the boss larger than a Workling cell.

| Foe | Role | Prompt clause (append after the global block) |
| --- | --- | --- |
| **Mote** | trivial swarm | `Subject: a tiny floating dust-mote gremlin — a single animated speck of grime and lint with two big simple eyes and stubby little limbs, faint gray-brown, almost harmless, slightly comical.` |
| **Flicker** | evasive | `Subject: a small semi-transparent imp-sprite made of unstable flickering light, its edges dissolving into static and afterimages, caught mid-dart in a jittery evasive pose, glowing mischievous eyes, cool blue-white glow. Hard to pin down.` |
| **Snag** | grabber | `Subject: a lurking creature formed from a knot of tangled dark thorny threads and roots, snagging barbed tendrils reaching outward, a single glaring eye deep in the tangle, muted greens and browns.` |
| **Monolith** | mini-boss | `Subject: a massive slow ancient golem built of stacked cracked stone and obsidian slabs like a looming monolith, dim runes barely glowing in its seams, dust and moss of ages, heavy and immovable, quietly menacing. Render larger and more imposing than a small creature.` |

## Notes for iteration

- These target the **current** hand-authored aesthetic. Once the 3D→2D pipeline exists, the Workling poses get regenerated from one rig per family — this prompt set is the interim.
- Character consistency across poses is the known weak spot of image models; if a pose drifts, regenerate with the anchor emphasized, or fix by hand.
- Ask and I'll expand any row into a fully-assembled, copy-paste prompt for all three families at once.
