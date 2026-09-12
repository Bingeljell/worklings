namespace Worklings.Core.Stage;

/// What a character's attack *looks* like, as distinct from what it does.
///
/// The split from ImpactFrames is the whole point of this file. ImpactFrames is
/// the physics of a landed blow — hit-stop, shake, knockback, the white flash —
/// and it is deliberately the same for everyone, because a hit weighing
/// differently per character would read as inconsistency rather than as
/// identity. This is the other half: what the *world* does when that character
/// connects, which should be different for everyone.
///
/// Read off the Path of Exile 2 reference shots Nikhil pulled. The finding
/// there was not "their effects are bigger" — it was **where** the effect
/// lives. In all three references the attacker's own body is doing very little
/// and the spectacle is on the victim and on the ground: lightning falls out of
/// the sky onto the target, a shockwave travels outward across the floor from
/// the point of impact, a volley crosses the space between the two. Nothing is
/// stuck to the attacker.
///
/// That matters here for a specific reason. The lunge and the ghost trail exist
/// because the fight read as "two models sliding at each other", and hanging
/// more effects off the attacker would hide that complaint rather than answer
/// it. Keeping the loud part on the victim and the floor is both the reference's
/// answer and ours.
public enum AbilitySignature
{
    /// Nothing beyond the universal impact reaction. The honest default for a
    /// character whose signature has not been designed yet — better than giving
    /// everyone the same generic burst and calling it identity.
    None,

    /// Zeus calling it down: a bolt out of frame onto the victim, a white core
    /// in a violet halo, the floor lit for a frame, a scorch left behind.
    LightningStrike,

    /// A ring travelling epicentre-outward across the floor with fire only at
    /// the leading edge, leaving a burn that stays. The reference shot's whole
    /// trick is that the interior is *dark* — the fire is a travelling rim, not
    /// a filled disc.
    FireShockwave,

    /// A staggered flight of emissive shapes crossing the gap. In the reference
    /// these are arrows; here they are the Flicker's paws, thrown ahead of the
    /// body so the cat strikes from further than its reach.
    GhostVolley,

    /// The floor opening under the victim during the wind-up and snapping taut
    /// on the crack. The one signature that runs mostly *before* contact, which
    /// is what a rooted foe with a long tell needs.
    Roots,
}

/// Which signature a creature throws.
///
/// **The roster carries this now** — it was one of five string-switches keyed
/// off a .glb basename, and the one whose silent fallback was least visible: a
/// creature missing from the table simply threw nothing, which is
/// indistinguishable from a creature whose signature is deliberately `None`.
///
/// Kept as a forward because the scene holds a model name at the call site.
/// Prefer `creature.Signature` where you have the creature.
public static class AbilitySignatures
{
    public static AbilitySignature For(string modelName) =>
        Worklings.Core.Roster.CreatureRoster.FindOrDefault(modelName).Signature;
}
