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
    /// **Nothing reads this yet.** Every family renders as the Tempest Ram,
    /// which is what `DesktopPetScene` and the Warren load unconditionally.
    /// Wiring it up is its own piece of work; this is the shape that work will
    /// need, filled in as far as the truth goes today.
    ///
    /// Relicborn's entry is the Pangolin that is already in the project, but as
    /// a *foe* stand-in — it is a pet model doing Monolith duty in the Warren.
    /// Setting it up as a pet body means going back to
    /// `worklings-blender-work/clockwork-pangolin-rigify.blend` and exporting it
    /// the way the Ram was, with the pet action set rather than the foe one.
    public static string? Model(PetFamily family) => family switch
    {
        PetFamily.Relicborn => "clockwork_pangolin",
        _ => null,
    };

    /// Whether a family can be picked.
    ///
    /// Wildkin is pickable because it is the default family and the one the live
    /// Workling belongs to — greying it would tell a player their own pet is
    /// "coming soon". Its Moss-Fox body is still to be rigged and animated.
    ///
    /// Relicborn is pickable because the Clockwork Pangolin is the nearest body
    /// to being ready; see `Model` for what "ready" still costs.
    ///
    /// Elemental, Glitchkin and Bloomglass are listed and not pickable, so the
    /// roster still reads as five. Elemental is the odd one: the Tempest Ram is
    /// *its* body by `FamilyEnergy.For`, and every Workling is currently wearing
    /// it regardless of family. That resolves itself the moment the model swap
    /// is wired.
    public static bool IsPickable(PetFamily family) =>
        family is PetFamily.Wildkin or PetFamily.Relicborn;
}
