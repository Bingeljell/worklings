using Godot;

/// Proves the .NET assembly actually loads and runs inside Godot, which a
/// successful `dotnet build` does not by itself demonstrate.
public partial class CSharpProbe : Node
{
    public override void _Ready()
    {
        GD.Print("CSHARP RUNTIME OK: ", Worklings.Core.BuildProbe.Describe());
        GetTree().Quit();
    }
}
