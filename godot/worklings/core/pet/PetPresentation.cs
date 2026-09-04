using System.Collections.Generic;
using Worklings.Core.Progression;

namespace Worklings.Core.Pet;

/// The colour a mood is shown in. Named by feeling rather than by hue, so the
/// art can change without every caller changing with it.
///
/// Ported from Sources/CompanionCore/PetPresentation.swift.
public enum PetPalette
{
    Bright,
    Calm,
    Hungry,
    Sleepy,
    Sad,
    Wary,
}

public enum PetFace
{
    Happy,
    Neutral,
    Hungry,
    Sleepy,
    Sad,
    Wary,
}

public static class PetPresentationExtensions
{
    public static string RawValue(this PetPalette palette) =>
        palette.ToString().ToLowerInvariant();

    public static string RawValue(this PetFace face) => face.ToString().ToLowerInvariant();

    public static string RawValue(this CompanionTransitionKind kind) =>
        char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString().Substring(1);
}

public enum CompanionTransitionKind
{
    Reveal,
    Conceal,
    FamilySwap,
}

public sealed class CompanionTransitionFrame : System.IEquatable<CompanionTransitionFrame>
{
    public int SpriteIndex { get; }
    public bool IsPetVisible { get; }
    public bool ShouldSwapFamily { get; }

    public CompanionTransitionFrame(int spriteIndex, bool isPetVisible, bool shouldSwapFamily)
    {
        SpriteIndex = spriteIndex;
        IsPetVisible = isPetVisible;
        ShouldSwapFamily = shouldSwapFamily;
    }

    public bool Equals(CompanionTransitionFrame? other) =>
        other is not null && SpriteIndex == other.SpriteIndex
        && IsPetVisible == other.IsPetVisible && ShouldSwapFamily == other.ShouldSwapFamily;

    public override bool Equals(object? obj) => Equals(obj as CompanionTransitionFrame);

    public override int GetHashCode() =>
        System.HashCode.Combine(SpriteIndex, IsPetVisible, ShouldSwapFamily);
}

/// When the pet appears, disappears, or changes into something else, a puff of
/// smoke covers the moment. This decides which frames the pet is visible in.
///
/// The point is the **obscuring frame**: the pet is swapped, revealed or hidden
/// exactly when the puff is at its thickest, so the change is never seen
/// happening. Pure, so the timing is checkable without a renderer.
///
/// The puff's own art is the legacy pixel-art sprite sheet — see the note in
/// `SmokePuff` — but the plan is about frames, not pixels, and outlives it.
public static class CompanionTransitionPlan
{
    public const int FrameCount = 8;
    public const int ObscuringFrameIndex = 4;

    public static IReadOnlyList<CompanionTransitionFrame> Frames(CompanionTransitionKind kind)
    {
        var frames = new List<CompanionTransitionFrame>(FrameCount);
        for (int index = 0; index < FrameCount; index++)
        {
            bool isPetVisible = kind switch
            {
                CompanionTransitionKind.Reveal => index >= ObscuringFrameIndex,
                CompanionTransitionKind.Conceal => index < ObscuringFrameIndex,
                _ => true,
            };
            frames.Add(new CompanionTransitionFrame(
                spriteIndex: index,
                isPetVisible: isPetVisible,
                shouldSwapFamily: kind == CompanionTransitionKind.FamilySwap
                                  && index == ObscuringFrameIndex));
        }
        return frames;
    }
}

/// How the pet reads right now: the word for its mood, the colour, the face, and
/// what it is thinking if anything.
///
/// The one place a mood becomes something a surface can draw. A reaction, when
/// there is one, overrides the face and supplies the thought but leaves the mood
/// label and palette alone — the pet is still hungry while it is delighted about
/// your commit, and saying otherwise would make the mood readout lie.
public sealed class PetPresentation
{
    public string MoodLabel { get; }
    public PetPalette Palette { get; }
    public PetFace Face { get; }
    public string? Thought { get; }

    public PetPresentation(string moodLabel, PetPalette palette, PetFace face, string? thought)
    {
        MoodLabel = moodLabel;
        Palette = palette;
        Face = face;
        Thought = thought;
    }

    /// The one place the level-and-class readout is formatted, so the care card,
    /// the menu header and accessibility labels can never drift into different
    /// spellings of the same fact.
    public static string LevelClassLabel(PetState state) =>
        $"Level {state.Level} {state.PetClass.DisplayName()}";

    /// Surfaces the condition multiplier — the care-to-XP coupling — as one
    /// plain line, so it stops being an invisible number players can only
    /// reverse-engineer from shrunken grants. Uses the same default floor the
    /// live brain runs with, so the percentage shown is the rate XP is actually
    /// granted at.
    public static int LearningRatePercent(PetState state)
    {
        double multiplier = state.Needs.XPMultiplier(
            new PetProgressionRates().ConditionMultiplierFloor);
        // Swift's `.rounded()` is half-away-from-zero. C# rounds half to even by
        // default, which would turn 45.5% into 46% on one side and 45% here.
        return (int)System.Math.Round(multiplier * 100, System.MidpointRounding.AwayFromZero);
    }

    public static string LearningRateLabel(PetState state) =>
        $"Learning at {LearningRatePercent(state)}% — a happier Workling earns faster";

    public static PetPresentation Make(PetState state, PetReaction? reaction = null)
    {
        var mood = state.Mood switch
        {
            PetMood.Happy => new PetPresentation("Happy", PetPalette.Bright, PetFace.Happy, null),
            PetMood.Content => new PetPresentation("Content", PetPalette.Calm, PetFace.Neutral, null),
            PetMood.Hungry => new PetPresentation(
                "Hungry", PetPalette.Hungry, PetFace.Hungry, "Snack time?"),
            PetMood.Sleepy => new PetPresentation(
                "Sleepy", PetPalette.Sleepy, PetFace.Sleepy, "So sleepy…"),
            PetMood.Sad => new PetPresentation(
                "Sad", PetPalette.Sad, PetFace.Sad, "Can we hang out?"),
            _ => new PetPresentation("Wary", PetPalette.Wary, PetFace.Wary, "I need some care."),
        };

        if (reaction is not PetReaction shown)
        {
            return mood;
        }

        var face = shown switch
        {
            PetReaction.TooTiredToPlay => PetFace.Sleepy,
            PetReaction.SharedSetback or PetReaction.NoticedYouAreAway => PetFace.Sad,
            PetReaction.TookABreak or PetReaction.WaitingOnYou => PetFace.Neutral,
            _ => PetFace.Happy,
        };

        return new PetPresentation(mood.MoodLabel, mood.Palette, face, ThoughtFor(shown));
    }

    /// What the pet says. The canonical table — `PetThought` draws these; it
    /// does not keep its own copy.
    public static string ThoughtFor(PetReaction reaction) => reaction switch
    {
        PetReaction.LikedFood => "Tasty!",
        PetReaction.LovedFood => "My favourite!",
        PetReaction.EnjoyedPlay => "That was fun!",
        PetReaction.LovedPlay => "Again, again!",
        PetReaction.Comforted => "I like you.",
        PetReaction.Rested => "Much better.",
        PetReaction.TooTiredToPlay => "Maybe after a nap…",
        PetReaction.HappyToSeeYou => "A new day!",
        PetReaction.CelebratedTask => "We did it!",
        PetReaction.SharedSetback => "We'll get the next one.",
        PetReaction.ProudOfMilestone => "Shipped!",
        PetReaction.GladYouAreBack => "You're back!",
        PetReaction.StartedWorking => "Let's get to work!",
        PetReaction.TookABreak => "Taking a breather.",
        PetReaction.WaitingOnYou => "Waiting on you…",
        PetReaction.NoticedYouAreAway => "Oh, you're away…",
        _ => "Logged!",
    };
}
