using Godot;
using Worklings.Core.Stage;

/// Prints where each attack's contact frame falls, so the timing can be checked
/// as numbers rather than judged from a screenshot.
public partial class TimingProbe : Node
{
    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn").Instantiate();
        AddChild(scene);
        CallDeferred(nameof(Report), scene);
    }

    private void Report(Node scene)
    {
        foreach (var (node, model, anims) in new (string, string, ActorAnimations)[]
                 {
                     ("Party", "tempest_ram", ActorAnimations.TempestRam),
                     ("Foe", "forest_flicker", ActorAnimations.ForestFlicker),
                 })
        {
            var actor = new StageActor(scene.GetNode<Node3D>(node), model, anims);
            double contact = actor.AttackImpactDelay();
            GD.Print($"TIMING {model}: attack clip contact at {contact:0.000}s "
                   + $"(point {anims.AttackImpactPoint:0.00}), beat runs "
                   + $"{AttackLunge.DurationFor(contact):0.000}s");
        }
        GetTree().Quit();
    }
}
