extends Node3D

## The Cache Warren — the first dungeon, as a playable scene.
##
## The stage (floor, lights, locked camera, actor slots) is instanced from
## `dungeon_stage.tscn`; this scene places the combatants in it and starts them
## idling. Combat will drive these animations from `CompanionCore` once that is
## ported; for now they loop so the room reads as inhabited rather than staged.
##
## Actor placement lives here rather than in the stage scene so the stage stays
## reusable for other dungeons — same room kit, different cast.

## Which animation each actor rests in. Matched on substring because action
## names differ per character (RamIdle_Breathe_Paw vs
## ForestFlicker_Idle_BreatheLook) and are still being trimmed.
const IDLE_HINT := "Idle"

@onready var party: Node3D = $Party
@onready var foe: Node3D = $Foe

func _ready() -> void:
    _play_idle(party)
    _play_idle(foe)

func _play_idle(actor: Node) -> void:
    var player := _find_player(actor)
    if player == null:
        push_warning("no AnimationPlayer under %s" % actor.name)
        return
    for name in player.get_animation_list():
        if name.findn(IDLE_HINT) != -1:
            var animation := player.get_animation(name)
            animation.loop_mode = Animation.LOOP_LINEAR
            player.play(name)
            return
    push_warning("no idle action found for %s" % actor.name)

func _find_player(node: Node) -> AnimationPlayer:
    if node is AnimationPlayer:
        return node
    for child in node.get_children():
        var found := _find_player(child)
        if found != null:
            return found
    return null
