using Godot;
using System.Linq;

namespace Worklings.Core.Stage;

/// One combatant on the stage: its model, its AnimationPlayer, and the actions
/// it can play. Wraps the imported .glb so callers ask for a beat ("wince")
/// rather than an animation name.
///
/// Also the thing impact frames act on — it owns the node whose transform gets
/// shaken and the point in an attack where the blow connects.
public sealed class StageActor
{
    public Node3D Root { get; }
    public string ModelName { get; }
    public ActorAnimations Animations { get; }

    private readonly AnimationPlayer? _player;
    private readonly Vector3 _restPosition;
    /// The body's upright orientation, rotation only. A death topple turns the
    /// node itself, and the foes are a pool swapped by visibility — so the body
    /// that fell over in encounter two is the same one the Monolith stands up in
    /// at encounter four, and something has to be able to put it back.
    private readonly Basis _restBasis;

    /// What is playing, and how much of it is left. Held so a finished one-shot
    /// can return to the idle loop by itself.
    private ActorAction _playing = ActorAction.Idle;
    private double _remaining;
    private bool _looping = true;

    /// The skinned mesh and the player driving it, for the ghost trail — which
    /// has to pose the skeleton itself to bake a snapshot of it. Nothing else
    /// should reach past Play() for them.
    public MeshInstance3D? Mesh { get; }
    public AnimationPlayer? Player => _player;

    public StageActor(Node3D root, string modelName, ActorAnimations animations)
    {
        Root = root;
        ModelName = modelName;
        Animations = animations;
        _player = FindPlayer(root);
        Mesh = FindMesh(root);
        _restPosition = root.Position;
        _restBasis = root.Transform.Basis.Orthonormalized();
        VerifyAnimations();
    }

    /// Checks every mapped name against what the model actually shipped, once,
    /// at startup. A missing animation otherwise fails silently — Play() does
    /// nothing and the actor just stands there, which reads as a bug in the
    /// combat logic rather than a typo in a table.
    private void VerifyAnimations()
    {
        if (_player == null)
        {
            GD.PushWarning($"[{ModelName}] no AnimationPlayer found");
            return;
        }
        var available = _player.GetAnimationList().ToHashSet();
        foreach (var (action, name) in Animations.All)
        {
            if (!available.Contains(name))
                GD.PushWarning($"[{ModelName}] {action} -> '{name}' not in the model "
                             + $"({available.Count} animations available)");
        }
    }

    public double Play(ActorAction action, bool loop = false)
    {
        var name = Animations.Name(action);
        if (_player == null || name == null || !_player.HasAnimation(name)) return 0;
        var animation = _player.GetAnimation(name);
        animation.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        _player.Play(name);
        _playing = action;
        _looping = loop;
        _remaining = loop ? 0 : animation.Length;
        return animation.Length;
    }

    /// Whether the actor is holding its downed pose.
    public bool IsDown => _playing == ActorAction.Downed;

    /// Returns a finished one-shot to the idle loop.
    ///
    /// **Nothing did this before, and it was the largest single reason combat
    /// read as static.** An AnimationPlayer given a non-looping clip stops on
    /// its last frame and stays there, so an attacker held the end of its swing
    /// for the whole three-second beat and a struck defender held the end of its
    /// wince until something else happened to it. Every creature on stage spent
    /// most of the fight frozen in a pose — which looks exactly like an
    /// animation that never played, because a pose and a still frame of a pose
    /// are the same picture.
    ///
    /// Downed is the deliberate exception: a body that gets up after being
    /// killed is worse than one that never fell.
    public void Tick(double delta)
    {
        if (_looping || _remaining <= 0) return;
        _remaining -= delta;
        if (_remaining > 0) return;
        _remaining = 0;
        if (_playing == ActorAction.Downed) return;
        Play(ActorAction.Idle, loop: true);
    }

    /// Seconds into an Attack at which the blow connects.
    public double AttackImpactDelay()
    {
        var name = Animations.Name(ActorAction.Attack);
        if (_player == null || name == null || !_player.HasAnimation(name)) return 0;
        return _player.GetAnimation(name).Length * Animations.AttackImpactPoint;
    }

    /// Displaces the model from its rest position — used by the hit reaction, so
    /// a struck actor is knocked rather than merely playing a wince clip.
    public void SetOffset(Vector3 offset) => Root.Position = _restPosition + offset;

    public void ClearOffset() => Root.Position = _restPosition;

    /// Tips the body over by `radians` about `axis`, keeping whatever scale the
    /// cast sized it to. How a creature with no death clip still falls down.
    public void SetTopple(float radians, Vector3 axis)
    {
        float scale = Root.Transform.Basis.Scale.X;
        var upright = _restBasis.Scaled(Vector3.One * scale);
        Root.Transform = new Transform3D(
            radians == 0 ? upright : new Basis(axis.Normalized(), radians) * upright,
            Root.Transform.Origin);
    }

    /// Stands the body back up on its mark. Called when a model is brought back
    /// on stage, because the pool is shared and the last thing this one did may
    /// have been to fall over.
    public void ResetPose()
    {
        SetTopple(0, Vector3.Up);
        ClearOffset();
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D m) return m;
        foreach (var child in node.GetChildren())
        {
            var found = FindMesh(child);
            if (found != null) return found;
        }
        return null;
    }

    private static AnimationPlayer? FindPlayer(Node node)
    {
        if (node is AnimationPlayer p) return p;
        foreach (var child in node.GetChildren())
        {
            var found = FindPlayer(child);
            if (found != null) return found;
        }
        return null;
    }
}
