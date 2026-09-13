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
    /// **There are two shapes of attack clip here, not one.** The Ram, the
    /// Flicker and the Pangolin are a long wind-up into a strike at the finish
    /// and land near 0.85. The Snag's whip and the Scamp's paw crack in the
    /// *middle* and recoil, and land near 0.44-0.50. Timing a mid-clip striker
    /// at 0.85 fires the flash and the damage while the limb is already on its
    /// way back; timing a late striker early does the reverse.
    ///
    /// **It can be derived from the file, and for two characters it was.** Step
    /// the action a frame at a time, take the world position of the striking
    /// bone's tip, and read where its speed peaks — the Snag at frame 12 of 24,
    /// the Scamp at frame 13 of 28. That also identifies *which* limb strikes:
    /// the Scamp's entire right side never moves. The three values still sitting
    /// at 0.82-0.86 are the eyeballed ones and are the obvious next measurement.
    public double AttackImpactPoint { get; }

    /// How far the attacker travels, as a share of the gap to its target.
    ///
    /// Per character because reach is per character. The Ram has none and must
    /// cross the floor to connect; the Snag has an arm's worth and only needs to
    /// commit its weight forward, so sending it the same distance would drag a
    /// rooted thing off the spot it is rooted to.
    public float TravelFraction { get; }

    /// How long the travel burst lasts, in seconds, ending on the contact frame.
    ///
    /// The wind-up plays on the mark and the character launches at the end of
    /// it. This is the length of that launch — short, because a short burst over
    /// a real distance is what reads as speed. The first version spent this at
    /// the *start* of the clip and then stood next to the target through the
    /// rest of the wind-up, which is why it read as sliding.
    public double TravelSeconds { get; }

    /// How many ghosts the trail holds. More reads as a smear, fewer as a row of
    /// separate bodies; the stills put the line at around ten for a full charge.
    public int GhostCount { get; }

    /// Whether `Downed` names a clip that actually shows the creature dying.
    ///
    /// **Only one of the five bodies does.** The Scamp has `Scamp_Death`; the
    /// Flicker and the Pangolin shipped with no death clip at all, and the
    /// Snag's is the broken one (see below), so all three point `Downed` at
    /// their own hit-react. A kill therefore played a wince and the body stayed
    /// standing until the next encounter swapped it out — which is precisely
    /// the "no death animation for anything after the Scamp" that the first
    /// play session reported, and it is an asset gap rather than a wiring bug.
    ///
    /// Declared rather than inferred from `Downed == Wince`, because the two
    /// being equal is a coincidence of the workaround and not the fact being
    /// stated. The stage reads this and falls the body over itself when it is
    /// false, so a kill reads on every creature; re-authoring the clips still
    /// replaces that with something better, and flipping this to true is how
    /// each one gets switched over.
    public bool HasDeathClip { get; }

    public ActorAnimations(Dictionary<ActorAction, string> map, double attackImpactPoint = 0.85,
                           float travelFraction = 0.62f, double travelSeconds = 0.24,
                           int ghostCount = 10, bool hasDeathClip = false)
    {
        _map = map;
        AttackImpactPoint = System.Math.Clamp(attackImpactPoint, 0, 1);
        TravelFraction = travelFraction;
        TravelSeconds = travelSeconds;
        GhostCount = ghostCount;
        HasDeathClip = hasDeathClip;
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
        attackImpactPoint: 0.86,
        // A charge: it has no reach, so it commits the whole gap.
        travelFraction: 0.62f, travelSeconds: 0.24, ghostCount: 7);

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
        attackImpactPoint: 0.82,
        // Lighter and faster than the Ram, so more ghosts spread thinner.
        travelFraction: 0.66f, travelSeconds: 0.20, ghostCount: 9);

    /// The Clockwork Pangolin. A pet model doing placeholder duty as the
    /// Monolith: the mini-boss has no model of its own, and a heavy armoured
    /// thing that slams reads far closer to a Colossus than a scaled-up cat
    /// does. The root-locked tail swipe is the one to use — AttackLunge moves
    /// the node itself, so a clip that also translates the body would fight it.
    public static readonly ActorAnimations ClockworkPangolin = new(
        new Dictionary<ActorAction, string>
        {
            [ActorAction.Idle] = "Pangolin_Rest_BreatheLook_v01",
            [ActorAction.Walk] = "Pangolin_Walk_InPlace_v01",
            [ActorAction.Attack] = "Pangolin_Attack_TailSwipe_L_RootLocked_v01",
            [ActorAction.Signature] = "Pangolin_Special_RearSlam_Sprite_v04",
            [ActorAction.Wince] = "Pangolin_HitReact_HeadTuck_Sprite_v01",
            [ActorAction.Downed] = "Pangolin_HitReact_HeadTuck_Sprite_v01",
        },
        attackImpactPoint: 0.85,
        // Heavy. It closes less far and takes longer doing it, so the trail
        // reads as mass rather than speed.
        travelFraction: 0.44f, travelSeconds: 0.30, ghostCount: 8);

    /// The Snag. Its own body at last, rather than a scaled-up Flicker — and
    /// the first character exported straight from the animated `.blend` through
    /// the command-line exporter.
    ///
    /// **No Walk**, deliberately: a Snag is rooted in the floor and never
    /// crosses it. Nothing plays Walk on a foe, so leaving it unmapped is
    /// honest rather than a gap — and mapping it to a clip that does not exist
    /// would warn at startup for a beat this character will never take.
    ///
    /// Signature is the same whip as Attack. The Snag shipped with one attack,
    /// and repeating it is better than the alternative of a foe standing still
    /// on the beat that is supposed to be its biggest.
    ///
    /// **The impact point is 0.50, not the 0.85 the others use** — measured, not
    /// eyeballed: the whipping tentacle's tip reaches peak speed at frame 12 of
    /// 24. This clip cracks in the middle and recoils, where the Ram's and the
    /// Flicker's are a long wind-up into a strike at the finish. Timing it at
    /// 0.85 would land the flash and the damage while the whip was already on
    /// its way back.
    ///
    /// **Downed is `Take_Damage`, not `Death`, and that is an interim.** The
    /// authored `Death` clip ends on a root rotation of 180 degrees about Z —
    /// on top of the 90-degrees-about-X every clip in every model carries as the
    /// Blender Z-up to glTF Y-up conversion. For a creature rooted in the floor
    /// that means upside down and *beneath the stage*: in the 2026-09-12 slice
    /// capture the Snag did not fall over when killed, it blinked out of
    /// existence between two frames. An audit of the root rotation of all 39
    /// clips across the five bodies found this to be the only one of its kind;
    /// the Scamp's death rotates too, but into a collapse that reads.
    ///
    /// Repointing the beat keeps a body on the floor in one line and is
    /// reversible in one. **The real fix is to re-author the clip**, which is
    /// Nikhil's, and it is the reason this is written down rather than quietly
    /// swapped.
    public static readonly ActorAnimations Snag = new(
        new Dictionary<ActorAction, string>
        {
            [ActorAction.Idle] = "Idle",
            [ActorAction.Attack] = "Attack_Whip_24f_Review",
            [ActorAction.Signature] = "Attack_Whip_24f_Review",
            [ActorAction.Wince] = "Take_Damage",
            [ActorAction.Downed] = "Take_Damage",
        },
        attackImpactPoint: 0.50,
        // A whip has reach. The body barely leaves its mark — just enough
        // forward weight to sell the crack — and the trail is short to match.
        travelFraction: 0.14f, travelSeconds: 0.14, ghostCount: 6);

    /// The Dungeon Scamp. The Cache Warren's first encounter, and the first
    /// fight anyone ever sees — which is why it mattered that it was being
    /// fought by a Flicker shrunk to 0.55 until the `.glb` was finally exported
    /// on 2026-09-12, four days after the animations were finished.
    ///
    /// **No Signature**, deliberately, and for the same reason the Snag has no
    /// Walk: the Scamp is `FoeBehavior.Mindless`. It attacks every turn and has
    /// no second move, so it can never reach the beat that would play one.
    /// Mapping it to a clip anyway would be inventing a capability the rules say
    /// it does not have.
    ///
    /// **The impact point is 0.44 — measured, not eyeballed.** Stepping the
    /// 28-frame clip and reading tip speed off the rig puts peak at frame 13,
    /// and it is unambiguously a one-paw strike: every `front_*.L` bone peaks
    /// together at 13 while the entire right side never moves. Like the Snag's
    /// whip and unlike the Ram's charge, this clip cracks in the middle and
    /// recoils — so two of the five characters are mid-clip strikers and the
    /// 0.85 the others share is not the default it looks like.
    public static readonly ActorAnimations DungeonScamp = new(
        new Dictionary<ActorAction, string>
        {
            [ActorAction.Idle] = "Scamp_Idle",
            [ActorAction.Walk] = "Scamp_Walk",
            [ActorAction.Attack] = "Scamp_Attack",
            [ActorAction.Wince] = "Scamp_Damage",
            [ActorAction.Downed] = "Scamp_Death",
        },
        attackImpactPoint: 0.44,
        // Small, light and quick. It has no reach at all, so it commits further
        // than the Ram and gets there faster, with the trail spread thin.
        travelFraction: 0.68f, travelSeconds: 0.18, ghostCount: 9,
        // The only body in the cast with a real death clip.
        hasDeathClip: true);

    /// Looked up by the .glb basename the actor was loaded from.
    public static ActorAnimations? For(string modelName) => modelName switch
    {
        "tempest_ram" => TempestRam,
        "forest_flicker" => ForestFlicker,
        "clockwork_pangolin" => ClockworkPangolin,
        "snag" => Snag,
        "dungeon_scamp" => DungeonScamp,
        _ => null,
    };
}
