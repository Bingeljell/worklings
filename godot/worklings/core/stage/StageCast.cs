using Godot;
using System.Collections.Generic;
using Worklings.Core.Roster;

namespace Worklings.Core.Stage;

/// Puts creatures on a stage, built from the roster at runtime.
///
/// **This is the other half of "how do we keep adding Worklings".** The roster
/// made a creature's facts one entry; this makes its *body* require no scene
/// edit. Before, every creature was a hand-authored node inside
/// `cache_warren.tscn` with its size baked into a transform — so adding the
/// sixteenth Workling meant editing a `.tscn`, four C# switches and an
/// animation table, and the `.tscn` is the one of those that cannot be reviewed
/// in a diff. Now the scene carries two empty markers and the cast is assembled
/// from `CreatureRoster`.
///
/// It also owns sizing. Each creature declares how tall it stands in world
/// units; this measures the instanced model's real bounds and solves for the
/// scale that gets it there. A body re-exported at a different authored size
/// therefore lands at the same on-screen height instead of silently changing
/// character — which is exactly what a baked `transform` could never do.
public sealed class StageCast
{
    private readonly Node _owner;
    private readonly Dictionary<string, StageActor> _actors = new();
    private readonly Dictionary<string, float> _modelHeights = new();
    /// Where each model's lowest point sits relative to its own origin, in its
    /// own units. See `Placement` — this is what stands a body ON the floor
    /// rather than through it.
    private readonly Dictionary<string, float> _modelBottoms = new();
    /// The idle auras of everything built, advanced together by `DrawAuras`.
    private readonly Dictionary<string, CreatureAura> _auras = new();
    private double _auraSeconds;

    public StageCast(Node owner) { _owner = owner; }

    /// Every creature currently built, by id.
    public IReadOnlyDictionary<string, StageActor> Actors => _actors;

    /// Builds a creature's body under `parent`, standing on `mark` and facing
    /// `facing`. Returns the existing actor if it is already built, so a delve
    /// that meets the same creature twice does not pay for it twice.
    ///
    /// `key` scopes the body to a slot, and defaults to the creature's id.
    /// **The party and the foes must pass different keys**, because a creature
    /// can be on both sides. Nothing is `CreatureRole.Either` today — the
    /// Flicker was until it was corrected to `Foe` — but a player wearing a
    /// creature they also fight would otherwise share a single body, and the
    /// fight would be one cat attacking itself. Cheap to keep right.
    ///
    /// A creature the roster says is not renderable is refused rather than
    /// half-built: a `Planned` entry has no animation table, and a StageActor
    /// without one warns on every beat it cannot play.
    public StageActor? Add(Creature creature, Node3D parent, Vector3 mark, Vector3 facing,
                           float? heightOverride = null, string? key = null)
    {
        key ??= creature.Id;
        if (_actors.TryGetValue(key, out var existing)) return existing;
        if (!creature.IsRenderable)
        {
            GD.PushWarning($"[cast] {creature.Id} is not renderable "
                         + $"({creature.Readiness}); nothing was placed");
            return null;
        }

        var packed = GD.Load<PackedScene>(creature.ScenePath);
        if (packed == null)
        {
            GD.PushWarning($"[cast] {creature.ScenePath} did not load");
            return null;
        }

        var root = packed.Instantiate<Node3D>();
        root.Name = key;
        parent.AddChild(root);

        // Measured before the transform is applied, so the numbers are the
        // model's own authored bounds rather than whatever it was last set to.
        var bounds = MeasureBounds(root);
        float modelHeight = bounds.Size.Y;
        _modelHeights[key] = modelHeight;
        _modelBottoms[key] = bounds.Position.Y;
        root.Transform = Placement(mark, facing,
                                   ScaleFor(creature, modelHeight, heightOverride),
                                   bounds.Position.Y, creature.GroundOffset);

        var actor = new StageActor(root, creature.Id, creature.Animations!);
        // A creature's idle identity comes with its body. Null for most of them,
        // and a body that has one gets it whichever side of the fight it is on.
        if (CreatureAura.For(creature.Id, actor.Mesh) is { } aura) _auras[key] = aura;
        _actors[key] = actor;
        return actor;
    }

    /// Re-sizes an already-built body — how a stand-in reads as something other
    /// than itself without a second copy of the body in the tree.
    public void SetHeight(string key, string creatureId, float height)
    {
        if (!_actors.TryGetValue(key, out var actor)) return;
        if (!_modelHeights.TryGetValue(key, out float modelHeight)) return;
        if (!_modelBottoms.TryGetValue(key, out float modelBottom)) return;
        float scale = modelHeight > 0.0001f ? height / modelHeight : 1f;
        var t = actor.Root.Transform;
        // The lift is proportional to the scale, so it has to be recomputed
        // here and not merely preserved: the Snag stands 4.81 units as itself
        // and 7.50 as the Monolith, and the same model buried to a different
        // depth at each size is how this went unnoticed for a size and became
        // glaring at the other.
        float lift = Lift(modelBottom, scale, CreatureRoster.FindOrDefault(creatureId).GroundOffset);
        var origin = new Vector3(t.Origin.X, lift, t.Origin.Z);
        actor.Root.Transform = new Transform3D(
            t.Basis.Orthonormalized().Scaled(Vector3.One * scale), origin);
        // The rest position the hit reaction and the death topple return to has
        // just moved, so the actor is told rather than left holding the old one.
        actor.Rebase(origin);
    }

    /// Shows exactly one of the keys given and hides every other body sharing
    /// that slot's prefix. The dungeon keeps its whole cast in the tree and
    /// swaps by visibility, because instancing a `.glb` mid-fight is a frame
    /// hitch at the worst possible moment.
    public void ShowOnly(string key, string slotPrefix)
    {
        foreach (var (id, actor) in _actors)
            if (id.StartsWith(slotPrefix, System.StringComparison.Ordinal))
                actor.Root.Visible = id == key;
    }

    public StageActor? Get(string key) => _actors.TryGetValue(key, out var a) ? a : null;

    /// Advances every built aura. One call per frame from the scene, rather than
    /// each aura owning a timer, so a paused or stepped scene freezes them all
    /// together and a capture tool gets a deterministic picture.
    public void DrawAuras(double delta)
    {
        if (_auras.Count == 0) return;
        _auraSeconds += delta;
        foreach (var aura in _auras.Values) aura.Draw((float)_auraSeconds);
    }

    /// How far above the floor mark a model's origin must sit for the model's
    /// own lowest point to rest ON the mark.
    ///
    /// **Four of the five bodies are authored with their origin at their feet,
    /// and the Snag is authored around its middle** — its mesh runs from -0.342
    /// to +0.345 in its own units. Dropping every origin straight onto the mark
    /// therefore buried the Snag to exactly half its height, and buried it
    /// deeper the larger it got: 2.4 units as itself, 3.75 as the Monolith.
    ///
    /// Solved from the measured bounds rather than carried as a hand-tuned
    /// number per creature, for the same reason the height is: a body
    /// re-exported with a different origin corrects itself, where a magic
    /// constant silently stops being true. `GroundOffset` is left on top for
    /// creatures that are meant to hover rather than stand.
    private static float Lift(float modelBottom, float scale, float groundOffset) =>
        -modelBottom * scale + groundOffset;

    private static float ScaleFor(Creature creature, float modelHeight, float? heightOverride)
    {
        float target = heightOverride ?? creature.StageHeight;
        if (modelHeight <= 0.0001f)
        {
            GD.PushWarning($"[cast] {creature.Id} measured no height; left at 1x");
            return 1f;
        }
        return target / modelHeight;
    }

    /// Stands a body on its mark, turned to face a point. Yaw only — a creature
    /// tipped to look at something above or below it reads as falling over.
    private static Transform3D Placement(Vector3 mark, Vector3 facing, float scale,
                                         float modelBottom, float groundOffset)
    {
        var flat = new Vector3(facing.X - mark.X, 0, facing.Z - mark.Z);
        float yaw = flat.LengthSquared() > 0.0001f ? Mathf.Atan2(flat.X, flat.Z) : 0f;
        var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
        return new Transform3D(basis,
                               new Vector3(mark.X, Lift(modelBottom, scale, groundOffset), mark.Z));
    }

    /// The model's bounds in its own units, from the union of every mesh's.
    ///
    /// Skinned meshes report bounds in skeleton space, so each one is walked up
    /// to the instance root rather than read in isolation — a mesh parented
    /// under a posed skeleton otherwise measures whatever that pose happened to
    /// be.
    /// A model's own authored bounds, before any placement transform. Shared with
    /// the desktop pet, which scales its body by the same rule the stage does.
    internal static Aabb MeasureBounds(Node3D root)
    {
        var box = new Aabb();
        bool any = false;
        Walk(root, root, ref box, ref any);
        return any ? box : new Aabb();
    }

    private static void Walk(Node node, Node3D root, ref Aabb box, ref bool any)
    {
        if (node is VisualInstance3D vis && vis.GetAabb().Size.LengthSquared() > 0)
        {
            var local = root.GlobalTransform.AffineInverse() * vis.GlobalTransform;
            var here = local * vis.GetAabb();
            box = any ? box.Merge(here) : here;
            any = true;
        }
        foreach (var child in node.GetChildren()) Walk(child, root, ref box, ref any);
    }
}
