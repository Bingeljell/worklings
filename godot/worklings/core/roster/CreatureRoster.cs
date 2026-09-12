using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Stage;

namespace Worklings.Core.Roster;

/// Every creature the game knows about, as data.
///
/// **Adding a creature is adding one entry to `All`.** Nothing else. Export the
/// `.glb` into `assets/characters/`, add its clip table to `ActorAnimations`,
/// append a `Creature` here, and it is pickable in the loadout, renderable on
/// any stage, correctly coloured, correctly sized, and throwing whichever
/// signature it was given. The design's eventual fifteen-to-twenty Worklings are
/// fifteen-to-twenty entries.
///
/// **A player will be locked into exactly one Workling** — one creature, one
/// class, chosen at onboarding and not changed afterwards. That is the shipped
/// rule, and this file does not implement it, because onboarding does not exist
/// yet. What it implements is the alpha stand-in: `Playable` returns everything
/// renderable, the loadout lists it, and the pool grows as bodies land. When
/// onboarding arrives the lock is a field on `PetState` naming a creature id,
/// and the loadout stops offering a choice — the roster itself does not change.
/// That is the whole reason the pool is a query rather than a hardcoded pair.
public static class CreatureRoster
{
    // MARK: - Worklings

    /// The Elemental's Tempest Ram. What every Workling wore before the roster,
    /// and still the default for a race with no body of its own.
    public static readonly Creature TempestRam = new(
        Id: "tempest_ram",
        DisplayName: "Tempest Ram",
        Family: PetFamily.Elemental,
        Animations: ActorAnimations.TempestRam,
        Signature: AbilitySignature.LightningStrike,
        Role: CreatureRole.Workling,
        Readiness: CreatureReadiness.Ready,
        StageHeight: 5.56f);

    /// The Relicborn's Key-back Pangolin. **No longer the Monolith's stand-in** —
    /// it was borrowed as the mini-boss while the Relicborn had no player body,
    /// and lending the boss a party creature stopped being acceptable the moment
    /// a player could walk in wearing it. The Monolith now stands in as a
    /// scaled-up Snag; see `Casting`.
    public static readonly Creature ClockworkPangolin = new(
        Id: "clockwork_pangolin",
        DisplayName: "Key-back Pangolin",
        Family: PetFamily.Relicborn,
        Animations: ActorAnimations.ClockworkPangolin,
        Signature: AbilitySignature.FireShockwave,
        Role: CreatureRole.Workling,
        Readiness: CreatureReadiness.Ready,
        // Long and low rather than tall. Matching the Ram's height would make a
        // pangolin eleven units long, which is a bus.
        StageHeight: 3.24f);

    // MARK: - Foes

    /// The Cache Warren's first encounter. Its own body at last: the `.blend`
    /// had been rigged and animated since 2026-09-08 and was never exported, so
    /// the fight that teaches the loop was being taught by a Flicker shrunk to
    /// 0.55 — the first thing a player ever sees, wearing someone else's face.
    public static readonly Creature DungeonScamp = new(
        Id: "dungeon_scamp",
        DisplayName: "Dungeon Scamp",
        Family: PetFamily.Glitchkin,
        Animations: ActorAnimations.DungeonScamp,
        Signature: AbilitySignature.None,
        Role: CreatureRole.Foe,
        Readiness: CreatureReadiness.Ready,
        // Deliberately small: under half the Ram. The first encounter should
        // look like a warm-up before the rules say it is one.
        StageHeight: 2.40f);

    /// The Wildkin's Forest Flicker. `Either`, and the clearest case for it:
    /// a Cache Warren foe, and also the Wildkin creature a player could wear
    /// once the race carries a body.
    public static readonly Creature ForestFlicker = new(
        Id: "forest_flicker",
        DisplayName: "Forest Flicker",
        Family: PetFamily.Wildkin,
        Animations: ActorAnimations.ForestFlicker,
        Signature: AbilitySignature.GhostVolley,
        Role: CreatureRole.Either,
        Readiness: CreatureReadiness.Ready,
        StageHeight: 4.34f);

    /// The rooted grabber. It exports 0.69 units tall and reads as a shrub at
    /// that size; 4.81 puts it at the Ram's shoulder, wider than it is tall,
    /// which is what a rooted grabber should look like. Checked in a rendered
    /// shot, not guessed — and moved here out of `cache_warren.tscn` so the size
    /// travels with the creature instead of belonging to one scene file.
    public static readonly Creature Snag = new(
        Id: "snag",
        DisplayName: "Snag",
        Family: PetFamily.Wildkin,
        Animations: ActorAnimations.Snag,
        Signature: AbilitySignature.Roots,
        Role: CreatureRole.Foe,
        Readiness: CreatureReadiness.Ready,
        StageHeight: 4.81f);

    /// Every creature, in roster order. The one list to append to.
    public static readonly IReadOnlyList<Creature> All = new[]
    {
        TempestRam, ClockworkPangolin, DungeonScamp, ForestFlicker, Snag,
    };

    private static readonly Dictionary<string, Creature> ById = Build();

    private static Dictionary<string, Creature> Build()
    {
        var map = new Dictionary<string, Creature>();
        foreach (var c in All) map[c.Id] = c;
        return map;
    }

    /// A creature by id, or null. Null is a real answer — a save naming a
    /// creature this build does not have is a downgrade, not corruption — so
    /// callers decide the fallback rather than being handed a silent Ram.
    public static Creature? Find(string id) => ById.TryGetValue(id, out var c) ? c : null;

    /// A creature by id, falling back to the Ram. For render paths that must
    /// put *something* on the stage.
    public static Creature FindOrDefault(string id) => Find(id) ?? TempestRam;

    /// What a player can be, today. The alpha pool — see the note on this class
    /// about why this is a query and not a pair of constants.
    public static IEnumerable<Creature> Playable()
    {
        foreach (var c in All) if (c.IsPlayable) yield return c;
    }

    /// The body a race wears, while a race still maps to one creature.
    ///
    /// This is `PetBody.Model`'s question and it is still a simplification: a
    /// race has five to nine creatures and `PetState` has no field naming which
    /// one you are. It resolves to the first playable creature of that race, so
    /// it stops being a hand-maintained table and starts being a consequence of
    /// the roster — and it disappears entirely when the save can name a creature.
    public static Creature ForRace(PetFamily race)
    {
        foreach (var c in All) if (c.Family == race && c.IsPlayable) return c;
        return TempestRam;
    }

    // MARK: - Casting

    /// Which creature stands on the stage for each foe in the bestiary, and at
    /// what size.
    ///
    /// **This is the one place the rules and the renderer touch**, and it is
    /// deliberately a lookup on the presentation side: the dungeon asks "who
    /// plays the Monolith", the bestiary never learns what a `.glb` is. Keeping
    /// the arrow pointing this way is what lets the combat probes resolve a
    /// whole delve headlessly with no models loaded at all.
    ///
    /// A stand-in carries a `Scale` override on top of the creature's own, which
    /// is why the value here multiplies rather than replaces.
    /// `Height` overrides the creature's own when a stand-in has to read as
    /// something other than itself.
    public sealed record Casting(Creature Creature, float? Height = null, bool IsStandIn = false)
    {
        public float StageHeight => Height ?? Creature.StageHeight;
    }

    /// **The Monolith is the only stand-in left.** It has no model, and the
    /// scaled-up Snag is Nikhil's call over a shrunk Flicker: a Colossus
    /// telegraphs a slam and hardens, and a rooted, bulky, wider-than-tall
    /// silhouette sells that where a fast wispy cat fights it. It is a
    /// placeholder and the capture will look like one — which is the point of
    /// marking it `IsStandIn` rather than quietly letting it pass as final.
    public static Casting For(string foeName) => foeName switch
    {
        "Dungeon Scamp" => new Casting(DungeonScamp),
        "Snag" => new Casting(Snag),
        "Flicker" => new Casting(ForestFlicker),
        // 7.50 against the Ram's 5.56. As a 1.3x Pangolin it stood 4.21 and the
        // player's own Workling was taller than the mini-boss.
        "Monolith" => new Casting(Snag, Height: 7.50f, IsStandIn: true),
        _ => new Casting(ForestFlicker, IsStandIn: true),
    };

    /// Every creature a delve may need on stage, so the dungeon can build its
    /// pool from the bestiary instead of from a hardcoded list of three.
    public static IEnumerable<Creature> CastFor(IEnumerable<string> foeNames)
    {
        var seen = new HashSet<string>();
        foreach (var name in foeNames)
        {
            var creature = For(name).Creature;
            if (seen.Add(creature.Id)) yield return creature;
        }
    }
}
