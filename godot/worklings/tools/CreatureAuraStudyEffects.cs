using Godot;
using System;
using System.Collections.Generic;

namespace Worklings.Tools;

/// An offline cache of sampled skinned surfaces across one idle cycle.
/// The study prepares this before capture; it never reads the GPU in its render loop.
public sealed class AuraPoseBank
{
    public readonly List<Vector3[]> Positions = new();
    public readonly List<Vector3[]> Normals = new();
    public double Duration;
    public Aabb Bounds;
    public int Count => Positions[0].Length;
    public Vector3 Point(int index, float time, float offset=0)
    {
        float f=Mathf.PosMod(time,(float)Duration)/(float)Duration*Positions.Count;
        int a=(int)f, b=(a+1)%Positions.Count;
        return Positions[a][index].Lerp(Positions[b][index],f-a)+Normals[a][index]*offset;
    }
    public int Nearest(Vector3 goal)
    {
        int chosen=0;float distance=float.MaxValue;
        for(int i=0;i<Count;i++) {
            float d=Positions[0][i].DistanceSquaredTo(goal);
            if(d<distance) { distance=d;chosen=i; }
        }
        return chosen;
    }
}

/// Three art-direction variants per creature. All nodes live under the model root.
/// This is a presentation experiment; it never changes the mesh, materials or combat logic.
public sealed class CreatureAuraStudyEffects
{
    private readonly Node3D _root = new();
    private readonly Node3D _model;
    private readonly Camera3D _camera;
    private readonly AuraPoseBank _poses;
    private readonly int _creature, _variant;
    private readonly ImmediateMesh _mesh = new();
    private readonly ShaderMaterial _material;
    private readonly OmniLight3D _light;
    private readonly List<int[]> _arcs = new();
    private readonly List<int> _anchors = new();
    private readonly MeshInstance3D[] _dust = new MeshInstance3D[48];
    private readonly StandardMaterial3D[] _dustMaterials = new StandardMaterial3D[48];
    private int _lastBucket=-1;
    private bool _open;
    private readonly float _size;
    private readonly Vector3 _center;

    public CreatureAuraStudyEffects(Node3D model,Camera3D camera,AuraPoseBank poses,int creature,int variant)
    {
        _model=model;_camera=camera;_poses=poses;_creature=creature;_variant=variant;
        _center=poses.Bounds.GetCenter();_size=poses.Bounds.Size.Length();
        model.AddChild(_root);
        _material=new ShaderMaterial {Shader=new Shader {Code="""
            shader_type spatial;
            render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
            void fragment() {
                float edge=pow(max(0.0,1.0-abs(UV.x*2.0-1.0)),1.8);
                ALBEDO=COLOR.rgb*3.5;
                ALPHA=COLOR.a*edge;
            }
            """}};
        _root.AddChild(new MeshInstance3D {Mesh=_mesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        _light=new OmniLight3D {Position=_center,LightColor=creature==0?new Color(.22f,.40f,1):new Color(1,.55f,.17f),
            OmniRange=_size*1.5f,LightEnergy=0};_root.AddChild(_light);
        // Runes use surface anchors on the upper shell in the third variation.
        for(int i=0;i<9;i++) {
            float z=poses.Bounds.Position.Z+poses.Bounds.Size.Z*(.28f+.07f*i);
            float x=_center.X+(i%2==0?-1:1)*poses.Bounds.Size.X*.3f;
            _anchors.Add(poses.Nearest(new Vector3(x,poses.Bounds.End.Y*.85f,z)));
        }
        if(creature==2) {
            var texture=MakeDust();
            for(int i=0;i<_dust.Length;i++) {
                _dustMaterials[i]=new StandardMaterial3D {AlbedoTexture=texture,
                    ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
                    BillboardMode=BaseMaterial3D.BillboardModeEnum.Enabled,
                    BillboardKeepScale=true,DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled};
                _dust[i]=new MeshInstance3D {Mesh=new QuadMesh {Size=Vector2.One,Material=_dustMaterials[i]},
                    CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
                _root.AddChild(_dust[i]);
            }
        }
    }

    public void Draw(float time)
    {
        _mesh.ClearSurfaces();_open=false;
        if(_creature==0) Ram(time);
        else if(_creature==1) Pangolin(time);
        else Snag(time);
        if(_open)_mesh.SurfaceEnd();
    }
    private void Vertex(Vector3 p,Vector2 uv,Color c)
    {
        _mesh.SurfaceSetColor(c);_mesh.SurfaceSetUV(uv);_mesh.SurfaceAddVertex(p);
    }
    private void Ribbon(Vector3[] points,float width,Color color)
    {
        if(!_open) { _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles,_material);_open=true; }
        var cameraLocal=_model.ToLocal(_camera.GlobalPosition);
        var sides=new Vector3[points.Length];
        for(int i=0;i<points.Length;i++) {
            var tangent=points[Math.Min(i+1,points.Length-1)]-points[Math.Max(i-1,0)];
            sides[i]=tangent.Normalized().Cross((cameraLocal-points[i]).Normalized()).Normalized()*width*.5f;
        }
        for(int i=0;i<points.Length-1;i++) {
            var a=points[i]-sides[i];var b=points[i+1]-sides[i+1];
            var c=points[i+1]+sides[i+1];var d=points[i]+sides[i];
            Vertex(a,new Vector2(0,0),color);Vertex(b,new Vector2(0,1),color);Vertex(c,new Vector2(1,1),color);
            Vertex(a,new Vector2(0,0),color);Vertex(c,new Vector2(1,1),color);Vertex(d,new Vector2(1,0),color);
        }
    }
    private void Glow(Vector3[] p,float width,Color color,float brightness=1)
    {
        var halo=color;halo.A=.13f*brightness;Ribbon(p,width*7,halo);
        var mid=color;mid.A=.45f*brightness;Ribbon(p,width*2.5f,mid);
        var core=color.Lerp(Colors.White,.42f);core.A=brightness;Ribbon(p,width,core);
    }
    private void MakeArcs(int bucket)
    {
        _lastBucket=bucket;_arcs.Clear();
        var random=new Random(944+bucket*313+_variant*1901);
        int count=_variant==0?6:_variant==1?24:13;
        var min=_poses.Bounds.Position;var extent=_poses.Bounds.Size;
        for(int a=0;a<count;a++) {
            int start=0;
            for(int tries=0;tries<60;tries++) {
                start=random.Next(_poses.Count);var p=_poses.Positions[0][start];
                float y=(p.Y-min.Y)/extent.Y;
                bool region=_variant==0?y>.70f:y>.16f;
                var normal=_poses.Normals[0][start];
                if(region && normal.Dot((_model.ToLocal(_camera.GlobalPosition)-p).Normalized())>-.15f)break;
            }
            var origin=_poses.Positions[0][start];
            var view=(_model.ToLocal(_camera.GlobalPosition)-_center).Normalized();
            var right=view.Cross(Vector3.Up).Normalized();
            var up=right.Cross(view).Normalized();
            var direction=(right*((float)random.NextDouble()-.5f)+up*((float)random.NextDouble()-.5f)).Normalized();
            float length=_size*(_variant==1?.21f:.13f);
            var indices=new int[10];
            for(int j=0;j<10;j++) {
                float f=j/9f;
                var goal=origin+direction*length*f+Vector3.Up*Mathf.Sin(f*13+a)*_size*.008f;
                float best=float.MaxValue;int selected=start;
                for(int i=0;i<_poses.Count;i++) {
                    if(_poses.Normals[0][i].Dot(view)<-.1f)continue;
                    var difference=_poses.Positions[0][i]-goal;
                    float depth=difference.Dot(view);
                    float score=(difference-view*depth).LengthSquared()+Mathf.Max(0,-depth)*_size*.025f;
                    if(score<best) {best=score;selected=i;}
                }
                indices[j]=selected;
            }
            _arcs.Add(indices);
        }
    }
    private void Ram(float t)
    {
        int bucket=(int)(t*7);
        if(bucket!=_lastBucket)MakeArcs(bucket);
        float pulse=.4f+.6f*Mathf.Abs(Mathf.Sin(t*39));
        var blue=_variant==1?new Color(.40f,.23f,1):new Color(.15f,.53f,1);
        for(int a=0;a<_arcs.Count;a++) {
            if((bucket+a)%4==0)continue;
            var path=new Vector3[10];
            for(int j=0;j<10;j++) {
                path[j]=_poses.Point(_arcs[a][j],t,_size*.010f);
                if(j>0&&j<9)path[j]+=new Vector3(Mathf.Sin(j*4+a+bucket),Mathf.Cos(j*7+a),Mathf.Sin(j*9+bucket))*_size*.004f;
            }
            Glow(path,_size*.0025f,blue,pulse);
        }
        if(_variant==2) {
            // A few airborne arcs bridge the upper silhouette, not a sphere around the pet.
            for(int a=0;a<5;a++) {
                if((a+bucket)%3==0)continue;
                var path=new Vector3[16];float angle=a*Mathf.Tau/5+t*.32f;
                for(int j=0;j<16;j++) {
                    float u=j/15f;float r=_poses.Bounds.Size.X*(.53f+.09f*Mathf.Sin(u*Mathf.Pi));
                    float q=angle+u*.9f;
                    path[j]=new Vector3(_center.X+Mathf.Cos(q)*r,_poses.Bounds.Position.Y+_poses.Bounds.Size.Y*(.63f+.27f*u),
                        _center.Z+Mathf.Sin(q)*r)+new Vector3(Mathf.Sin(j*3+bucket),Mathf.Cos(j*7+a),0)*_size*.009f;
                }
                Glow(path,_size*.0013f,new Color(.33f,.32f,1),pulse*.58f);
            }
        }
        _light.LightEnergy=.10f+pulse*(_variant==0?.09f:.18f);
    }

    private void Glyph(Vector3 at,float size,int shape,Color color,float power,Vector3? normal=null)
    {
        Vector3 right,up;
        if(normal.HasValue) {
            right=normal.Value.Cross(Vector3.Forward).Normalized();up=right.Cross(normal.Value).Normalized();
        } else {
            right=_model.GlobalBasis.Inverse()*_camera.GlobalBasis.X;
            up=_model.GlobalBasis.Inverse()*_camera.GlobalBasis.Y;
            right=right.Normalized();up=up.Normalized();
        }
        void Line(float ax,float ay,float bx,float by) => Glow(new[]{at+(right*ax+up*ay)*size,at+(right*bx+up*by)*size},size*.045f,color,power);
        Line(0,-.6f,0,.6f);
        switch(shape%4) {
            case 0: Line(0,.55f,-.4f,.15f);Line(-.4f,.15f,0,-.2f);Line(0,-.2f,.4f,.15f);Line(.4f,.15f,0,.55f);break;
            case 1: Line(0,.5f,.45f,.1f);Line(.45f,.1f,0,-.15f);Line(0,.03f,-.4f,-.3f);break;
            case 2: Line(-.42f,.38f,.42f,.38f);Line(-.42f,-.3f,.42f,-.3f);Line(-.42f,.38f,-.42f,.05f);break;
            default:Line(-.4f,.3f,0,0);Line(0,0,.4f,.3f);Line(-.4f,-.25f,0,0);Line(0,0,.4f,-.25f);break;
        }
    }
    private void Pangolin(float t)
    {
        var gold=new Color(1,.58f,.13f);var teal=new Color(.13f,.87f,.79f);
        float radius=_poses.Bounds.Size.X*.75f;
        float depth=_poses.Bounds.Size.Z*.38f;
        if(_variant<2) {
            int count=_variant==0?7:12;
            for(int i=0;i<count;i++) {
                float a=i*Mathf.Tau/count+t*(_variant==0?.27f:-.21f);
                var at=_center+new Vector3(Mathf.Cos(a)*radius,_size*(.025f+.025f*Mathf.Sin(t*1.6f+i)),Mathf.Sin(a)*depth);
                if(_variant==1)at.Y+=_size*.10f*Mathf.Sin(a*2+t*.3f);
                Glyph(at,_size*.065f,i,_variant==1&&i%3==0?teal:gold,.45f+.30f*Mathf.Pow(Mathf.Sin(t*1.2f+i),2));
            }
            if(_variant==1) {
                var ring=new Vector3[81];
                for(int i=0;i<81;i++) {float a=i/80f*Mathf.Tau;
                    ring[i]=_center+new Vector3(Mathf.Cos(a)*radius*1.14f,_size*.035f*Mathf.Sin(a*2+t),Mathf.Sin(a)*depth*1.14f);}
                Glow(ring,_size*.0009f,gold,.2f);
            }
        } else {
            for(int i=0;i<_anchors.Count;i++) {
                float power=.25f+.65f*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*1.7f-i*.6f)),3);
                int anchor=_anchors[i];
                Glyph(_poses.Point(anchor,t,_size*.004f),_size*.056f,i,gold,power,_poses.Normals[0][anchor]);
            }
            for(int i=0;i<4;i++) {
                float a=i*Mathf.Tau/4+t*.16f;
                var at=_center+new Vector3(Mathf.Cos(a)*radius*1.05f,_size*.12f,Mathf.Sin(a)*depth*.75f);
                Glyph(at,_size*.050f,i,teal,.28f);
            }
        }
        _light.LightEnergy=.12f;
    }

    private static ImageTexture? _dustTexture;
    private static ImageTexture MakeDust()
    {
        if(_dustTexture!=null)return _dustTexture;
        var image=Image.CreateEmpty(64,64,false,Image.Format.Rgba8);
        var noise=new FastNoiseLite {Seed=183,Frequency=.19f,FractalOctaves=3};
        for(int y=0;y<64;y++)for(int x=0;x<64;x++) {
            float r=new Vector2((x-31.5f)/32,(y-31.5f)/32).Length();
            float n=.65f+noise.GetNoise2D(x,y)*.35f;
            float a=Mathf.Pow(Mathf.Max(0,1-r*r),1.4f)*n;
            image.SetPixel(x,y,new Color(n,n,n,a));
        }
        return _dustTexture=ImageTexture.CreateFromImage(image);
    }
    private void Snag(float t)
    {
        int count=_variant==0?20:_variant==1?32:48;
        for(int i=0;i<_dust.Length;i++) {
            _dust[i].Visible=i<count;if(i>=count)continue;
            float a=i*2.39996f+t*(_variant==1?.11f:.23f);
            float radius=_poses.Bounds.Size.X*(.45f+.035f*(i%7));
            float y=_poses.Bounds.Position.Y+_poses.Bounds.Size.Y*(.20f+.13f*(i%7))
                +Mathf.Sin(t*.9f+i*1.5f)*_size*.03f;
            _dust[i].Position=new Vector3(_center.X+Mathf.Cos(a)*radius,y,_center.Z+Mathf.Sin(a)*radius*.83f);
            float size=_size*(_variant==0?.008f:_variant==1?.013f:.0055f)*(1+.18f*(i%3));
            _dust[i].Scale=Vector3.One*size;
            var c=_variant==2?new Color(.72f,.58f,.32f):new Color(.54f,.43f,.29f);
            c.A=(_variant==1?.65f:.8f)*(.65f+.35f*Mathf.Sin(t*.8f+i));
            _dustMaterials[i].AlbedoColor=c;
        }
    }
    public void Release()=>_root.QueueFree();
}
