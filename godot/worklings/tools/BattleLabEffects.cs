using Godot;
using System;
using System.Collections.Generic;

namespace Worklings.Tools;

/// Experimental, seekable geometry for the battle lab. No gameplay or persistence dependencies.
public sealed class BattleLabEffects
{
    private readonly ImmediateMesh _glow = new();
    private readonly ImmediateMesh _solid = new();
    private readonly Node3D _root = new();
    private readonly Camera3D _camera;
    private readonly ShaderMaterial _lightMaterial;
    private readonly StandardMaterial3D _rockMaterial;
    private readonly OmniLight3D _flash;
    private readonly List<Vector3[]> _bolts = new();
    private readonly List<Vector3[]> _cracks = new();
    private readonly List<Vector3[]> _roots = new();
    private readonly Vector3 _from, _to;
    private readonly int _kind;
    private readonly Color _energy;
    private bool _glowOpen, _solidOpen;
    private readonly MeshInstance3D[] _dust = new MeshInstance3D[18];
    private readonly StandardMaterial3D[] _dustMaterials = new StandardMaterial3D[18];

    public BattleLabEffects(Node3D parent, Camera3D camera, int kind, Vector3 from, Vector3 to)
    {
        _camera = camera; _kind = kind; _from = from; _to = to;
        _energy = kind switch { 0 => new Color(.20f,.48f,1), 1 => new Color(1,.19f,.015f),
            2 => new Color(.18f,1,.68f), _ => new Color(.53f,.78f,.15f) };
        parent.AddChild(_root);
        _lightMaterial = new ShaderMaterial { Shader=new Shader { Code="""
            shader_type spatial;
            render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
            void fragment() {
                float edge = pow(max(0.0, 1.0 - abs(UV.x * 2.0 - 1.0)), 1.4);
                ALBEDO = COLOR.rgb * 4.0;
                ALPHA = COLOR.a * edge;
            }
            """ } };
        _rockMaterial = new StandardMaterial3D { VertexColorUseAsAlbedo = true,
            Roughness = .95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _root.AddChild(new MeshInstance3D { Mesh = _glow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _root.AddChild(new MeshInstance3D { Mesh = _solid });
        _flash = new OmniLight3D { Position = to + Vector3.Up * 2, OmniRange = 16,
            LightColor = _energy, LightEnergy = 0 };
        _root.AddChild(_flash);
        var smoke=SmokeTexture();
        for(int i=0;i<_dust.Length;i++) {
            _dustMaterials[i]=new StandardMaterial3D {AlbedoTexture=smoke,
                Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode=BaseMaterial3D.BillboardModeEnum.Enabled,
                DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled};
            _dust[i]=new MeshInstance3D {Mesh=new QuadMesh {Size=new Vector2(1,1),Material=_dustMaterials[i]},
                Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            _root.AddChild(_dust[i]);
        }
        var rng = new Random(912 + kind);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b-a);
        for (int b=0; b<9; b++)
        {
            var path = new Vector3[19];
            var top = to + new Vector3(Rand(-3,3), 16+Rand(0,5), Rand(-3,3));
            for (int j=0;j<path.Length;j++) {
                float u=j/18f; float spread=Mathf.Sin(u*Mathf.Pi)*.85f;
                path[j]=top.Lerp(to+Vector3.Up*.25f,u)+new Vector3(Rand(-spread,spread),0,Rand(-spread,spread));
            }
            _bolts.Add(path);
        }
        for(int b=0;b<13;b++) {
            float angle=b*Mathf.Tau/13+Rand(-.16f,.16f);
            float length=Rand(3.5f,7);
            var path=new Vector3[12];
            for(int j=0;j<12;j++) {
                float r=length*j/11; float a=angle+Rand(-.14f,.14f);
                path[j]=to+new Vector3(Mathf.Cos(a)*r,.035f,Mathf.Sin(a)*r);
            }
            _cracks.Add(path);
        }
        for(int b=0;b<8;b++) {
            float a=b*Mathf.Tau/8+.2f+Rand(-.18f,.18f); var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            var side=new Vector3(-radial.Z,0,radial.X);
            float reach=Rand(3.8f,5.1f),height=Rand(2.4f,3.8f),twist=Rand(-.9f,.9f);
            var path=new Vector3[22];
            for(int j=0;j<22;j++) {
                float u=j/21f;
                path[j]=to+radial*(reach*(1-u)+.25f)+side*(Mathf.Sin(u*Mathf.Pi)*twist+Mathf.Sin(u*14+b)*.18f*Mathf.Sin(u*Mathf.Pi))
                    +Vector3.Up*(Mathf.Sin(u*Mathf.Pi)*height+.05f+Mathf.Sin(u*18+b)*.12f*Mathf.Sin(u*Mathf.Pi));
            }
            _roots.Add(path);
        }
    }

    public void Draw(double relative)
    {
        float t=(float)relative;
        _glow.ClearSurfaces(); _solid.ClearSurfaces(); _glowOpen=false; _solidOpen=false;
        _flash.LightEnergy=0;
        if(t > -1.1f && t < 3.7f) {
            if(_kind==0) Lightning(t);
            else if(_kind==1) Lava(t);
            else if(_kind==2) Claws(t);
            else Roots(t);
        }
        if(_glowOpen) _glow.SurfaceEnd();
        if(_solidOpen) _solid.SurfaceEnd();
        Dust(t);
    }

    private static ImageTexture? _smoke;
    private static ImageTexture SmokeTexture() {
        if(_smoke!=null) return _smoke;
        var noise=new FastNoiseLite {Seed=42,Frequency=.065f,FractalOctaves=4};
        var img=Image.CreateEmpty(128,128,false,Image.Format.Rgba8);
        for(int y=0;y<128;y++) for(int x=0;x<128;x++) {
            float r=new Vector2((x-63.5f)/64,(y-63.5f)/64).Length();
            float n=noise.GetNoise2D(x,y)*.5f+.5f;
            float a=Mathf.Clamp((1-r)*2.1f,0,1)*Mathf.Clamp(n*1.5f-.25f,0,1);
            img.SetPixel(x,y,new Color(.65f+n*.35f,.65f+n*.35f,.65f+n*.35f,a));
        }
        return _smoke=ImageTexture.CreateFromImage(img);
    }
    private void Dust(float t) {
        for(int i=0;i<_dust.Length;i++) {
            float u=t-i%4*.027f;
            _dust[i].Visible=u>0&&u<2.1f;
            if(!_dust[i].Visible)continue;
            float a=i*2.39996f;float distance=(.8f+i%5*.37f)+u*(1.2f+i%3*.3f);
            _dust[i].Position=_to+new Vector3(Mathf.Cos(a)*distance,.25f+u*.55f,Mathf.Sin(a)*distance);
            _dust[i].Scale=Vector3.One*(.7f+u*1.35f);
            float alpha=Mathf.Sin(Mathf.Min(u*7,Mathf.Pi/2))*Mathf.Pow(1-u/2.1f,2)*(_kind==2?.13f:.32f);
            var c=_kind==1?new Color(.24f,.17f,.12f):_kind==3?new Color(.19f,.22f,.12f):new Color(.18f,.23f,.31f);
            c.A=alpha;_dustMaterials[i].AlbedoColor=c;
        }
    }

    private Color Hot(float power, float white=.15f) {
        var c=_energy.Lerp(Colors.White,white); return new Color(c.R*power,c.G*power,c.B*power,1);
    }
    private void Tri(bool glow, Vector3 a, Vector3 b, Vector3 c, Color color,
                     Vector2? uvA=null,Vector2? uvB=null,Vector2? uvC=null) {
        var mesh=glow?_glow:_solid;
        if(glow && !_glowOpen) { mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles,_lightMaterial); _glowOpen=true; }
        if(!glow && !_solidOpen) { mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles,_rockMaterial); _solidOpen=true; }
        var n=(b-a).Cross(c-a).Normalized();
        mesh.SurfaceSetColor(color); mesh.SurfaceSetNormal(n); mesh.SurfaceSetUV(uvA??new Vector2(.5f,.5f)); mesh.SurfaceAddVertex(a);
        mesh.SurfaceSetColor(color); mesh.SurfaceSetNormal(n); mesh.SurfaceSetUV(uvB??new Vector2(.5f,.5f)); mesh.SurfaceAddVertex(b);
        mesh.SurfaceSetColor(color); mesh.SurfaceSetNormal(n); mesh.SurfaceSetUV(uvC??new Vector2(.5f,.5f)); mesh.SurfaceAddVertex(c);
    }
    private void Ribbon(Vector3[] p, float width, Color color, bool floor=false, float progress=1, bool glow=true) {
        float extent=Mathf.Clamp(progress,0,1)*(p.Length-1);
        for(int j=0;j<p.Length-1 && j<extent;j++) {
            var a=p[j]; var b=p[j].Lerp(p[j+1],Mathf.Min(1,extent-j));
            var tangent=(b-a).Normalized(); if(tangent.LengthSquared()<.001f) continue;
            var normal=floor?Vector3.Up:(_camera.GlobalPosition-(a+b)*.5f).Normalized();
            var side=tangent.Cross(normal).Normalized();
            float taper=Mathf.Lerp(1,.12f,j/(float)(p.Length-1));
            var s=side*width*taper*.5f;
            Tri(glow,a-s,b-s,b+s,color,new Vector2(0,0),new Vector2(0,1),new Vector2(1,1));
            Tri(glow,a-s,b+s,a+s,color,new Vector2(0,0),new Vector2(1,1),new Vector2(1,0));
        }
    }
    private void GlowPath(Vector3[] p,float width,float power,bool floor=false,float progress=1) {
        Ribbon(p,width*9,Hot(power*.10f,0),floor,progress);
        Ribbon(p,width*3,Hot(power*.26f,0),floor,progress);
        Ribbon(p,width,Hot(power,_kind==0?.7f:_kind==1?.06f:.25f),floor,progress);
    }
    private void Ring(Vector3 at,float radius,float width,float power,float phase=0) {
        var p=new Vector3[81];
        for(int j=0;j<p.Length;j++) { float a=j/80f*Mathf.Tau; float r=radius*(1+.025f*Mathf.Sin(a*13+phase));
            p[j]=at+new Vector3(Mathf.Cos(a)*r,.055f,Mathf.Sin(a)*r); }
        Ribbon(p,width,Hot(power),true);
    }
    private void Shard(Vector3 at,Vector3 size,float angle,Color color,bool glow=false) {
        var x=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*size.X;
        var z=new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle))*size.Z;
        var top=at+Vector3.Up*size.Y; var bottom=at-Vector3.Up*size.Y*.6f;
        var p=new[]{at+x,at+z,at-x,at-z};
        for(int i=0;i<4;i++) { Tri(glow,p[i],p[(i+1)%4],top,color); Tri(glow,p[(i+1)%4],p[i],bottom,color*.7f); }
    }
    private void Debris(float t,int count,float speed=1) {
        if(t<0 || t>1.65f) return;
        for(int j=0;j<count;j++) {
            float a=j*2.39996f; float v=(2+j%7*.52f)*speed;
            var velocity=new Vector3(Mathf.Cos(a)*v,3+j%5*.68f,Mathf.Sin(a)*v);
            var at=_to+Vector3.Up*.12f+velocity*t+Vector3.Down*(5.8f*t*t);
            if(at.Y<.03f) continue;
            float s=.09f+(j%4)*.065f;
            Shard(at,new Vector3(s,s*1.5f,s*.8f),a+t*4,new Color(.15f,.17f,.19f));
            if(j%3==0) GlowPath(new[]{at-velocity*.035f,at},.035f,2.5f);
        }
    }
    private void Sparks(float t,int count,float speed=1) {
        if(t<0 || t>1.1f) return;
        for(int j=0;j<count;j++) {
            float a=j*2.39996f; float v=(3+j%9*.55f)*speed;
            var vel=new Vector3(Mathf.Cos(a)*v,1+j%6*.7f,Mathf.Sin(a)*v);
            var at=_to+Vector3.Up*.2f+vel*t+Vector3.Down*(2.5f*t*t);
            if(at.Y<0) continue;
            GlowPath(new[]{at-vel*.026f,at},.023f,Mathf.Pow(1-t/1.1f,2)*4);
        }
    }
    private void Lightning(float t) {
        if(t<0) {
            float p=Mathf.Clamp((t+1)/1,0,1);
            Ring(_to,2.4f-p*1.5f,.018f,p*.5f);
            for(int j=0;j<5;j++) GlowPath(_cracks[j],.015f,p*.8f,true,p*.6f);
            _flash.LightEnergy=p*.7f;
            return;
        }
        float strike=t<.11f?1:t>.19f&&t<.27f?.8f:Mathf.Max(0,1-(t-.27f)/.15f)*.35f;
        if(t<.42f) {
            int jag=(int)(t*30)%3;
            GlowPath(_bolts[jag],.075f,strike*5);
            for(int j=0;j<5;j++) {
                var branch=new Vector3[8];
                var origin=_bolts[jag][5+j*2];
                for(int k=0;k<8;k++) branch[k]=origin+new Vector3((j%2==0?1:-1)*k*.30f,-k*.30f,Mathf.Sin(k*3+j)*.30f);
                GlowPath(branch,.028f,strike*2);
            }
            _flash.LightEnergy=strike*7;
        }
        for(int j=0;j<_cracks.Count;j++) {
            Ribbon(_cracks[j],.10f,new Color(.018f,.022f,.028f),true,1,false);
            GlowPath(_cracks[j],.025f,Mathf.Exp(-t*2)*1.7f,true,Mathf.Min(1,t*11));
        }
        if(t<.4f) Ring(_to,.4f+t*13,.045f,(1-t/.4f)*1.5f);
        Debris(t,28); Sparks(t,48);
    }
    private void Lava(float t) {
        float growth=t<0?Mathf.Clamp((t+.7f)/.7f,0,1)*.35f:Mathf.Min(1,.35f+t*2.4f);
        float heat=t<0?.4f:Mathf.Exp(-Mathf.Max(0,t-.6f)*1.5f)*3;
        for(int j=0;j<_cracks.Count;j++) {
            Ribbon(_cracks[j],.34f,new Color(.022f,.015f,.012f),true,growth,false);
            GlowPath(_cracks[j],.20f,heat,true,growth);
        }
        if(t<0) { _flash.LightEnergy=growth*2; return; }
        _flash.LightEnergy=Mathf.Exp(-t*4)*7;
        for(int j=0;j<32;j++) {
            float delay=(j%8)*.042f; float u=t-delay; if(u<0) continue;
            float a=j*2.39996f; float r=1.4f+(j%8)*.58f;
            float lift=Mathf.Max(0,Mathf.Sin(Mathf.Min(u*3,Mathf.Pi)))*(1+j%3*.30f);
            var at=_to+new Vector3(Mathf.Cos(a)*r,lift-.10f,Mathf.Sin(a)*r);
            Shard(at,new Vector3(.35f,.35f+j%3*.18f,.38f),a,new Color(.10f,.075f,.06f));
            if(u<.65f) {
                var jet=new Vector3[9]; for(int k=0;k<9;k++) { float f=k/8f;
                    jet[k]=at+Vector3.Up*f*(1.1f+j%3*.35f)*(1-u/.65f)+new Vector3(Mathf.Sin(f*3+j)*.10f*f,0,0); }
                GlowPath(jet,.32f,Mathf.Max(0,1-u/.65f)*1.8f);
            }
        }
        Debris(t,50,1.2f); Sparks(t,65,1.2f);
        if(t<.55f) Ring(_to,.8f+t*13,.07f,Mathf.Pow(1-t/.55f,2)*2);
    }
    private void Claws(float t) {
        for(int swipe=0;swipe<3;swipe++) {
            float u=t+.24f-swipe*.085f;
            if(u<0 || u>.60f) continue;
            float travel=Mathf.Clamp(u/.24f,0,1);
            var center=(_from+Vector3.Up*1.7f).Lerp(_to+Vector3.Up*1.8f,travel);
            var axis=(_to-_from).Normalized(); var side=axis.Cross(Vector3.Up).Normalized();
            float power=u<.24f?2.8f:Mathf.Max(0,1-(u-.24f)/.36f)*3;
            for(int claw=0;claw<3;claw++) {
                var path=new Vector3[22];
                for(int k=0;k<22;k++) {
                    float f=k/21f; float a=Mathf.Lerp(-1.15f,1.1f,f);
                    path[k]=center+side*(Mathf.Sin(a)*1.8f+(claw-1)*.43f)
                        +Vector3.Up*(Mathf.Cos(a)*1.7f-.8f)+axis*(Mathf.Sin(a)*.65f+(claw-1)*.22f);
                }
                GlowPath(path,.11f,power,false,Mathf.Min(1,u*12));
                var tail=new[]{center-axis*(.5f+travel*3)+side*(claw-1)*.4f,center+side*(claw-1)*.4f};
                GlowPath(tail,.10f,power*.035f);
            }
        }
        if(t>=0) {
            _flash.LightEnergy=Mathf.Exp(-t*6)*4;
            Sparks(t,27,.7f);
            for(int j=0;j<3;j++) {
                var axis=(_to-_from).Normalized(); var side=axis.Cross(Vector3.Up);
                var p=new[]{_to-axis*1.8f+side*(j-1)*.6f+Vector3.Up*.05f,
                    _to+side*(j-1)*.5f+Vector3.Up*.06f,_to+axis*1.4f+side*(j-1)*.4f+Vector3.Up*.05f};
                Ribbon(p,.16f,new Color(.015f,.025f,.022f),true,1,false);
                GlowPath(p,.045f,Mathf.Exp(-t*1.6f),true);
            }
        }
    }
    private void Tube(Vector3[] path,float growth,float retract,int index) {
        float extent=Mathf.Clamp(growth,0,1)*(path.Length-1);
        for(int j=0;j<path.Length-1 && j<extent;j++) {
            var a=path[j]; var b=a.Lerp(path[j+1],Mathf.Min(1,extent-j));
            a.Y-=retract; b.Y-=retract;
            var forward=(b-a).Normalized(); var side=forward.Cross(Vector3.Forward).Normalized();
            var up=forward.Cross(side).Normalized();
            float radius=.32f*Mathf.Pow(1-j/(float)path.Length,.9f)*(1+.12f*Mathf.Sin(j*2+index));
            for(int k=0;k<7;k++) {
                float x=k/7f*Mathf.Tau, y=(k+1)/7f*Mathf.Tau;
                var s=side*Mathf.Cos(x)+up*Mathf.Sin(x); var e=side*Mathf.Cos(y)+up*Mathf.Sin(y);
                var c=new Color(.09f+k%3*.020f,.061f+k%3*.016f,.028f+k%3*.008f);
                Tri(false,a+s*radius,b+s*radius*.93f,b+e*radius*.93f,c);
                Tri(false,a+s*radius,b+e*radius*.93f,a+e*radius,c);
            }
            if(j%4==2) {
                var tip=a+up*.7f+side*(index%2==0?.4f:-.4f);
                Tri(false,a+side*.18f,a-side*.18f,tip,new Color(.22f,.18f,.075f));
                Tri(false,a-side*.18f,a-forward*.20f,tip,new Color(.14f,.11f,.045f));
            }
            if(j%5==0) GlowPath(new[]{a+up*radius,b+up*radius},.02f,.22f);
        }
    }
    private void Roots(float t) {
        for(int j=0;j<_roots.Count;j++) {
            float progress=Mathf.Clamp((t+.6f-j*.028f)/.57f,0,1);
            float retract=Mathf.Max(0,t-1.2f)*2.9f;
            if(retract<4.2f) Tube(_roots[j],progress,retract,j);
        }
        if(t>=0) {
            _flash.LightEnergy=Mathf.Exp(-t*5)*2.5f;
            Debris(t,38,.8f);
            for(int j=0;j<24;j++) {
                float a=j*2.4f; float u=t; if(u>1.5f) continue;
                var p=_to+new Vector3(Mathf.Cos(a)*(1+u*2),.5f+u*3-u*u*2,Mathf.Sin(a)*(1+u*2));
                if(p.Y>0) Shard(p,new Vector3(.12f,.025f,.24f),a+u*3,new Color(.29f,.36f,.08f));
            }
        }
    }
    public void Release() => _root.QueueFree();
}
