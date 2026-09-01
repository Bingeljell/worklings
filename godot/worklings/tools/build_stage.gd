@tool
extends Node3D

## Generates `res://scenes/dungeon_stage.tscn` — the Cache Warren room — then
## saves it as a real, editable scene rather than building it at runtime.
##
## The point of the Godot move is the visual editor, so the stage must exist as
## an inspectable scene tree. This script is the one-time seed; after it runs,
## the .tscn is authored by hand in the editor and this becomes reference for
## how the numbers were derived.
##
## Every constant below is carried over from `DungeonStage3D.swift` so the two
## renderers frame an identical shot. See docs/design/dungeons.md.

const TILE_SIZE := 4.0          # the baked floor tile is 4x4 world units
const FLOOR_W := 44.0
const FLOOR_D := 34.0
const FLOOR_THICKNESS := 0.3    # top surface lands at y = 0

# Locked Cache Warren camera: azimuth 59.7, elevation 39.7, radius 27.95,
# 32 degrees vertical FOV. Found with the SceneKit Dungeon Stage Camera Tool.
const CAM_POS := Vector3(16.65, 17.76, 13.14)
const CAM_TARGET := Vector3(-1.92, -0.10, 2.29)
const CAM_FOV := 32.0

# Actor slots, from DungeonStageSlot.
const PARTY_SLOT := Vector3(-3, 0, 8.5)
const FOE_SLOT := Vector3(0, 0, -2)

func _build() -> Node3D:
    var root := Node3D.new()
    root.name = "DungeonStage"

    # --- floor: one baked 4x4 tile repeated, not a displaced mesh ---
    var floor_node := MeshInstance3D.new()
    floor_node.name = "Floor"
    var box := BoxMesh.new()
    box.size = Vector3(FLOOR_W, FLOOR_THICKNESS, FLOOR_D)
    floor_node.mesh = box
    var mat := StandardMaterial3D.new()
    mat.albedo_texture = load("res://assets/kit/floorTile_albedo.png")
    mat.normal_enabled = true
    mat.normal_texture = load("res://assets/kit/floorTile_normal.png")
    mat.roughness = 0.9
    # A surface w x d repeats the tile w/4 x d/4 times.
    mat.uv1_scale = Vector3(FLOOR_W / TILE_SIZE, FLOOR_D / TILE_SIZE, 1.0)
    mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
    floor_node.material_override = mat
    floor_node.position = Vector3(0, -FLOOR_THICKNESS / 2.0, 0)
    root.add_child(floor_node)

    # --- lights, matching the SceneKit rig ---
    var key := DirectionalLight3D.new()
    key.name = "KeyLight"
    key.light_color = Color(1.0, 0.8, 0.58)      # warm
    key.light_energy = 1.6
    key.rotation = Vector3(deg_to_rad(-45), deg_to_rad(-45), 0)
    key.shadow_enabled = true
    root.add_child(key)

    # Fixture lights, positioned where the Blender room's props sit.
    # Blender is Z-up: (x, y, z)_blender -> (x, z, -y)_godot.
    var torch := OmniLight3D.new()
    torch.name = "TorchLight"
    torch.light_color = Color(1.0, 0.6, 0.2)
    torch.light_energy = 4.0
    torch.omni_range = 14.0
    torch.position = Vector3(-8.0, 1.2, 10.0)
    root.add_child(torch)

    var crystal := OmniLight3D.new()
    crystal.name = "CrystalLight"
    crystal.light_color = Color(0.4, 0.85, 0.95)
    crystal.light_energy = 2.5
    crystal.omni_range = 12.0
    crystal.position = Vector3(6.0, 1.5, -3.0)
    root.add_child(crystal)

    var world_env := WorldEnvironment.new()
    world_env.name = "Environment"
    var env := Environment.new()
    env.background_mode = Environment.BG_COLOR
    env.background_color = Color(0.04, 0.04, 0.04)
    env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
    env.ambient_light_color = Color(0.22, 0.22, 0.22)
    env.ambient_light_energy = 1.0
    env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
    world_env.environment = env
    root.add_child(world_env)

    # --- camera: the locked shot ---
    var cam := Camera3D.new()
    cam.name = "StageCamera"
    cam.fov = CAM_FOV                 # vertical, matching SceneKit's 32
    cam.far = 500.0
    cam.current = true
    cam.position = CAM_POS
    cam.look_at_from_position(CAM_POS, CAM_TARGET, Vector3.UP)
    root.add_child(cam)

    # --- actor slots: empty markers, so placement is visible in the editor ---
    for slot in [["PartySlot", PARTY_SLOT], ["FoeSlot", FOE_SLOT]]:
        var m := Marker3D.new()
        m.name = slot[0]
        m.position = slot[1]
        root.add_child(m)

    # own every node so the scene packs with its children
    for child in root.get_children():
        child.owner = root
    return root

func build_and_save(path: String) -> void:
    var root := _build()
    var packed := PackedScene.new()
    var err := packed.pack(root)
    if err != OK:
        push_error("pack failed: %d" % err); return
    err = ResourceSaver.save(packed, path)
    if err != OK:
        push_error("save failed: %d" % err); return
    print("wrote ", path, " with ", root.get_child_count(), " nodes")
