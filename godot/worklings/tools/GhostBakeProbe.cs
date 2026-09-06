using System.Diagnostics;
using System.Linq;
using Godot;

/// Measures MeshInstance3D.BakeMeshFromCurrentSkeletonPose() before anything is
/// built on it.
///
/// The motion-trail effect (docs/design/dungeons.md, open: motion trails) rests
/// on one assumption: that a ghost is a *static* snapshot of the posed mesh, so
/// eight of them cost triangles and no skinning. Godot 4.7 has a method that
/// claims to produce exactly that, and the C# docs attach a warning to it —
/// "Mesh data needs to be retrieved from the GPU, stalling the RenderingServer".
/// A readback stall is not free, and eight stalls inside one swing is the
/// difference between a cheap effect and a frame hitch.
///
/// So this asks four things, in the order that would kill the design fastest:
///
///   1. does the bake actually reflect the animated pose, or the rest pose?
///   2. what does one bake cost in milliseconds?
///   3. does passing an existing mesh reuse it, or silently allocate anyway?
///   4. what comes across — surfaces, materials, vertex count?
///
/// Deliberately a throwaway. It prints numbers and quits; nothing imports it.
public partial class GhostBakeProbe : Node
{
    private const string ModelPath = "res://assets/characters/snag.glb";
    private const string Clip = "Attack_Whip_24f_Review";

    /// How many ghosts a trail would hold at once. The per-swing cost below is
    /// this many bakes, which is the number that decides the design.
    private const int GhostCount = 8;

    private static readonly double[] SamplePoints = { 0.0, 0.25, 0.5, 0.75, 1.0 };

    /// The whole roster, because the first run raised the question the single
    /// measurement could not answer: is a bake a fixed pipeline stall, or does
    /// it scale with the mesh? Those have opposite consequences — a fixed stall
    /// means no amount of decimation saves the effect, while a per-vertex cost
    /// means a low-poly ghost proxy does.
    private static readonly string[] Roster =
        { "snag", "forest_flicker", "tempest_ram", "clockwork_pangolin" };

    public override async void _Ready()
    {
        var packed = GD.Load<PackedScene>(ModelPath);
        if (packed == null) { GD.Print($"could not load {ModelPath}"); GetTree().Quit(1); return; }

        var model = packed.Instantiate<Node3D>();
        AddChild(model);

        var mi = FindNode<MeshInstance3D>(model);
        var player = FindNode<AnimationPlayer>(model);
        var skeleton = FindNode<Skeleton3D>(model);

        GD.Print($"model            {ModelPath.Split('/')[^1]}");
        if (mi == null || player == null || skeleton == null)
        {
            GD.Print($"missing node     mesh={mi != null} anim={player != null} skel={skeleton != null}");
            GetTree().Quit(1);
            return;
        }

        var source = mi.Mesh;
        GD.Print($"mesh instance    {mi.Name}  surfaces {source.GetSurfaceCount()}  verts {VertexCount(source)}");
        // The docs make this a hard precondition: "Requires a skeleton with a
        // registered skin to work." A null skin is the difference between a
        // posed bake and silence.
        // Two separate things, and the distinction is the whole first failure:
        // `Skin` is the resource on the node, while the engine's precondition is
        // an internal skin_ref that only exists once `Skeleton` resolves to a
        // real Skeleton3D from inside the tree. A non-null Skin with an
        // unresolved path passes the obvious check and fails the bake.
        var pointedAt = mi.Skeleton.IsEmpty ? null : mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton);
        GD.Print($"skin resource    {(mi.Skin != null ? "yes" : "no")}");
        GD.Print($"skeleton path    \"{mi.Skeleton}\" -> {(pointedAt == null ? "UNRESOLVED" : pointedAt.Name.ToString())}");
        GD.Print($"mesh instances   {CountOf<MeshInstance3D>(model)} in the model");
        GD.Print($"skeleton         {skeleton.Name}, {skeleton.GetBoneCount()} bones");

        if (!player.HasAnimation(Clip)) { GD.Print($"no clip          {Clip}"); GetTree().Quit(1); return; }
        double length = player.GetAnimation(Clip).Length;
        GD.Print($"animation        {Clip}  {length:F2}s");

        // The mesh has to have been skinned on the GPU before there is anything
        // to read back, and nothing has rendered yet inside _Ready.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print("");
        GD.Print($"pose bakes — fresh ArrayMesh each, {SamplePoints.Length} points across the clip");
        var centroids = new Vector3[SamplePoints.Length];
        var ids = new ulong[SamplePoints.Length];
        for (int i = 0; i < SamplePoints.Length; i++)
        {
            await SeekTo(player, SamplePoints[i] * length);

            var watch = Stopwatch.StartNew();
            var baked = mi.BakeMeshFromCurrentSkeletonPose();
            watch.Stop();

            centroids[i] = Centroid(baked);
            ids[i] = baked.GetInstanceId();
            GD.Print($"  t={SamplePoints[i]:F2}  verts {VertexCount(baked),6}  " +
                     $"centroid {Fmt(centroids[i])}  aabb {baked.GetAabb().Size.Length():F2}  " +
                     $"{watch.Elapsed.TotalMilliseconds,6:F2} ms");
        }

        // The failure this exists to catch: a bake that returns the rest pose
        // every time looks perfectly successful and produces a trail of eight
        // identical, motionless ghosts.
        float spread = 0;
        for (int i = 1; i < centroids.Length; i++)
            spread = Mathf.Max(spread, centroids[i].DistanceTo(centroids[0]));
        GD.Print($"poses differ     {(spread > 0.001f ? "yes" : "NO — every bake is the same pose")}  " +
                 $"(max centroid delta {spread:F4})");
        GD.Print($"fresh instances  {(ids.Distinct().Count() == ids.Length ? "yes, one mesh per call" : "reused unexpectedly")}");

        GD.Print("");
        GD.Print($"reuse — one ArrayMesh, {GhostCount} bakes written into it");
        var pool = new ArrayMesh();
        ulong poolId = pool.GetInstanceId();
        var times = new double[GhostCount];
        bool stable = true;
        for (int i = 0; i < GhostCount; i++)
        {
            await SeekTo(player, (double)i / GhostCount * length);
            var watch = Stopwatch.StartNew();
            var got = mi.BakeMeshFromCurrentSkeletonPose(pool);
            watch.Stop();
            times[i] = watch.Elapsed.TotalMilliseconds;
            if (got.GetInstanceId() != poolId) stable = false;
        }
        var sorted = times.OrderBy(t => t).ToArray();
        GD.Print($"  buffer reused  {(stable ? "yes — same ArrayMesh instance every call" : "NO — a new mesh came back")}");
        GD.Print($"  per bake       min {sorted[0]:F2} ms   median {sorted[sorted.Length / 2]:F2} ms   max {sorted[^1]:F2} ms");
        GD.Print($"  {GhostCount} ghosts       {times.Sum():F2} ms in one swing   (a 60fps frame is 16.67 ms)");
        GD.Print($"  surfaces after {pool.GetSurfaceCount()}  verts {VertexCount(pool)}");

        // Documented, but worth seeing: the ghost needs its own material anyway,
        // which is convenient rather than costly — a trail wants an unshaded
        // family-tinted one, not the character's.
        GD.Print($"  material        {(pool.SurfaceGetMaterial(0) == null ? "not copied (as documented)" : "copied")}");

        GD.Print("");
        GD.Print("cost vs mesh size — one attack clip per character, 5 bakes each");
        GD.Print($"  {"character",-20} {"verts",8} {"bones",6} {"median",9}  {"us/1k verts",12}");
        foreach (string name in Roster)
        {
            var result = await TimeCharacter(name);
            if (result == null) { GD.Print($"  {name,-20} skipped"); continue; }
            var (verts, bones, median) = result.Value;
            GD.Print($"  {name,-20} {verts,8} {bones,6} {median,6:F2} ms  {median * 1000 / (verts / 1000.0),9:F1}");
        }

        GetTree().Quit();
    }

    /// Puts the skeleton in the pose the clip holds at `time`.
    ///
    /// Seek alone moves the AnimationPlayer; the skeleton's bone transforms and
    /// then the GPU skinning both have to catch up before there is a posed mesh
    /// to read back, which is why this costs a frame.
    private async System.Threading.Tasks.Task SeekTo(AnimationPlayer player, double time)
    {
        player.Play(Clip);
        player.Seek(time, update: true);
        player.Pause();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static int VertexCount(Mesh mesh)
    {
        int total = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            if (arrays[(int)Mesh.ArrayType.Vertex].Obj is Vector3[] v) total += v.Length;
        }
        return total;
    }

    /// Average vertex position — a cheap fingerprint of the pose. Two bakes of
    /// the same pose land on the same centroid; a whip mid-crack does not.
    private static Vector3 Centroid(Mesh mesh)
    {
        var sum = Vector3.Zero;
        int n = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            if (arrays[(int)Mesh.ArrayType.Vertex].Obj is not Vector3[] verts) continue;
            foreach (var v in verts) sum += v;
            n += verts.Length;
        }
        return n == 0 ? Vector3.Zero : sum / n;
    }

    private static string Fmt(Vector3 v) => $"({v.X,6:F2},{v.Y,6:F2},{v.Z,6:F2})";

    /// Loads one character, plays its own attack clip, and times five bakes.
    /// The clip comes from ActorAnimations rather than being guessed here —
    /// same table the fight uses, so this measures the real animation.
    private async System.Threading.Tasks.Task<(int Verts, int Bones, double Median)?> TimeCharacter(string name)
    {
        var packed = GD.Load<PackedScene>($"res://assets/characters/{name}.glb");
        if (packed == null) return null;
        var model = packed.Instantiate<Node3D>();
        AddChild(model);

        var mi = FindNode<MeshInstance3D>(model);
        var player = FindNode<AnimationPlayer>(model);
        var skeleton = FindNode<Skeleton3D>(model);
        string? clip = Worklings.Core.Stage.ActorAnimations.For(name)?.Name(Worklings.Core.Stage.ActorAction.Attack);
        if (mi == null || player == null || skeleton == null || clip == null || !player.HasAnimation(clip))
        {
            model.QueueFree();
            return null;
        }

        double length = player.GetAnimation(clip).Length;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var pool = new ArrayMesh();
        var times = new double[SamplePoints.Length];
        for (int i = 0; i < SamplePoints.Length; i++)
        {
            player.Play(clip);
            player.Seek(SamplePoints[i] * length, update: true);
            player.Pause();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var watch = Stopwatch.StartNew();
            mi.BakeMeshFromCurrentSkeletonPose(pool);
            watch.Stop();
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        int verts = VertexCount(mi.Mesh);
        int bones = skeleton.GetBoneCount();
        model.QueueFree();
        var sorted = times.OrderBy(t => t).ToArray();
        return (verts, bones, sorted[sorted.Length / 2]);
    }

    private static int CountOf<T>(Node from) where T : Node
    {
        int n = from is T ? 1 : 0;
        foreach (var child in from.GetChildren()) n += CountOf<T>(child);
        return n;
    }

    private static T? FindNode<T>(Node from) where T : Node
    {
        if (from is T hit) return hit;
        foreach (var child in from.GetChildren())
            if (FindNode<T>(child) is T found) return found;
        return null;
    }
}
