# Character Generation Prompts

Prompts for generating Worklings and dungeon foes.

## Art direction — not pixel art

**The art style is no longer pixel art.** The direction is **premium stylized 3D**: soft
continuous shading, real lighting, matte materials, readable large forms. The approved
five-race lineup is `assets/art-direction/approved-visual-direction.png` — that sheet is
the source of truth for how a Workling should look, not the shipped
`assets/worklings-*-spritesheet.png` sheets, which are legacy pixel art.

Note that the approved lineup also **revises several family designs**: Elemental reads as
a **storm ram** rather than the ember-newt, and Glitchkin and Bloomglass have designs now
(a dark-violet glitch fox with a dissolving tail; a translucent iridescent floating form).
The legacy anchors in the appendix describe the old pixel designs and should not be used
for new work.

## Two prompts per character

A character needs **two separate prompts**, and they must not be merged:

| | Purpose | Style |
| --- | --- | --- |
| **Conditioning-image prompt** | The input to the image→3D model. This is the one that matters. | Continuous shading, real lighting, smooth gradients |
| **Pixel-art sprite prompt** | The legacy 2D path, kept available | Pixel clusters, dithering, quantised colour |

The 3D model reads depth out of continuous shading and lighting. **Dithering and quantised
colour give it noise instead of depth**, so pixel-art language actively degrades the mesh.
Keeping the two prompts separate means the pixel path stays available without compromising
the 3D one.

## What the image→3D pipeline can and can't reconstruct

**The concept image is not reference — it is the input.** Each creature is generated as a
3D mesh from a single concept image, then rigged and baked to sprites. The design of that
image decides what geometry comes out, so the prompt is a *modelling* decision, not an
illustration one.

### The failure mode

Fine surface detail reconstructs as **zero-thickness sheets** — single-sided surfaces with
no back face. Fur, scales, carved relief and thin tangles look correct head-on and vanish
when seen from behind.

This is worse for a sprite bake than it would be in a game engine. The bake renders with a
transparent film, so a missing back face isn't a dark patch — **it becomes actual
transparency, and the sprite ships with holes punched through it.**

### The repair, and what it costs

Thickening the sheets closes every hole. But the repair works *by adding thickness*, so it
**fixes solid forms and fights hollow ones**: a design whose identity is the gaps between
its parts gets those gaps narrowed.

Two further limits:

- **Deep interior cavities are invented, not reconstructed.** Nothing in a recess was
  visible in the source image, so the model makes it up.
- **Recessed features lose their texture and glow.** Texture can only be projected onto
  surfaces the source view could actually see, so a deep-set glowing eye comes back unlit —
  and painting it back afterwards doesn't fully work. We've hit this on a real asset.

### The rules that follow

1. **Smooth over textured.** Surface detail is the single biggest driver of mesh damage —
   **93× worse in a controlled test than changing the creature's proportions.** Describe
   form through large shapes, not fine texture.
2. **Fewer and thicker over many and thin.** Thin tapering geometry is exactly where sheets
   and holes appear.
3. **Solid over hollow.** Negative space can't be reconstructed from one view and the
   repair pass narrows it anyway.
4. **Surface over recessed.** Anything that must glow or carry texture has to sit where the
   camera can see it.

### The honest trade

A 3D-pipeline creature reads **chunkier and less intricate** than the same character drawn
in 2D. That gap is real and it is the current cost of this pipeline. The alternative is a
beautiful silhouette that bakes to a sprite with holes in it.

### Roster implications

Worth knowing before these get generated, because several **approved** designs are built
out of precisely what the pipeline destroys:

| Design | The at-risk feature |
| --- | --- |
| **Glitchkin** | The tail dissolving into pixel fragments — fine, thin, gap-heavy |
| **Bloomglass** | Translucent membranous wings — thin sheets by definition |
| **Wildkin** | Individual flowers and leaf edges on the moss |
| **Elemental** | Arcing lightning filaments around the body |

For all four, the answer is the same: **bake the mesh solid and do the effect in-game.**
Model the Glitchkin's tail as a solid tapering form and produce the dissolve at runtime;
the effect then also stops looping, responds to combat state, and costs no geometry. See
the glitch-layer discussion for the Glitchkin case specifically.

## The conditioning-image prompt

### Global style block

```
Stylized 3D creature render for a game bestiary. Single subject, full body,
centered, 3/4 front-side view facing left, slight downward camera angle (~18°).
Soft studio key light from the upper-left, gentle fill, subtle rim light, soft
ambient occlusion. Smooth continuous shading with clear form-defining light and
shadow. Matte materials, no glossy highlights, no dithering, no visible texture
noise. Clean fully transparent background, no ground plane, no cast shadow, no
text, no border. Square composition, high resolution.
```

### Snag — grabber *(regenerate)*

Two problems with the first pass. It came back as a **stone gorilla** — a coherent animal
body in pale stone, which is Monolith's material and Monolith's read. And the prompt that
produced it was written for illustration, not for reconstruction: the tangle, the thin
tapering tendrils, the deep-set eye and the bark texture are the four things this pipeline
handles worst.

```
Subject: a lurking grabber creature with no true body — a single dense low mound
of thick woody vines and roots fused together into one solid mass, clearly wider
than it is tall, hugging the ground. The vines are merged into the mound rather
than loosely tangled; the form reads through large simple shapes.

Three heavy barbed tendrils grow out of the front of the mound and reach forward
toward the viewer, each roughly as thick as an arm, curving and tapering only
slightly, each ending in one blunt hooked thorn. Solid weighty limbs — not
threads, not wires, not filigree.

One large amber eye sits on the front of the mound, close to the surface, fully
visible and unobstructed — not recessed, not in shadow, not inside a cavity.

Muted mossy greens and deep wet browns. Smooth matte surface with broad soft
shading — no bark grain, no leaf litter, no hair-thin roots, no fine texture.
Menacing, patient, waiting to seize something.

No face, no skull, no snout, no additional eyes, no arms or legs, no stone, no
rock, no armor, no gorilla or bear or any recognizable animal anatomy.
```

**What changed and why:**

| Original | Changed to | Reason |
| --- | --- | --- |
| "open gaps and negative space between the coils… reads as a snarl rather than a solid lump" | "one dense low mound… the vines fused together, not a loose tangle" | Negative space can't be reconstructed from one view, and the hole-repair pass narrows gaps |
| "four or five barbed tendrils… tapering to sharp thorn tips" | "three heavy barbed tendrils… each as thick as an arm… blunt hooked thorn" | Thin tapering geometry is where sheets and holes appear; fewer, thicker features survive intact |
| "a single large glowing amber eye deep inside the knot, half-buried in shadow" | "one large amber eye on the front of the mound near the surface, clearly visible, not buried" | A recessed feature loses its texture and glow |
| "damp bark and dead-leaf texture" | "smooth matte surface, form described by large shapes rather than fine texture" | Surface detail is the biggest driver of mesh damage — 93× proportions |

**The silhouette test**: fill the render with flat black. It should read as *a low mound
with three heavy arms reaching out of it* — wide and ground-hugging, unmistakable against
Monolith's tall rectangle. If the black shape reads as an animal, it has failed regardless
of how good the surface looks.

### Flicker — evasive *(regenerate)*

Flicker is the hardest character on the roster for this pipeline, because **its written
design is a list of everything the pipeline destroys**: semi-transparency, edges
dissolving into static, afterimages, and a thin darting build. Prompted literally, it
comes back as sheets and holes.

The resolution is the same one the Glitchkin tail needs: **model it solid, do the
instability in-game.** The concept image describes a completely opaque creature whose
*form* implies speed; the transparency, the static, the afterimages and the jitter are all
runtime effects layered over the baked sprite. That also fixes the loop problem — instability
that repeats on an 8-frame cycle stops reading as instability.

The first pass had a second, separate problem: it came back **solid, serene, cream-coloured
and appealing** — it read as a companion, not a foe. That model is better redeployed as the
Glitchkin Workling. Flicker needs to be *unsettling*.

```
Subject: a small fast predatory creature built for darting — lean, compact and
low-slung, hunched forward on four thin-but-solid legs, coiled mid-crouch as if
about to bolt sideways. Wiry and sharp rather than delicate.

Its head is narrow and wedge-shaped, carrying two long upright blade-like ears
that rise well above the body — thick solid slabs with real edge thickness, not
membranes. A short stiff crest of the same solid blades runs down its neck. The
tail is a single solid tapering spike, thick at the base, held out straight and
stiff behind it for balance.

The eyes are small, hard and bright — pinpoint glowing slits, unfriendly, with
no soft roundness and no visible pupils.

Cold blue-white and pale grey body, fully opaque and solid throughout, with a
few sharp angular dark markings along the flanks and ears. Smooth matte surface
with broad soft shading — no fur, no fine detail, no texture noise. Tense,
twitchy, unsettling, hard to pin down.

Completely solid and opaque. No transparency, no translucency, no glow effects,
no motion blur, no afterimages, no particles, no wisps, no smoke, no fragments
or dissolving edges, no thin membranes or filaments. No cute or friendly
expression, no large round eyes, no soft rounded body.
```

**The bit that will feel wrong and shouldn't be fixed in the prompt:** the exclusion list
strips out Flicker's entire identity — transparency, static, afterimages. That is
deliberate. Those are the **in-game layer**, applied over this sprite: opacity dips,
RGB-split, trailing echo copies, and a per-pixel dissolve on the edges. Baking them would
give you a looping animation of instability instead of instability.

**The silhouette test**: fill the render with flat black. It should read as *thin, angular,
tall-eared and mid-crouch* — spiky and unstable in outline, immediately distinct from
Snag's low mound and Monolith's rectangle. The ears and the stiff tail spike are doing most
of that work, which is why they're specified as solid slabs with real thickness rather than
as blades or membranes.

## Notes for iteration

- **Silhouettes must not collide.** Each foe in a delve chain has to be identifiable as a
  black shape at cell size: Mote a tiny blob, Flicker thin and spindly, Snag low and wide
  and radial, Monolith a tall rectangle. Two grey stone masses in one bestiary is the
  failure this rule exists to catch.
- Character consistency across poses is a known weak spot of image models — but under the
  3D pipeline it matters far less, since poses come from **one rigged mesh** rather than
  from re-generating the character per pose.
- Foes may run a darker, cooler dungeon palette than the companion Worklings.

## Technical / delivery notes

- The app crops sprites from a **4-column grid** and derives the cell size from the sheet's
  own width, so 256px-cell and 512px-cell sheets both work and families can be re-baked one
  at a time.
- **New art targets a 512×512 cell** — a 4×5 sheet is **2048×2560**. Generate at 2048² and
  downscale to a clean 512². The shipped sheets are still the old 256px cell (1024×1280)
  and keep working. Full rationale and the 1080p math live in the
  [bake spec](bake-spec.md#5-output--the-1080p-update).
- Row index 3 is Strike, Hurt, Low-HP, Victory; row index 4 is Downed, Brace, Signature,
  unused.
- Foe poses are separate files per pose, single still or 4-column animation sheet — see
  [bake spec §9](bake-spec.md#9-foe-pose-sheets).

---

# Appendix — legacy pixel-art prompts

**Superseded.** These produced the shipped `assets/worklings-*-spritesheet.png` sheets and
are kept so the 2D path stays reproducible. **Do not use them as input to the image→3D
pipeline** — the dithering and quantised colour degrade the mesh.

### Legacy global style block

```
Detailed high-resolution pixel-art creature sprite in the style of a modern indie RPG:
painterly shading built from clean pixel clusters with subtle dithering, crisp readable
silhouette, warm expressive character. Single creature, full body, centered, 3/4
front-side view facing left. Transparent background, no ground, no shadow, no text, no
border, no frame. Consistent soft light from the upper-left with a gentle rim light.
Square composition.
```

### Legacy family anchors

Describe the **old** pixel designs, revised by the approved lineup above.

- **Wildkin (moss-fox):** `a small fox cub with warm tan-and-orange fur, large pointed ears and a huge bushy tail both formed of fern-green leafy foliage, patches of green moss along its back, a small golden bell on a brown collar, bright green eyes.`
- **Elemental (ember-newt):** `a small charcoal-black salamander with glowing molten-orange lava cracks and speckles across its skin, bright orange flame-frill fins along its neck like fiery gills, a glowing ember orb at the tip of its tail, large expressive amber eyes, faint floating ember sparks.`
- **Relicborn (keyback pangolin):** `a small pangolin armored in overlapping cream-and-gold scales with glowing cyan seams, a brass wind-up key set into a runed escutcheon plate on its back, a soft brown underbelly and clawed limbs, a curled scaly tail, calm blue eyes.`

### Legacy combat-pose clauses

| Pose | Read | Pose clause (append after the anchor) |
| --- | --- | --- |
| **Strike** | landing an attack | `mid-lunge attacking pose, body stretched forward, striking with a front paw/claw, dynamic motion, fierce determined expression, a small impact spark at the strike point.` |
| **Hurt** | taking a hit | `recoiling backward from a blow, head turned aside, wincing in pain, off-balance with one limb raised, a small burst of impact sparks against its body.` |
| **Low-HP** | on the ropes | `battle-worn and staggering, hunched low and breathing hard, weakened trembling stance, scuffed and weary but still standing, exhausted-yet-determined expression. Clearly a combat exhaustion, not sleeping.` |
| **Victory** | fight won | `triumphant celebratory pose, chest up and one paw raised high, bright joyful confident expression, an energetic little hop, a sparkle or two. More dynamic and heroic than a calm happy idle.` |
| **Downed** | knocked out | `collapsed and knocked out, lying on its side/back with limbs splayed, eyes shut or dizzy swirls, a small puff of dust. Defeated, not curled up asleep.` |
| **Brace** | defending | `defensive bracing crouch, hunkered low behind a raised guard (paw/tail/scaled back), bracing for impact, focused eyes.` |
| **Signature** | class special | `charging a powerful signature move, dynamic heroic stance wreathed in a glowing elemental aura, building energy, intense expression.` For the aura, use the family element: Wildkin → swirling green nature/leaf glow; Elemental → flaring orange fire; Relicborn → radiant cyan rune-light. |

### Legacy foe clauses

| Foe | Role | Prompt clause |
| --- | --- | --- |
| **Mote** | trivial swarm | `Subject: a tiny floating dust-mote gremlin — a single animated speck of grime and lint with two big simple eyes and stubby little limbs, faint gray-brown, almost harmless, slightly comical.` |
| **Flicker** | evasive | `Subject: a small semi-transparent imp-sprite made of unstable flickering light, its edges dissolving into static and afterimages, caught mid-dart in a jittery evasive pose, glowing mischievous eyes, cool blue-white glow. Hard to pin down.` |
| **Snag** | grabber | `Subject: a lurking creature formed from a knot of tangled dark thorny threads and roots, snagging barbed tendrils reaching outward, a single glaring eye deep in the tangle, muted greens and browns.` |
| **Monolith** | mini-boss | `Subject: a massive slow ancient golem built of stacked cracked stone and obsidian slabs like a looming monolith, dim runes barely glowing in its seams, dust and moss of ages, heavy and immovable, quietly menacing. Render larger and more imposing than a small creature.` |
