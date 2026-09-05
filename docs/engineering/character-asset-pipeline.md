# Character Asset Pipeline — Blender to Godot

> Evolving doc, not a frozen spec — see [docs/README](../README.md).

How a rigged Workling or foe becomes a file the game loads. Implemented in
[`scripts/blender_export_character.py`](../../scripts/blender_export_character.py),
run against a live Blender over the `execute_code` RPC.

Applies to the **dungeon and character screen** (live 3D). The desktop pet is still
baked sprites — see [bake spec](../design/bake-spec.md).

## The standard (locked 2026-09-01)

Reviewed in Godot at the locked Cache Warren camera *and* at close range, on the real
lit stage — not in Blender's viewport, whose flat shading hides exactly the detail loss
that matters.

| | value | why |
| --- | --- | --- |
| **Triangles** | **20,000** | 40k and 20k are indistinguishable from the 283k original. 10k and below visibly flattens the Ram's fleece. Note: *triangles*, not vertices — 20k tris is roughly 10k verts. |
| **Textures** | **1024 × 1024** | 4096, 2048 and 1024 are identical at dungeon distance, and 1024 holds up closer than the character screen goes. |
| **Action length** | **≤ 44 frames** | Animation data is the real size driver — see below. |

## Running the exporter

Run the exporter in a separate background Blender process. It opens and simplifies an
in-memory copy of the source, so the authored `.blend` remains the editable master and
the currently open Blender session is not replaced.

```bash
/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python-exit-code 1 \
  --python scripts/blender_export_character.py \
  -- /absolute/path/character.blend /absolute/path/character.glb \
  --keep-action Walk \
  --keep-action Idle \
  --keep-action Attack \
  --keep-action Take_Damage
```

`--keep-action` is repeatable because every character owns a different action set. Omit
it to export every Action in the file. A requested name that is not present fails before
the exporter removes anything. The final JSON report includes the animation names read
back from the GLB, along with skin and joint counts; an export with no skin or no
animations is rejected.

Complex or non-manifold meshes may not reach the nominal Collapse ratio in one pass.
The exporter measures the result and applies additional Decimate passes until the real
triangle count reaches the requested budget; it fails if Blender cannot reach that
budget. `--allow-over-budget` is an explicit review-only escape hatch for a topology
floor: the GLB is written and `tri_budget_met` remains `false` in the report. It also
repairs invalid mesh data before export rather than passing glTF a mesh Blender itself
considers invalid, including a second validation after destructive simplification.
`--python-exit-code 1` matters because Blender otherwise exits zero
after a Python traceback, which makes a failed export look successful to automation.

Optional delivery controls:

```bash
--tri-budget 20000       # cannot go below the reviewed 20k floor
--texture-size 1024      # default
--no-texture-resize      # keep authored image dimensions
--allow-over-budget      # explicit exception when Collapse hits a topology floor
```

The output is a runtime/review `.glb`, not a replacement for the `.blend`: glTF carries
the skinned mesh, deform skeleton and baked actions, but not Rigify control widgets,
constraints or a reliably round-trippable Blender control rig.

Result across the current roster: **27 MB for four characters.**

| | before | after |
| --- | --- | --- |
| Tempest Ram | 23.0 MB | 7.1 MB |
| Forest Flicker | 10.4 MB | 3.3 MB |
| Clockwork Pangolin | 39.3 MB | 12.6 MB |
| Snag | 15.7 MB (.blend) | 3.9 MB |

## The Snag: the first character over the triangle budget

The Snag stops at **37,826 triangles**, near twice the 20k standard, and it was exported
that way with `--allow-over-budget` after review.

Collapse cannot go further. The first pass at ratio 0.069 lands at 39,285; a second at
0.509 moves it to 38,263, a 3% gain, and the exporter stops rather than keep damaging a
topology it cannot simplify. **This is not the seam problem** the weld exists to fix —
the weld already takes the mesh from 263,964 boundary edges to 787, leaving it
essentially manifold. The floor is in the root ribbons themselves.

It is affordable anyway, because triangles are not what costs: at 27 bones and four
clips of 23–33 frames, the Snag is **3.9 MB — lighter than the 20k-triangle Ram's 7.1
MB**. Which is the same finding as everywhere else in this doc, from the other side:
animation data and joint count set the size, not geometry.

Worth a look in Blender if the roots are ever reauthored. Not worth blocking the
character on.

**Downscale on export, never in the source.** Keep the .blend authored at 2048 or higher
and let the exporter reduce it. Downscaling is one-way and the .blend is the only place
the original exists — the same principle as decimating on export rather than in the
source mesh. Re-exporting at a higher resolution later (a hero render, marketing art, a
character screen that turns out to want more) must stay possible.

## Animation data is the size driver, not geometry or textures

The least intuitive finding, and the one that changes where effort goes.

Cost scales with **frames × 283 joints**, so a long clip on a Rigify skeleton is
expensive. The Pangolin's actions run to 120 frames where the Ram's and Flicker's sit at
32–44, which is why it carries roughly 12 MB of animation against the Ram's 4 MB — and
why it stayed heavy even after its 4096 textures came down to 1024.

Decimating below 20k triangles is close to pointless by comparison: 40k → 5k saved 2.5 MB
while visibly costing quality, where trimming the Ram's 17 actions to its real four saves
~3.6 MB and costs nothing.

**The cap is guidance, not a rule.** It warns and never fails, and it is not a reason to
compromise an animation that needs the length — the vision wins. `export_character`
returns `actions_over_frame_cap` so the cost is visible when the trade is being made.

### OPEN: the cap is probably wrong for idles

Applied uniformly, a 44-frame cap makes an idle loop 1.8 seconds at 24fps. The Ram's
`RamIdle_Breathe_Paw` is deliberately 144 frames (6s), and a breathing loop that short may
well read as twitchy. The cap is a good fit for attacks, winces and impacts — beats the
player watches once — and a poor fit for something looping continuously in the background.

Worth splitting into two numbers rather than one. Not decided; flagged so the cap isn't
applied blindly to idles and the animation work redone.

## What the script does, and why each step exists

All learned the hard way on 2026-09-01. Every one of these failed **silently**.

1. **Unhide before selecting.** `select_set()` does nothing on a hidden object, and
   `use_selection` then drops it with no error. The Flicker and Pangolin rigs were hidden
   in their .blend files (reasonable while modelling — you hide the rig to see the
   character), so their first exports came out as a mesh with **no skeleton and no
   animations**, reported as success. The Ram worked only because its rig was visible.
2. **Weld before decimating.** The meshes carry tens of thousands of split vertices at
   UV/normal seams — the Ram: 57k of 198k. Collapse will not simplify across a seam it
   reads as a boundary, so skipping the weld gives an unevenly dense result.
3. **Apply the decimate destructively.** The glTF exporter writes a skinned mesh's *base*
   topology and ignores live modifiers, so a Decimate left in the stack exports at full
   resolution without complaint.
4. **Bypass emission-mix materials.** glTF cannot represent a curvature-driven emission
   blend, so it flattens the mix into a uniform `emissiveFactor`. The Ram's crackle came
   through as solid blue at 3× strength across the whole mesh, drowning the texture.
   Per-character effects get rebuilt as runtime shaders; the .blend keeps its nodes.
5. **Y-up.** Godot is Y-up, Blender is Z-up. Exporting Y-up avoids a runtime rotation on
   every character.
6. **Verify the result.** The exporter reports success whether or not a skeleton made it
   in, so the script reads the `.glb` JSON back and raises if `skins` or `animations` is
   zero. This is the only honest check that the file is usable.

## Godot-side gotcha

**Changing a `.glb` does not always invalidate Godot's cached import.** A model can keep
rendering with its old material after the source file changes. `rm -rf .godot/imported`
then reopen forces a clean reimport.

## Not yet settled

- **Action trimming.** The Ram ships 17 actions, most of them iteration history
  (`RamWalk_Natural_Baseline`, `_PreLoop`, `_LoopTangentAttempt`, `RigifyWalk`). The
  Flicker already has a clean set of five. Trimming needs a human call on which variant
  is the good one; `keep_actions` takes the set once decided.
- **The idle frame cap**, above.
- **Per-character effects** (the Ram's crackle, the Flicker's glitch, the Pangolin's rune
  glow) must be rebuilt as Godot shaders. glTF does carry the vertex colours USD dropped,
  so `Crackle_Curvature` arrives intact — but a texture-baked curvature map would decouple
  effect detail from mesh density, which matters now that we decimate to 20k.

## Repo gotcha: `scripts/committer` force-adds

`scripts/committer` runs `git add --force`, which **bypasses `.gitignore`**. Passing it a
directory stages everything underneath, ignore rules included. This put 33.9 MB of
Godot's `.godot/` import cache — 104 files that regenerate on first open — into the repo
in `dd5f614`, despite a correct ignore rule sitting right there.

Pass explicit file paths, not directories, when committing inside a Godot project.
