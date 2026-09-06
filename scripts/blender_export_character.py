"""Export a rigged character from Blender to glTF for Godot.

The pipeline for every Workling and foe. Run against a live Blender over the
`execute_code` RPC (see docs/design/dungeons.md), one character at a time.

Why each step exists — all of these were learned the hard way on 2026-09-01:

* **Weld before decimating.** The meshes carry tens of thousands of split
  vertices at UV/normal seams (the Ram: 57k of 198k). Collapse will not
  simplify across a seam it reads as a boundary, so without a weld the result
  is unevenly dense.
* **Apply the decimate destructively.** The glTF exporter writes a skinned
  mesh's *base* topology and silently ignores live modifiers, so a Decimate
  left in the stack exports at full resolution with no warning.
* **Bypass emission-mix materials.** glTF cannot represent a curvature-driven
  emission blend, so it flattens the mix into a uniform `emissiveFactor` and
  glows the entire mesh (the Ram's crackle came through as solid blue at 3x).
  Per-character effects are rebuilt as runtime shaders; the .blend is left
  untouched.
* **Unhide before selecting.** `select_set()` silently does nothing on a hidden
  object, and `use_selection` then drops it with no error. The Forest Flicker
  and Clockwork Pangolin rigs are hidden in their .blend files, so their first
  exports came out as a mesh with **no skeleton and no animations** and no
  warning explaining why — while the Ram, whose rig happens to be visible,
  worked. Always unhide and clear `hide_select` first.
* **Y-up.** Godot is Y-up and Blender is Z-up. Exporting Y-up avoids a runtime
  rotation on every character.
* **Trim actions.** Experimental variants dominate the file. Animation data,
  not geometry, is the size floor: the Ram at 40k tris is 9.2 MB with all 17
  actions and 5.6 MB with the four the game uses.

**The standard, locked 2026-09-01** after reviewing every level in Godot at the
locked camera and at close range:

* **20,000 triangles** (not vertices — 20k tris is roughly 10k verts). 40k and
  20k are indistinguishable from the 283k original; 10k and below visibly
  flattens the Ram's fleece.
* **1024x1024 textures.** 4096, 2048 and 1024 are identical at dungeon
  distance, and 1024 holds up closer than the character screen goes. Downscaled
  *on export* — keep the .blend authored at 2048 or higher, because
  downscaling is one-way and the source is the only place the original exists.
* **Actions of 44 frames or fewer.** Animation data, not geometry or textures,
  is the real size driver: cost scales with frames x 283 joints. The Pangolin's
  120-frame actions make it ~12 MB of animation against the Ram's 4 MB, which
  is why it stays heavy even at 1024 textures. Exceeding the cap warns rather
  than fails, so existing characters still export.
"""

import argparse
import json
import os
import sys

import bmesh
import bpy

TRI_BUDGET = 20_000       # triangles, not vertices
TRI_FLOOR = 20_000

# Per-character overrides, keyed by the exported .glb basename.
#
# 20k is the right default and three of the four characters hit it exactly. The
# Snag cannot: it is a braid of thin tubes, and every QEM collapse either
# punches through a tube wall or fuses two neighbouring coils, so Collapse
# stalls around 38k no matter how many passes it is given. Forcing it lower does
# not produce a smaller Snag, it produces a shattered one — at 20k the coils
# collapse into spikes and the amber eye disappears, while 60k keeps the braid
# intact for 1.8 MB more. Measured 2026-09-06; see docs/engineering/
# snag-mesh-investigation.md for the related pipeline bug.
TRI_BUDGETS = {
    "snag": 60_000,
}
TEXTURE_SIZE = 1024       # square, per map
FRAME_CAP = 44            # per action; longer clips dominate file size


def _skinned_mesh(scene):
    """The heaviest mesh carrying an Armature modifier — the character itself,
    not a proxy, a widget, or a prop."""
    best = None
    for obj in scene.objects:
        if obj.type != "MESH":
            continue
        if not any(m.type == "ARMATURE" for m in obj.modifiers):
            continue
        if best is None or len(obj.data.vertices) > len(best.data.vertices):
            best = obj
    return best


def _bypass_emission_mix(mesh_obj):
    """Route the Principled BSDF straight to the material output, skipping any
    Mix Shader. Returns the names of materials it rewired."""
    rewired = []
    for mat in mesh_obj.data.materials:
        if not mat or not mat.use_nodes:
            continue
        tree = mat.node_tree
        out = next((n for n in tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
        bsdf = next((n for n in tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if out is None or bsdf is None:
            continue
        surface = out.inputs["Surface"]
        if surface.is_linked and surface.links[0].from_node.type == "BSDF_PRINCIPLED":
            continue  # already clean
        for link in list(surface.links):
            tree.links.remove(link)
        tree.links.new(bsdf.outputs["BSDF"], surface)
        rewired.append(mat.name)
    return rewired


def _make_selectable(obj):
    """A hidden object cannot be selected, and `use_selection` then drops it
    from the export without complaint — see the module docstring."""
    obj.hide_viewport = False
    obj.hide_select = False
    try:
        obj.hide_set(False)
    except RuntimeError:
        pass  # not in the active view layer; nothing to unhide


def _tris(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def _decimate_to_budget(geo, tri_budget, win, max_passes=1):
    """Apply Decimate passes, destructively, to reach the requested budget.

    **One pass by default.** This used to allow four, on the reasoning that a
    single Collapse is not guaranteed to hit its nominal ratio, so the remainder
    could be fed into another pass. That reasoning is right about the arithmetic
    and wrong about the consequence: a mesh that misses its target is one
    Collapse cannot simplify, and running it again simply collapses an already
    damaged mesh. The Snag shipped that way — four passes, still 1.9x over
    budget, and shredded into spikes. Its single-pass export at a reachable
    budget is 1,745 triangles larger and looks like the concept art.

    So a miss is now a reportable fact rather than something to grind at. The
    caller raises unless `--allow-over-budget` says otherwise.
    """
    passes = []
    for pass_number in range(1, max_passes + 1):
        before = _tris(geo.data)
        if before <= tri_budget:
            break
        ratio = tri_budget / before
        dec = geo.modifiers.new(f"ExportDecimate{pass_number}", "DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = ratio
        geo.modifiers.move(geo.modifiers.find(dec.name), 0)  # above Armature
        with bpy.context.temp_override(
            window=win, screen=win.screen, object=geo,
            active_object=geo, selected_objects=[geo],
        ):
            bpy.ops.object.modifier_apply(modifier=dec.name)
        after = _tris(geo.data)
        passes.append({
            "pass": pass_number,
            "ratio": round(ratio, 6),
            "tris_before": before,
            "tris_after": after,
        })
        # Stop rather than repeatedly damaging a topology that Collapse cannot
        # simplify. The caller decides whether an over-budget result is allowed.
        if after >= before or (before - after) / before < 0.05:
            break
    return passes


def export_character(blend_path, out_path, keep_actions=None, tri_budget=None,
                    texture_size=TEXTURE_SIZE, allow_over_budget=False):
    """Open `blend_path`, simplify, and write a Godot-ready .glb to `out_path`.

    `keep_actions` is a set of action names to ship; None keeps everything.
    `texture_size` downsizes every image to that square resolution; None keeps
    them as authored. Textures dominate file size once geometry is decimated —
    the Pangolin's 4096 maps are 26.9 MB of its 39 MB.
    """
    if tri_budget is None:
        tri_budget = TRI_BUDGETS.get(
            os.path.basename(out_path).rsplit(".", 1)[0], TRI_BUDGET
        )
    if tri_budget < TRI_FLOOR:
        raise ValueError(
            f"{tri_budget} is below the {TRI_FLOOR} floor — see the module docstring"
        )
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    scene = bpy.context.scene
    geo = _skinned_mesh(scene)
    if geo is None:
        raise RuntimeError(f"no skinned mesh found in {blend_path}")
    rig = next(m.object for m in geo.modifiers if m.type == "ARMATURE")
    _make_selectable(geo)
    _make_selectable(rig)

    report = {"blend": os.path.basename(blend_path), "mesh": geo.name, "rig": rig.name}
    report["rewired_materials"] = _bypass_emission_mix(geo)

    if keep_actions is not None:
        keep_actions = set(keep_actions)
        available = {action.name for action in bpy.data.actions}
        missing = sorted(keep_actions - available)
        if missing:
            raise ValueError(
                f"requested actions are not present in {blend_path}: {missing}; "
                f"available actions: {sorted(available)}"
            )
        report["actions_before"] = len(bpy.data.actions)
        for action in list(bpy.data.actions):
            if action.name not in keep_actions:
                action.use_fake_user = False
                bpy.data.actions.remove(action)
    report["actions"] = sorted(a.name for a in bpy.data.actions)
    over = {
        a.name: int(round(a.frame_range[1] - a.frame_range[0] + 1))
        for a in bpy.data.actions
        if (a.frame_range[1] - a.frame_range[0] + 1) > FRAME_CAP
    }
    if over:
        report["actions_over_frame_cap"] = over

    if texture_size is not None:
        resized = []
        for image in bpy.data.images:
            if image.size[0] > texture_size or image.size[1] > texture_size:
                resized.append(f"{image.name} {image.size[0]}->{texture_size}")
                image.scale(texture_size, texture_size)
        report["textures_resized"] = resized

    mesh = bmesh.new()
    mesh.from_mesh(geo.data)
    bmesh.ops.remove_doubles(mesh, verts=mesh.verts, dist=1e-5)
    mesh.to_mesh(geo.data)
    mesh.free()
    report["mesh_repaired"] = bool(
        geo.data.validate(verbose=False, clean_customdata=False)
    )
    geo.data.update()
    report["tris_source"] = _tris(geo.data)

    win = bpy.context.window_manager.windows[0]
    if report["tris_source"] > tri_budget:
        for obj in bpy.context.view_layer.objects:
            obj.select_set(False)
        geo.select_set(True)
        bpy.context.view_layer.objects.active = geo
        report["decimation_passes"] = _decimate_to_budget(geo, tri_budget, win)
    report["mesh_repaired_after_decimate"] = bool(
        geo.data.validate(verbose=False, clean_customdata=False)
    )
    geo.data.update()
    report["tris_exported"] = _tris(geo.data)
    report["tri_budget"] = tri_budget
    report["tri_budget_met"] = report["tris_exported"] <= tri_budget
    if not report["tri_budget_met"] and not allow_over_budget:
        raise RuntimeError(
            f"decimation stopped at {report['tris_exported']} triangles, above "
            f"the requested {tri_budget} budget; passes: "
            f"{report.get('decimation_passes', [])}. Use --allow-over-budget "
            "only after reviewing this character's exported mesh."
        )

    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    geo.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    out_dir = os.path.dirname(os.path.abspath(out_path))
    os.makedirs(out_dir, exist_ok=True)
    with bpy.context.temp_override(
        window=win, screen=win.screen, object=rig,
        active_object=rig, selected_objects=[geo, rig],
    ):
        bpy.ops.export_scene.gltf(
            filepath=out_path, export_format="GLB", use_selection=True,
            export_animations=True, export_animation_mode="ACTIONS",
            export_skins=True, export_yup=True,
            export_optimize_animation_size=True,
        )
    report["mb"] = round(os.path.getsize(out_path) / 1_000_000, 2)
    report.update(_verify(out_path))
    if report["glb_skins"] == 0 or report["glb_animations"] == 0:
        raise RuntimeError(
            f"{out_path} exported with skins={report['glb_skins']} "
            f"animations={report['glb_animations']} — the armature or its actions "
            f"did not make it into the file. Check that the rig is visible and "
            f"selectable in the .blend."
        )
    return report


def _verify(glb_path):
    """Read the .glb's JSON chunk back and confirm a skeleton and animations are
    present. The exporter reports success either way, so this is the only
    honest check that the file is usable."""
    import struct

    with open(glb_path, "rb") as handle:
        struct.unpack("<III", handle.read(12))
        chunk_len, _ = struct.unpack("<II", handle.read(8))
        doc = json.loads(handle.read(chunk_len).decode("utf-8"))
    skins = doc.get("skins", [])
    animations = doc.get("animations", [])
    return {
        "glb_skins": len(skins),
        "glb_joints": len(skins[0]["joints"]) if skins else 0,
        "glb_animations": len(animations),
        "glb_animation_names": [animation.get("name") for animation in animations],
    }


def _parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Export a rigged Blender character to a verified GLB."
    )
    parser.add_argument("blend_path", help="source .blend file")
    parser.add_argument("out_path", help="destination .glb file")
    parser.add_argument(
        "--keep-action",
        action="append",
        dest="keep_actions",
        metavar="NAME",
        help="action to include; repeat for each action (default: include all)",
    )
    parser.add_argument(
        "--tri-budget",
        type=int,
        default=None,
        help=f"maximum exported triangle count (default: {TRI_BUDGET}, "
             f"or the per-character override in TRI_BUDGETS: {TRI_BUDGETS})",
    )
    parser.add_argument(
        "--texture-size",
        type=int,
        default=TEXTURE_SIZE,
        help=f"maximum square texture size (default: {TEXTURE_SIZE})",
    )
    parser.add_argument(
        "--no-texture-resize",
        action="store_true",
        help="keep authored texture dimensions",
    )
    parser.add_argument(
        "--allow-over-budget",
        action="store_true",
        help="export when Collapse cannot reach the triangle target; report the miss",
    )
    return parser.parse_args(argv)


def main(argv=None):
    args = _parse_args(argv if argv is not None else [])
    report = export_character(
        os.path.abspath(args.blend_path),
        os.path.abspath(args.out_path),
        keep_actions=args.keep_actions,
        tri_budget=args.tri_budget,
        texture_size=None if args.no_texture_resize else args.texture_size,
        allow_over_budget=args.allow_over_budget,
    )
    print(json.dumps(report, indent=2, sort_keys=True))
    return report


if __name__ == "__main__":
    if "--" not in sys.argv:
        raise SystemExit(
            "Pass exporter arguments after Blender's `--`, for example: "
            "blender --background --python scripts/blender_export_character.py "
            "-- character.blend character.glb"
        )
    main(sys.argv[sys.argv.index("--") + 1:])
