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

    /// Which creatures carry an aura, and which of the three authored strengths
    /// each one wears. Nikhil's calls, 2026-09-14: Living Lightning for the Ram,
    /// Breathing Energy for the Pangolin.
    public static (int Kind, int Variant)? Recipe(string creatureId) => creatureId switch
    {
        "tempest_ram" => (0, 1),
        "clockwork_pangolin" => (1, 2),
        _ => null,
    };

    /// Builds the aura for a creature, or null if it has none or the model is
    /// not shaped the way the shader needs (one surface, StandardMaterial3D,
    /// an albedo texture).
    /// `strength` scales the whole effect. It is a per-scene call, not a global
    /// one: the aura is `blend_add`, so how far it reads depends on what it is
    /// added to. The Cache Warren is dark and 1.0 is right there; the desktop is
    /// lit bright over near-white fleece and needs more to say anything at all.
    public static CreatureAura? For(string creatureId, MeshInstance3D? mesh, float strength = 1f)
    {
        if (mesh == null) return null;
        if (Recipe(creatureId) is not var (kind, variant)) return null;
        if (mesh.GetActiveMaterial(0) is not StandardMaterial3D original || original.AlbedoTexture == null)
        {
            GD.PushWarning($"[aura] {creatureId} has no albedo texture; skipped");
            return null;
        }
        return new CreatureAura(mesh, original.AlbedoTexture, kind, variant, strength);
    }

    private CreatureAura(MeshInstance3D mesh, Texture2D albedo, int kind, int variant, float strength)
    {
        _mesh = mesh;
        _previous = mesh.MaterialOverlay;
        _overlay = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _overlay.SetShaderParameter("base_tex", albedo);
        _overlay.SetShaderParameter("kind", (float)kind);
        _overlay.SetShaderParameter("variation", (float)variant);
        _overlay.SetShaderParameter("strength", strength);
        mesh.MaterialOverlay = _overlay;
    }

    /// Advance the effect. Pass seconds since the aura was created; the shader
    /// takes time explicitly rather than reading TIME so a capture tool stepping
    /// frames gets the same picture as a live frame.
    public void Draw(float seconds) => _overlay.SetShaderParameter("phase", seconds);

    /// Puts back whatever overlay the mesh had. Call before the body is freed or
    /// re-worn; never stack two auras on one mesh.
    public void Release() => _mesh.MaterialOverlay = _previous;

    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never;
        uniform sampler2D base_tex : source_color, filter_linear_mipmap;
        uniform float phase = 0.0;
        uniform float kind = 0.0;
        uniform float variation = 0.0;
        uniform float strength = 1.0;
        varying vec3 rest;
        float lum(vec2 uv) {return dot(texture(base_tex,uv).rgb,vec3(0.2126,0.7152,0.0722));}
        void vertex() {rest=VERTEX;}
        void fragment() {
            vec2 px=1.0/vec2(textureSize(base_tex,0));
            float center=lum(UV);
            float surround=max(max(lum(UV+px*vec2(3,0)),lum(UV-px*vec2(3,0))),max(lum(UV+px*vec2(0,3)),lum(UV-px*vec2(0,3))));
            surround=max(surround,max(max(lum(UV+px*vec2(5,5)),lum(UV-px*vec2(5,5))),max(lum(UV+px*vec2(5,-5)),lum(UV-px*vec2(5,-5)))));
            float valley=smoothstep(0.005,0.065,surround-center);
            float dark=1.0-smoothstep(0.002,kind<0.5?0.17:0.030,center);
            float seam=dark*valley;
            float wave=0.5+0.5*sin(rest.y*11.0+rest.z*5.0-phase*1.5);
            // Two incommensurate drifts, so strikes never settle into a beat, and a
            // high exponent so each reads as a discrete strike rather than shimmer.
            float drift=sin(rest.x*9.0-phase*0.61)*1.7+sin(rest.z*6.0+phase*0.43)*1.3;
            float flash=pow(0.5+0.5*sin(rest.y*25.0+rest.x*17.0+sin(rest.z*21.0)*2.0+drift-phase*4.6),9.0);
            float power;
            vec3 blue;
            if(kind<0.5) {
                power=(0.45+variation*0.65)*(0.25+flash*4.2+wave*0.3);
                if(variation>1.5)power*=0.45+1.1*pow(0.5+0.5*sin(phase*3.0),3.0);
                blue=mix(vec3(0.045,0.34,1.0),vec3(0.48,0.82,1.0),flash);
            } else {
                power=variation<0.5?0.65:1.8;
                if(variation>1.5)power*=0.25+0.85*(0.5+0.5*sin(phase*0.85-rest.z*2.2+rest.y*1.4));
                blue=vec3(0.035,0.40,1.0);
            }
            ALBEDO=blue*3.0*power*strength*seam;
            ALPHA=clamp(seam*strength,0.0,1.0);
        }
        """;
}
