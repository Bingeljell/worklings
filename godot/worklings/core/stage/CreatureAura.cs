using System;
using Godot;

namespace Worklings.Core.Stage;

/// A creature's persistent idle identity: energy living *in* the body, distinct
/// from the attack effects in `AbilityVfx`.
///
/// **This is a material overlay on the skinned mesh, and that is the whole
/// design.** The glow is computed per-fragment from the model's own albedo, so
/// it follows every animation for free — no pose cache, no bone anchors, nothing
/// to re-bake when a clip is re-exported. An aura authored once is correct in
/// the idle, the walk, the attack and the death.
///
/// Promoted from `tools/CreatureAuraStudyEffects.cs`, where it was proven as a
/// study. Two things changed on the way in: it keys off the creature **id**
/// rather than an int, matching `AbilitySignature.For`, so adding a creature
/// stays one roster entry; and an unknown creature returns null rather than
/// throwing, because a body with no aura is the normal case.
///
/// **Everything spatial is in body units, not model units.** `body_origin` and
/// `body_size` normalise the mesh's own bounds to 0..1, and `flow_axis` points
/// down its longest axis. The first pass wrote wavelengths in model units, which
/// on a 1.5-unit body came out longer than the creature — so the whole animal
/// brightened and dimmed at once instead of energy travelling over it, which is
/// invisible on a small desktop window and easy to miss at dungeon distance.
///
/// **The crevice mask is an approximation.** It derives seams from local
/// darkness in the albedo, which is not the same as an authored emission mask —
/// it can light dark facial details or metal as though they were fur gaps. It
/// reads well at gameplay distance and it is the known debt to pay down when a
/// creature needs its aura to be exact.
public sealed class CreatureAura
{
    private readonly MeshInstance3D _mesh;
    private readonly Material? _previous;
    private readonly ShaderMaterial _overlay;
    /// The Ram's arcs, or null for a creature whose energy stays on the skin.
    private readonly MeshInstance3D? _shell;
    private readonly ShaderMaterial? _shellMaterial;

    /// How far arcs bow off the body, as a fraction of its height.
    private const float ArcLift = 0.055f;

    /// Which creatures carry an aura, which of the three authored strengths each
    /// one wears, and how hard it is driven. Nikhil's calls, 2026-09-14: Living
    /// Lightning for the Ram, Breathing Energy for the Pangolin.
    ///
    /// **`Gain` is the brightness knob, and it is per creature because the thing
    /// it compensates for is per creature.** The Ram's current sits in white
    /// fleece and needs no help; the Pangolin's runes sit in narrow seams between
    /// dark plates, which is a fraction of the silhouette and the first thing to
    /// disappear when the model is small or the camera is back. It multiplies
    /// with the per-scene `strength`, so the desktop's push stacks on top of it.
    public static (int Kind, int Variant, float Gain)? Recipe(string creatureId) => creatureId switch
    {
        "tempest_ram" => (0, 1, 1.0f),
        "clockwork_pangolin" => (1, 2, 1.8f),
        _ => null,
    };

    /// Builds the aura for a creature, or null if it has none or the model is
    /// not shaped the way the shader needs (one surface, StandardMaterial3D,
    /// an albedo texture).
    ///
    /// `strength` scales the whole effect. It is a per-scene call, not a global
    /// one: the aura is `blend_add`, so how far it reads depends on what it is
    /// added to. The Cache Warren is dark and 1.0 is right there; the desktop is
    /// lit bright over near-white fleece and needs more to say anything at all.
    public static CreatureAura? For(string creatureId, MeshInstance3D? mesh, float strength = 1f)
    {
        if (Recipe(creatureId) is not var (kind, variant, gain)) return null;
        return Wear(mesh, kind, variant, strength * gain, creatureId);
    }

    /// The same effect addressed by kind and variant rather than by creature —
    /// how `AuraStudy` renders the variants side by side. The study and the game
    /// run the identical shader; there is no second copy to keep in step.
    public static CreatureAura? Wear(MeshInstance3D? mesh, int kind, int variant,
                                     float strength = 1f, string label = "aura")
    {
        if (mesh == null) return null;
        if (mesh.GetActiveMaterial(0) is not StandardMaterial3D original || original.AlbedoTexture == null)
        {
            GD.PushWarning($"[aura] {label} has no albedo texture; skipped");
            return null;
        }
        return new CreatureAura(mesh, original.AlbedoTexture, kind, variant, strength);
    }

    private CreatureAura(MeshInstance3D mesh, Texture2D albedo, int kind, int variant, float strength)
    {
        _mesh = mesh;
        var bounds = mesh.GetAabb();
        var size = bounds.Size;
        if (size.X < 0.0001f || size.Y < 0.0001f || size.Z < 0.0001f) size = Vector3.One;
        var flow = size.X >= size.Y && size.X >= size.Z ? Vector3.Right
                 : size.Y >= size.Z ? Vector3.Up : Vector3.Back;

        _previous = mesh.MaterialOverlay;
        _overlay = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _overlay.SetShaderParameter("base_tex", albedo);
        _overlay.SetShaderParameter("kind", (float)kind);
        _overlay.SetShaderParameter("variation", (float)variant);
        Frame(_overlay, bounds.Position, size, flow, strength);
        mesh.MaterialOverlay = _overlay;

        // Lightning is the one energy that does not stay on the skin. The arcs
        // ride a second instance of the *same* skinned mesh, sharing its skin and
        // skeleton, so they deform with the body exactly as the overlay does —
        // still no pose cache. The shader pushes that copy out along its normals
        // where an arc is passing, which is what makes a filament leave the fur,
        // cross the air and land again.
        if (kind >= 0.5f) return;
        var parent = mesh.GetParent();
        if (parent == null || mesh.Mesh == null) return;
        _shellMaterial = new ShaderMaterial { Shader = new Shader { Code = ArcShaderCode } };
        Frame(_shellMaterial, bounds.Position, size, flow, strength);
        _shellMaterial.SetShaderParameter("lift", ArcLift);
        _shell = new MeshInstance3D
        {
            Name = "AuraArcs",
            Mesh = mesh.Mesh,
            Skin = mesh.Skin,
            Transform = mesh.Transform,
            MaterialOverride = _shellMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The shell renders wider than the mesh it copies, so it needs the
            // slack or it pops out at the edge of frame.
            ExtraCullMargin = ArcLift * size.Y * 2f,
        };
        parent.AddChild(_shell);
        var skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        if (skeleton != null) _shell.Skeleton = _shell.GetPathTo(skeleton);
    }

    private static void Frame(ShaderMaterial material, Vector3 origin, Vector3 size, Vector3 flow, float strength)
    {
        material.SetShaderParameter("body_origin", origin);
        material.SetShaderParameter("body_size", size);
        material.SetShaderParameter("flow_axis", flow);
        material.SetShaderParameter("strength", strength);
    }

    /// Advance the effect. Pass seconds since the aura was created; the shader
    /// takes time explicitly rather than reading TIME so a capture tool stepping
    /// frames gets the same picture as a live frame.
    public void Draw(float seconds)
    {
        _overlay.SetShaderParameter("phase", seconds);
        _shellMaterial?.SetShaderParameter("phase", seconds);
    }

    /// Puts back whatever overlay the mesh had and takes the arcs out of the
    /// tree. Call before the body is freed or re-worn; never stack two auras on
    /// one mesh.
    public void Release()
    {
        _mesh.MaterialOverlay = _previous;
        _shell?.QueueFree();
    }

    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never;
        uniform sampler2D base_tex : source_color, filter_linear_mipmap;
        uniform float phase = 0.0;
        uniform float strength = 1.0;
        uniform vec3 body_origin = vec3(0.0);
        uniform vec3 body_size = vec3(1.0);
        uniform vec3 flow_axis = vec3(0.0, 1.0, 0.0);
        uniform float kind = 0.0;
        uniform float variation = 0.0;
        varying vec3 raw;
        varying vec3 body;
        varying float along;
        float lum(vec2 uv) {return dot(texture(base_tex,uv).rgb,vec3(0.2126,0.7152,0.0722));}
        void vertex() {
            raw=VERTEX;
            body=(VERTEX-body_origin)/body_size;
            along=dot(body,flow_axis);
        }
        void fragment() {
            vec2 px=1.0/vec2(textureSize(base_tex,0));
            float center=lum(UV);
            float surround=max(max(lum(UV+px*vec2(3,0)),lum(UV-px*vec2(3,0))),max(lum(UV+px*vec2(0,3)),lum(UV-px*vec2(0,3))));
            surround=max(surround,max(max(lum(UV+px*vec2(5,5)),lum(UV-px*vec2(5,5))),max(lum(UV+px*vec2(5,-5)),lum(UV-px*vec2(5,-5)))));
            float valley=smoothstep(0.005,0.065,surround-center);
            float dark=1.0-smoothstep(0.002,kind<0.5?0.17:0.030,center);
            float seam=dark*valley;
            float power;
            vec3 blue;
            if(kind<0.5) {
                // Untouched. The current in the fleece was already right; what the
                // Ram was missing is off the body, and that is the arc shell's job.
                float wave=0.5+0.5*sin(raw.y*11.0+raw.z*5.0-phase*1.5);
                // Two incommensurate drifts, so strikes never settle into a beat, and a
                // high exponent so each reads as a discrete strike rather than shimmer.
                float drift=sin(raw.x*9.0-phase*0.61)*1.7+sin(raw.z*6.0+phase*0.43)*1.3;
                float flash=pow(0.5+0.5*sin(raw.y*25.0+raw.x*17.0+sin(raw.z*21.0)*2.0+drift-phase*4.6),9.0);
                power=(0.45+variation*0.65)*(0.25+flash*4.2+wave*0.3);
                if(variation>1.5)power*=0.45+1.1*pow(0.5+0.5*sin(phase*3.0),3.0);
                blue=mix(vec3(0.045,0.34,1.0),vec3(0.48,0.82,1.0),flash);
            } else {
                // Three bands travelling the body at incommensurate speeds and
                // wavelengths. One band is a stripe sliding tail to snout on a loop
                // you can time; three interfering ones light a patch, kill it, and
                // re-light somewhere else. The high exponent is what leaves most of
                // the shell dark at any moment — a glow everywhere at once is what
                // made this read as a blue creature rather than energy going
                // somewhere, and it is what vanishes when the model is small.
                float s1=sin((along*1.9-phase*0.42)*6.2831853);
                float s2=sin((along*1.31+body.y*0.8-phase*0.27)*6.2831853+1.7);
                float s3=sin((along*3.7-body.x*1.3-phase*0.61)*6.2831853+4.1);
                float crest=pow(0.5+0.5*(s1*0.55+s2*0.3+s3*0.25)/1.1,3.0+variation*1.5);
                float ripple=0.5+0.5*sin(body.y*8.0+body.x*5.0-phase*1.9);
                power=(0.9+variation*0.55)*(0.03+2.0*crest*(0.72+0.38*ripple));
                blue=mix(vec3(0.035,0.40,1.0),vec3(0.20,0.58,1.0),crest);
            }
            ALBEDO=blue*3.0*power*strength*seam;
            ALPHA=clamp(seam*strength,0.0,1.0);
        }
        """;

    /// The Ram's off-body arcs, on a displaced copy of the skinned mesh.
    ///
    /// The bow outward is per-vertex and the filament is per-fragment, and they
    /// are deliberately different shapes: a broad travelling packet lifts the
    /// shell — smooth enough that 25k vertices carry it without faceting — and
    /// the thin arc is drawn inside that packet, where the surface is already
    /// standing off the fur.
    private const string ArcShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
        uniform float phase = 0.0;
        uniform float strength = 1.0;
        uniform float lift = 0.085;
        uniform vec3 body_origin = vec3(0.0);
        uniform vec3 body_size = vec3(1.0);
        uniform vec3 flow_axis = vec3(0.0, 1.0, 0.0);
        varying vec3 body;
        varying float along;
        float hash(float n) {return fract(sin(n*127.1+31.7)*43758.5453);}
        // Where on the body an arc is allowed to be, right now.
        //
        // `skew` is the load-bearing term: with the lane read off the long axis
        // alone, every point at the same distance down the body arcs at the same
        // instant, which is why the first pass fired all four legs together and
        // ringed both horns identically. Folding x and z in breaks that mirror.
        float skew(vec3 b, float a) {return a*1.0+b.x*0.85+b.z*0.35;}
        float packet(vec3 b, float a, float t) {
            float s=skew(b,a);
            float p1=pow(0.5+0.5*sin((s*1.4-t*0.75)*6.2831853),4.0);
            float p2=pow(0.5+0.5*sin((s*0.83+b.z*1.1-t*0.47)*6.2831853+2.3),3.0);
            return clamp(p1*0.75+p2*0.6,0.0,1.0);
        }
        void vertex() {
            body=(VERTEX-body_origin)/body_size;
            along=dot(body,flow_axis);
            VERTEX+=NORMAL*lift*body_size.y*packet(body,along,phase);
        }
        void fragment() {
            // Bands wrapped around the body and warped, so a filament reads as a
            // discharge path rather than a stripe. Four octaves, because one is a
            // contour line drawn round the animal: the coarse pair moves the arc,
            // the fine pair is the kink in it.
            float warp=sin(body.x*7.0+phase*2.3)*0.16+sin(body.z*5.0-phase*1.7)*0.13
                      +sin(body.x*27.0+body.z*19.0-phase*1.1)*0.045
                      +sin(body.z*53.0-body.x*37.0+phase*0.9)*0.022;
            float lane=skew(body,along)*5.0+warp-phase*0.55;
            float thin=smoothstep(0.05,0.004,abs(fract(lane)-0.5));
            // Each lane keeps its own clock, rate and duration, seeded off its
            // index, so arcs strike raggedly instead of the whole animal blinking.
            float seed=hash(floor(lane));
            float strike=pow(0.5+0.5*sin(phase*(3.4+seed*5.2)+seed*37.0),3.0+seed*5.0);
            // A lane is a closed loop around the body, and a whole lit loop is a
            // hoop, not a spark — most visible on the horns, where the shell is
            // wide compared to what it wraps and the ring floats clear of the
            // model. Gated by the angle around the body so only a short segment
            // of any loop carries current, and it travels.
            float around=atan(body.z-0.5,body.x-0.5);
            float segment=pow(0.5+0.5*sin(around*2.0+phase*2.1+seed*23.0),5.0);
            float arc=thin*strike*segment*packet(body,along,phase);
            if(arc<0.004) discard;
            ALBEDO=mix(vec3(0.10,0.45,1.0),vec3(0.78,0.93,1.0),arc)*4.5*arc*strength;
            ALPHA=clamp(arc*strength,0.0,1.0);
        }
        """;
}
