# 3D → Sprite Bake Spec

The authoring contract for generating Workling and foe sprites from Blender. Everything
here is a **parameter**, not a baked-in authoring choice: the camera is a turntable and
the framing is derived, so changing the angle later is a re-render, not a re-author.

Companion docs: [Characters](characters.md) for the roster and the pose contract,
[Sprite prompts](sprite-prompts.md) for the interim image-model prompts this pipeline
eventually replaces.


> **Dungeon actors moved to live 3D (2026-09-01).** The dungeon no longer uses baked
> sprite billboards for characters — see [dungeons](dungeons.md)'s "Rendering approach".
> Billboards were a performance optimisation for a budget that turned out not to be
> tight (2–4 characters per dungeon). This spec still governs the **desktop pet**, where
> the constraints differ; the stage-bake sections (per-azimuth frame sequences, ground-
> offset fractions, the stage frame library) are retained as the record of that pass
> rather than as live guidance for the dungeon. The live-3D path has its own spec —
> see [character asset pipeline](../engineering/character-asset-pipeline.md).
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
**translate the object** between poses — so pose the bones, never the object. Lunges,
hops and the downed sprawl translate the topmost bone of the spine chain, with a budget of
**±0.25 BU horizontal / ±0.15 BU vertical**; the safe margin below is sized for exactly
that envelope.

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

**Where that lives:** `~/projects/worklings-blender-work/` (a sibling directory to this
repo, not inside it — nothing under it is git-tracked here). Two things go there:

- **One `.blend` per character** at the top level — e.g.
  `tempest-ram-rigify-natural-walk.blend`, `clockwork-pangolin-rigify.blend` — each
  carrying that character's mesh, Rigify rig, actions, and (per §2/§10) its
  `CAM_TARGET`/`QuickCam2` camera rig. This is the reusable working file per character;
  see "Per-character workflow" above for why it's one file per character rather than
  one shared file.
- **`test-renders/stage-frames/`** — the raw baked PNG frame sequences `blender_stage_bake.py`
  (§10) produces, named `<character>_<action>_az<azimuth>_f<frame>.png`. This is
  dev-preview output for the dungeon-stage tool, not the final §5 sheets above — no
  grid assembly, no 512px downsample, nothing committed. Frame-selection and sheet
  assembly (turning this into the real §5/§9 deliverable) hasn't started yet.

## 6. The rig — as built

**This section records the rig that exists, and supersedes the earlier draft in this
doc's history.** The approach changed in a way worth naming: motion is not transferred
between characters as posed data, it is **generated by script against fixed bone names**.
Per-character work is *fitting*, not posing.

### The contract

**25 bones, fixed names, fixed hierarchy.** The names are the contract — `blender_walk_cycle.py`
addresses bones by name, so a gait authored once **retargets to a new character by
re-fitting markers rather than by re-authoring motion**.

That inverts the earlier plan. Under a pose-asset library, every pose is authored data
that has to be applied and fixed up per rig. Here the motion lives in the script, keyed to
names, and a new character costs a marker pass. The consequence is the same as before but
sharper: **a rig that renames a bone silently opts out of every gait**, so names are not
a convention, they are the interface.

### Per-character workflow

**One `.blend` file per character** — not one shared file with every character imported.
A new character's file gets its own mesh import, its own rig fit (the marker pass above),
and its own action names; nothing about authoring wants them to share a scene graph, and
a shared file would mean every character's actions live in one flat `bpy.data.actions`
namespace, fighting over names like `Walk`.

What *is* shared, and copied forward into each new file rather than re-decided: the
rig-building script (this section), the marker system, the lighting rig (§3), and the
room-locked camera convention (§2 for character-screen facing, §10 for dungeon-stage
facing) — those values don't change per character, only the mesh being fitted to them
does. `blender_stage_bake.py` (§10) makes the *render* step mechanical once a file has
the camera rig — but building that rig in the first place (the `CAM_TARGET` empty +
`QuickCam2` ortho camera, §2) is still manual RPC surgery per file, and it's the part
that just broke: Pangolin's rig was built ad hoc during the 2026-08-26 session and never
saved, and its ortho scale was sized off bbox height alone, which clips on any body
that isn't roughly as tall as it is long (§10 Open has the full story). **Not yet
built, worth doing before the next character**: a `blender_build_stage_rig.py` that
takes an already-rigged-and-animated file, builds the `CAM_TARGET`/`QuickCam2` rig at
the mid-body height read off the mesh's own world bbox, sizes ortho scale off the
*posed* extent across every action at both stage azimuths (not just a rest-pose
height guess), and — since `DungeonStageCameraTool.swift`'s `groundOffsetFraction`
table has to be hand-updated every time ortho scale changes, which is exactly the kind
of step that gets forgotten — writes a small per-character metadata file (ortho scale,
ground-offset fraction, bbox) that `StageFrameLibrary` could read instead of the
hardcoded Swift dictionary. That would close the loop: mesh in, camera rig and
metadata out, no manual RPC and no Swift edit, for every character after this one.

### Hierarchy

| Block | Contents |
| --- | --- |
| Spine | spine chain |
| Head | neck → head → jaw |
| Ears | ear ×2 |
| Limbs | four limb chains, each ending **heel + toe** |
| Tail | tail ×2 |

> **To fill in:** the authoritative 25 names and their parenting, lifted from
> `blender_walk_cycle.py` rather than retyped, so the doc cannot drift from the script.

### The 31 markers

Markers are **the only per-character work**. Each is expressed as a **fraction of that
character's bounding box**, never as an absolute position — assets arrive unit-normalised
(see §1), so absolute numbers do not transfer between characters and fractions do.

**Every marker must exist, even where the anatomy does not.** Snag has no tail; its three
tail markers are parked as a stub inside the rump, so the bones still exist and take
negligible weight. A missing marker is not an option — a degenerate *placement* is. This
is what keeps the bone list at a hard 25 for every creature, which is in turn what lets
the script assume it.

(The counts line up: a two-bone tail chain needs three markers — base, mid, tip.)

> **To fill in:** the 31 marker names and their bounding-box fractions for a reference
> character.

### Why the foot is two segments

`paw` is the **heel**; `toe` sits ahead of it.

A single `wrist → paw` bone pivots like a peg: lifting it **raises the toe instead of
dropping it**, and the foot reads as a stiff paddle in every gait and every planted pose.
Heel plus toe lets the foot **roll** — heel strike, roll through, toe-off — which is most
of what makes a walk cycle read as weight rather than as sliding.

This is the one place where an extra bone pair pays for itself several times over, and
it's worth protecting if the bone count is ever squeezed.

### What still holds from the earlier plan

- **Unit-normalised assets** (§1) — reinforced, not superseded: it is precisely what makes
  fractional markers portable.
- **The object never translates** (§4). Translation happens on the topmost bone of the
  spine chain, within the framing budget in §4.

### Superseded

The earlier draft specified 40 bones legged / 16 legless, a five-bone tail, no jaw bone,
face-on-shape-keys-only, and a shared pose-asset library. None of that describes the rig
that exists. The shape-key expression recipes are **parked pending a decision** — the
as-built rig has a `jaw` bone, so some expression is now bone-driven, and how much of the
face remains on shape keys is an open question rather than a settled one.

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

## 9. Foe pose sheets

Foes do **not** use the Workling 4×5 pose sheet. They have three poses — `idle`,
`attack`, `hurt` — delivered as **one file per pose**, and each file is either a single
still or an animation sheet.

| | Single still | Animation sheet |
| --- | --- | --- |
| Layout | one cell | **4 columns, row-major**, filled left-to-right, top-to-bottom |
| Size | `cell × cell` | `(4 × cell) × (rows × cell)` |
| Example | 512×512 | 8 frames → 2048×1024 |

Cell size is read off the sheet's own width (`width / 4`), so any cell size works. **New
foe art uses the 512px cell** (§5). Everything in §2–§4 applies unchanged — same camera,
same lighting, same ortho scale, same ground-contact line — so a foe's size relative to a
Workling comes out physically true. Snag is **0.95 BU** (§1).

### Filenames

```
assets/foes/<base>-idle.png
assets/foes/<base>-attack.png
assets/foes/<base>-hurt.png
```

`<base>` is lowercase and matches the entry in `FoeSpriteAsset.resourceBase(for:)`.
Registered today: `mote` (display name "Dungeon Scamp"), `snag` ("Snag").

### Frame count is declared, not inferred

The frame count lives in `FoeSpriteAsset.frameCount(foe:pose:)`, not in the file. A 4×4
sheet is square and therefore indistinguishable from a single still, so inferring it from
the image would be a guess that silently breaks at 16 frames. Adding an animated pose is
one line there.

### Playback

- **12 fps** (`FoeSprite.frameDuration`). Slow enough to read as hand-animated next to
  the pixel art, fast enough that an 8-frame swing lands inside a combat beat.
- **A pose plays once and holds its last frame.** It restarts from frame 0 every time the
  foe enters that pose, rather than joining a free-running loop mid-swing.
- **`idle` is the exception — it loops.**

> The consequence worth designing around: **the last frame of an animated pose is what
> the foe sits on for the rest of the beat.** Author it as a settle that reads held, not
> as the end of a follow-through.

### Recommended attack breakdown — 8 frames

8 frames at 12fps is 0.67s, which fits comfortably inside a combat beat.

| Frames | Beat | Notes |
| --- | --- | --- |
| 0–1 | **Anticipation** | Wind back *away* from the pet. The clearest read in the whole swing; don't rush it |
| 2 | **Contact** | The extreme. One frame only — impact reads as a snap, not a hold |
| 3–4 | **Follow-through** | Overshoot past the contact pose, then start back |
| 5–7 | **Recovery** | Settle toward neutral; **frame 7 should read as a held pose** |

For Snag specifically — a knot of tangled thorny threads that *grabs* rather than strikes
— the anticipation is the tendrils gathering/coiling inward and the contact is them
lashing outward. It has a Snare (an Agility debuff), so the silhouette wants to read as
*seizing*, not punching.

### Dropping a new foe in

1. Bake the three PNGs to `assets/foes/`.
2. Add them to `Package.swift` `resources:` — `.copy("../../assets/foes/snag-idle.png")`
   and the other two. **A `.copy` of a missing file fails the build**, which is why the
   entries are not pre-added for art that doesn't exist yet.
3. Register the base name in `FoeSpriteAsset.resourceBase(for:)` if it isn't already.
4. Declare any multi-frame pose in `FoeSpriteAsset.frameCount(foe:pose:)`.

Until step 1 lands, `hasArt` is false and the foe renders the existing placeholder — a
registered-but-unbaked foe degrades rather than breaks.

## 10. Dungeon stage-facing (diagonal corners)

The Cache Warren's battle stage (see [Dungeons](dungeons.md)) is a **fixed-camera room**
where the party can enter at any of its four corners — bottom-left, top-left,
bottom-right, top-right — always facing the opposite corner, where the foes wait, then
walking past them toward the exit corner on a win. Because every actor is a flat
billboard in a room whose camera never moves, whatever direction a bake visually faces is
exactly how it reads once placed — there's no runtime rotation to correct it.

This is a **different facing convention from §2's `face-left`/`face-right`**, which is a
mild ±35° turn for the character-screen portrait context. The dungeon needs a character's
body actually oriented toward a diagonal corner of the room, which turned out to need a
much larger camera turn than ±35° — closer to a back 3/4 view than a front one.

### The four corners, two real bakes

Only two of the four corner-facings are ever rendered directly; the other two are a
horizontal image mirror, because mirroring only flips the **left/right** component of a
diagonal direction and leaves **up/down** alone. Mirror pairs therefore stay on the same
vertical half — never cross top/bottom:

| Bake | `CAM_TARGET` Z | Corner it's for | Produced by |
| --- | --- | --- | --- |
| `stageFaceBL` | **+35°** | Top-right (faces down-left, toward the party at BL) | Direct render |
| `stageFaceTR` | **245°** | Bottom-left (faces up-right, toward the foes at TR) | Direct render |
| `stageFaceBR` | mirror of `stageFaceBL` | Top-left (faces down-right, toward BR) | Horizontal mirror |
| `stageFaceTL` | mirror of `stageFaceTR` | Bottom-right (faces up-left, toward TL) | Horizontal mirror |

Elevation locked at **−28°** (steeper than §2's shipped −18°), bracketed against the
room's own locked 39.7° elevation. Both azimuths confirmed 2026-08-22 in the dungeon-stage
tool against the Tempest Ram's real animated rig (walk cycle) rather than a placeholder —
`stageFaceBL` at 35° first, then `stageFaceTR` narrowed from the 235°/245° bracket to 245°
(245° read marginally better for face/eye visibility; 235° was judged an acceptable
alternative, not a clear miss).

**Applies to animated poses too.** Since motion is a real 3D animation on the rig (§6),
not per-angle hand-drawn art, an animated pose (walk, attack, hurt, die, ...) only needs
its full frame sequence rendered from the two real azimuths above — the mirrored corners
reuse those same frames unchanged, no extra animation authoring.

## Open

- **The §2 angle itself.** −18° / +35° is the working default for the character-screen
  facing, inside the recommended 15–25° band. It is deliberately not locked; the
  turntable rig and this manifest are what keep it a cheap parameter rather than an
  expensive commitment. Bake one character at 0° / −18° / −35° elevation and compare
  three stills before locking.
- **§10's elevation and both azimuths are now locked** (−28° / 35° / 245°, see the table
  above) — confirmed 2026-08-22 in-scene against the dungeon-stage tool's live camera,
  using the **Tempest Ram** (Elemental family)'s real animated rig rather than a
  placeholder or a single static still. Bumped from 640² to **1024²** the same day once
  the dungeon-stage tool's window (locked to 1280×720) made the original render look soft
  by comparison. Still fast-preview quality (no supersample, ortho scale widened to 2.6 to
  fit this character's horns) and not the full §5 2048²→512 pipeline — the numbers are
  locked, the pixels backing them are not production-final yet.
- **Asymmetric-accessory mirroring is unresolved for §10 — but no longer for Pangolin.**
  §2 already avoids mirroring production Worklings for exactly this reason — an
  off-center accessory lands on the wrong side under a horizontal flip. Nikhil rebuilt
  the Clockwork Pangolin fully symmetric (2026-08-27), so its back key no longer poses
  this problem and it's dropped as a live concern for this character. The general
  question is still open for any future asymmetric character (the moss-fox's bell, the
  newt's tail ember were the original examples) — `stageFaceBR`/`stageFaceTL` mirroring,
  multiplied across every animated frame, not just one still. Floated but not decided:
  simplify or drop asymmetric detail specifically for dungeon-scale sprites (small, seen
  at a distance) while keeping it correct on the character screen, which is a separate
  live-3D render rather than this baked pipeline.
- **Pangolin's ortho scale was too tight, and it clips in exactly the way a
  height-derived auto-size predicts.** Found 2026-08-27 by actually looking at the
  dungeon-stage tool: the Pangolin's tail ran off the frame edge at rest, and got worse
  mid-animation. Its `CAM_TARGET`/`QuickCam2` rig, built during the 2026-08-26 bake pass,
  had never been saved back into `clockwork-pangolin-rigify.blend` — reopening the file
  showed only a bare perspective `Camera`, no turntable rig at all — so there was no
  record of what ortho scale had even been used. Root cause of the clip once the rig was
  rebuilt: the Pangolin's world-space bounding box is 2.22 BU nose-to-tail but only 1.11
  BU tall — over 2× longer than it is tall — and the auto-sizing rule (bbox height × 1.5)
  that the other two characters' rigs used only accounts for height, so it starved the
  horizontal axis for a body shape this elongated. Fixed by projecting the evaluated
  (posed, not just rest) mesh into camera space across every baked action at both stage
  azimuths and taking the true worst-case extent — 2.59 BU, from the tail mid-swing in
  `Attack_TailSwipe_CW180` — then setting ortho scale to **3.0** (roughly 15% margin over
  that measured worst case, not a guess). All five picked actions (§ above) were rebaked
  at both azimuths with this scale; the `.blend` was resaved with the rig included this
  time. **Consequence for `groundOffsetFraction`** (`DungeonStageCameraTool.swift`):
  that fraction is ground-contact-z ÷ ortho-scale, so widening the scale from ~1.67 to
  3.0 moved Pangolin's value from 0.305 to **0.179** — it was recomputed and updated
  alongside the rebake, not left stale. Lesson for future characters: don't auto-size
  ortho scale off bounding-box height alone for a non-compact body plan; a script should
  measure the full posed-across-all-actions extent (see the reusable-rig-setup item
  below) rather than repeat this per character by hand.
- **Animation — rigs done for three characters, export still pending.** The Rigify-vs-manual
  detour below is settled: Rigify won. As of 2026-08-26, **all three** currently-rigged
  characters carry a full action set on the Rigify `rig` armature: **Tempest Ram**
  (`RamWalk_Natural_FrontFix`, `RamHeadbutt_Power`, `RamDamage_ChestLed_Wince`,
  `RamIdle_Breathe_Paw` — walk/attack/hurt/idle, idle landing this session and closing the
  "no idle pose yet" gap), **Forest Flicker** (`ForestFlicker_Walk_Feline`,
  `_Attack_RightSwipe`, `_Damage_Wince_TailDown`, `_Special_DoublePawSlam`,
  `_Idle_BreatheLook`), and **Clockwork Pangolin** (`Pangolin_Walk_InPlace_v01`,
  `_Attack_TailSwipe_CW180_Sprite_v03`, `_HitReact_HeadTuck_Sprite_v01`,
  `_Special_RearSlam_Sprite_v04`, `_Rest_BreatheLook_v01` — Pangolin's file carries several
  versioned variants per slot; only the picks above have been rendered so far, the rest are
  unreviewed alternates, not rejects). Frame index in the naming convention (§5) is still
  the only concession made toward export; actual frame-selection and sheet assembly still
  hasn't started (see the 2026-08-26 pickup note).
- **RESOLVED (was: 2026-08-21 pickup note, Rigify-vs-manual rig comparison).** Rigify won
  outright: the Basic Quadruped metarig's missing head/neck/jaw (the blocker noted below)
  got a head/neck chain added, closing the gap with the manual rig, and the manual
  pipeline (`tempest-ram-markers.blend`) hasn't been touched since. `rig` in
  `tempest-ram-rigify-natural-walk.blend` is the live armature going forward — 283 bones
  total (Rigify's full mechanism/deform/widget set, not the 25-bone §6 contract count,
  which only counts the deform layer). The walk-direction bug (gait running backward) is
  also fixed — confirmed by eye in the dungeon-stage tool, not just by rerunning the
  render.
- **RESOLVED (was: 2026-08-22 pickup note, items 1–3).** Idle landed for the Tempest Ram;
  the reusable frame-sequence render script got built (`blender_stage_bake.py`, see below);
  and Forest Flicker + Clockwork Pangolin both got rigged with full action sets, not just
  queued. Item 4 (frame-selection and sheet assembly) is still open — see the new pickup
  note below, which picks up exactly where this one left off.
- **PICKUP NOTE (2026-08-26, stopping for the night).** Tonight's session took the
  reusable-render-script item from the previous pickup note and ran it across all three
  rigged characters:
  - **`image-to-3dlab/scripts/blender_stage_bake.py`** is the reusable frame-sequence
    render script from the prior pickup note. Talks to a live Blender over the
    `execute_code` RPC (port 9876) rather than running headless, so it renders whatever
    `.blend` is currently open; switching files is the caller's job. Parameterized by
    action name, label, azimuth, elevation, ortho scale, frame range/step, and resolution.
    It assumes the section-6/section-2 rig contract (`rig` armature, `CAM_TARGET` empty,
    ortho camera named `QuickCam2` parented to it) already exists in the file — it never
    authors that rig itself. Forest Flicker and Pangolin didn't have one yet, so that rig
    (empty + parented ortho camera, ortho scale auto-sized off the mesh's own bounding-box
    height × 1.5, matching the §2 ratio) got built once per file before baking.
  - **All three characters are now baked at both locked stage azimuths** (35°/245°, §10)
    into `worklings-blender-work/test-renders/stage-frames/` (outside the repo, per the
    weight policy in §5's "Weight" note) — 1312 frames total, 849MB. Frame policy: full
    native frame rate (24fps, no subsampling) for actions ≤60 frames — every walk, attack,
    hit-react, and wince — and half-rate subsampling only for the long idle-breathing loops
    (121–144 native frames), since those are slow enough that skipping every other frame is
    invisible. This was a deliberate call against under-sampling (an earlier plan to bake
    only 4–6 frames per action was rejected as not enough to read as real motion).
  - **`DungeonStageCameraTool.swift`** now plays these back for real instead of showing the
    old single frame-08 still: `StageFrameLibrary` indexes the frame folder, and the tool
    got live pickers so any character/action can be dropped into either the az-35 ("foe")
    or az-245 ("party") stage-corner slot and compared against the locked camera.
  - **Pitfall worth remembering**: the first implementation animated the swap via a
    `CAKeyframeAnimation` on `SCNMaterial.diffuse.contents` with `NSImage` values — this
    crashes. SceneKit's CA→C3D animation bridge only understands animatable scalar/vector/
    color types, not opaque images, and throws inside
    `-[SCNMaterial addAnimationPlayer:forKey:]` the instant the scene's `pointOfView` is
    set (confirmed via the crash log's exception backtrace, not guessed). The fix was an
    `SCNAction` sequence (`.run` to swap the texture + `.wait` per frame, `repeatForever`)
    driven off the node instead of the material — the supported way to animate opaque
    `contents` in SceneKit. If a future pass ever wants to go back to CA-driven playback for
    performance, this is why it can't be a plain keyframe animation.
  - **RESOLVED same night — verified by eye, and two real bugs found and fixed.** Checked
    against the actual running tool (not just "compiles"):
    1. **Playback was in slow motion.** The tool played every billboard back at a hardcoded
       12fps regardless of how the frames were sampled. Walk/attack/hit-react/wince were
       baked at full native rate (24fps, step 1 — see the frame-policy note above), so
       playing them at 12fps ran everything at exactly half real speed. Fixed by having
       `StageFrameLibrary` read the actual gap between consecutive baked frame indices (the
       `_f###` in each filename) and derive playback fps as `24 / step` per selection,
       instead of assuming one fixed rate for every action.
    2. **Forest Flicker and Pangolin were clipping into the stage platforms.** Root cause:
       each character's own baked frame puts its ground-contact line at a different
       fraction of the frame height — a function of that file's `CAM_TARGET` z divided by
       its camera's ortho scale (Ram 0.196, Flicker 0.335, Pangolin 0.305 — Flicker's
       ground line sits nearly twice as far from frame-center as Ram's). The tool's two
       billboards had one fixed node-y each, tuned by eye against whichever character
       happened to be loaded when it was last dragged into place, so swapping characters
       via the picker silently broke the ground alignment for anyone but that one
       character. Fixed by reading the real platform-top heights straight out of
       `DungeonStageScene.build()` (foe platform top = 0.4, party floor top = 0.0 —
       `DungeonStage3D.swift`) and repositioning each billboard's node to
       `platformTop + groundOffsetFraction × planeHeight` every time a selection changes,
       using the per-character fractions above instead of one number for everyone.
    Both confirmed fixed against the live tool, not just by reasoning about the code.
  - Still open, unchanged from before: frame-selection and sheet assembly per §5/§9 haven't
    started (everything baked so far is stage-preview output, not sprite-sheet export); the
    asymmetric-accessory mirroring question (two bullets up) now applies to Pangolin's
    back-key too, visible in the renders; and Pangolin's several versioned attack/special
    actions haven't been reviewed against each other to pick a canonical one.
  - **Separate, adjacent backlog surfaced in conversation tonight, not yet in this doc's
    scope**: the Cache Warren's actual bestiary needs animation too — Snag has a model but
    no rig/actions yet, and the delve's final boss has a model with animation not yet
    started either. Neither has a `.blend` in `worklings-blender-work/` yet, so there's
    nothing for `blender_stage_bake.py` to run against until that rigging pass happens.
  - **Open discussion, raised tonight, not decided: shipped frame budget.** Tonight's bake
    is 1312 frames across three characters and lives outside git — fine for a dev-preview
    tool, but a real question once frame-selection/sheet-assembly (bullet above) actually
    starts: how many frames per action does the *shipped* game need once more characters,
    foes, and combat effects stack up? §9 already caps a foe's attack at 8 frames played
    once at 12fps and idle at a small loop, which is nowhere near tonight's raw per-action
    counts (32–72) — so the shipping budget is probably fine by design, but it hasn't been
    checked against this session's real numbers yet. Pick up tomorrow.
  - **Raised tonight, thought settled — reopened 2026-08-27.** The stage room today is
    flat-colored `SCNBox` blockout geometry (`DungeonStage3D.swift`), obviously
    placeholder, which raised the question of whether a baked 2D image could stand in
    for it the way sprites do for characters. This entry originally said no, settled,
    room stays real 3D geometry with PBR materials. **That call is back open** — real
    reference art (a full painted cave scene, camera-matched) made the flat-backdrop
    case concrete enough to actually test rather than assume against. See
    `docs/design/dungeons.md`'s Open item 9 for the live checklist (contact shadow done,
    atmospheric depth/render-fidelity/directional-light matching still open) and the
    2026-08-27 changelog entries for what's been tried so far in the Dungeon Stage
    Camera Tool. Not re-decided yet either way.
  - **New reusable technique, 2026-08-27: curvature-driven emissive materials.** Built
    for the Tempest Ram's electrical crackle (full story in dungeons.md's "Effects —
    baked vs. live" and the same-day changelog entries) — a per-vertex curvature
    attribute (bmesh, vertex-normal-vs-neighbor-average, percentile-clipped to isolate
    real ridges from noise) drives an emissive Mix Shader, with frame-number drivers for
    timing so the effect needs no per-frame authoring. Worth reaching for again for any
    future character effect that has to trace the mesh's actual contours rather than
    sit in flat image space — Forest Flicker's glitch theming is the next likely
    candidate, whenever its animation set is far enough along to be worth juicing.
