using Worklings.Core.Pet;
using Worklings.Core.Stage;

namespace Worklings.Core.Roster;

/// Whether a creature can be worn by a player, fought as a foe, or both.
///
/// Both is the normal case rather than the exception: the design intends foes
/// and Worklings to be drawn from the same five races so the bestiary reads as
/// one universe. A creature restricted to one side is making a claim, so it has
/// to say so.
public enum CreatureRole
{
    /// A body a player can be locked into. Appears in the alpha loadout.
    Workling,

    /// A body only the dungeon wears.
    Foe,

    /// Both sides. The Flicker is the live example — a foe in the Cache Warren
    /// and a Wildkin creature a player could be.
    Either,
}

/// How close a creature is to being usable in this build.
///
/// This is the same question `PetBody.BodyStatus` asked, kept because it is a
/// real one, and narrowed to the two answers that change behaviour: either the
/// renderer can put this body on the stage today, or it cannot.
public enum CreatureReadiness
{
    /// A rigged, animated `.glb` is in the project and the animation table maps
    /// every beat this creature takes. Renderable now.
    Ready,

    /// Named in the roster, art not finished. Listed so the roster still reads
    /// as the full cast, never handed to the renderer.
    Planned,
}

/// One creature in the roster, and everything the game needs to know to put it
/// on a stage.
///
/// **This type is the answer to "how do we keep adding Worklings".** Before it,
/// a creature's facts were spread across five string-switches keyed off its
/// `.glb` basename — `PetBody.Model`, `AbilitySignatures.For`,
/// `ActorAnimations.For`, `FamilyEnergy.For`, and a private `PresenceFor` inside
/// the dungeon scene. Adding a creature meant finding all five and editing each,
/// and every one of them had a silent `_ =>` fallback, so missing one produced a
/// Workling that rendered as a Ram, glowed Bloomglass and threw no signature
/// rather than an error. Adding the sixteenth creature that way is not a job
/// anyone would do correctly.
///
/// Now a creature is one entry in one list. The renderer asks the roster; the
/// roster is the only place a creature's facts exist.
///
/// **Combat rules deliberately do not appear here.** A creature's stat block and
/// turn logic live in `Core.Combat.Bestiary`, on the other side of the seam this
/// codebase keeps between rules and rendering — the rules stay verifiable
/// headlessly and the renderer stays swappable. `CreatureRoster.Casting` is the
/// one crossing point, and it crosses in the safe direction: presentation
/// looking up a foe by name, never the rules reaching for a model.
public sealed record Creature(
    /// Stable key. The `.glb` basename, so it is also the asset path, and so the
    /// save files and captures written before the roster existed still resolve.
    string Id,

    /// What a player is told they are, or what the foe plate reads.
    string DisplayName,

    /// The race, which drives the energy colour through `FamilyEnergy.Of` — HP
    /// bar, damage numbers, hit sparks, impact flash tint.
    PetFamily Family,

    /// Which combat beat maps to which clip in the `.glb`, plus the contact
    /// timing and travel the lunge needs. Null only for a Planned creature.
    ActorAnimations? Animations,

    /// What the world does when this creature connects. `None` is the honest
    /// default for a creature whose signature has not been designed — better
    /// than giving everyone the same burst and calling it identity.
    AbilitySignature Signature,

    CreatureRole Role,
    CreatureReadiness Readiness,

    /// How tall this creature stands on a stage, in world units.
    ///
    /// **A height, not a scale multiplier**, and that is the whole point.
    /// Authored model sizes disagree wildly — the Snag exports at 0.69 units
    /// tall and the Flicker at 1.55, so the numbers that made them look right
    /// were 7.0 and 2.8, which say nothing and cannot be sanity-checked. A
    /// height can be: the Ram is 5.56, so a Scamp at 2.40 is visibly a warm-up
    /// and a Monolith at 7.50 visibly towers.
    ///
    /// `StageCast` measures the instanced model's real bounds and solves for the
    /// scale, so re-exporting a body at a different authored size self-corrects
    /// instead of silently resizing the character. These lived in
    /// `cache_warren.tscn`'s baked transforms before, where they belonged to one
    /// scene and were invisible to every capture tool.
    ///
    /// **This is where the Monolith bug was hiding.** As a Pangolin at 1.3x it
    /// stood 4.21 units — shorter than the 5.56 Ram the player brings. Nothing
    /// said so, because the number on screen was "1.3".
    float StageHeight = 5.0f)
{
    /// Where the body loads from. One place, so the convention is not retyped.
    public string ScenePath => $"res://assets/characters/{Id}.glb";

    /// The energy colour this creature fights in.
    public Godot.Color Energy => FamilyEnergy.Of(Family);

    /// Whether the renderer may be handed this creature.
    public bool IsRenderable => Readiness == CreatureReadiness.Ready && Animations != null;

    /// Whether a player can pick this body in the alpha loadout.
    public bool IsPlayable =>
        IsRenderable && Role is CreatureRole.Workling or CreatureRole.Either;
}
