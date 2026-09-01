extends Node3D

## Renders the dungeon stage to a PNG without opening the editor.
##
## For showing someone what a change looks like, or checking a model landed
## right, without a Godot window taking over the screen. Run:
##
##     Godot --path godot/worklings tools/stage_shot.tscn --quit-after 3000
##
## `--quit-after` matters: if this script fails to parse, nothing calls quit()
## and the window sits open forever.
##
## Edit ACTORS to choose who stands where. Slots come from the stage scene's
## Marker3D nodes, so they stay in step with the real placement.

const OUT := "user://stage_shot.png"

## [glb basename, slot node name, scale, y-rotation degrees, action substring]
const ACTORS := [
    ["tempest_ram", "PartySlot", 3.7, 160.0, "Idle"],
    ["forest_flicker", "FoeSlot", 2.8, 20.0, "Idle"],
]

func _ready() -> void:
    var stage: Node3D = load("res://scenes/dungeon_stage.tscn").instantiate()
    add_child(stage)

    for actor in ACTORS:
        var packed = load("res://assets/characters/%s.glb" % actor[0])
        if packed == null:
            push_warning("missing character: %s" % actor[0])
            continue
        var slot := stage.get_node_or_null(actor[1]) as Marker3D
        if slot == null:
            push_warning("missing slot: %s" % actor[1])
            continue
        var model: Node3D = packed.instantiate()
        model.scale = Vector3(actor[2], actor[2], actor[2])
        model.position = slot.position
        model.rotation = Vector3(0, deg_to_rad(actor[3]), 0)
        stage.add_child(model)
        _play(model, actor[4])

    await get_tree().process_frame
    await get_tree().create_timer(0.8).timeout
    await RenderingServer.frame_post_draw
    var path := ProjectSettings.globalize_path(OUT)
    get_viewport().get_texture().get_image().save_png(path)
    print("saved -> ", path)
    get_tree().quit()

func _play(root: Node, action_hint: String) -> void:
    var player := _find_player(root)
    if player == null:
        return
    for name in player.get_animation_list():
        if action_hint == "" or name.findn(action_hint) != -1:
            player.play(name)
            return
    var names := player.get_animation_list()
    if names.size() > 0:
        player.play(names[0])

func _find_player(node: Node) -> AnimationPlayer:
    if node is AnimationPlayer:
        return node
    for child in node.get_children():
        var found := _find_player(child)
        if found != null:
            return found
    return null
