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

    public StageActor(Node3D root, string modelName, ActorAnimations animations)
    {
        Root = root;
        ModelName = modelName;
        Animations = animations;
        _player = FindPlayer(root);
        _restPosition = root.Position;
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
        return animation.Length;
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
