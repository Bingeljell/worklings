using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Stage;

/// Family colour, in one place, because it drives everything visual about a
/// combatant — HP bar, damage numbers, hit sparks, the impact flash tint.
///
/// This is not a decorative palette. It is read off the approved character
/// direction (assets/art-direction/approved-visual-direction.png), where each
/// family already carries a signature energy: the Elemental Ram's lightning,
/// the Relicborn Pangolin's rune-glow, the Glitchkin's chromatic break. Keying
/// combat VFX to it means a crit reads differently per family with no extra art
/// — one system, five readings.
public static class FamilyEnergy
{
    public static readonly Color Elemental = new("7B6BFF");   // arc lightning
    public static readonly Color Wildkin = new("8FD16A");     // leaf and pollen
    public static readonly Color Relicborn = new("E0A340");   // rune-glow
    public static readonly Color Glitchkin = new("FF4FD8");   // datamosh
    public static readonly Color Bloomglass = new("C9D8FF");  // iridescence

    /// A crit reads hotter than the family colour without abandoning it, so the
    /// family stays identifiable at the loudest moment rather than everything
    /// converging on the same orange.
    public static readonly Color Crit = new("FF5A3C");

    public static Color Of(PetFamily family) => family switch
    {
        PetFamily.Elemental => Elemental,
        PetFamily.Wildkin => Wildkin,
        PetFamily.Relicborn => Relicborn,
        PetFamily.Glitchkin => Glitchkin,
        PetFamily.Bloomglass => Bloomglass,
        _ => Bloomglass,
    };

    /// Which family a creature belongs to.
    ///
    /// **The roster carries this now.** This was a fifth string-switch keyed off
    /// the .glb basename, with a silent Bloomglass fallback that made an
    /// unregistered creature look deliberate. It stays as a one-line forward
    /// because callers hold a model name rather than a Creature; prefer
    /// `creature.Family` where you have the creature.
    public static PetFamily For(string modelName) =>
        Worklings.Core.Roster.CreatureRoster.FindOrDefault(modelName).Family;

    /// A lighter partner for gradients and bar fills.
    public static Color Lift(Color c, float amount = 0.42f) =>
        c.Lerp(new Color(1, 1, 1), amount);
}
