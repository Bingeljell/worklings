extends Node3D

## Boot scene used once to emit `scenes/dungeon_stage.tscn`, then to render a
## verification shot of it with the Ram standing in the party slot.

const OUT := "/private/tmp/claude-501/-Users-nikhilshahane-projects-worklings/414de031-4269-4c5b-93f3-d34e6a40687e/scratchpad/godot_stage.png"

func _ready() -> void:
    var builder: Node3D = load("res://tools/build_stage.gd").new()
    builder.build_and_save("res://scenes/dungeon_stage.tscn")
    builder.free()

    var stage: Node3D = load("res://scenes/dungeon_stage.tscn").instantiate()
    add_child(stage)

    var slot: Marker3D = stage.get_node("PartySlot")
    var ram: Node3D = load("res://assets/characters/tempest_ram.glb").instantiate()
    ram.scale = Vector3(3.7, 3.7, 3.7)
    ram.position = slot.position
    ram.rotation = Vector3(0, deg_to_rad(160), 0)
    stage.add_child(ram)
    _kill_emission(ram)
    var ap := _find_ap(ram)
    if ap and ap.has_animation("RamIdle_Breathe_Paw"):
        ap.play("RamIdle_Breathe_Paw")

    await get_tree().process_frame
    await get_tree().create_timer(0.8).timeout
    await RenderingServer.frame_post_draw
    get_viewport().get_texture().get_image().save_png(OUT)
    print("saved -> ", OUT)
    get_tree().quit()

func _find_ap(n: Node) -> AnimationPlayer:
    if n is AnimationPlayer: return n
    for c in n.get_children():
        var r := _find_ap(c)
        if r: return r
    return null

# The Blender crackle effect flattens to a uniform blue emissive on export;
# suppressed until it is rebuilt as a real shader. See dungeons.md.
func _kill_emission(n: Node) -> void:
    if n is MeshInstance3D:
        var m: MeshInstance3D = n
        for i in m.mesh.get_surface_count():
            var mat := m.mesh.surface_get_material(i)
            if mat is BaseMaterial3D:
                var d: BaseMaterial3D = mat.duplicate()
                d.emission_enabled = false
                m.set_surface_override_material(i, d)
    for c in n.get_children(): _kill_emission(c)
