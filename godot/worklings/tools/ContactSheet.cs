using Godot;

/// Stitches stills into one side-by-side strip.
///
/// Exists because there is no ImageMagick or PIL on this machine and a
/// comparison is worthless if the reader has to flip between tabs to make it.
/// Godot already has an image loader and a blitter, so it does the job.
public partial class ContactSheet : Node
{
    private const int Height = 660;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Length < 2) { GD.Print("usage: --  out.png  in1 in2 ..."); GetTree().Quit(1); return; }

        string output = args[0];
        var panels = new System.Collections.Generic.List<Image>();
        for (int i = 1; i < args.Length; i++)
        {
            var image = Image.LoadFromFile(args[i]);
            if (image == null) { GD.Print($"could not load {args[i]}"); continue; }
            // Normalise on height so panels of different aspect still line up.
            int width = Mathf.RoundToInt(image.GetWidth() * (float)Height / image.GetHeight());
            image.Resize(width, Height, Image.Interpolation.Lanczos);
            image.Convert(Image.Format.Rgba8);
            panels.Add(image);
        }

        const int Gap = 8;
        int total = 0;
        foreach (var p in panels) total += p.GetWidth() + Gap;

        var sheet = Image.CreateEmpty(total - Gap, Height, false, Image.Format.Rgba8);
        sheet.Fill(new Color("0b0d12"));
        int x = 0;
        foreach (var p in panels)
        {
            sheet.BlitRect(p, new Rect2I(0, 0, p.GetWidth(), Height), new Vector2I(x, 0));
            x += p.GetWidth() + Gap;
        }
        sheet.SavePng(output);
        GD.Print($"{output}  {sheet.GetWidth()}x{sheet.GetHeight()}  ({panels.Count} panels)");
        GetTree().Quit();
    }
}
