using Godot;
using System;
using System.Collections.Generic;
using Worklings.Core.Stage;
using Worklings.Tools;

/// Three synchronized portrait viewports, one native animated comparison per creature.
/// Separate entry point: it imports no dungeon controllers and reads no save state.
public partial class AuraStudy : Node
{
    private const int Fps=30, Frames=240;
    private readonly string[] _models={"tempest_ram","clockwork_pangolin","snag"};
    private readonly string[] _titles={"TEMPEST RAM / LIVING ELECTRICITY","CLOCKWORK PANGOLIN / RUNIC PRESENCE","SNAG / DRIFTING EARTH"};
    private readonly string[][] _variants={
        new[]{"01 · HORN STATIC","02 · STORM VEINS","03 · STORM MANTLE"},
        new[]{"01 · ORBITING INSCRIPTIONS","02 · RUNIC ORRERY","03 · SHELL SIGILS"},
        new[]{"01 · EARTH MOTES","02 · DUST PUFFS","03 · GOLDEN SPORES"}};
    private readonly string[][] _descriptions={
        new[]{"Short blue arcs, concentrated near the horns","Violet current flickering across the body","Surface sparks with a few airborne filaments"},
        new[]{"A slow procession of small amber runes","Layered gold and teal symbols, a fine orbit","Pulsing shell marks with a few floating glyphs"},
        new[]{"Twenty small, quiet flecks of drifting earth","Slightly softer, dustier floating particles","More numerous, finer warm specks"}};
    private Control _layout=null!;
    private readonly bool _internal=OS.GetEnvironment("WORKLINGS_AURA_INTERNAL")=="1";

    public override async void _Ready()
    {
        try {
            GetWindow().Size=new Vector2I(1600,900);
            GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
            string output=OS.GetEnvironment("WORKLINGS_AURA_OUT");
            if(output.Length==0)output=ProjectSettings.GlobalizePath("res://../../build/model-auras/frames");
            string select=OS.GetEnvironment("WORKLINGS_AURA_SELECT");
            for(int creature=0;creature<3;creature++) {
                if(_internal && creature==2)continue;
                if(select.Length>0 && select!=_models[creature])continue;
                await Capture(creature,output);
            }
            GD.Print("AURA STUDIES COMPLETE: "+output);GetTree().Quit();
        } catch(Exception ex) {GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
    private void Text(string value,Vector2 at,int size,Color color)
    {
        var label=new Label {Text=value,Position=at};label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color);_layout.AddChild(label);
    }
    private async System.Threading.Tasks.Task Capture(int creature,string output)
    {
        _layout=new Control();AddChild(_layout);
        _layout.AddChild(new ColorRect {Size=new Vector2(1600,900),Color=new Color(.026f,.037f,.057f)});
        Text(_titles[creature],new Vector2(24,21),30,Colors.White);
        Text("AMBIENT MODEL EFFECTS  /  THREE LIVE VARIANTS  /  SAME MODEL, LIGHTING AND CAMERA",new Vector2(25,61),14,new Color(.58f,.68f,.81f));
        var actors=new List<StageActor>();var effects=new List<CreatureAuraStudyEffects>();
        var internalEffects=new List<CreatureAura>();
        string[] internalNames=creature==0?new[]{"01 · RESTING CURRENT","02 · LIVING LIGHTNING","03 · SURGING STORM"}:new[]{"01 · RUNIC EMBERS","02 · AWAKENED CORE","03 · BREATHING ENERGY"};
        string[] internalDescriptions=creature==0?new[]{"Blue current caught in the fur's creases","Bright branching current through fur and horns","Waves of white-blue energy across the body"}:new[]{"A quiet blue glow from the shell's crevices","Luminous blue seams beneath golden plates","A slow pulse of energy beneath the armor"};
        AuraPoseBank? bank=null;
        for(int variation=0;variation<3;variation++) {
            int x=22+variation*526;
            Text(_internal?internalNames[variation]:_variants[creature][variation],new Vector2(x,105),19,new Color(.85f,.90f,1));
            Text(_internal?internalDescriptions[variation]:_descriptions[creature][variation],new Vector2(x,823),15,new Color(.62f,.71f,.83f));
            var container=new SubViewportContainer {Position=new Vector2(x,143),Size=new Vector2(510,655),Stretch=true};
            _layout.AddChild(container);
            var viewport=new SubViewport {Size=new Vector2I(510,655),OwnWorld3D=true,
                RenderTargetUpdateMode=SubViewport.UpdateMode.Always,Msaa3D=Viewport.Msaa.Msaa4X};
            container.AddChild(viewport);
            var world=new Node3D();viewport.AddChild(world);
            world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {
                BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color(.065f,.085f,.12f),
                AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color(.60f,.68f,.80f),AmbientLightEnergy=.60f,
                TonemapMode=Godot.Environment.ToneMapper.Filmic,GlowEnabled=true,GlowIntensity=.85f,
                GlowStrength=1.1f,GlowBloom=.025f,GlowHdrThreshold=1.1f,
                SsaoEnabled=true,SsaoRadius=.15f,SsaoIntensity=1.4f}});
            var root=GD.Load<PackedScene>($"res://assets/characters/{_models[creature]}.glb").Instantiate<Node3D>();world.AddChild(root);
            var actor=new StageActor(root,_models[creature],ActorAnimations.For(_models[creature])!);
            if(actor.Player==null||actor.Mesh==null)throw new Exception("Missing model animation/mesh: "+_models[creature]);
            actor.Player.CallbackModeProcess=AnimationMixer.AnimationCallbackModeProcess.Manual;
            actor.Play(ActorAction.Idle,true);actor.Player.Advance(0);
            var camera=new Camera3D {Current=true,Fov=39,Near=.01f,Far=100,Position=new Vector3(3,3,3)};world.AddChild(camera);camera.LookAt(Vector3.Zero);
            if(bank==null)bank=await Bake(actor);
            var bounds=bank.Bounds;var center=bounds.GetCenter();float span=bounds.Size.Length();
            var direction=(creature==1?new Vector3(2.1f,1.6f,3):new Vector3(1.4f,.9f,3)).Normalized();
            camera.Position=center+direction*10;camera.LookAt(center);
            float halfWidth=0,halfHeight=0,halfDepth=0;
            for(int k=0;k<8;k++) {
                var d=bounds.GetEndpoint(k)-center;
                halfWidth=Mathf.Max(halfWidth,Mathf.Abs(d.Dot(camera.GlobalBasis.X)));
                halfHeight=Mathf.Max(halfHeight,Mathf.Abs(d.Dot(camera.GlobalBasis.Y)));
                halfDepth=Mathf.Max(halfDepth,Mathf.Abs(d.Dot(direction)));
            }
            float tan=Mathf.Tan(Mathf.DegToRad(camera.Fov*.5f));
            float distance=Mathf.Max(halfHeight/tan,halfWidth/(tan*510/655f))*(creature==0?1.03f:1.16f)+halfDepth*.45f;
            if(_internal && creature==1)distance*=.80f;
            camera.Position=center+direction*distance;camera.LookAt(center);
            world.AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-38,-25,0),LightColor=new Color(.92f,.95f,1),LightEnergy=1.6f,ShadowEnabled=true});
            world.AddChild(new OmniLight3D {Position=center+new Vector3(-span,span*.5f,-span*.4f),LightColor=new Color(.33f,.52f,1),LightEnergy=1.2f,OmniRange=span*3});
            var floorMat=new StandardMaterial3D {AlbedoColor=new Color(.065f,.083f,.11f),Roughness=.85f};
            world.AddChild(new MeshInstance3D {Mesh=new PlaneMesh {Size=Vector2.One*span*20},
                Position=new Vector3(0,bounds.Position.Y-.015f,0),MaterialOverride=floorMat});
            actors.Add(actor);
            // The study wears the shipped `CreatureAura`, not a copy of it: the
            // Ram's off-body arcs now ride the skinned mesh, so what renders here
            // is what the game renders.
            if(_internal)
                internalEffects.Add(CreatureAura.Wear(actor.Mesh,creature,variation,
                    CreatureAura.Recipe(_models[creature])?.Gain ?? 1f)
                    ?? throw new Exception("No aura for "+_models[creature]));
            else effects.Add(new CreatureAuraStudyEffects(root,camera,bank,creature,variation));
        }
        Text("WORKLINGS  /  AURA STUDIES",new Vector2(25,870),13,new Color(.42f,.52f,.66f));
        string directory=output+"/"+_models[creature];DirAccess.MakeDirRecursiveAbsolute(directory);
        for(int frame=0;frame<Frames;frame++) {
            float t=frame/(float)Fps;
            for(int i=0;i<actors.Count;i++) {
                actors[i].Player!.Seek(t%(float)bank!.Duration,true);
                if(_internal)internalEffects[i].Draw(t);
                if(i<effects.Count)effects[i].Draw(t);
            }
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Error result=GetViewport().GetTexture().GetImage().SavePng($"{directory}/{frame:D4}.png");
            if(result!=Error.Ok)throw new Exception("Could not save frame: "+result);
        }
        GD.Print($"AURA {_models[creature]}: {Frames} frames, 3 variants; bounds {bank!.Bounds}");
        foreach(var effect in effects)effect.Release();
        foreach(var effect in internalEffects)effect.Release();
        _layout.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
    private async System.Threading.Tasks.Task<AuraPoseBank> Bake(StageActor actor)
    {
        var bank=new AuraPoseBank();var player=actor.Player!;var mesh=actor.Mesh!;
        string clip=actor.Animations.Name(ActorAction.Idle)!;
        bank.Duration=player.GetAnimation(clip).Length;
        if(bank.Duration<=0)throw new Exception("Empty idle animation");
        for(int frame=0;frame<24;frame++) {
            player.Seek(bank.Duration*frame/24,true);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var baked=mesh.BakeMeshFromCurrentSkeletonPose();
            var arrays=baked.SurfaceGetArrays(0);
            if(arrays[(int)Godot.Mesh.ArrayType.Vertex].Obj is not Vector3[] vertices || vertices.Length==0)
                throw new Exception("Baked mesh has no surface vertices");
            if(arrays[(int)Godot.Mesh.ArrayType.Normal].Obj is not Vector3[] normals || normals.Length!=vertices.Length)
                throw new Exception("Baked mesh normals do not match its vertices");
            int stride=Math.Max(1,vertices.Length/4000);
            var transform=actor.Root.GlobalTransform.AffineInverse()*mesh.GlobalTransform;
            var normalBasis=transform.Basis.Inverse().Transposed();
            var points=new List<Vector3>();var directions=new List<Vector3>();
            for(int i=0;i<vertices.Length;i+=stride) {
                var p=transform*vertices[i];points.Add(p);directions.Add((normalBasis*normals[i]).Normalized());
                bank.Bounds=frame==0&&i==0?new Aabb(p,Vector3.Zero):bank.Bounds.Expand(p);
            }
            bank.Positions.Add(points.ToArray());bank.Normals.Add(directions.ToArray());
        }
        player.Seek(0,true);
        GD.Print($"Surface cache {actor.ModelName}: {bank.Count} samples x 24 poses, {bank.Duration:F2}s idle");
        return bank;
    }
}
