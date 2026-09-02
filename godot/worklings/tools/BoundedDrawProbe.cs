using Godot;
using Worklings.Core.Combat;

/// Compares SeededGenerator's bounded draw against reference values captured
/// from the Swift original.
///
/// This exists because NextBelow was written from the shape of the old
/// modulo-with-rejection algorithm and was wrong: Swift uses Lemire's
/// multiply-shift, which returns the high half of the 128-bit product rather
/// than a remainder. Nothing called it until Delve started picking drops, so
/// the bug sat latent behind a passing build and a green combat check.
public partial class BoundedDrawProbe : Node
{
    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        var seeds = new ulong[] { 1, 2, 3, 7, 42, 99, 12345, 0 };

        o.AppendLine("== next(upperBound:) ==");
        var bounds = new ulong[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 10, 15, 16, 100, 1000, 65536,
            3037000499, 9223372036854775807, 18446744073709551615,
        };
        foreach (var bound in bounds)
        {
            var line = new System.Text.StringBuilder($"bound {bound}:");
            foreach (var seed in seeds)
            {
                var g = new SeededGenerator(seed);
                line.Append($" {g.NextBelow(bound)}");
            }
            o.AppendLine(line.ToString());
        }

        o.AppendLine("== stream consumption ==");
        foreach (ulong bound in new ulong[] { 3, 5, 8, 1000 })
        {
            var g = new SeededGenerator(7);
            var draws = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 6; i++) draws.Add(g.NextBelow(bound).ToString());
            o.AppendLine($"bound {bound}: {string.Join(",", draws)} then word {g.Next()}");
        }

        o.AppendLine("== Int.random(in:) ==");
        foreach (var (lo, hi) in new[] { (0, 4), (1, 6), (-5, 5), (0, 0), (10, 11) })
        {
            var line = new System.Text.StringBuilder($"{lo}...{hi}:");
            foreach (var seed in new ulong[] { 1, 2, 3, 7, 42 })
            {
                var g = new SeededGenerator(seed);
                line.Append($" {g.NextInt(lo, hi)}");
            }
            o.AppendLine(line.ToString());
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
