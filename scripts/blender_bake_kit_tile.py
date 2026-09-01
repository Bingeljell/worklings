"""Bake a procedural Blender surface into a seamlessly tileable texture pair.

How every dungeon room-kit piece gets its material. The Cache Warren floor was
the first: a 21,091-vertex mesh carrying two Displace modifiers became a flat
4-vertex quad plus a 1024x1024 albedo/normal pair, with no visible difference at
the locked camera. At a 39.7 degree elevation that displacement never read in
silhouette, so it was spending 41,600 triangles on shading a normal map gives
for free.

Run against a live Blender (see blender_rpc.py). The same routine applies to
wall segments, platform edges and corner pieces — only the source material and
tile size change.

**Tiles are authored at 4x4 world units.** A surface w x d then repeats the tile
w/4 x d/4 times. In Godot that is `StandardMaterial3D.uv1_scale`; in SceneKit it
was a scaled `contentsTransform`. Keep the authored size and the engine-side
scale in agreement or the texture density drifts between pieces.

Set anisotropic filtering on the result. Tiled ground viewed at a glancing angle
is exactly where mip filtering smears the far half of the floor into flat colour.
"""

import os

import bmesh
import bpy

TILE_SIZE = 4.0
TEXTURE_SIZE = 1024
HI_SUBDIVISIONS = 200   # density of the bake source; only its shading is kept


def _grid(name, size, subdivisions, collection):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    builder = bmesh.new()
    bmesh.ops.create_grid(
        builder, x_segments=subdivisions, y_segments=subdivisions,
        size=size / 2, calc_uvs=True,
    )
    builder.to_mesh(mesh)
    builder.free()
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    return obj


def _unwrap_flat(obj, size):
    """UV the low-poly quad across the full 0-1 square."""
    uvs = obj.data.uv_layers.active.data
    for i, loop in enumerate(obj.data.loops):
        co = obj.data.vertices[loop.vertex_index].co
        uvs[i].uv = ((co.x + size / 2) / size, (co.y + size / 2) / size)


def make_seamless(image, texture_size=TEXTURE_SIZE):
    """Make `image` tile without a visible seam.

    A separable triangular-window cross-dissolve against the half-shifted copy.
    The window vanishes at the borders and sums to 1 with its own half-shift, so
    the wrap is continuous *by construction* rather than by tuning — which is
    the point, because the obvious four-shifted-copies blend does not actually
    produce a continuous edge (measured seam delta 0.036 where it should be 0).

    Costs contrast and can ghost slightly: it is a cross-dissolve of the image
    with itself. Invisible at dungeon distance; the first thing that would break
    if the camera ever pushes in close.

    Returns the measured seam delta on each axis — both should be ~0.001, which
    is the one-pixel wrap offset rather than a real discontinuity.
    """
    import numpy as np

    width = height = texture_size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)

    u = np.arange(width, dtype=np.float32) / width
    window_x = (1.0 - np.abs(2.0 * u - 1.0))[None, :, None]
    pixels = pixels * window_x + np.roll(pixels, -width // 2, axis=1) * (1.0 - window_x)

    v = np.arange(height, dtype=np.float32) / height
    window_y = (1.0 - np.abs(2.0 * v - 1.0))[:, None, None]
    pixels = pixels * window_y + np.roll(pixels, -height // 2, axis=0) * (1.0 - window_y)

    pixels[..., 3] = 1.0
    if image.name.endswith("normal"):
        vectors = pixels[..., :3] * 2.0 - 1.0
        lengths = np.linalg.norm(vectors, axis=2, keepdims=True).clip(1e-6)
        pixels[..., :3] = (vectors / lengths) * 0.5 + 0.5

    delta_x = float(np.abs(pixels[:, 0, :3] - pixels[:, -1, :3]).mean())
    delta_y = float(np.abs(pixels[0, :, :3] - pixels[-1, :, :3]).mean())
    image.pixels.foreach_set(pixels.ravel())
    return {"seam_delta_x": round(delta_x, 5), "seam_delta_y": round(delta_y, 5)}


def bake_tile(source_material, displace_textures, out_dir, name,
              tile_size=TILE_SIZE, texture_size=TEXTURE_SIZE):
    """Bake `source_material` (plus optional displacement) into a tileable pair.

    `displace_textures` is a list of (blender_texture_name, strength) applied to
    the high-poly bake source, reproducing whatever geometric detail the
    original surface had as a normal map.

    Writes `<name>_albedo.png` and `<name>_normal.png` into `out_dir`.
    """
    scene = bpy.context.scene
    kit = bpy.data.collections.get("dungeonKit")
    if kit is None:
        kit = bpy.data.collections.new("dungeonKit")
        scene.collection.children.link(kit)
    for stale in [o for o in kit.objects if o.name.startswith(f"{name}_")]:
        bpy.data.objects.remove(stale, do_unlink=True)

    high = _grid(f"{name}_hi", tile_size, HI_SUBDIVISIONS, kit)
    high.data.materials.append(bpy.data.materials[source_material])
    for texture_name, strength in displace_textures:
        modifier = high.modifiers.new(f"Disp_{texture_name}", "DISPLACE")
        modifier.texture = bpy.data.textures[texture_name]
        modifier.strength = strength
        modifier.mid_level = 0.5
        modifier.direction = "NORMAL"
        modifier.texture_coords = "GLOBAL"

    low = _grid(name, tile_size, 1, kit)
    _unwrap_flat(low, tile_size)

    material = bpy.data.materials.get(f"{name}_mat") or bpy.data.materials.new(f"{name}_mat")
    material.use_nodes = True
    low.data.materials.clear()
    low.data.materials.append(material)
    tree = material.node_tree
    for node in [n for n in tree.nodes if n.type == "TEX_IMAGE"]:
        tree.nodes.remove(node)

    images = {}
    for kind, colorspace, fill in (
        ("albedo", "sRGB", (0.5, 0.5, 0.5, 1)),
        ("normal", "Non-Color", (0.5, 0.5, 1, 1)),
    ):
        key = f"{name}_{kind}"
        if key in bpy.data.images:
            bpy.data.images.remove(bpy.data.images[key])
        image = bpy.data.images.new(key, texture_size, texture_size, alpha=False)
        image.colorspace_settings.name = colorspace
        image.pixels = list(fill) * (texture_size * texture_size)
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = image
        node.name = kind
        images[kind] = (image, node)

    previous_engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    settings = scene.render.bake
    settings.use_selected_to_active = True
    settings.cage_extrusion = 0.5
    settings.max_ray_distance = 1.2
    settings.margin = 16
    try:
        for obj in bpy.context.view_layer.objects:
            obj.select_set(False)
        high.select_set(True)
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        for kind, bake_type in (("normal", "NORMAL"), ("albedo", "DIFFUSE")):
            tree.nodes.active = images[kind][1]
            if bake_type == "DIFFUSE":
                settings.use_pass_direct = False
                settings.use_pass_indirect = False
                settings.use_pass_color = True
            bpy.ops.object.bake(type=bake_type)
    finally:
        scene.render.engine = previous_engine

    os.makedirs(out_dir, exist_ok=True)
    report = {"tile_size": tile_size, "texture_size": texture_size}
    for kind in ("albedo", "normal"):
        image = images[kind][0]
        report[kind] = make_seamless(image, texture_size)
        image.filepath_raw = os.path.join(out_dir, f"{name}_{kind}.png")
        image.file_format = "PNG"
        image.save()
    return report
