namespace Worklings.Core.Pet;

/// The cosmetic-identity axis, separate from PetClass (which carries the
/// mechanics). Declaration order matches Swift, because the raw values are
/// persisted and CaseIterable order drives selection UI.
///
/// Glitchkin and Bloomglass are the two design-stage families. They carry their
/// full mechanical identity (stat lean, passive, item attunement) from the
/// moment they exist here; only their art is outstanding, so they render the
/// placeholder glyph until their sheets are baked. Adding them ahead of the art
/// is deliberate — the roster, the family passives, and Guard/Agility item
/// attunement all key off this enum.
///
/// Ported from Sources/CompanionCore/PetState.swift.
public enum PetFamily
{
    Wildkin,
    Elemental,
    Relicborn,
    Glitchkin,
    Bloomglass,
}

public static class PetFamilyExtensions
{
    public static readonly PetFamily[] AllCases =
    {
        PetFamily.Wildkin, PetFamily.Elemental, PetFamily.Relicborn,
        PetFamily.Glitchkin, PetFamily.Bloomglass,
    };

    public static string DisplayName(this PetFamily family) => family switch
    {
        PetFamily.Wildkin => "Wildkin",
        PetFamily.Elemental => "Elemental",
        PetFamily.Relicborn => "Relicborn",
        PetFamily.Glitchkin => "Glitchkin",
        PetFamily.Bloomglass => "Bloomglass",
        _ => family.ToString(),
    };

    /// Whether the family has a baked sprite sheet. Selection UI uses this to
    /// mark the design-stage families rather than hiding them.
    public static bool HasArt(this PetFamily family) => family switch
    {
        PetFamily.Wildkin or PetFamily.Elemental or PetFamily.Relicborn => true,
        _ => false,
    };

    /// The Swift `rawValue`, which is what the JSON save format stores.
    public static string RawValue(this PetFamily family) =>
        family.ToString().ToLowerInvariant();
}
