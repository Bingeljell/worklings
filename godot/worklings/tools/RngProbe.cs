using Godot;
using Worklings.Core.Combat;

/// Compares the C# SeededGenerator against reference values captured from the
/// Swift original. If these diverge, every seeded fight diverges — so this is
/// checked directly rather than inferred from a passing build.
public partial class RngProbe : Node
{
    public override void _Ready()
    {
        var g = new SeededGenerator(12345);
        var words = new string[8];
        for (int i = 0; i < 8; i++) words[i] = g.Next().ToString();
        GD.Print("CS_WORDS ", string.Join(",", words));

        var g2 = new SeededGenerator(99);
        var doubles = new string[6];
        for (int i = 0; i < 6; i++) doubles[i] = g2.NextDouble().ToString("G17");
        GD.Print("CS_DOUBLES ", string.Join(",", doubles));

        var g3 = new SeededGenerator(7);
        var chances = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++) chances.Append(g3.Chance(0.5) ? '1' : '0');
        GD.Print("CS_CHANCE ", chances.ToString());

        var g4 = new SeededGenerator(2024);
        var closed = new string[6];
        for (int i = 0; i < 6; i++) closed[i] = g4.NextDoubleClosed(-0.15, 0.15).ToString("G17");
        GD.Print("CS_CLOSED ", string.Join(",", closed));

        var g5 = new SeededGenerator(555);
        var mixed = new string[6];
        for (int i = 0; i < 6; i++)
            mixed[i] = i % 2 == 0
                ? (g5.Chance(0.5) ? "T" : "F")
                : g5.NextDoubleClosed(-1.0, 1.0).ToString("G17");
        GD.Print("CS_MIXED ", string.Join(",", mixed));

        GetTree().Quit();
    }
}
