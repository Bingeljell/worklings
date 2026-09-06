# Character Rigging Pipeline — Trellis Mesh to a Weighted Rigify Rig

> Evolving doc, not a frozen spec — see [docs/README](../README.md).

How a raw Trellis reconstruction becomes a rigged, weighted character that Blender
can pose. This doc ends exactly where
[character asset pipeline](character-asset-pipeline.md) begins: that one takes a
finished `.blend` and produces a `.glb` for Godot; this one gets you the finished
`.blend`.

Written against Blender **5.2 LTS**, worked out on the dungeon scamp (`dungeon-scamp-rigify`)
on 2026-09-06. Every number below is measured from that model, not estimated.

## The short version

1. Delete duplicate imports, clear the glTF parent empty, apply all transforms.
2. **Merge by Distance** — this is the step that makes everything else possible.
3. Recalculate normals outside.
4. Fit the metarig. Add any bones Rigify's template doesn't give you.
5. Generate Rig.
6. Parent mesh to rig **With Automatic Weights**.
7. Audit the weights numerically. Fix unweighted verts. **Limit Total 4, then Normalize All.**

---

## 1. The split-vertex trap

A Trellis mesh imported through glTF looks catastrophically broken and almost never is.
The scamp arrived as:

| | count |
| --- | --- |
| vertices | 201,338 |
| triangles | 288,427 |
| non-manifold edges | 106,597 |
| loose parts | **4,791** |

Four thousand disconnected shells reads like a shredded mesh. It isn't. glTF stores
attributes per-vertex, so every UV and normal seam becomes a duplicated vertex, and
Blender's importer does not merge them by default. For a closed triangle mesh
`V ≈ T/2`, so 288k triangles should give ~144k vertices; the extra 57k are seam
duplicates.

**Edit Mode → `A` → `M` → By Distance, at the default `0.0001`:**

| | before | after |
| --- | --- | --- |
| vertices | 201,338 | 144,035 |
| non-manifold edges | 106,597 | **824** |
| loose parts | 4,791 | **28** |
| largest part | 1,753 verts | **140,775 verts (97.7%)** |

One step, and the mesh goes from unusable to 97.7% a single connected shell. Follow it
with `Shift+N` (Recalculate Normals Outside).

This is the same root cause as the weld in the
[export pipeline](character-asset-pipeline.md#what-the-script-does-and-why-each-step-exists)
and the [Snag investigation](snag-mesh-investigation.md) — split vertices at seams,
breaking an operation that needs a connected surface. There it blocks Decimate; here it
blocks bone heat weighting.

## 2. Transforms, before anything else

A glTF import arrives parented to a `world` empty (the Y-up→Z-up correction) and
usually carries an unapplied scale. The scamp was at **scale 2.0**. Importing twice
leaves a full duplicate stack (`geometry_0.001` + `world.001`) sitting in the same
place, invisible until you wonder why edits do nothing.

1. Delete the duplicate mesh **and its empty**.
2. `Alt+P` → **Clear Parent and Keep Transformation**.
3. `Ctrl+A` → **All Transforms**.

Neither step moves the mesh in world space, so a metarig already fitted to it stays
fitted. Unapplied scale is the classic cause of weights that look correct in Blender and
explode in Godot.

## 3. metarig vs rig — the thing that confuses everyone

`Add → Armature → Rigify → Basic Quadruped` creates **one** object, named `metarig`.
Generate Rig then creates a **second** object named `rig`. They overlap exactly, both
selectable, and clicking a bone in the viewport selects whichever object owns that
particular bone — which is why editing feels haunted.

- **`metarig`** is the **input**. Bone positions, parenting, `rigify_type` annotations.
  This is what you maintain.
- **`rig`** is **generated output**. Disposable. Rebuilt from scratch on every generate.

**Never hand-edit `rig`.** Anything you change there dies at the next generate.

Two settings worth checking:

- `metarig.data.rigify_target_rig` must point at `rig`. If it's unset, Generate spawns a
  new `rig.001` and any mesh bound to the old one silently keeps following a stale
  armature.
- Hide whichever armature you aren't editing (Outliner eye icon). This costs nothing and
  removes an entire category of confusion.

**A failed Generate leaves rubble.** When generation aborts on an error it does not roll
back — the scamp's `rig` went from 283 bones to a 38-bone stump. That's recoverable
(fix the metarig, generate again) but don't diagnose anything from a `rig` in that state.
A healthy generate has `DEF-`, `ORG-`, `MCH-` and control bones. Check the count went
*up*.

## 4. Adding bones the template doesn't give you

The basic quadruped metarig has no ears. Adding a chain is the fiddliest part of the
whole pipeline, and naming is the least of it.

### Naming alone does nothing

A bone called `ear.L` with no `rigify_type` generates a `DEF-ear.L` that deforms the
mesh but has **no control bone** — it deforms and you cannot animate it. Two things are
required: a **parent**, and a **Rigify Type**.

You can read the rule off the stock metarig. `front_shin.L`, `front_foot.L` and
`front_toe.L` all have an empty type because `limbs.front_paw` on `front_thigh.L` claims
the whole *connected* chain beneath it. Same for `spine.010`/`spine.011` under
`spines.super_head`. **Untyped-but-connected gets absorbed by the parent's rig type.**
Ears branch off the head rather than continuing a chain, so they need their own type.

### Which type

| type | gives you |
| --- | --- |
| `limbs.simple_tentacle` | an FK chain with per-joint tweaks — ears, antennae, tails |
| `basic.super_copy` | one rigid bone, rotates at its base |

Only the **first** bone of a chain gets a type. The rest stay blank.

### Naming convention: both forms work

`ear.L.001` and `ear.001.L` are parsed **identically** by Rigify — it strips a trailing
`.###` before looking for the side token. Verified directly:

```
ear.L.001   -> mirror=ear.R.001   base='ear.001'  side=LEFT
ear.001.L   -> mirror=ear.001.R   base='ear.001'  side=LEFT
```

`.L` does *not* have to be the final suffix. `ear.001.L` is Rigify's house style (its
human metarig uses `f_index.01.L`) but that's taste.

**What does matter is that the numbering follows the chain order.** Subdivide reuses
indices freely, and the scamp ended up with `ear.L → ear.L.004 → ear.L.002 → ear.L.003`.
Rigify walks the chain by parenting so it generates fine, but the resulting controls are
numbered out of order and miserable to animate against.

### The parenting trap

`Ctrl+P → Keep Offset` with the **whole chain selected** re-parents every bone to the
target, flattening the chain into four siblings. The symptom is
`Input to rig type must be a chain of 2 or more bones` on generate. Parent the chain
*root* to the head; the rest keep their chain parenting.

Symptoms and where to look:

| symptom | cause |
| --- | --- |
| "chain of 2 or more bones" | chain flattened onto the parent, or type on a childless bone |
| bone deforms but has no control | `rigify_type` not set |
| controls numbered out of order | subdivide reused indices |

### Trellis output is not symmetric

Turn **X-Axis Mirror off** before placing bones. The scamp's ears were genuinely
asymmetric — the +X tip at `(0.543, -0.399, 1.174)`, the −X tip at
`(-0.329, -0.725, 1.148)` where a mirror would put it near `(-0.543, -0.399, 1.174)`.
Reconstructions are rarely symmetric, and mirroring will fight you.

### Verify coverage before you bind

Cheap and worth doing: compare the deform-bone bounding box against the mesh bounding
box, and check what fraction of vertices sit far from any bone. On the scamp, 11.2% of
verts sat above the topmost deform bone — which turned out to be the ears, correctly
identified as needing bones. Bones stopping short of geometry is the thing to catch
*before* weights are baked in.

## 5. Weighting

**Try `Ctrl+P → With Automatic Weights` first.** Once the mesh is one connected shell
after the merge, bone heat usually solves. On the scamp it worked first try and the
proxy route below was never needed.

If it fails with `Bone Heat Weighting: failed to find solution for one or more bones`,
fall back to a voxel proxy:

1. Duplicate the mesh (`Shift+D`, `Esc`), name it `proxy`.
2. **Remesh** modifier → Voxel, size `0.01`. Apply.
   Check the legs stay separate volumes — if the voxel size fuses limbs to the body, the
   weights bleed across them. Drop to `0.006` if so.
3. Auto-weight the *proxy* to the rig.
4. On the **real** mesh: **Data Transfer** modifier, Source = `proxy`, Vertex Data →
   Vertex Group(s), mapping **Nearest Face Interpolated**, click **Generate Data Layers**
   (without it the target has no groups and you get nothing). Apply.
5. Parent real mesh to rig with plain **Armature Deform**. Delete the proxy.

The Voxel Heat Diffuse Skinning addon is **not installed** and is not required — the
proxy route is the free equivalent.

## 6. Audit the weights numerically

Posing and squinting does not find these. Four checks, all scriptable over
[`blender_rpc.py`](../../scripts/blender_rpc.py):

| check | scamp, as generated | why it matters |
| --- | --- | --- |
| unweighted vertices | **492** | frozen in world space; stretched spikes on the first pose |
| deform bones with zero influence | 0 | a bone that moves nothing is a rig error |
| max influences per vertex | **17** | glTF/Godot expects 4 |
| weights summing to 1.0 | — | unnormalized weights shrink the mesh when posed |

### Unweighted vertices

Leftovers from small floating islands. `Select → Select All by Trait → Ungrouped
Vertices` catches them **only if they're in no group at all** — verts assigned at weight
zero slip through, and then assigning by nearest bone is the reliable fix.

Nearest *bone* is a blunt instrument: on the scamp it sent 226 jaw vertices to
`DEF-breast.L`, because the head is lowered and the chest bone is genuinely the closest
thing to the chin. **Inherit from the nearest already-weighted vertex instead** — on a
144k-vert mesh that neighbour is a millimetre away and its weights are locally correct.

### Weight bleed from added bones

Bone heat gives a bone volume proportional to how buried it is. The scamp's ear-base
bones, anchored inside the skull, took **18,373 vertices at weight > 0.2** — against
~8,200 vertices of actual ear. Rotating an ear dragged skull with it.

The fix that generalises: a vertex keeps its ear weight only where an ear bone is
genuinely its **nearest** bone, blending out over a band where another bone is clearly
closer, with the freed weight handed to that nearer bone. Self-calibrating — no magic
radius to tune per character.

### Limit and normalize, in that order

In Weight Paint mode:

1. **Weights → Limit Total**, limit `4`
2. **Weights → Normalize All**

**Order matters** — normalizing first and limiting second breaks the normalization.
The scamp shed 222,603 excess weights and landed at:

```
unweighted verts:       0
max influences/vert:    4
influences histogram:   {1: 3672, 2: 20846, 3: 16610, 4: 102910}
verts not summing to 1: 0
deform bones w/ zero influence: 0
```

That's the state to hand to [the exporter](character-asset-pipeline.md).

## Gotchas, learned the hard way

**Shortcuts route to the area under the mouse.** Select an object in the Outliner and
press `Alt+P` with the cursor still over the Outliner and nothing happens. Hover the 3D
viewport. This burned three separate steps in one session. Every shortcut has a menu
equivalent — `Object → Parent → …`, `Object → Apply → …`, `Mesh → Merge → …` — and the
menus are worth using while learning.

**On macOS, Blender uses `Ctrl`, not `Cmd`.** `Cmd+A` is Select All and will do the
wrong thing where `Ctrl+A` means Apply. Also check **Emulate 3 Button Mouse** is off in
Preferences → Input: when on, it eats `Alt` and every `Alt+key` shortcut silently dies.

**`A` doing nothing usually means everything was already selected.** Freshly imported
meshes come in fully selected.

**In Edit Mode, `armature.data.bones` is stale.** The live data is
`armature.data.edit_bones`. Reading the wrong one produced a completely false picture of
the ear parenting — bones that were correctly connected reported as flat siblings. Any
script that inspects an armature must branch on `object.mode`.

**`view_layer.update()` does not re-evaluate a multi-hop constraint chain.** Rigify
drives deform through `control → tweak → ORG → DEF`, four hops. A script that poses a
control and immediately reads the DEF matrix sees stale values and reports zero
deformation on a perfectly good rig. **Posing must be verified in the viewport by a
human**, not by script. Same applies to `render.opengl` immediately after a pose change.

**`bpy.ops` needs a context override over RPC**, and some operators
(`object.select_all`) fail their poll even with one — use direct API calls
(`obj.select_set()`) instead. See the [`blender_rpc.py`](../../scripts/blender_rpc.py)
docstring.

**Save early.** The scamp session ran for hours on an unsaved file
(`bpy.data.filepath` empty).

## Not yet settled

- **None of the audit scripts are in the repo.** The unweighted-vertex check,
  zero-influence-bone check, influence histogram and the bleed fix were written ad hoc
  in a scratchpad and are gone. They are the reusable part of this doc and belong in
  `scripts/` alongside the exporter, indexed in [tools](tools.md).
- **The bleed fix over-reached.** Identifying previously-fixed vertices by the signature
  "single group at weight 1.0" matched 1,934 vertices where only 492 were the target, so
  ~1,442 legitimately rigid-weighted vertices were also re-assigned from their nearest
  neighbour. Almost certainly harmless at this vertex density, but it is the kind of
  broad match that needs an explicit index list instead.
- **Ear rig quality is unverified.** The chain generates and the constraint wiring is
  correct, but the actual deformation has not been reviewed by a human in the viewport.
- **Whether the front half of this pipeline should be scripted at all.** Steps 1 and 2
  are entirely mechanical and identical for every Trellis import. Steps 3 and 4 need
  human judgement and probably always will.
