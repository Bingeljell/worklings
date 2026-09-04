using Worklings.Core.Pet;

namespace Worklings.Core.Stage;

/// How far a race's body is from being wearable in this build.
///
/// The three "not yet" cases are different distances away, and collapsing them
/// hides which one is close.
public enum BodyStatus
{
    /// A rigged `.glb` is in the project. Pickable.
    Live,

    /// The model is in the project but rigged for something else, and needs a
    /// pass in Blender and a re-export before a Workling can wear it.
    NeedsExport,

    /// The creature exists and is drawn — its 2D sprite sheet is live in the
    /// Swift app — but it has never been modelled in 3D.
    NeedsModel,

    /// No art of any kind. The creatures are named in the roster and nothing
    /// has been drawn.
    Undrawn,
}

/// Which body each race wears, and how ready it is.
///
/// **Vocabulary**, from `docs/design/worklings_race_creature_roster.md`:
///
/// - A **race** is Wildkin, Elemental, Relicborn, Bloomglass or Glitchkin. The
///   code calls this `PetFamily`.
/// - A **creature** is one animal within a race — the Moss Fox is Wildkin's
///   first, the Tempest Ram is Elemental's second, the Key-back Pangolin is
///   Relicborn's first. Each race has five to nine of them.
/// - A **class** is Wellspring, Juggernaut, Aegis, Maverick or Tinkerer, and is
///   a separate axis entirely.
///
/// **A body is a creature, not a race.** That matters here because this table
/// maps a race to *one* body, which is a simplification that holds only while
/// each race has at most one creature modelled. `PetState` has no creature field
/// yet; choosing which animal within a race you are is a real choice the design
/// intends and the save cannot currently hold.
///
/// **The roster, as of 2026-09-04**, confirmed by Nikhil, who makes them:
///
/// | Race | Creature | 2D | 3D |
/// | --- | --- | --- | --- |
/// | Elemental | Tempest Ram | `worklings-elemental-spritesheet.png` | **rigged as a pet, in the project** — what every Workling wears today |
/// | Relicborn | Key-back Pangolin | `worklings-relicborn-spritesheet.png` | in the project, but rigged and exported as a **foe** — it stands in for the Monolith. Source at `worklings-blender-work/clockwork-pangolin-rigify.blend` |
/// | Wildkin | Moss Fox | `worklings-wildkin-spritesheet.png` — **live in the Swift app** | not modelled yet; still to be rigged and animated |
/// | Glitchkin | Sparktail and eight others | — | — |
/// | Bloomglass | Starpetal Fawn and eight others | — | — |
///
/// **Foes belong to the same races.** The Forest Flicker in the project is a
/// foe, not a pet creature, which is why it is not in the roster's race lists —
/// and `FamilyEnergy.For` mapping it to a race is deliberate rather than a
/// mistake. The intent is that foes are drawn from the same five races as
/// Worklings, so the whole bestiary reads as one universe; which race each foe
/// belongs to is still open.
///
/// So `PetFamily.HasArt` is not wrong, as I first assumed — it answers "does
/// this race have a sprite sheet", it does so correctly, and all three of those
/// sheets are in `assets/`. It is simply a different question from the one this
/// build asks, which is whether there is something to *render in 3D*.
///
/// **Nothing reads `Model` yet.** Every Workling renders as the Tempest Ram
/// regardless of race, because both scenes load it unconditionally. That is why
/// a Wildkin looks like an Elemental today.
///
/// **Choices are deliberately unlocked.** Race, class and name can all be
/// changed at any time. Onboarding and lore will lock the first two at creation,
/// and the name is expected to lock when multiplayer arrives — none of that
/// exists yet, so nothing here should be read as a permanent rule.
public static class PetBody
{
    /// The `.glb` basename a race's modelled creature wears, or null when it has
    /// none. Null does not mean invisible: a Workling still renders, as
    /// `DefaultModel`.
    public static string? Model(PetFamily race) => race switch
    {
        PetFamily.Elemental => "tempest_ram",
        PetFamily.Relicborn => "clockwork_pangolin",
        _ => null,
    };

    /// What every Workling wears until the swap is wired, and the fallback for a
    /// race with no body of its own after it is.
    public const string DefaultModel = "tempest_ram";

    public static BodyStatus Status(PetFamily race) => race switch
    {
        PetFamily.Elemental => BodyStatus.Live,
        PetFamily.Relicborn => BodyStatus.NeedsExport,
        PetFamily.Wildkin => BodyStatus.NeedsModel,
        _ => BodyStatus.Undrawn,
    };

    /// Whether a race can be chosen. Only a body this build can actually render
    /// counts. The rest stay listed so the roster still reads as five.
    public static bool IsPickable(PetFamily race) => Status(race) == BodyStatus.Live;

    /// What to say about a race that cannot be picked yet.
    ///
    /// Two wordings, not four: a player does not need to know whether we are
    /// waiting on a Blender export or on a rig, only whether the creature exists.
    public static string Label(PetFamily race) => Status(race) switch
    {
        BodyStatus.Live => race.DisplayName(),
        BodyStatus.Undrawn => $"{race.DisplayName()} (coming soon)",
        _ => $"{race.DisplayName()} (body on the way)",
    };
}
