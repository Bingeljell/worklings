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

import os
import bmesh
import bpy

TRI_BUDGET = 20_000       # triangles, not vertices
TRI_FLOOR = 20_000
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


def export_character(blend_path, out_path, keep_actions=None, tri_budget=TRI_BUDGET,
                    texture_size=TEXTURE_SIZE):
    """Open `blend_path`, simplify, and write a Godot-ready .glb to `out_path`.

    `keep_actions` is a set of action names to ship; None keeps everything.
    `texture_size` downsizes every image to that square resolution; None keeps
    them as authored. Textures dominate file size once geometry is decimated —
    the Pangolin's 4096 maps are 26.9 MB of its 39 MB.
    """
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
    report["tris_source"] = _tris(geo.data)

    win = bpy.context.window_manager.windows[0]
    if report["tris_source"] > tri_budget:
        for obj in bpy.context.view_layer.objects:
            obj.select_set(False)
        geo.select_set(True)
        bpy.context.view_layer.objects.active = geo
        dec = geo.modifiers.new("Dec", "DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = tri_budget / report["tris_source"]
        geo.modifiers.move(geo.modifiers.find("Dec"), 0)  # above the Armature
        with bpy.context.temp_override(
            window=win, screen=win.screen, object=geo,
            active_object=geo, selected_objects=[geo],
        ):
            bpy.ops.object.modifier_apply(modifier="Dec")
    report["tris_exported"] = _tris(geo.data)

    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    geo.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
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
    import json
    import struct

    with open(glb_path, "rb") as handle:
        struct.unpack("<III", handle.read(12))
        chunk_len, _ = struct.unpack("<II", handle.read(8))
        doc = json.loads(handle.read(chunk_len).decode("utf-8"))
    skins = doc.get("skins", [])
    return {
        "glb_skins": len(skins),
        "glb_joints": len(skins[0]["joints"]) if skins else 0,
        "glb_animations": len(doc.get("animations", [])),
    }
