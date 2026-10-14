using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public class MechanismConfig {public int schema,geometrySplitVersion;public OutpostGeometry outpost;public RuneGeometry rune;public GuidanceGeometry baseGuide;}
    [Serializable] public class GuidanceGeometry {public float[] center,normal;public float diameter;public string evidence;}
    [Serializable] public class MovingGeometry {public string @fixed,rotor,hub;public float[] pivot,axis;}
    [Serializable] public class OutpostGeometry:MovingGeometry {public MechanismPanel[] panels;public RuneShellPartition shellPartition;}
    [Serializable] public class MechanismPanel {public float[] center,normal,half;}
    [Serializable] public class RuneGeometry:MovingGeometry {public string hubMotion;public int[] solidGroups,hubSolidGroups;public RuneArmGeometry[] arms;public RuneSelectionAnchor[] selectionAnchors;public RuneShellPartition shellPartition;}
    [Serializable] public class RuneSelectionAnchor {public string team;public float[] center,normal;}
    [Serializable] public class RuneShellPartition {public int freeShellCount,movingFreeShellCount,unassignedTriangles,sourceTriangles,movingShellTriangles;public int[] fixedShellGroups,movingShellGroups;}
    [Serializable] public class RuneArmGeometry {public string name,team,mesh;public int index;public int[] solidGroups,shellGroups;public float[] targetCenter,targetNormal;public float targetRadius;}
    // Marker used to exclude moving meshes from immutable projectile geometry.
    public sealed class ReplayMovingCad:MonoBehaviour {}

    public partial class ReplayViewer
    {
        MechanismConfig mechanismConfig;
        ReplayMechanismTimeline mechanismTimeline;
        readonly Dictionary<string,Transform> outpostRotors=new Dictionary<string,Transform>();
        readonly Dictionary<string,Material> guideMaterials=new Dictionary<string,Material>();
        readonly Dictionary<string,Transform> guideLenses=new Dictionary<string,Transform>();
        readonly Dictionary<string,Material> primitiveMaterials=new Dictionary<string,Material>();
        readonly Dictionary<string,Transform> markerEyes=new Dictionary<string,Transform>();
        readonly List<RuneArmVisual> runeArms=new List<RuneArmVisual>();
        Transform runeRotor;
        class RuneArmVisual {public string team;public int index;public Material material;public Renderer body,targetPattern,successDot;public RuneArmGeometry geometry;}
        Mesh runeTargetMesh,runeDotMesh,guideLensMesh;
        Material SharedPrimitiveMaterial(Color color,bool unlit=false)
        {
            string key=ColorUtility.ToHtmlStringRGBA(color)+(unlit?"u":"s");
            if(!primitiveMaterials.TryGetValue(key,out var material)){material=Material(color,unlit);primitiveMaterials[key]=material;}
            return material;
        }
        void LoadMechanismAssets()
        {
            var path=Path.Combine(folder,"mechanisms_config.json");
            if(File.Exists(path))mechanismConfig=JsonUtility.FromJson<MechanismConfig>(File.ReadAllText(path));
            if(mechanismConfig!=null && mechanismConfig.schema!=1)throw new InvalidDataException("机构模型版本无效");
            if(mechanismConfig!=null)ReplayMechanismChecks.Geometry(mechanismConfig);
        }
        string MechanismFixedMesh(FieldPart part)
        {
            if(appMode=="game" && gameBase!=null && part.binding.EndsWith("_base"))return gameBase.@fixed;
            if(mechanismConfig==null)return part.mesh;
            if(part.binding.EndsWith("_outpost"))return mechanismConfig.outpost.@fixed;
            if(part.id.StartsWith("cad_2702539_"))return mechanismConfig.rune.@fixed;
            return part.mesh;
        }
        GameObject CadChild(string name,Transform parent,string mesh,Material material,Vector3 offset,bool moving)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=offset;
            obj.AddComponent<MeshFilter>().sharedMesh=ReadMesh(mesh);obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(moving)obj.AddComponent<ReplayMovingCad>();return obj;
        }
        void BuildCadMechanisms(FieldPart part,Transform node,Material material)
        {
            BuildGameBase(part,node,material);
            if(part.binding.EndsWith("_base") || part.binding.EndsWith("_outpost"))guideMaterials[part.binding]=material;
            if(mechanismConfig==null)return;
            if(part.binding.EndsWith("_base") && mechanismConfig.baseGuide!=null)
            {
                var g=mechanismConfig.baseGuide;
                var lamp=GameObject.CreatePrimitive(PrimitiveType.Sphere);lamp.name="55mm green dart guide lens (upper reserved circular bezel)";
                lamp.transform.SetParent(node,false);lamp.transform.localPosition=PassagePose.Vector(g.center);
                lamp.transform.localRotation=Quaternion.LookRotation(PassagePose.Vector(g.normal),Vector3.up);
                lamp.transform.localScale=new Vector3(g.diameter,g.diameter,.014f);
                var collider=lamp.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
                if(!guideLensMesh)
                {
                    guideLensMesh=Instantiate(lamp.GetComponent<MeshFilter>().sharedMesh);guideLensMesh.name="Independent 55mm guide lens";
                    var colors=new Color32[guideLensMesh.vertexCount];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(1,244,1,255);
                    guideLensMesh.colors32=colors;owned.Add(guideLensMesh);
                }
                lamp.GetComponent<MeshFilter>().sharedMesh=guideLensMesh;
                var renderer=lamp.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                guideLenses[part.binding]=lamp.transform;
            }
            if(part.binding.EndsWith("_outpost"))
            {
                var g=mechanismConfig.outpost;Vector3 pivot=PassagePose.Vector(g.pivot);
                var root=new GameObject("Three armor rotor").transform;root.SetParent(node,false);root.localPosition=pivot;
                var rotorMaterial=new Material(material);owned.Add(rotorMaterial);cadMaterials.Add(rotorMaterial);
                CadChild("Source CAD middle armor and frame",root,g.rotor,rotorMaterial,-pivot,true);
                outpostRotors[part.team]=root;
            }
            if(part.id.StartsWith("cad_2702539_"))
            {
                var g=mechanismConfig.rune;Vector3 pivot=PassagePose.Vector(g.pivot);
                runeRotor=new GameObject("Shared red blue rune rotor").transform;runeRotor.SetParent(node,false);runeRotor.localPosition=pivot;
                // The centre is fixed to the assembly, at its native upright
                // orientation. Only arms and their target markings rotate.
                CadChild("Stationary upright R marks and centre covers",node,g.hub,material,Vector3.zero,false);
                foreach(var arm in g.arms)
                {
                    var paint=new Material(material);paint.SetFloat("_Team",arm.team=="red"?0:1);paint.SetFloat("_RuneSurface",1);
                    owned.Add(paint);cadMaterials.Add(paint);
                    var body=CadChild(arm.name,runeRotor,arm.mesh,paint,-pivot,true);
                    var visual=new RuneArmVisual {team=arm.team,index=arm.index,material=paint,geometry=arm,body=body.GetComponent<Renderer>()};
                    if(arm.targetCenter!=null)
                    {
                        if(!runeTargetMesh)runeTargetMesh=RuneSignalMesh(false);
                        if(!runeDotMesh)runeDotMesh=RuneSignalMesh(true);
                        var normal=PassagePose.Vector(arm.targetNormal);
                        var point=PassagePose.Vector(arm.targetCenter)-pivot+normal*.004f;
                        visual.targetPattern=RuneSignalNode("300mm activation mark",runeRotor,point,normal,runeTargetMesh,paint);
                        visual.successDot=RuneSignalNode("Activated centre dot",runeRotor,point,normal,runeDotMesh,paint);
                    }
                    runeArms.Add(visual);
                }
                buildingNodes["neutral_energy_mechanism"]=node;
                if(field.landmarks!=null)
                {
                    var marker=Array.Find(field.landmarks,m=>m.role=="energy_mechanism");
                    if(marker!=null){var point=node.TransformPoint(pivot);marker.x=point.x;marker.y=point.z;marker.z=point.y;marker.modelNode=part.id;}
                }
            }
        }
        void ResetMechanismField()
        {
            outpostRotors.Clear();guideMaterials.Clear();guideLenses.Clear();runeArms.Clear();runeRotor=null;markerEyes.Clear();
        }
        void UpdateMechanisms()
        {
            if(mechanismTimeline==null)return;
            foreach(var entry in guideMaterials)
            {
                bool lamp=mechanismTimeline.GuideLamp(entry.Key.StartsWith("red")?"red":"blue",entry.Key.EndsWith("_base"),playhead,out bool known);
                entry.Value.SetFloat("_GuideLight",known?(lamp?1:0):-1);
            }
            if(mechanismConfig==null)return;
            foreach(var entry in outpostRotors)
                entry.Value.localRotation=OutpostRotation(entry.Value.parent,entry.Key,playhead);
            if(runeRotor)runeRotor.localRotation=Quaternion.AngleAxis(mechanismTimeline.RuneAngle(playhead),PassagePose.Vector(mechanismConfig.rune.axis));
            var redRune=mechanismTimeline.RuneMechanism("red",playhead);var blueRune=mechanismTimeline.RuneMechanism("blue",playhead);
            foreach(var arm in runeArms)
            {
                var state=arm.team=="red"?redRune:blueRune;
                int order=(arm.index-Mathf.Abs(mechanismTimeline.direction*31)%5+5)%5;
                int progress=Mathf.Clamp(state.kind=="small"?state.arms:state.arms/2,0,5);
                bool target=order==progress || state.kind=="large" && order==progress+1;
                bool completed=state.state=="activating" && order<progress;
                float intensity=state.state=="activated"?1:state.state=="activating" && (target || completed)?1:state.state=="activating"?.25f:.08f;
                arm.material.SetFloat("_RuneLight",intensity);arm.material.SetFloat("_RunePhase",playhead);
                if(arm.targetPattern)arm.targetPattern.enabled=state.state=="activating" && target;
                if(arm.successDot)arm.successDot.enabled=state.state=="activated" || completed;
            }
            foreach(var unit in units)
            {
                if(unit.data.role!="outpost")continue;
                var boxes=unit.armorColliders??Array.Empty<BoxCollider>();
                for(int i=0;i<Mathf.Min(3,boxes.Length);i++)
                {
                    var panel=PanelAt(unit,playhead,i);boxes[i].transform.SetPositionAndRotation(panel.center,panel.rotation);boxes[i].size=panel.half*2;
                }
            }
        }
        string RuneStateName(RuneState state)
        {
            string status=state.state=="activated"?"已激活":state.state=="activating"?"正在激活":state.state=="inactive"?"未激活":state.state=="failed"?"失败":"未知";
            string name=state.kind=="small"?"小机关":"大机关";
            if(state.state=="activated" && state.kind!=ReplayMechanismTimeline.RuneStage(playhead))name+="增益";
            return name+" · "+status+(state.arms>=0?" · 进度 "+state.arms:"");
        }
        void DrawRuneInspector()
        {
            string stage=ReplayMechanismTimeline.RuneStage(playhead)=="small"?"小阶段":"大阶段";
            Text(1230,420,356,30,"能量机关 · "+stage+" · 共轴");
            float y=456;
            foreach(string team in new[]{"red","blue"})
            {
                var state=mechanismTimeline.RuneMechanism(team,playhead);var benefit=mechanismTimeline.Rune(team,playhead);GUI.color=team=="red"?red:blue;
                Text(1230,y,356,25,(team=="red"?"红方 ":"蓝方 ")+RuneStateName(state),small);GUI.color=Color.white;
                string endLabel=state.state=="activating"?"激活窗口至 ":"已知有效期至 ";
                string detail=state.confirmed?(state.ends>=0?endLabel+Clock(state.ends):"有效期/结束时刻未知")+"\n记录："+state.evidence:state.evidence;
                if(benefit.kind!=state.kind && benefit.state=="activated")detail="小机关增益仍有效至 "+Clock(benefit.ends)+"\n当前大机关状态未知";
                Text(1230,y+25,356,48,detail,small);y+=83;
            }
            string motion=mechanismTimeline.RuneVariable(playhead)?"变速 · 共用转速策略为示意":"匀速";
            Text(1230,622,356,40,$"同步 {Mathf.Abs(mechanismTimeline.RuneSpeed(playhead)):0.0}°/s · {motion}\n各自正面看旋向相反；实际角度/灯序未知。",small);
        }
        Mesh RuneSignalMesh(bool dot)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            Action<float,float> ring=(inner,outer)=>
            {
                int offset=vertices.Count;
                for(int i=0;i<=64;i++)
                {
                    float a=i*Mathf.PI*2/64;var radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                    vertices.Add(radial*inner);vertices.Add(radial*outer);
                }
                for(int i=0;i<64;i++) {int k=offset+i*2;indices.AddRange(new[]{k,k+1,k+3,k,k+3,k+2});}
            };
            if(dot)ring(0,.012f);
            else
            {
                ring(.025f,.035f);ring(.065f,.075f);ring(.1175f,.135f);
                for(int i=0;i<4;i++)
                {
                    var rotation=Quaternion.Euler(0,0,i*90);int offset=vertices.Count;
                    foreach(var p in new[]{new Vector3(.018f,.035f,0),new Vector3(.018f,.15f,0),new Vector3(-.018f,.15f,0),new Vector3(-.018f,.035f,0)})vertices.Add(rotation*p);
                    indices.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
                }
            }
            var mesh=new Mesh {name=dot?"Rune activated mark":"Rule figure5-22 target mark"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
            var colors=new Color32[vertices.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,0,0,255);mesh.colors32=colors;
            mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);return mesh;
        }
        Renderer RuneSignalNode(string name,Transform parent,Vector3 point,Vector3 normal,Mesh mesh,Material material)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=point;
            obj.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<ReplayMovingCad>();var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return renderer;
        }
        ArmorBox RunePanelAt(int index,float time)
        {
            var geometry=runeArms[index].geometry;var g=mechanismConfig.rune;
            Vector3 pivot=PassagePose.Vector(g.pivot),axis=PassagePose.Vector(g.axis);
            Quaternion rotation=Quaternion.AngleAxis(mechanismTimeline.RuneAngle(time),axis);
            Vector3 center=pivot+rotation*(PassagePose.Vector(geometry.targetCenter)-pivot);
            var normal=rotation*PassagePose.Vector(geometry.targetNormal);
            var node=runeRotor.parent;
            return new ArmorBox {center=node.TransformPoint(center),rotation=node.rotation*Quaternion.LookRotation(normal,rotation*Vector3.up),half=new Vector3(.15f,.15f,.008f),panel=-2,armor=false,unit=null};
        }
        Quaternion OutpostRotation(Transform node,string team,float time)
        {
            // Official instances include a small assembly lean. The orbit is
            // horizontal in the arena, even when the original CAD is tilted.
            return Quaternion.AngleAxis(mechanismTimeline.OutpostAngle(team,time),node.InverseTransformDirection(Vector3.up).normalized);
        }
        bool MovingOutpostPanel(Unit unit,float time,int index,out ArmorBox box)
        {
            box=default;
            if(unit.data.role!="outpost" || !unit.cadNode || mechanismConfig?.outpost?.panels==null || mechanismTimeline==null)return false;
            var g=mechanismConfig.outpost;var panel=g.panels[index];Vector3 pivot=PassagePose.Vector(g.pivot);
            var rotation=OutpostRotation(unit.cadNode,unit.data.team,time);
            var center=pivot+rotation*(PassagePose.Vector(panel.center)-pivot);
            var normal=rotation*PassagePose.Vector(panel.normal);
            box=new ArmorBox {unit=unit,panel=index,armor=true,center=unit.cadNode.TransformPoint(center),
                rotation=Quaternion.LookRotation(unit.cadNode.TransformDirection(normal),Vector3.up),half=PassagePose.Vector(panel.half)};return true;
        }
        Vector3 MarkerLabelPosition(BuildingAnchor marker)
        {
            string key=marker.team+"_"+marker.role;
            if(markerEyes.TryGetValue(key,out var lens))return lens.position+Vector3.up*.12f;
            if(buildingNodes.TryGetValue(key,out var node))
            {
                if(marker.role=="radar_platform")return node.position+Vector3.up*3.7f;
                if(marker.role=="energy_mechanism" && runeRotor)return runeRotor.position+Vector3.up*1.05f;
                return node.position+Vector3.up*.5f;
            }
            return new Vector3(marker.x,GroundHeight(marker.x,marker.y)+.2f,marker.y);
        }
        GameObject PlatformBox(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool floor=false)
        {
            var obj=Shape(PrimitiveType.Cube,parent,position,size,material);obj.name=name;
            if(floor)obj.layer=12;else Destroy(obj.GetComponent<Collider>());return obj;
        }
        void Rail(Transform parent,Vector3 a,Vector3 b,Material material,float width=.04f)
        {
            var obj=Shape(PrimitiveType.Cylinder,parent,(a+b)*.5f,new Vector3(width,(b-a).magnitude*.5f,width),material);
            obj.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);Destroy(obj.GetComponent<Collider>());
        }
        void BuildRadarPlatforms(Transform parent)
        {
            var markers=new List<BuildingAnchor>(field.landmarks??Array.Empty<BuildingAnchor>());
            markers.RemoveAll(m=>m.role=="radar" || m.role=="radar_platform");
            var steel=SharedPrimitiveMaterial(new Color(.16f,.18f,.20f));var dark=SharedPrimitiveMaterial(new Color(.04f,.05f,.06f));
            var silver=SharedPrimitiveMaterial(new Color(.42f,.45f,.49f));
            var outside=Shape(PrimitiveType.Cube,parent,new Vector3(14,-.11f,7.5f),new Vector3(40,.12f,23),SharedPrimitiveMaterial(new Color(.07f,.085f,.10f)));
            outside.name="Outside field floor";outside.layer=8;
            foreach(string team in new[]{"red","blue"})
            {
                var root=new GameObject(team+" radar platform").transform;root.SetParent(parent,false);
                root.position=new Vector3(team=="red"?-1.51f:field.width+1.51f,0,field.depth*.5f);
                root.rotation=Quaternion.Euler(0,team=="red"?0:180,0);
                PlatformBox(root,"3.4m sensor deck",new Vector3(0,2.42f,0),new Vector3(1.16f,.16f,3.4f),steel,true);
                PlatformBox(root,"Stair landing",new Vector3(0,2.42f,-2.305f),new Vector3(1.16f,.16f,1.21f),steel,true);
                foreach(float x in new[]{-.5f,.5f})foreach(float z in new[]{-2.8f,1.6f})
                    PlatformBox(root,"Deck support",new Vector3(x,1.18f,z),new Vector3(.10f,2.36f,.10f),steel);
                foreach(float x in new[]{-.58f,.58f})
                {
                    Rail(root,new Vector3(x,3.6f,-2.91f),new Vector3(x,3.6f,1.7f),silver);
                    for(float z=-2.9f;z<=1.71f;z+=.30f)
                    {
                        if(x<0 && z<-1.7f)continue;
                        Rail(root,new Vector3(x,2.5f,z),new Vector3(x,3.6f,z),silver,.028f);
                    }
                }
                Rail(root,new Vector3(-.58f,3.6f,1.7f),new Vector3(.58f,3.6f,1.7f),silver);
                for(int i=0;i<11;i++)
                {
                    float top=2.5f-(i+1)*2.5f/11,x=-.58f-(i+.5f)*.225f;
                    PlatformBox(root,"Walkable stair "+i,new Vector3(x,Mathf.Max(.045f,top-.045f),-2.305f),new Vector3(.23f,.09f,.9f),steel,true);
                    foreach(float z in new[]{-2.755f,-1.855f})Rail(root,new Vector3(x,top,z),new Vector3(x,top+1.1f,z),silver,.028f);
                }
                foreach(float z in new[]{-2.755f,-1.855f})Rail(root,new Vector3(-.58f,3.6f,z),new Vector3(-3.055f,1.1f,z),silver);
                PlatformBox(root,"Compute shelf",new Vector3(.12f,.925f,-2.305f),new Vector3(.9f,.05f,.9f),silver);
                PlatformBox(root,"Compute unit",new Vector3(.12f,1.035f,-2.305f),new Vector3(.38f,.17f,.28f),dark);
                var teamMaterial=SharedPrimitiveMaterial(team=="red"?red:blue);
                PlatformBox(root,"Team plate",new Vector3(.589f,2.0f,.65f),new Vector3(.015f,.30f,.42f),teamMaterial);
                var radar=new GameObject(team+" tripod camera").transform;radar.SetParent(root,false);radar.localPosition=new Vector3(0,2.5f,.15f);
                for(int leg=0;leg<3;leg++)
                {
                    float angle=leg*120*Mathf.Deg2Rad;
                    var foot=new Vector3(Mathf.Cos(angle)*.42f,.025f,Mathf.Sin(angle)*.42f);
                    Rail(radar,foot,new Vector3(0,1.02f,0),silver,.028f);
                    PlatformBox(radar,"Tripod foot",foot,new Vector3(.065f,.04f,.065f),dark);
                }
                Rail(radar,new Vector3(0,.9f,0),new Vector3(0,1.34f,0),dark,.045f);
                PlatformBox(radar,"Camera head mount",new Vector3(0,1.34f,0),new Vector3(.14f,.05f,.13f),dark);
                PlatformBox(radar,"Hikvision industrial camera schematic",new Vector3(.015f,1.425f,0),new Vector3(.14f,.15f,.10f),silver);
                Rail(radar,new Vector3(.09f,1.425f,0),new Vector3(.15f,1.425f,0),dark,.075f);
                var lens=Shape(PrimitiveType.Cylinder,radar,new Vector3(.154f,1.425f,0),new Vector3(.051f,.003f,.051f),SharedPrimitiveMaterial(new Color(.04f,.18f,.25f)));
                lens.transform.localRotation=Quaternion.Euler(0,0,90);Destroy(lens.GetComponent<Collider>());
                var eye=new GameObject("Lens view anchor").transform;eye.SetParent(radar,false);eye.localPosition=new Vector3(.165f,1.425f,0);
                Rail(radar,new Vector3(-.05f,1.4f,.03f),new Vector3(-.10f,.05f,.05f),dark,.012f);
                buildingNodes[team+"_radar_platform"]=root;buildingNodes[team+"_radar"]=radar;markerEyes[team+"_radar"]=eye;
                markers.Add(new BuildingAnchor {team=team,role="radar_platform",name=(team=="red"?"红方":"蓝方")+"雷达平台",x=root.position.x,y=root.position.z,z=2.5f,source="规则图4-18/4-19，平台中线与场地中线对齐；楼梯示意"});
                markers.Add(new BuildingAnchor {team=team,role="radar",name=(team=="red"?"红方":"蓝方")+"雷达相机",x=eye.position.x,y=eye.position.z,z=eye.position.y,source="用户指定三脚架+海康工业相机示意，平台面以上约1.5m"});
                BatchPlatformMeshes(root);
            }
            field.landmarks=markers.ToArray();
            Debug.Log("REPLAY_RADAR_PLATFORMS_OK decks=2 cameras=2 deck=3.4x1.16 height=2.5 sensorHeight=1.5");
        }
        void BatchPlatformMeshes(Transform root)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter=renderer.GetComponent<MeshFilter>();if(!filter)continue;
                var material=renderer.sharedMaterial;
                if(!groups.TryGetValue(material,out var pieces)){pieces=new List<CombineInstance>();groups[material]=pieces;}
                pieces.Add(new CombineInstance {mesh=filter.sharedMesh,transform=root.worldToLocalMatrix*renderer.transform.localToWorldMatrix});
                renderer.enabled=false;
            }
            foreach(var group in groups)
            {
                var mesh=new Mesh {name="Batched platform "+group.Key.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                mesh.CombineMeshes(group.Value.ToArray(),true,true);owned.Add(mesh);
                var obj=new GameObject("Platform shared material batch");obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
        }
    }
}
