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

    public StageCast(Node owner) { _owner = owner; }

    /// Every creature currently built, by id.
    public IReadOnlyDictionary<string, StageActor> Actors => _actors;

    /// Builds a creature's body under `parent`, standing on `mark` and facing
    /// `facing`. Returns the existing actor if it is already built, so a delve
    /// that meets the same creature twice does not pay for it twice.
    ///
    /// `key` scopes the body to a slot, and defaults to the creature's id.
    /// **The party and the foes must pass different keys**, because a creature
    /// can be on both sides: the Flicker is `CreatureRole.Either`, so a player
    /// wearing one and fighting one would otherwise share a single body and the
    /// fight would be one cat attacking itself.
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

        // Measured before the transform is applied, so the number is the
        // model's own authored height rather than whatever it was last set to.
        float modelHeight = MeasureHeight(root);
        _modelHeights[key] = modelHeight;
        root.Transform = Placement(mark, facing, ScaleFor(creature, modelHeight, heightOverride));

        var actor = new StageActor(root, creature.Id, creature.Animations!);
        _actors[key] = actor;
        return actor;
    }

    /// Re-sizes an already-built body — how a stand-in reads as something other
    /// than itself without a second copy of the body in the tree.
    public void SetHeight(string key, float height)
    {
        if (!_actors.TryGetValue(key, out var actor)) return;
        if (!_modelHeights.TryGetValue(key, out float modelHeight)) return;
        float scale = modelHeight > 0.0001f ? height / modelHeight : 1f;
        var t = actor.Root.Transform;
        actor.Root.Transform = new Transform3D(t.Basis.Orthonormalized().Scaled(Vector3.One * scale),
                                               t.Origin);
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
    private static Transform3D Placement(Vector3 mark, Vector3 facing, float scale)
    {
        var flat = new Vector3(facing.X - mark.X, 0, facing.Z - mark.Z);
        float yaw = flat.LengthSquared() > 0.0001f ? Mathf.Atan2(flat.X, flat.Z) : 0f;
        var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
        return new Transform3D(basis, mark);
    }

    /// The model's height in its own units, from the union of every mesh's
    /// bounds.
    ///
    /// Skinned meshes report bounds in skeleton space, so each one is walked up
    /// to the instance root rather than read in isolation — a mesh parented
    /// under a posed skeleton otherwise measures whatever that pose happened to
    /// be.
    private static float MeasureHeight(Node3D root)
    {
        var box = new Aabb();
        bool any = false;
        Walk(root, root, ref box, ref any);
        return any ? box.Size.Y : 0f;
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
