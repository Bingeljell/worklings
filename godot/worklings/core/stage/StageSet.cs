using Godot;
using System;

namespace Worklings.Core.Stage;

/// Which arena a fight is staged in.
public enum StageKind
{
    /// The original Cache Warren: bright sand floor under a warm key, authored
    /// in `scenes/dungeon_stage.tscn`. **Kept, not replaced.** It is the A/B
    /// against everything below, and the only honest way to ask whether the new
    /// stage earned the change.
    CacheWarren,

    /// Cool stone under a blue key with warm perimeter braziers. The default
    /// since 2026-09-12.
    MoonlitRuins,

    /// Basalt hexagons over molten seams, from a higher camera. An optional
    /// variation, retained from the studies.
    EmberVault,
}

/// Builds an arena at runtime: floor, masonry, lights, fog, camera and the two
/// marks the combatants stand on.
///
/// **Why the new stages are code and the old one is a `.tscn`.** The Moonlit
/// Ruins set is a few hundred procedurally-placed blocks with a seeded RNG —
/// authoring that as a scene file would be several thousand lines of baked
/// transforms that no one can review, and the studies that produced it were
/// already code. The original stage stays a `.tscn` because it is hand-placed
/// and someone may still want to open it in the editor.
///
/// **Extracted from `tools/BattleLab.MakeSet`, and the lab now consumes this**
/// rather than keeping its own copy. That is an acceptance criterion of the
/// handover and not merely tidiness: a preview built from different code than
/// the game is a preview of something that does not exist, which is how the
/// studies came to be framed for actor marks the live dungeon did not use.
///
/// The node this returns is named `Stage` and carries `StageCamera`,
/// `PartySlot` and `FoeSlot`, matching `dungeon_stage.tscn` exactly — the live
/// scene looks up `Stage/StageCamera` and that path is preserved deliberately.
public static class StageSet
{
    /// Where the two combatants stand, and where the camera was framed for.
    ///
    /// **These are the studies' marks, not the original scene's.** The Cache
    /// Warren stood them at (-3, 0, 8.5) and (0, 0, -2) — over eleven units
    /// apart — and the reviewed camera was framed around (-3.6, 0, 4.5) and
    /// (2.8, 0, -2). Adopting the camera without the marks frames an empty
    /// floor, which is the specific trap the handover warns about.
    public static readonly Vector3 PartyMark = new(-3.6f, 0, 4.5f);
    public static readonly Vector3 FoeMark = new(2.8f, 0, -2f);

    /// What the camera is aimed at: between the two marks and slightly above
    /// the floor, so both bodies and the ground effects under them are in frame.
    private static readonly Vector3 LookAt = new(0, 0.6f, 1.1f);

    public static string DisplayName(StageKind kind) => kind switch
    {
        StageKind.MoonlitRuins => "Moonlit Ruins",
        StageKind.EmberVault => "Ember Vault",
        _ => "Cache Warren",
    };

    /// Builds one of the procedural arenas. `CacheWarren` is not built here —
    /// it is an authored scene, and the caller instances it instead.
    public static Node3D Build(StageKind kind)
    {
        if (kind == StageKind.CacheWarren)
            throw new ArgumentException(
                "The Cache Warren is an authored scene; instance dungeon_stage.tscn instead.");

        bool ember = kind == StageKind.EmberVault;
        var set = new Node3D { Name = "Stage" };

        set.AddChild(new WorldEnvironment { Environment = MakeEnvironment() });
        set.AddChild(MakeCamera(ember));
        AddLights(set);

        var rng = new Random(400 + (ember ? 1 : 0));
        var stone = new StoneShop();

        // Mortar bed under everything, so a gap between tiles reads as a seam
        // rather than as a hole through to the background colour.
        Box(set, new Vector3(0, -0.4f, 1), new Vector3(39, 0.6f, 35),
            stone.Of(new Color(.025f, .033f, .043f)));

        AddFloor(set, stone, rng, ember);
        AddMasonry(set, stone, rng);
        AddBraziers(set, stone);
        if (ember) AddMoltenSeams(set);

        set.AddChild(new Marker3D { Name = "PartySlot", Position = PartyMark });
        set.AddChild(new Marker3D { Name = "FoeSlot", Position = FoeMark });
        return set;
    }

    /// The reviewed look. Filmic tonemapping, a glow pass and SSAO, over a
    /// near-black background with thin fog.
    ///
    /// **Glow is not optional here.** Every effect in the signature layer is
    /// additive geometry, and additive geometry with no bloom pass is flat
    /// colour — this is the finding that the bright original stage surfaced,
    /// carried forward rather than rediscovered.
    private static Godot.Environment MakeEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(.009f, .013f, .025f),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(.40f, .48f, .64f),
        AmbientLightEnergy = .42f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        GlowEnabled = true,
        GlowIntensity = .7f,
        GlowStrength = 1.0f,
        GlowBloom = .04f,
        GlowHdrThreshold = 1.4f,
        SsaoEnabled = true,
        SsaoRadius = 1.3f,
        SsaoIntensity = 1.8f,
        FogEnabled = true,
        FogLightColor = new Color(.055f, .075f, .11f),
        FogDensity = .0018f,
    };

    /// The reviewed framing. Wider than the original close combat view, to hold
    /// an attack and the ground effects spreading out from it.
    ///
    /// **Not yet proven for a party.** These cameras frame a one-on-one study;
    /// four-member placement and readability are untested, and a wider camera
    /// alone is not evidence either way.
    private static Camera3D MakeCamera(bool ember)
    {
        var at = ember ? new Vector3(12, 31, 22) : new Vector3(17, 24, 27);
        var camera = new Camera3D
        {
            Name = "StageCamera",
            Current = true,
            Fov = 36,
            Near = .1f,
            Far = 150,
        };
        // LookAtFromPosition, not Position-then-LookAt: `LookAt` reads the
        // node's *global* transform and errors out on a node that is not in the
        // tree yet, leaving the camera pointing down the -Z axis at nothing.
        // This stage is built before it is added, so the aim has to come with
        // the position rather than after it.
        camera.LookAtFromPosition(at, LookAt, Vector3.Up);
        return camera;
    }

    /// Cool key, cool fill, warm rim. The warm rim is what keeps the bodies off
    /// the blue floor; without it a cool-lit creature on cool stone has no edge.
    private static void AddLights(Node3D set)
    {
        set.AddChild(new DirectionalLight3D
        {
            Name = "KeyLight",
            LightColor = new Color(.72f, .82f, 1),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
            RotationDegrees = new Vector3(-52, -30, 0),
        });
        set.AddChild(new OmniLight3D
        {
            Name = "FillLight",
            Position = new Vector3(-7, 9, 7),
            LightColor = new Color(.55f, .68f, 1),
            LightEnergy = 2.0f,
            OmniRange = 24,
        });
        set.AddChild(new OmniLight3D
        {
            Name = "RimLight",
            Position = new Vector3(4, 8, -7),
            LightColor = new Color(1, .56f, .24f),
            LightEnergy = 3,
            OmniRange = 23,
        });
    }

    /// Square flags for the Ruins, hexagons for the Vault. Per-tile shade jitter
    /// so a large flat floor does not read as one painted plane.
    private static void AddFloor(Node3D set, StoneShop stone, Random rng, bool ember)
    {
        for (int x = -8; x <= 8; x++)
        for (int z = -7; z <= 7; z++)
        {
            float shade = .075f + (float)rng.NextDouble() * .055f;
            var tint = ember
                ? new Color(shade * .8f, shade * .76f, shade * .75f)
                : new Color(shade * .88f, shade, shade * 1.17f);
            if (ember)
            {
                Mesh(set, new CylinderMesh
                {
                    TopRadius = 1.23f, BottomRadius = 1.23f, Height = .25f, RadialSegments = 6,
                }, new Vector3(x * 2.14f + (z % 2 == 0 ? 0 : 1.07f), -.125f, z * 1.86f + 1),
                   stone.Of(tint));
            }
            else
            {
                Box(set, new Vector3(x * 2.15f, -.1f, z * 2.15f + 1),
                    new Vector3(2.08f, .20f, 2.08f), stone.Of(tint),
                    ((float)rng.NextDouble() - .5f) * .012f);
            }
        }
    }

    /// Perimeter walls, columns and rubble. Framed to enclose the fight without
    /// standing between the camera and either combatant.
    private static void AddMasonry(Node3D set, StoneShop stone, Random rng)
    {
        var mat = stone.Of(new Color(.13f, .16f, .19f));
        for (int i = -8; i <= 8; i++)
        {
            Box(set, new Vector3(i * 2.1f, .3f, -13.8f), new Vector3(2, .6f, 1.1f), mat);
            Box(set, new Vector3(-18, .25f, i * 1.7f), new Vector3(1.2f, .5f, 1.6f), mat);
        }
        // Broken colonnade: alternating heights so the skyline is not a comb.
        for (int i = 0; i < 6; i++)
        {
            var at = new Vector3(-13 + i * 5.2f, 0, -11.6f);
            Box(set, at + Vector3.Up * .22f, new Vector3(1.8f, .44f, 1.8f), mat);
            float height = i % 2 == 0 ? 4.5f : 2.8f;
            Mesh(set, new CylinderMesh
            {
                TopRadius = .56f, BottomRadius = .65f, Height = height, RadialSegments = 8,
            }, at + Vector3.Up * (height * .5f + .44f), mat);
            Box(set, at + Vector3.Up * (height + .54f), new Vector3(1.45f, .26f, 1.45f), mat);
        }
        // Scattered rubble, skipping the box the fight happens in.
        for (int i = 0; i < 30; i++)
        {
            float x = (float)rng.NextDouble() * 32 - 16;
            float z = (float)rng.NextDouble() * 24 - 11;
            if (Mathf.Abs(x) < 7 && z > -7 && z < 9) continue;
            float s = .2f + (float)rng.NextDouble() * .6f;
            Box(set, new Vector3(x, s * .4f, z), new Vector3(s * 1.6f, s * .8f, s), mat,
                (float)rng.NextDouble() * 3);
        }
    }

    /// The warm practical lights. Four of them, placed off the fight line, so
    /// the cool stage has a colour to contrast against and the bodies catch a
    /// warm edge from somewhere the audience can see.
    private static void AddBraziers(Node3D set, StoneShop stone)
    {
        var mat = stone.Of(new Color(.13f, .16f, .19f));
        foreach (var at in new[]
                 {
                     new Vector3(-9, 0, 3), new Vector3(8, 0, -7),
                     new Vector3(-7, 0, -9), new Vector3(10, 0, 8),
                 })
        {
            Mesh(set, new CylinderMesh
            {
                TopRadius = .35f, BottomRadius = .5f, Height = 1.2f, RadialSegments = 8,
            }, at + Vector3.Up * .6f, mat);
            Mesh(set, new SphereMesh { Radius = .25f, Height = .7f }, at + Vector3.Up * 1.4f,
                new StandardMaterial3D
                {
                    AlbedoColor = new Color(1, .28f, .025f),
                    EmissionEnabled = true,
                    Emission = new Color(1, .2f, .015f),
                    EmissionEnergyMultiplier = 3,
                });
            set.AddChild(new OmniLight3D
            {
                Position = at + Vector3.Up * 2,
                LightColor = new Color(1, .32f, .07f),
                LightEnergy = 3,
                OmniRange = 7,
            });
        }
    }

    /// The Ember Vault's molten cracks. Emissive slivers just above the floor —
    /// the light comes from the braziers, not from these.
    private static void AddMoltenSeams(Node3D set)
    {
        var lava = new StandardMaterial3D
        {
            AlbedoColor = new Color(.7f, .07f, .008f),
            EmissionEnabled = true,
            Emission = new Color(1, .09f, .004f),
            EmissionEnergyMultiplier = 2,
        };
        for (int i = 0; i < 15; i++)
        {
            float x = -15 + i * 2.1f;
            Box(set, new Vector3(x, .012f, -8.3f + Mathf.Sin(i * 1.6f) * .7f),
                new Vector3(2.2f, .022f, .11f), lava, Mathf.Sin(i) * .3f);
            Box(set, new Vector3(-10 + Mathf.Sin(i) * .6f, .013f, -10 + i * 1.4f),
                new Vector3(.12f, .024f, 1.8f), lava);
        }
    }

    // MARK: - Geometry helpers

    /// One noise texture shared by every stone material, generated once.
    ///
    /// Without it each of the ~270 tiles is a flat untextured colour and the
    /// floor reads as a chessboard of paint chips. Materials still differ per
    /// tile — only the texture is shared.
    private sealed class StoneShop
    {
        private ImageTexture? _noise;

        public StandardMaterial3D Of(Color tint, float roughness = .9f) => new()
        {
            AlbedoColor = tint,
            Roughness = roughness,
            AlbedoTexture = Noise(),
        };

        private ImageTexture Noise()
        {
            if (_noise != null) return _noise;
            var noise = new FastNoiseLite { Seed = 741, Frequency = .095f, FractalOctaves = 5 };
            var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float n = .66f + noise.GetNoise2D(x, y) * .40f;
                img.SetPixel(x, y, new Color(n, n, n));
            }
            return _noise = ImageTexture.CreateFromImage(img);
        }
    }

    private static MeshInstance3D Mesh(Node3D parent, Mesh mesh, Vector3 at, Material mat)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = mat };
        parent.AddChild(node);
        return node;
    }

    private static void Box(Node3D parent, Vector3 at, Vector3 size, Material mat, float yaw = 0)
        => Mesh(parent, new BoxMesh { Size = size }, at, mat).Rotation = new Vector3(0, yaw, 0);
}
