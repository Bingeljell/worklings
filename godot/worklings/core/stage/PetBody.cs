using Worklings.Core.Pet;

namespace Worklings.Core.Stage;

/// Which body a family wears, and which families can be chosen at all.
///
/// **This replaces `PetFamily.HasArt` as the gate.** That property is about the
/// Swift app's legacy pixel sprite sheets, and it says three families have art —
/// which was true there and is not true here. In this build the question is
/// whether a family has a rigged `.glb`, and the answer is a different set.
///
/// Kept apart from `FamilyEnergy.For`, which maps the other way — a model to its
/// family — for the stage's colour work. That one answers "what is this thing on
/// screen"; this one answers "what may the player become".
public static class PetBody
{
    /// The `.glb` a family wears, or null while it has none.
    ///
    /// Only Relicborn has its body in the project today. Everything else still
    /// renders as the Tempest Ram, which is what `DesktopPetScene` and the
    /// Warren load regardless — wiring this up is its own piece of work.
    public static string? Model(PetFamily family) => family switch
    {
        PetFamily.Relicborn => "clockwork_pangolin",
        _ => null,
    };

    /// Whether a family can be picked.
    ///
    /// Relicborn's Clockwork Pangolin is in the project. Wildkin's Moss-Fox is
    /// baked and waiting to be brought in, so it is offered — the gap between
    /// choosing it and seeing it is short and deliberate. Elemental, Glitchkin
    /// and Bloomglass have no body and are listed but not pickable, so the
    /// roster still reads as five.
    public static bool IsPickable(PetFamily family) =>
        family is PetFamily.Wildkin or PetFamily.Relicborn;
}
