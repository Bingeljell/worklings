using Worklings.Core.Pet;

namespace Worklings.Core.Stage;

/// How ready a family's body is.
public enum BodyStatus
{
    /// The `.glb` is in the project and rigged as a pet. Pickable.
    Live,

    /// The model exists but is not usable as a pet body yet — it needs a pass in
    /// Blender and a re-export before it can be worn.
    NeedsSetup,

    /// No model has been made yet.
    NotDelivered,
}

/// Which body each family wears, and how ready it is.
///
/// **The roster, as of 2026-09-04.** Confirmed by Nikhil, who makes them:
///
/// | Family | Body | State |
/// | --- | --- | --- |
/// | Elemental | Tempest Ram | **In the project and rigged as a pet.** It is what every Workling is currently wearing. |
/// | Relicborn | Clockwork Pangolin | In the project, but rigged and exported as a **foe** — it stands in for the Monolith in the Warren. Source at `worklings-blender-work/clockwork-pangolin-rigify.blend`; becoming a pet body means re-exporting it the way the Ram was, with the pet action set. |
/// | Wildkin | Moss-Fox | Not made yet — still to be rigged and animated. |
/// | Glitchkin | — | No body, and no design-stage model either. |
/// | Bloomglass | — | As above. |
///
/// **This replaces `PetFamily.HasArt` as the gate.** That property is about the
/// Swift app's legacy pixel sprite sheets and answers a question this build does
/// not ask.
///
/// **Nothing reads `Model` yet.** Every Workling renders as the Tempest Ram
/// regardless of family, because `DesktopPetScene` and the Warren both load it
/// unconditionally. That is why a Wildkin pet looks like an Elemental one today.
/// Wiring the swap up is its own piece of work, and this is the shape it needs.
public static class PetBody
{
    /// The `.glb` basename a family wears, or null while it has none.
    ///
    /// A family with no body of its own falls back to the Ram at the point of
    /// use — every Workling has to render as something.
    public static string? Model(PetFamily family) => family switch
    {
        PetFamily.Elemental => "tempest_ram",
        PetFamily.Relicborn => "clockwork_pangolin",
        _ => null,
    };

    /// The body every Workling wears until the swap is wired, and the fallback
    /// for a family that has no body of its own after it is.
    public const string DefaultModel = "tempest_ram";

    public static BodyStatus Status(PetFamily family) => family switch
    {
        PetFamily.Elemental => BodyStatus.Live,
        PetFamily.Relicborn => BodyStatus.NeedsSetup,
        _ => BodyStatus.NotDelivered,
    };

    /// Whether a family can be chosen. Only a body that is ready to be worn
    /// counts — the rest are listed so the roster still reads as five, and each
    /// un-greys on its own the day its model lands.
    public static bool IsPickable(PetFamily family) => Status(family) == BodyStatus.Live;

    /// What to say about a family that cannot be picked, in the menu's voice.
    ///
    /// The two reasons are worth telling apart: one is waiting on an export and
    /// the other on the animal existing at all.
    public static string Label(PetFamily family) => Status(family) switch
    {
        BodyStatus.Live => family.DisplayName(),
        BodyStatus.NeedsSetup => $"{family.DisplayName()} (body not set up yet)",
        _ => $"{family.DisplayName()} (coming soon)",
    };
}
