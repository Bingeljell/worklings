using Godot;
using System;
using Worklings.Core.Stage;
using Worklings.Tools;

/// Standalone art-direction test. Frame stepping also drives skeletons manually,
/// so the exported contact frame and hit-stop match the procedural effects.
public partial class BattleLab : Node3D
{
    private const int Fps = 60;
    private const double Step = 1.0/Fps;
    private readonly string[] _models = { "tempest_ram", "clockwork_pangolin", "forest_flicker", "snag" };
    private readonly string[] _names = { "TEMPEST RAM", "CLOCKWORK PANGOLIN", "FOREST FLICKER", "SNAG" };
    private readonly string[] _moves = { "THUNDERFALL", "FAULTLINE", "PHANTOM RAKE", "BRIAR PRISON" };
    private Node3D _set = null!;
    private Camera3D _camera = null!;
    private Label _title = null!, _subtitle = null!, _phase = null!;
    private ColorRect _flash = null!;
    private Vector3 _cameraHome;
    private readonly Vector3 _left = new(-3.6f,0,4.5f), _right = new(2.8f,0,-2);
    private string _out = "";
    private ImageTexture? _stoneTexture;

    public override async void _Ready()
    {
        try {
            _out=OS.GetEnvironment("WORKLINGS_LAB_OUT");
            if(string.IsNullOrEmpty(_out)) _out=ProjectSettings.GlobalizePath("res://../../build/battle-lab/frames");
            DisplayServer.WindowSetSize(new Vector2I(1280,720));
            GetTree().Root.ContentScaleSize=new Vector2I(1280,720);
            GetViewport().Msaa3D=Viewport.Msaa.Msaa4X;
            MakeHud();
            string selection=OS.GetEnvironment("WORKLINGS_LAB_TAKE");
            for(int set=0;set<2;set++) for(int kind=0;kind<4;kind++) {
                string id=$"{set+1}{kind+1}-{_moves[kind].ToLower().Replace(' ','-')}";
                if(selection.Length>0 && !id.StartsWith(selection)) continue;
                MakeSet(set);
                await Capture(set,kind,id);
                _set.QueueFree();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            GD.Print("BATTLE LAB COMPLETE: "+_out);
            GetTree().Quit();
        } catch(Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }

    private StandardMaterial3D Stone(Color c,float roughness=.9f) {
        if(_stoneTexture==null) {
            var noise=new FastNoiseLite {Seed=741,Frequency=.095f,FractalOctaves=5};
            var img=Image.CreateEmpty(128,128,false,Image.Format.Rgba8);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++) {
                float n=.66f+noise.GetNoise2D(x,y)*.40f;
                img.SetPixel(x,y,new Color(n,n,n));
            }
            _stoneTexture=ImageTexture.CreateFromImage(img);
        }
        return new StandardMaterial3D { AlbedoColor=c, Roughness=roughness,AlbedoTexture=_stoneTexture };
    }
    private MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh,Vector3 pos, Material mat) {
        var node=new MeshInstance3D { Mesh=mesh,Position=pos,MaterialOverride=mat }; parent.AddChild(node); return node;
    }
    private void Box(Vector3 pos,Vector3 size,Material mat,float yaw=0) {
        Mesh(_set,new BoxMesh {Size=size},pos,mat).Rotation=new Vector3(0,yaw,0);
    }
    private void MakeSet(int theme)
    {
        _set=new Node3D(); AddChild(_set);
        var env=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.Color, BackgroundColor=new Color(.009f,.013f,.025f),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,
            AmbientLightColor=new Color(.40f,.48f,.64f),AmbientLightEnergy=.42f,
            TonemapMode=Godot.Environment.ToneMapper.Filmic,
            GlowEnabled=true,GlowIntensity=.7f,GlowStrength=1.0f,GlowBloom=.04f,GlowHdrThreshold=1.4f,
            SsaoEnabled=true,SsaoRadius=1.3f,SsaoIntensity=1.8f,
            FogEnabled=true,FogLightColor=new Color(.055f,.075f,.11f),FogDensity=.0018f };
        _set.AddChild(new WorldEnvironment {Environment=env});
        _camera=new Camera3D {Current=true,Fov=36,Near=.1f,Far=150};
        _set.AddChild(_camera);
        _cameraHome=theme==0?new Vector3(17,24,27):new Vector3(12,31,22);
        _camera.Position=_cameraHome; _camera.LookAt(new Vector3(0,.6f,1.1f));
        var key=new DirectionalLight3D { LightColor=new Color(.72f,.82f,1),LightEnergy=1.3f,
            ShadowEnabled=true,RotationDegrees=new Vector3(-52,-30,0) };
        _set.AddChild(key);
        var fill=new OmniLight3D {Position=new Vector3(-7,9,7),LightColor=new Color(.55f,.68f,1),LightEnergy=2.0f,OmniRange=24};
        _set.AddChild(fill);
        var rim=new OmniLight3D {Position=new Vector3(4,8,-7),LightColor=new Color(1,.56f,.24f),LightEnergy=3,OmniRange=23};
        _set.AddChild(rim);
        var mortar=Stone(new Color(.025f,.033f,.043f));
        Box(new Vector3(0,-.4f,1),new Vector3(39,.6f,35),mortar);
        var rng=new Random(400+theme);
        for(int x=-8;x<=8;x++) for(int z=-7;z<=7;z++) {
            float shade=.075f+(float)rng.NextDouble()*.055f;
            var c=theme==0?new Color(shade*.88f,shade,shade*1.17f):new Color(shade*.8f,shade*.76f,shade*.75f);
            if(theme==0)
                Box(new Vector3(x*2.15f,-.1f,z*2.15f+1),new Vector3(2.08f,.20f,2.08f),Stone(c),((float)rng.NextDouble()-.5f)*.012f);
            else
                Mesh(_set,new CylinderMesh {TopRadius=1.23f,BottomRadius=1.23f,Height=.25f,RadialSegments=6},
                    new Vector3(x*2.14f+(z%2==0?0:1.07f),-.125f,z*1.86f+1),Stone(c));
        }
        var stone=Stone(new Color(.13f,.16f,.19f));
        // Perimeter masonry frames the battle without blocking the actors.
        for(int i=-8;i<=8;i++) {
            Box(new Vector3(i*2.1f,.3f,-13.8f),new Vector3(2,.6f,1.1f),stone);
            Box(new Vector3(-18,.25f,i*1.7f),new Vector3(1.2f,.5f,1.6f),stone);
        }
        for(int i=0;i<6;i++) {
            var at=new Vector3(-13+i*5.2f,0,-11.6f);
            Box(at+Vector3.Up*.22f,new Vector3(1.8f,.44f,1.8f),stone);
            float height=i%2==0?4.5f:2.8f;
            Mesh(_set,new CylinderMesh {TopRadius=.56f,BottomRadius=.65f,Height=height,RadialSegments=8},at+Vector3.Up*(height*.5f+.44f),stone);
            Box(at+Vector3.Up*(height+.54f),new Vector3(1.45f,.26f,1.45f),stone);
        }
        for(int i=0;i<30;i++) {
            float x=(float)rng.NextDouble()*32-16, z=(float)rng.NextDouble()*24-11;
            if(Mathf.Abs(x)<7 && z>-7 && z<9) continue;
            float s=.2f+(float)rng.NextDouble()*.6f;
            Box(new Vector3(x,s*.4f,z),new Vector3(s*1.6f,s*.8f,s),stone,(float)rng.NextDouble()*3);
        }
        foreach(var at in new[]{new Vector3(-9,0,3),new Vector3(8,0,-7),new Vector3(-7,0,-9),new Vector3(10,0,8)}) {
            Mesh(_set,new CylinderMesh {TopRadius=.35f,BottomRadius=.5f,Height=1.2f,RadialSegments=8},at+Vector3.Up*.6f,stone);
            var fire=new StandardMaterial3D {AlbedoColor=new Color(1,.28f,.025f),EmissionEnabled=true,Emission=new Color(1,.2f,.015f),EmissionEnergyMultiplier=3};
            Mesh(_set,new SphereMesh {Radius=.25f,Height=.7f},at+Vector3.Up*1.4f,fire);
            _set.AddChild(new OmniLight3D {Position=at+Vector3.Up*2,LightColor=new Color(1,.32f,.07f),LightEnergy=3,OmniRange=7});
        }
        if(theme==1) {
            var lava=new StandardMaterial3D {AlbedoColor=new Color(.7f,.07f,.008f),EmissionEnabled=true,Emission=new Color(1,.09f,.004f),EmissionEnergyMultiplier=2};
            for(int i=0;i<15;i++) {
                float x=-15+i*2.1f;
                Box(new Vector3(x,.012f,-8.3f+Mathf.Sin(i*1.6f)*.7f),new Vector3(2.2f,.022f,.11f),lava,Mathf.Sin(i)*.3f);
                Box(new Vector3(-10+Mathf.Sin(i)*.6f,.013f,-10+i*1.4f),new Vector3(.12f,.024f,1.8f),lava);
            }
        }
    }

    private void MakeHud() {
        var layer=new CanvasLayer {Layer=10}; AddChild(layer);
        var top=new ColorRect {Position=Vector2.Zero,Size=new Vector2(1280,88),Color=new Color(.015f,.02f,.03f,.88f)}; layer.AddChild(top);
        _title=new Label {Position=new Vector2(40,19)}; _title.AddThemeFontSizeOverride("font_size",25);layer.AddChild(_title);
        _subtitle=new Label {Position=new Vector2(41,53)};_subtitle.AddThemeFontSizeOverride("font_size",13); _subtitle.Modulate=new Color(.65f,.73f,.83f); layer.AddChild(_subtitle);
        _phase=new Label {Position=new Vector2(40,665)};_phase.AddThemeFontSizeOverride("font_size",16); layer.AddChild(_phase);
        var caption=new Label {Text="WORKLINGS  /  BATTLE STUDIES  /  01",Position=new Vector2(880,665)};
        caption.AddThemeFontSizeOverride("font_size",13);caption.Modulate=new Color(.65f,.73f,.83f);layer.AddChild(caption);
        _flash=new ColorRect {Size=new Vector2(1280,720),Color=new Color(1,1,1,0),MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(_flash);
    }
    private StageActor Spawn(string model,Vector3 at,Vector3 target) {
        var root=GD.Load<PackedScene>($"res://assets/characters/{model}.glb").Instantiate<Node3D>();
        float scale=model=="tempest_ram"?3.7f:model=="snag"?7:2.8f;
        var direction=target-at;
        root.Scale=Vector3.One*scale;root.Position=at;
        root.Rotation=new Vector3(0,Mathf.Atan2(direction.X,direction.Z),0);
        _set.AddChild(root);
        var actor=new StageActor(root,model,ActorAnimations.For(model)!);
        if(actor.Player!=null) actor.Player.CallbackModeProcess=AnimationMixer.AnimationCallbackModeProcess.Manual;
        actor.Play(ActorAction.Idle,true); actor.Player?.Advance(.01);
        return actor;
    }
    private async System.Threading.Tasks.Task Capture(int theme,int kind,string id) {
        var attacker=Spawn(_models[kind],_left,_right);
        var defender=Spawn(kind==0?"forest_flicker":"tempest_ram",_right,_left);
        var effects=new BattleLabEffects(_set,_camera,kind,_left,_right);
        _title.Text=_names[kind]+"  /  "+_moves[kind];
        _subtitle.Text=(theme==0?"01  •  MOONLIT RUINS":"02  •  EMBER VAULT")+"     /     1 V 1     /     TURN-BASED DUNGEON STUDY";
        string directory=_out+"/"+id;
        DirAccess.MakeDirRecursiveAbsolute(directory);
        const double lead=.7;
        double windup=kind==1?1.08:kind==3?.72:.82;
        double contact=lead+windup;
        double duration=6.2;
        bool started=false,hit=false,recovered=false;
        double fightClock=0,stop=0,hitWall=-100;
        double clipEnd=0;
        for(int frame=0;frame<(int)(duration*Fps);frame++) {
            double wall=frame*Step;
            double delta=stop>0?0:Step;
            if(stop>0) stop-=Step;
            fightClock+=delta;
            if(!started && fightClock>=lead) {
                started=true;
                var action=kind==1?ActorAction.Signature:ActorAction.Attack;
                double length=attacker.Play(action);
                double impactPoint=kind==1?.76:attacker.Animations.AttackImpactPoint;
                double speed=length*impactPoint/windup;
                if(attacker.Player!=null) attacker.Player.SpeedScale=(float)speed;
                clipEnd=lead+length/speed;
            }
            double relative=fightClock-contact;
            if(!hit && relative>=0) {
                hit=true;hitWall=wall;stop=kind==1?.10:.067;
                defender.Play(ActorAction.Wince);
            }
            if(started && !recovered && fightClock>clipEnd+.06) {
                recovered=true;if(attacker.Player!=null)attacker.Player.SpeedScale=1;
                attacker.Play(ActorAction.Idle,true);
            }
            attacker.Player?.Advance(delta);defender.Player?.Advance(delta);
            // Modest commitment followed by a deliberate recovery, not a long slide.
            float push=kind==0?.18f:kind==2?.29f:kind==1?.04f:.015f;
            float move=relative<-.19?0:relative<0?Mathf.Pow((float)(relative+.19)/.19f,2):Mathf.Max(0,1-(float)relative/.65f);
            attacker.Root.Position=_left+(_right-_left)*push*move;
            if(hit) {
                float u=(float)(wall-hitWall);
                var axis=(_right-_left).Normalized();
                defender.Root.Position=_right+axis*(Mathf.Sin(Mathf.Min(u/.45f,1)*Mathf.Pi)*.65f);
                float shake=Mathf.Exp(-u*13)*(kind==1?.22f:.14f);
                _camera.Position=_cameraHome+_camera.GlobalBasis.X*Mathf.Sin(u*145)*shake+_camera.GlobalBasis.Y*Mathf.Cos(u*123)*shake*.55f;
                _flash.Color=new Color(.7f,.85f,1,u<.035f?.07f:0);
            }
            effects.Draw(relative);
            _phase.Text=relative<-.9?"PREPARE":relative<0?"WIND-UP":relative<.22?"IMPACT":relative<1.2?"AFTERMATH":"RECOVERY";
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var result=GetViewport().GetTexture().GetImage().SavePng($"{directory}/{frame:D4}.png");
            if(result!=Error.Ok) throw new Exception("Frame save failed: "+result);
        }
        GD.Print($"TAKE {id}: {duration:F1}s, {duration*Fps} frames, impact {contact:F3}s");
        effects.Release();
    }
}
