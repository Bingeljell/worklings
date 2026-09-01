using System.Collections.Generic;

namespace Worklings.Core.Stage;

/// Which action a combat beat should play.
public enum ActorAction { Idle, Walk, Attack, Signature, Wince, Downed }

/// The map from a combat beat to a character's actual animation name.
///
/// This replaces substring matching ("find something containing 'Attack'"),
/// which picked *an* animation rather than the intended one — the Ram has three
/// headbutt variants and matching would take whichever sorted first. That is
/// tolerable for a placeholder and not tolerable for impact frames, which have
/// to be timed against a specific clip.
///
/// One table, one place to change when the action lists get trimmed. Names are
/// checked against the loaded AnimationPlayer at startup rather than assumed —
/// see StageActor, which logs a warning instead of silently playing nothing.
public sealed class ActorAnimations
{
    private readonly Dictionary<ActorAction, string> _map;

    /// Where in the clip the blow actually connects, 0..1 of its duration.
    ///
    /// These sit near the END of the animation. Both attack clips are built as
    /// a long wind-up into a strike at the finish, so a mid-clip contact fires
    /// the flash and the damage while the attacker is still rearing back.
    ///
    /// Eyeballed per character and expected to be retuned once the actions are
    /// trimmed and shortened; there is no way to derive it from the file.
    public double AttackImpactPoint { get; }

    public ActorAnimations(Dictionary<ActorAction, string> map, double attackImpactPoint = 0.85)
    {
        _map = map;
        AttackImpactPoint = System.Math.Clamp(attackImpactPoint, 0, 1);
    }

    public string? Name(ActorAction action) => _map.TryGetValue(action, out var n) ? n : null;

    public IEnumerable<KeyValuePair<ActorAction, string>> All => _map;

    /// The Tempest Ram. Chosen from the 17 shipped actions, most of which are
    /// iteration history; these are the ones that read as the intended beat.
    /// The choice of headbutt variant in particular is a judgement call and the
    /// obvious thing to change here.
    public static readonly ActorAnimations TempestRam = new(
        new Dictionary<ActorAction, string>
        {
            [ActorAction.Idle] = "RamIdle_Breathe_Paw",
            [ActorAction.Walk] = "RamWalk_Natural_FrontFix",
            [ActorAction.Attack] = "RamHeadbutt_Power_Impact",
            [ActorAction.Signature] = "RamHeadbutt_Power",
            [ActorAction.Wince] = "RamDamage_HeavyFront_Wince",
            [ActorAction.Downed] = "RamDamage_HeavyFront",
        },
        attackImpactPoint: 0.86);

    /// The Forest Flicker. Its five actions are already a clean set — one per
    /// beat, no variants — which is what the Ram's should be trimmed down to.
    public static readonly ActorAnimations ForestFlicker = new(
        new Dictionary<ActorAction, string>
        {
            [ActorAction.Idle] = "ForestFlicker_Idle_BreatheLook",
            [ActorAction.Walk] = "ForestFlicker_Walk_Feline",
            [ActorAction.Attack] = "ForestFlicker_Attack_RightSwipe",
            [ActorAction.Signature] = "ForestFlicker_Special_DoublePawSlam",
            [ActorAction.Wince] = "ForestFlicker_Damage_Wince_TailDown",
            [ActorAction.Downed] = "ForestFlicker_Damage_Wince_TailDown",
        },
        attackImpactPoint: 0.82);

    /// Looked up by the .glb basename the actor was loaded from.
    public static ActorAnimations? For(string modelName) => modelName switch
    {
        "tempest_ram" => TempestRam,
        "forest_flicker" => ForestFlicker,
        _ => null,
    };
}
