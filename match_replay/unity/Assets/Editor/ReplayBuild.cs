using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using TcrReplay;

public static class ReplayBuild
{
    const string Scene = "Assets/Scenes/Replay.unity";
    [MenuItem("TCR Replay/Prepare scene")]
    public static void Prepare()
    {
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("TCR Replay").AddComponent<ReplayViewer>();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Scene);
        EditorBuildSettings.scenes=new [] {new EditorBuildSettingsScene(Scene,true)};
        PlayerSettings.companyName="TCR"; PlayerSettings.productName="2026 TCR Replay";
        PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.resizableWindow=true;
        // The viewer owns F11/Alt+Enter so the engine's native shortcut cannot
        // toggle a second time or bypass restoration of the previous window size.
        PlayerSettings.allowFullscreenSwitch=false;
        PlayerSettings.runInBackground=true;
        PlayerSettings.enableFrameTimingStats=true;
        foreach(string shaderName in new [] {"Standard","Unlit/Color","TCR/Replay CAD surface","TCR/Replay robot CAD","TCR/Replay armor surface","TCR/Replay projectile","TCR/Avia point cloud","TCR/Replay safety boundary"})
        {
            string name=shaderName=="TCR/Replay safety boundary"?"ReplayBoundary":shaderName=="Standard"?"ReplayStandard":shaderName=="Unlit/Color"?"ReplayUnlit":shaderName=="TCR/Replay robot CAD"?"ReplayRobot":shaderName=="TCR/Replay armor surface"?"ReplayArmor":shaderName=="TCR/Replay projectile"?"ReplayProjectile":shaderName=="TCR/Avia point cloud"?"ReplayLidar":"ReplayCad";
            string path="Assets/Resources/"+name+".mat";
            if(!AssetDatabase.LoadAssetAtPath<Material>(path))
                AssetDatabase.CreateAsset(new Material(Shader.Find(shaderName)),path);
        }
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        AssetDatabase.SaveAssets();
        Verify();
    }
    public static void Verify()
    {
        string folder=Path.Combine(Application.streamingAssetsPath,"replay");
        bool demo=ReplayResourceProfile.Load(folder).SyntheticDemo;
        ReplayLidarChecks.Run();
            LidarFrameChecks.Run();
        DeveloperChecks.Run();
        ReplayCameraChecks.Motion();
        if(!demo)ReplayRobotChecks.Contracts();
        ReplayMechanismChecks.Rules();
        if(!demo)
        {
            DeveloperPhysicsChecks.Assets(folder);
            ReplayMechanismChecks.Geometry(JsonUtility.FromJson<MechanismConfig>(File.ReadAllText(Path.Combine(folder,"mechanisms_config.json"))));
        }
        var fields=JsonUtility.FromJson<FieldCatalog>(File.ReadAllText(Path.Combine(folder,"fields_catalog.json")));
        if(fields.schema!=1 || fields.fields==null || fields.fields.Length<1 ||
           !Array.Exists(fields.fields,f=>f.id==fields.defaultField)) throw new Exception("场地目录无效");
        var fieldIds=new HashSet<string>();
        foreach(var entry in fields.fields)
        {
            var field=JsonUtility.FromJson<FieldConfig>(File.ReadAllText(Path.Combine(folder,entry.file)));
            if(!fieldIds.Add(entry.id) || field.id!=entry.id) throw new Exception("场地身份不一致");
            VerifyField(field);
            if(field.meshFormat=="assembly_binary_v1") {VerifyAssembly(folder,field);VerifyAppearance(folder,field);}
        }
        var catalog=JsonUtility.FromJson<MatchCatalog>(File.ReadAllText(Path.Combine(folder,"catalog.json")));
        var search=new ReplaySearchIndex(catalog);
        foreach(var school in catalog.schools??Array.Empty<SchoolAliases>())
            if(search.Query(school.name,m=>true,0).Count<1)throw new Exception("学校检索未找到已登记对局："+school.name);
        int states=0,events=0;
        foreach(var entry in catalog.matches)
        {
            MatchData data;
            try {data=Timeline.ReadMatch(Path.Combine(folder,entry.file));}
            catch(Exception ex){throw new InvalidDataException("回放包读取失败："+entry.file,ex);}
            if(data.gameId!=entry.gameId || data.redSchool!=entry.redSchool || data.blueSchool!=entry.blueSchool || data.matchNumber!=entry.matchNumber || data.round!=entry.round)
                throw new Exception("比赛目录与包身份不一致");
            if(!fieldIds.Contains(data.fieldId)) throw new Exception("比赛来源场地未登记");
            foreach(float time in new [] {0f,1f,60.5f,120.5f,419f,420f,60.5f,1f,0f})
            {
                int expected=0;
                foreach(var e in data.events) if(e.t<=time) expected++;
                if(Timeline.EventEnd(data.events,time)!=expected) throw new Exception("事件 seek 索引不一致");
                foreach(var entity in data.entities)
                {
                    int linear=-1;
                    for(int i=0;i<entity.samples.Length;i++) if(entity.samples[i].t<=time) linear=i;
                    if(Timeline.StateIndex(entity.samples,time)!=linear) throw new Exception("状态 seek 索引不一致");
                    bool valid=Timeline.Pose(entity,time,out var position);
                    if(valid && (!float.IsFinite(position.x)||!float.IsFinite(position.z))) throw new Exception("非有限显示坐标");
                }
            }
            foreach(var entity in data.entities) states+=entity.samples.Length;
            events+=data.events.Length;
        }
        // Gaps and invalid samples must never be interpolated into a fabricated path.
        var example=new EntityData {samples=new [] {new Sample {t=1,x=1,y=1,valid=true},new Sample {t=2,x=9,y=9,valid=false},new Sample {t=5,x=5,y=5,valid=true}}};
        if(Timeline.Pose(example,2.5f,out _) || Timeline.Pose(example,0,out _)) throw new Exception("缺失状态被显示");
        Timeline.Pose(example,1.5f,out var held);
        if(held.x!=1) throw new Exception("跨无效坐标插值");
        var duplicates=new [] {new ReplayEvent {t=1},new ReplayEvent {t=1},new ReplayEvent {t=2}};
        if(Timeline.EventEnd(duplicates,1)!=2) throw new Exception("同秒事件丢失");
        Debug.Log($"REPLAY_VERIFY_OK matches={catalog.matches.Length} states={states} events={events}");
    }
    static void VerifyField(FieldConfig field)
    {
        if(field.buildings==null || field.buildings.Length!=4 || field.landmarks==null)
            throw new Exception("建筑标识配置不完整");
        foreach(string role in new [] {"base","outpost"})
        {
            var red=Array.Find(field.buildings,a=>a.role==role && a.team=="red");
            var blue=Array.Find(field.buildings,a=>a.role==role && a.team=="blue");
            // CAD preserves the actual assembly's small placement differences.
            float tolerance=field.meshFormat=="assembly_binary_v1"?.15f:.001f;
            if(red==null || blue==null || Mathf.Abs(red.x+blue.x-field.width)>tolerance || Mathf.Abs(red.y+blue.y-field.depth)>tolerance)
                throw new Exception("建筑不满足规则图中心对称布局");
        }
        var redBase=Array.Find(field.buildings,a=>a.role=="base" && a.team=="red");
        var outpost=Array.Find(field.buildings,a=>a.role=="outpost" && a.team=="red");
        var fortress=Array.Find(field.landmarks,a=>a.role=="fortress" && a.team=="red");
        var pad=Array.Find(field.landmarks,a=>a.role=="helipad" && a.team=="red");
        if(fortress==null || pad==null || redBase.x>=3 || Mathf.Abs(redBase.y-field.depth/2)>.1f || outpost.x<=fortress.x || outpost.y>=field.depth/2)
            throw new Exception("基地、前哨站与堡垒的规则布局不符");
        if(Vector2.Distance(new Vector2(outpost.x,outpost.y),new Vector2(fortress.x,fortress.y))<4 ||
           Vector2.Distance(new Vector2(redBase.x,redBase.y),new Vector2(pad.x,pad.y))<4)
            throw new Exception("前哨站与堡垒或基地与停机坪混淆");
        Debug.Log($"REPLAY_FIELD_OK id={field.id} buildings={field.buildings.Length} landmarks={field.landmarks.Length} rules={field.rulesVersion}");
    }
    static void VerifyAssembly(string folder,FieldConfig field)
    {
        var assembly=JsonUtility.FromJson<FieldAssembly>(File.ReadAllText(Path.Combine(folder,field.mesh)));
        if(assembly.schema!=1 || assembly.parts==null || assembly.parts.Length<1) throw new Exception("CAD装配缺失");
        var ids=new HashSet<string>();var meshes=new HashSet<string>();var bindings=new HashSet<string>();
        int walkable=0;
        foreach(var part in assembly.parts)
        {
            if(!ids.Add(part.id) || part.matrix==null || part.matrix.Length!=16 || part.color==null || part.color.Length!=3)
                throw new Exception("CAD实例结构无效");
            foreach(float value in part.matrix) if(!float.IsFinite(value)) throw new Exception("CAD变换非有限");
            Vector3 right=new Vector3(part.matrix[0],part.matrix[4],part.matrix[8]);
            Vector3 up=new Vector3(part.matrix[1],part.matrix[5],part.matrix[9]);
            Vector3 forward=new Vector3(part.matrix[2],part.matrix[6],part.matrix[10]);
            if(Mathf.Abs(right.sqrMagnitude-1)>.0001f || Mathf.Abs(up.sqrMagnitude-1)>.0001f ||
               Mathf.Abs(forward.sqrMagnitude-1)>.0001f || Vector3.Dot(Vector3.Cross(right,up),forward)<.999f)
                throw new Exception("CAD装配旋转无效");
            if(part.walkable) walkable++;
            if(!string.IsNullOrEmpty(part.binding) && !bindings.Add(part.binding)) throw new Exception("CAD建筑绑定重复");
            if(!string.IsNullOrEmpty(part.binding) && !part.binding.StartsWith(part.team+"_")) throw new Exception("CAD材质阵营与建筑身份不一致");
            if(meshes.Add(part.mesh))
            {
                using(var reader=new BinaryReader(File.OpenRead(Path.Combine(folder,part.mesh))))
                {
                    string magic=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
                    if(magic!="TCRMESH1" && magic!="TCRMESH2") throw new Exception("CAD网格格式无效");
                    int vertices=reader.ReadInt32(),indices=reader.ReadInt32();
                    if(vertices<1 || indices<3 || indices%3!=0 || reader.BaseStream.Length!=16L+vertices*(magic=="TCRMESH2"?16L:12L)+indices*4L)
                        throw new Exception("CAD网格大小无效");
                    for(int i=0;i<vertices*3;i++) if(!float.IsFinite(reader.ReadSingle())) throw new Exception("CAD网格坐标无效");
                    for(int i=0;i<indices;i++) {int value=reader.ReadInt32();if(value<0 || value>=vertices) throw new Exception("CAD网格索引无效");}
                }
            }
        }
        if(walkable<1) throw new Exception("CAD缺少场地碰撞网格");
        foreach(var anchor in field.buildings)
        {
            var part=Array.Find(assembly.parts,p=>p.id==anchor.modelNode);
            if(part==null || part.binding!=anchor.team+"_"+anchor.role ||
               Mathf.Abs(part.matrix[3]-anchor.x)>.001f || Mathf.Abs(part.matrix[11]-anchor.y)>.001f || Mathf.Abs(part.matrix[7]-anchor.z)>.001f)
                throw new Exception("CAD建筑身份或锚点不一致");
        }
        Debug.Log($"REPLAY_CAD_VERIFY_OK parts={assembly.parts.Length} meshes={meshes.Count} bindings={bindings.Count}");
    }
    static void VerifyAppearance(string folder,FieldConfig field)
    {
        if(string.IsNullOrEmpty(field.surfaceTexture)) return;
        var shader=Shader.Find("TCR/Replay CAD surface");
        if(!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("CAD贴图着色器编译失败");
        var map=field.textureScaleOffset;
        if(map==null || map.Length!=4 || map[0]<=0 || map[1]<=0 || map[2]<0 || map[3]<0) throw new Exception("官方贴图轴向或边界无效");
        var image=new Texture2D(2,2);
        if(!image.LoadImage(File.ReadAllBytes(Path.Combine(folder,field.texture))) || !File.Exists(Path.Combine(folder,field.surfaceTexture)))
            throw new Exception("官方贴图资产缺失");
        int leftRed=0,leftBlue=0,rightRed=0,rightBlue=0;
        // Measure the source pixels in the projected field, excluding orange
        // branding. A mirrored texture must fail the build.
        for(float x=.05f;x<field.width;x+=.05f)
        for(float y=.05f;y<field.depth;y+=.05f)
        {
            var color=image.GetPixelBilinear(x/field.width*map[0]+map[2],y/field.depth*map[1]+map[3]);
            bool red=color.r>.3f && color.r>color.g*1.6f && color.r>color.b*1.6f && Mathf.Abs(color.g-color.b)<.15f;
            bool blue=color.b>.3f && color.b>color.r*1.6f && color.b>color.g*1.6f;
            if(x<field.width/2) {if(red)leftRed++;if(blue)leftBlue++;}
            else {if(red)rightRed++;if(blue)rightBlue++;}
        }
        UnityEngine.Object.DestroyImmediate(image);
        if(leftRed<100 || rightBlue<100 || leftRed<=rightRed*3 || rightBlue<=leftBlue*3)
            throw new Exception("官方俯视贴图红蓝分区不符");
        Debug.Log($"REPLAY_TEXTURE_VERIFY_OK leftRed={leftRed} rightBlue={rightBlue} leftBlue={leftBlue} rightRed={rightRed}");
    }
    [MenuItem("TCR Replay/Build Linux")]
    public static void Linux() {Prepare();Build(BuildTarget.StandaloneLinux64,"../builds/linux/TCRReplay.x86_64");}
    [MenuItem("TCR Replay/Build Windows")]
    public static void Windows() {Prepare();Build(BuildTarget.StandaloneWindows64,"../builds/windows/TCRReplay.exe");}
    static void Build(BuildTarget target,string output)
    {
        output=Environment.GetEnvironmentVariable("TCR_REPLAY_OUTPUT")??output;Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new [] {Scene},locationPathName=output,target=target,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("构建失败: "+report.summary.result);
        Debug.Log("REPLAY_BUILD_OK "+Path.GetFullPath(output));
    }
}
