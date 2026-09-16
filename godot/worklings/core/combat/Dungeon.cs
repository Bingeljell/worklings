using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// How close a dungeon is to being enterable in this build.
///
/// The same question `CreatureReadiness` asks, and for the same reason: a place
/// named in the roster but not finished should still be *listed*, so the world
/// reads as bigger than one room, while never being handed to the delve.
public enum DungeonReadiness
{
    /// Its chain is built and its foes all have bodies. Enterable now.
    Open,

    /// Named so the selector reads as a world rather than a single door. Shown
    /// greyed, never descended into.
    Planned,
}

/// One place you can descend into, as data.
///
/// **This is the seam that turns "the dungeon" into "a dungeon".** The delve
/// engine was already general — `Delve` takes its regular encounters and its
/// boss as constructor arguments — but the only way to build one was
/// `Delve.CacheWarrenDelve`, a factory that named `CacheWarren.Encounters` and
/// `CacheWarren.Boss` directly. So the engine could run any chain and the app
/// could only ask for one.
///
/// A dungeon is now the chain plus the words around it: what it is called, how
/// it is pitched to a player standing at its mouth, and the hint about what
/// kind of prep it rewards. That last part matters — the briefing's one
/// gameplay job is telling you what to pack for, and a briefing that belongs to
/// the scene rather than to the place cannot do that job for a second place.
///
/// **Adding a dungeon is one entry in `DungeonRoster.All`.** Same contract as
/// `CreatureRoster`: append, and it is selectable, enterable and narrated.
public sealed record Dungeon(
    /// Stable key, used by saves and captures. Lowercase, underscored.
    string Id,

    /// What the player is told they are entering.
    string DisplayName,

    /// The pitch, read at the mouth. Two or three sentences: what lives here and
    /// what that implies about what to bring.
    string Briefing,

    /// The regular encounters, in order.
    IReadOnlyList<Foe> Encounters,

    /// The one at the bottom.
    Foe Boss,

    DungeonReadiness Readiness = DungeonReadiness.Open,

    /// A short line under the name in the selector — the shape of the place in
    /// half a dozen words, so a player choosing between two doors has something
    /// to choose on besides the name.
    string Flavour = "")
{
    /// How many fights a full clear is, boss included.
    public int EncounterCount => Encounters.Count + 1;

    /// Whether the delve may be handed this dungeon.
    public bool IsEnterable =>
        Readiness == DungeonReadiness.Open && Encounters.Count > 0;
}

/// Every place the game knows about.
///
/// One list, appended to. See `Dungeon` for why this exists at all.
public static class DungeonRoster
{
    /// The first dungeon, and the one the whole delve loop was built against: a
    /// warm-up, a wall, an accuracy test, then something heavy.
    public static readonly Dungeon CacheWarrenDungeon = new(
        Id: "cache_warren",
        DisplayName: "The Cache Warren",
        Briefing:
            "A dungeon looms. If this is the Cache Warren, expect a nimble scamp, a "
          + "grabbing Snag, an evasive Flicker — and something heavy at the bottom. "
          + "You may want to pack for accuracy. Or bring a Ward.",
        Encounters: CacheWarren.Encounters,
        Boss: CacheWarren.Boss,
        Flavour: "Four fights. Something heavy at the bottom.");

    /// Every dungeon, in the order they are offered.
    public static readonly IReadOnlyList<Dungeon> All = new[]
    {
        CacheWarrenDungeon,
    };

    /// The ones a player may actually descend into today.
    public static IEnumerable<Dungeon> Enterable()
    {
        foreach (var d in All) if (d.IsEnterable) yield return d;
    }

    /// A dungeon by id, or null. Null is a real answer — a save naming a place
    /// this build does not have is a downgrade, not corruption.
    public static Dungeon? Find(string id)
    {
        foreach (var d in All) if (d.Id == id) return d;
        return null;
    }

    /// The default door. The first enterable dungeon rather than a named
    /// constant, so removing or gating the Warren does not strand the app on a
    /// place it cannot enter.
    public static Dungeon Default
    {
        get
        {
            foreach (var d in All) if (d.IsEnterable) return d;
            return CacheWarrenDungeon;
        }
    }
}
