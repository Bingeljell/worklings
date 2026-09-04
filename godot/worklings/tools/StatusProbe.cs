using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;

/// Compares condition and presentation against reference output captured from
/// the Swift original: which needs are worth mentioning, in what order, in what
/// words, and how the pet reads while it is doing it.
///
/// The ordering is the part worth probing. A condition's rank interleaves
/// urgency with whether the need is a body or a feeling — a critically hungry
/// pet outranks a critically sad one, but an urgently hungry one does not
/// outrank a critically sad one — and only the top two ever reach the summary.
/// Every threshold is checked on its boundary and one either side, because each
/// one is a `>=` or a `<=` that would read identically if it were wrong.
public partial class StatusProbe : Node
{
    private static readonly System.DateTimeOffset Base = PetStateCodec.FromSwiftDate(800_000_000);
    private readonly System.Text.StringBuilder o = new();

    private static string F(double v) =>
        v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private static PetState Pet(
        double hunger = 20, double energy = 80, double happiness = 70, double trust = 50,
        double xp = 0) =>
        new PetState(
            name: "Fren",
            family: PetFamily.Wildkin,
            needs: new PetNeeds(hunger, energy, happiness, trust),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: Base,
            totalXP: xp);

    private void Status(string label, PetState state)
    {
        var status = PetCareStatus.Make(state);
        var parts = new List<string>();
        foreach (var c in status.Conditions)
        {
            parts.Add($"{c.Kind.RawValue()}/{c.Urgency.RawValue()}/{F(c.Value)}/\"{c.Phrase}\"");
        }
        var ambient = status.AmbientCondition;
        o.AppendLine($"{label}: [{string.Join(" ", parts)}] "
                   + $"ambient={(ambient is null ? "-" : ambient.Kind.RawValue())} "
                   + $"summary=\"{status.HoverSummary}\"");
    }

    public override void _Ready()
    {
        o.AppendLine("== one need at a time, on every threshold ==");
        foreach (double v in new double[] { 0, 54, 55, 74, 75, 89, 90, 100 })
        {
            Status($"hunger {F(v)}", Pet(hunger: v));
        }
        foreach (double v in new double[] { 100, 46, 45, 21, 20, 11, 10, 0 })
        {
            Status($"energy {F(v)}", Pet(energy: v));
        }
        foreach (double v in new double[] { 100, 46, 45, 31, 30, 16, 15, 0 })
        {
            Status($"happiness {F(v)}", Pet(happiness: v));
        }
        foreach (double v in new double[] { 100, 36, 35, 21, 20, 11, 10, 0 })
        {
            Status($"trust {F(v)}", Pet(trust: v));
        }

        o.AppendLine("== the ranking, when several are true at once ==");
        // A body need and a feeling at the same urgency: the body goes first.
        Status("hungry and sad", Pet(hunger: 80, happiness: 25));
        // A critical feeling outranks an urgent body need.
        Status("sleepy and very unhappy", Pet(energy: 15, happiness: 10));
        // Both critical, both physical: hunger before energy.
        Status("starving and exhausted", Pet(hunger: 95, energy: 5));
        // Both critical, both emotional: trust before happiness.
        Status("very unhappy and untrusting", Pet(happiness: 10, trust: 5));
        // Four at once, and only the first two are spoken.
        Status("everything at once", Pet(hunger: 95, energy: 5, happiness: 10, trust: 5));
        Status("three, mixed urgency", Pet(hunger: 60, energy: 15, trust: 5));
        Status("nothing wrong", Pet());
        // "Happy" and "doing well" are different sentences for no conditions.
        Status("nothing wrong, happy", Pet(hunger: 5, energy: 95, happiness: 95, trust: 95));

        o.AppendLine("== what the menu may offer ==");
        foreach (var (label, state) in new (string, PetState)[]
                 {
                     ("ordinary", Pet()),
                     ("completely full", Pet(hunger: 0)),
                     ("barely peckish", Pet(hunger: 0.5)),
                     ("too tired to play", Pet(energy: 14)),
                     ("just awake enough", Pet(energy: 15)),
                     ("fully rested", Pet(energy: 100)),
                     ("almost rested", Pet(energy: 99.5)),
                 })
        {
            var status = PetCareStatus.Make(state);
            var parts = new List<string>();
            foreach (var action in PetCareActionKindExtensions.AllCases)
            {
                var a = status.Availability(action, state);
                parts.Add($"{action.RawValue()}={(a.IsEnabled ? "yes" : $"no:{a.Explanation}")}");
            }
            o.AppendLine($"{label}: {string.Join("  ", parts)}");
        }

        o.AppendLine("== presentation ==");
        foreach (var (label, state) in new (string, PetState)[]
                 {
                     ("happy", Pet(hunger: 5, energy: 95, happiness: 95, trust: 95)),
                     ("content", Pet()),
                     ("hungry", Pet(hunger: 80)),
                     ("sleepy", Pet(energy: 15)),
                     ("sad", Pet(happiness: 20)),
                     ("wary", Pet(trust: 15)),
                 })
        {
            Present(label, state, null);
        }
        // A reaction takes the face and the thought and leaves the mood alone:
        // the pet is still hungry while it is delighted about your commit.
        foreach (var reaction in new[]
                 {
                     PetReaction.LikedFood, PetReaction.TooTiredToPlay,
                     PetReaction.SharedSetback, PetReaction.NoticedYouAreAway,
                     PetReaction.TookABreak, PetReaction.WaitingOnYou,
                     PetReaction.ProudOfMilestone, PetReaction.LoggedWork,
                 })
        {
            Present($"hungry + {reaction.RawValue()}", Pet(hunger: 80), reaction);
        }

        o.AppendLine("== every thought ==");
        foreach (var reaction in new[]
                 {
                     PetReaction.LikedFood, PetReaction.LovedFood, PetReaction.EnjoyedPlay,
                     PetReaction.LovedPlay, PetReaction.Comforted, PetReaction.Rested,
                     PetReaction.TooTiredToPlay, PetReaction.HappyToSeeYou,
                     PetReaction.CelebratedTask, PetReaction.SharedSetback,
                     PetReaction.ProudOfMilestone, PetReaction.GladYouAreBack,
                     PetReaction.StartedWorking, PetReaction.TookABreak,
                     PetReaction.WaitingOnYou, PetReaction.NoticedYouAreAway,
                     PetReaction.LoggedWork,
                 })
        {
            o.AppendLine($"{reaction.RawValue()}: \"{PetPresentation.ThoughtFor(reaction)}\"");
        }

        o.AppendLine("== labels ==");
        foreach (var (label, state) in new (string, PetState)[]
                 {
                     ("fresh", Pet()),
                     ("levelled", Pet(xp: 2600)),
                     ("neglected", Pet(hunger: 95, energy: 5, happiness: 10, trust: 5)),
                     ("thriving", Pet(hunger: 0, energy: 100, happiness: 100, trust: 100)),
                 })
        {
            o.AppendLine($"{label}: \"{PetPresentation.LevelClassLabel(state)}\" "
                       + $"{PetPresentation.LearningRatePercent(state)}% "
                       + $"\"{PetPresentation.LearningRateLabel(state)}\"");
        }

        o.AppendLine("== the transition ==");
        foreach (var kind in new[]
                 {
                     CompanionTransitionKind.Reveal, CompanionTransitionKind.Conceal,
                     CompanionTransitionKind.FamilySwap,
                 })
        {
            var parts = new List<string>();
            foreach (var frame in CompanionTransitionPlan.Frames(kind))
            {
                parts.Add($"{frame.SpriteIndex}{(frame.IsPetVisible ? "v" : "-")}"
                        + $"{(frame.ShouldSwapFamily ? "S" : "")}");
            }
            o.AppendLine($"{kind.RawValue()}: {string.Join(" ", parts)}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private void Present(string label, PetState state, PetReaction? reaction)
    {
        var p = PetPresentation.Make(state, reaction);
        o.AppendLine($"{label}: mood=\"{p.MoodLabel}\" palette={p.Palette.RawValue()} "
                   + $"face={p.Face.RawValue()} thought={(p.Thought is null ? "-" : $"\"{p.Thought}\"")}");
    }
}
