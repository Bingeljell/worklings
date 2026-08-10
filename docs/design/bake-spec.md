# 3D → Sprite Bake Spec

The authoring contract for generating Workling and foe sprites from Blender. Everything
here is a **parameter**, not a baked-in authoring choice: the camera is a turntable and
the framing is derived, so changing the angle later is a re-render, not a re-author.

Companion docs: [Characters](characters.md) for the roster and the pose contract,
[Sprite prompts](sprite-prompts.md) for the interim image-model prompts this pipeline
eventually replaces.

## 1. World scale

One number everything else derives from:

> **1.00 Blender Unit = the height of a standard Workling, nose-to-ground in idle.**

Build every family rig to exactly 1.0 BU tall. Size differences between creatures are
expressed by **building the model bigger**, never by moving the camera or changing the
ortho scale.

| Creature | Height (BU) |
| --- | --- |
| Workling (all five families) | 1.00 |
| Mote | 0.35 |
| Flicker | 0.70 |
| Snag | 0.95 |
| Monolith (boss) | 1.80 |

## 2. Camera

Never rotate the camera directly — Euler order will diverge across rigs. Use a turntable:

```
Empty (plain axes), named CAM_TARGET, at (0, 0, 0.5)   ← mid-body of a 1.0 BU creature
└── Camera (child of CAM_TARGET)
      local location  = (0, -6, 0)
      local rotation  = (90°, 0°, 0°)      ← set once, never touched again
      type            = Orthographic
      Orthographic Scale = 1.5
```

The **only** two knobs, both on `CAM_TARGET`:

| Knob | Blender field | Value |
| --- | --- | --- |
| Elevation — the 3/4 down-angle | `CAM_TARGET` rotation **X** | **−18°** |
| Azimuth — the 3/4 side-turn | `CAM_TARGET` rotation **Z** | **+35°** |

Orthographic, not perspective: a perspective lens makes the Strike lunge read subtly
larger than the idle, and you will fight that forever. Ortho also means the ortho scale
is a single global constant, which is what makes the framing contract in §4 automatic.

**Ortho Scale = 1.5 is global.** Same value for every pose, every family, every foe.
Never re-frame a shot.

### Facing

Existing sheets face **left**. All three current anchors carry asymmetric accessories
(the moss-fox's bell, the pangolin's back key, the newt's tail ember), so a horizontal
mirror puts them on the wrong side. Bake both facings rather than flipping in code:

- `face-left` → `CAM_TARGET` Z = **+35°**
- `face-right` → `CAM_TARGET` Z = **−35°**

Same pose data, one extra render pass, no extra authoring.

## 3. Lighting

Three **Sun** lamps (not points — sun lamps light identically wherever in frame the pose
sits), all parented to `CAM_TARGET` so they follow the angle if it ever changes.

| Lamp | Azimuth (rel. camera) | Elevation | Strength | Role |
| --- | --- | --- | --- | --- |
| **Key** | −35° | 50° | 1.0 | The upper-left key light the existing sprites read as |
| **Fill** | +110° | 15° | 0.20 | Lifts the shadow side, keeps the silhouette readable |
| **Rim** | +160° | 60° | 0.60 | The "gentle rim light" that separates from a dark dungeon |

Render settings:

- **Film → Transparent: ON.** No ground plane, no baked shadow.
- **View transform: Standard.** Not Filmic/AgX — those desaturate, and the palettes are
  hand-picked in `assets/worklings-<family>-spritesheet.png`. Match the existing sheets.
- **Colour: sRGB, straight alpha, 8-bit PNG.**

### Contact shadow

The sprite carries **no shadow**. The game draws a contact ellipse under the sprite at
runtime, so it can move and scale per encounter. This is what lets a 3/4 angle read as a
place while the sprite itself stays a clean transparent cutout.

If you want a shape-matched shadow later, render it as a **separate pass to its own PNG**
— never composite it into the cell.

## 4. Framing contract

Because the camera is fixed and ortho, all of this is automatic. It only breaks if you
**translate the object** between poses — so pose the bones, never the object.

At the 512px cell (§5), with Ortho Scale 1.5:

- **341 px per Blender Unit.** A standard 1.0 BU Workling is **341px tall in a 512px
  cell** — 67% of cell height.
- **Ground contact sits at y = 488** (24px above the cell bottom).
- **Root/hips centre at x = 256.**
- **Safe margin: 16px.** Nothing may enter the outer 16px of any cell.

Frame once for the **widest** pose in the set — Strike's forward lunge and Victory's
raised paw — and let the quiet poses sit in empty space. Do not tighten the frame on
idle; a sheet that re-frames per pose visibly breathes in game.

The Monolith at 1.8 BU renders 614px and overflows a 512 cell. **Bosses bake into a
1024px double cell**, same camera, same ortho scale, so their size relative to a Workling
is physically true rather than art-directed.

## 5. Output — the 1080p update

**This supersedes the previous 256px cell.** The dungeon stage bakes once at a ceiling
that serves a 1080p fullscreen stage (and downsamples cleanly to the 720p window), so the
old 256px cell is roughly half the resolution needed.

| | Old | **New** |
| --- | --- | --- |
| Cell | 256 × 256 | **512 × 512** |
| Sheet (4×5 grid) | 1024 × 1280 | **2048 × 2560** |
| Render size (per pose) | 1024² | **2048²** |
| Downsample | 4× Lanczos | **4× Lanczos** |

Render each pose at **2048², downscale to 512²** with Lanczos. The 4× supersample is what
produces the clean pixel-cluster read without dither noise.

The app no longer hardcodes the cell size — `WorklingSprite` derives it from the sheet's
own width (`sheet.width / 4`), so **1024-wide and 2048-wide sheets both work**. Families
can be re-baked one at a time; nothing has to land as a big-bang swap.

### Grid layout

Fixed 4-column × 5-row grid, matching `WorklingSpriteFrame` in
`Sources/Worklings/WildkinPetView.swift`:

| Row | Col 0 | Col 1 | Col 2 | Col 3 |
| --- | --- | --- | --- | --- |
| **0** | idle | idleBlink | walkContact | walkPassing |
| **1** | walkContactOpposite | walkPassingOpposite | happy | caredFor |
| **2** | hungry | sleepy | sad | wary |
| **3** | strike | hurt | lowHP | victory |
| **4** | downed | brace | signature | *(unused)* |

### Naming

Name renders with a **frame index from day one**, even though every pose is a single
still today:

```
wildkin_strike_face-left_0000.png
```

The whole point of moving to 3D is that Strike becomes a 6-frame loop later. The index
costs nothing now; retrofitting it across five families of assets costs a lot.

### Weight

Commit the **assembled sheets only**. Keep `.blend` files and per-pose intermediate
renders out of git (or under LFS) — the repo's history is already heavy, and a 2048×2560
RGBA sheet is roughly 4× the old one.

## 6. The rig

Rigify is overkill for a creature this size. Hand-build the armature to the spec below.

### The one constraint that matters

**Every family rig uses identical bone names, identical bone counts, and identical
shape-key names.**

Blender pose assets store transforms *keyed by bone name*, so an identical skeleton means
each of the 19 poses is authored **once** and applied to every family. Differing bone
*lengths and proportions* are fine — a pangolin and a newt can share a pose. Differing
bone *names or counts* are not; that turns 19 authoring jobs into 95. This is free if
decided before rig #1 and expensive after.

### Setup

- One armature object per family, named `RIG_<family>` (e.g. `RIG_wildkin`).
- One mesh, bound with an **Armature modifier** — no rigid child-of parenting.
- **Apply all transforms** (`Ctrl+A` → All Transforms) on the mesh before binding, so
  scale is 1.0 and the 1.0 BU height in §1 is real.
- **Rest pose**: neutral standing, all paws planted, weight settled — the `idle` silhouette
  before any expression. Facing **−Y** (toward the camera at azimuth 0).
- Use Blender's `.L` / `.R` suffixes exactly; symmetry tools and pose mirroring depend on
  them.
- **Bone roll must match across families.** Two rigs with the same names but different
  rolls will apply the same pose asset and read differently. Set rolls from a consistent
  axis (`Armature → Recalculate Roll → Global +Z`) on every rig.

### Bone hierarchy

**Core — mandatory on every family rig** (16 bones):

```
root                      ← the only bone permitted to translate
└── COG                   ← centre of gravity / hips; drives lunge, crouch, recoil
    ├── spine_01          ← lower back
    │   └── spine_02
    │       └── spine_03  ← chest
    │           └── neck
    │               └── head
    │                   ├── ear.L
    │                   └── ear.R
    ├── tail_01 → tail_02 → tail_03 → tail_04 → tail_05
    └── accessory_01 → accessory_02
```

- **`root` is the only bone that may translate.** The object stays at world origin
  forever (§4 depends on it). Lunges, hops and the downed sprawl move `root`. Budget:
  **±0.25 BU horizontal, ±0.15 BU vertical** — the framing in §4 is built for that
  envelope, and exceeding it breaks the safe margin.
- **`tail_01..05` is five bones on every rig**, even for a family with a stub tail. Unused
  segments stay at zero rotation. Five is set by the moss-fox's foliage tail and the newt's
  ember tail, which carry a lot of silhouette.
- **`accessory_01..02` is a two-bone chain** so hanging accessories swing with lag. Per
  family: Wildkin → collar + bell; Elemental → tail-tip ember orb (chain along the ember,
  independent of `tail_*`); Relicborn → the wind-up key in its escutcheon plate;
  Glitchkin → the leading pulse-line / afterimage anchor; Bloomglass → the orbiting
  crystal shard. A family with no accessory keeps the bones unused, at zero.

**Legs — mandatory on every *legged* family** (24 bones):

```
COG
├── leg_fore_shoulder.L → leg_fore_upper.L → leg_fore_lower.L → paw_fore.L
├── leg_fore_shoulder.R → …
├── leg_hind_hip.L      → leg_hind_upper.L → leg_hind_lower.L → paw_hind.L
└── leg_hind_hip.R      → …

root
├── IK_paw_fore.L   IK_paw_fore.R   IK_paw_hind.L   IK_paw_hind.R
└── POLE_fore.L     POLE_fore.R     POLE_hind.L     POLE_hind.R
```

- IK constraint on each `*_lower` bone, **chain length 2**, target `IK_paw_*`, pole target
  `POLE_*`. IK targets parent to `root`, not `COG`, so planted paws stay planted when the
  body crouches — which is the whole reason Brace and the Strike wind-up read.
- `paw_*` bones take **Copy Rotation** from their IK target.
- A genuinely legless family — Bloomglass reads as a floating mass — **omits the leg block
  entirely**. Poses then transfer for everything except leg rotation, which is the correct
  degradation rather than a problem to solve.

Total: **40 bones** legged, 16 legless.

### The face — shape keys, not bones

No jaw bone, no eye bones. Ten shape keys, driven 0.0–1.0:

`eye_blink` · `eye_wide` · `eye_squint` · `brow_up` · `brow_down` · `brow_worry` ·
`mouth_open` · `mouth_smile` · `mouth_frown` · `mouth_grit`

`brow_down` is the focused/angry brow, `brow_worry` the raised-inner-corner one — they are
different shapes and both earn their place across the 19 poses.

Shape keys are why expression transfers across families that share no topology — a
pangolin and a newt have nothing in common mesh-wise, but `mouth_frown` at 0.8 means the
same thing on both. Every family must ship all nine, even where one barely moves.

**Expression recipes** — the face half of each pose, so it's consistent across families:

| Pose | Shape keys |
| --- | --- |
| idle | — (all zero) |
| idleBlink | `eye_blink` 1.0 |
| walk ×4 | — |
| happy | `mouth_smile` 0.9, `eye_squint` 0.3, `brow_up` 0.5 |
| caredFor | `mouth_smile` 0.5, `eye_blink` 0.6, `brow_up` 0.2 |
| hungry | `mouth_open` 0.4, `brow_worry` 0.6, `eye_wide` 0.3 |
| sleepy | `eye_blink` 0.75, `mouth_open` 0.7, `brow_worry` 0.2 |
| sad | `mouth_frown` 0.9, `brow_worry` 1.0, `eye_squint` 0.2 |
| wary | `eye_wide` 0.8, `brow_worry` 0.5, `mouth_grit` 0.3 |
| strike | `mouth_grit` 1.0, `eye_squint` 0.6, `brow_down` 0.8 |
| hurt | `mouth_open` 0.8, `eye_squint` 1.0, `brow_worry` 0.9 |
| lowHP | `mouth_open` 0.5, `eye_squint` 0.7, `brow_worry` 0.6 |
| victory | `mouth_smile` 1.0, `eye_squint` 0.5, `brow_up` 1.0 |
| downed | `eye_blink` 1.0, `mouth_open` 0.3 |
| brace | `mouth_grit` 0.8, `eye_squint` 0.8, `brow_down` 0.6 |
| signature | `eye_wide` 0.9, `brow_down` 0.7, `mouth_grit` 0.5 |

`sleepy` vs `downed` and `lowHP` vs `sad` are the pairs that collide; note that the
separation is carried by **body**, not face — sleepy is settled and downed is sprawled,
lowHP is upright-and-trembling and sad is closed-and-drooping.

### Pose assets

Author each pose once, on rig #1, and save it to the Asset Browser:

- **Select every bone** before saving the pose asset — a pose asset only stores what was
  selected, and a partial pose applied to another rig leaves stale rotations behind.
- Name assets to match the frame names exactly: `pose_strike`, `pose_lowHP`, and so on.
- Keep them in one `poses.blend` library shared by all five family files, so fixing a
  pose fixes it everywhere.
- Shape-key values are **not** carried by pose assets. Keep the recipe table above as the
  source of truth and set them per render, or drive them from a custom property on `root`.

## 7. The poses

19 total: the 12 base poses (shipped) plus the 7 combat poses. Reads are the contract —
each must be distinguishable from every other at cell size.

### Base (12)

| Pose | Read |
| --- | --- |
| **idle** | Neutral standing, weight settled, alert but calm |
| **idleBlink** | Identical to idle, eyes closed — the only difference |
| **walkContact** / **walkPassing** | Two-frame walk cycle, lead leg forward |
| **walkContactOpposite** / **walkPassingOpposite** | The same cycle on the opposite legs |
| **happy** | Bright and up — ears up, tail up, open expression |
| **caredFor** | Softer than happy — contented, eyes half-closed, settled |
| **hungry** | Drooping, looking up/off expectantly, one paw raised |
| **sleepy** | Heavy-lidded, head low, mid-yawn or about to be |
| **sad** | Ears flat, head and tail down, closed posture |
| **wary** | Low crouch, head forward, alert and suspicious — tense, not scared |

### Combat (7)

| Pose | Read |
| --- | --- |
| **strike** | Mid-lunge, body stretched forward, front paw/claw landing, fierce |
| **hurt** | Recoiling back, head turned aside, off-balance, wincing |
| **lowHP** | Hunched, staggering, breathing hard — combat exhaustion, *not* sleep |
| **victory** | Chest up, one paw raised high, joyful and heroic — more than happy |
| **downed** | Collapsed on its side, limbs splayed, eyes shut — defeated, *not* asleep |
| **brace** | Defensive crouch behind a raised guard (paw/tail/scaled back), focused |
| **signature** | Charging a special: heroic stance, building energy, family-coloured aura |

The signature aura is family-coloured: Wildkin swirling green nature-glow, Elemental
flaring orange fire, Relicborn radiant cyan rune-light, Glitchkin unstable violet-white
signal static, Bloomglass pale refracted starlight.

Two pairs are the ones that go wrong most often and are worth checking side by side:
**sleepy vs downed** and **lowHP vs sad**.

## 8. Manifest

Commit `assets/bake-manifest.json` **before rendering anything**. It is what makes the
camera decision reversible — without it, "re-bake at 22°" is archaeology across five
`.blend` files.

```json
{
  "version": 1,
  "world": { "unit": "1.0 BU = standard Workling height" },
  "camera": { "type": "ortho", "elevationDeg": -18, "azimuthDeg": 35, "orthoScale": 1.5,
              "targetHeightBU": 0.5 },
  "lighting": { "rigVersion": 1, "keyAzimuthDeg": -35, "keyElevationDeg": 50,
                "fillStrength": 0.2, "rimStrength": 0.6, "viewTransform": "Standard" },
  "render": { "renderPx": 2048, "cellPx": 512, "columns": 4, "rows": 5,
              "downsample": "lanczos", "alpha": "straight" },
  "framing": { "pxPerBU": 341, "groundContactY": 488, "safeMarginPx": 16 },
  "heightsBU": { "workling": 1.0, "mote": 0.35, "flicker": 0.7, "snag": 0.95,
                 "monolith": 1.8 },
  "facings": ["face-left", "face-right"]
}
```

## Open

- **The angle itself.** −18° / +35° is the working default, inside the recommended
  15–25° band. It is deliberately not locked; the turntable rig and this manifest are
  what keep it a cheap parameter rather than an expensive commitment. Bake one character
  at 0° / −18° / −35° elevation and compare three stills before locking.
- **Animation.** Everything is single-frame today. The frame index in the naming
  convention is the only concession made in advance.
