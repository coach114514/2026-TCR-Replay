using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace TcrReplay
{
    public static class PassagePose
    {
        // A spatial curve is deterministic at every playhead, including backwards
        // seeks and missing positions. It never depends on an Enter/Exit callback.
        public static float Amount(PassageRegion[] passages, Vector3 position)
        {
            float result=0;
            if(passages==null) return result;
            foreach(var passage in passages)
            {
                float distance=Mathf.Max(Mathf.Abs(position.x-passage.x)-passage.halfX,
                    Mathf.Abs(position.z-passage.y)-passage.halfY);
                float amount=1-Mathf.InverseLerp(passage.holdMargin,passage.approachMargin,distance);
                result=Mathf.Max(result,Mathf.SmoothStep(0,1,amount));
            }
            return result;
        }
        public static Vector3 Vector(float[] values) => new Vector3(values[0],values[1],values[2]);
        public static Quaternion Rotation(float[] values) => new Quaternion(values[0],values[1],values[2],values[3]);
        public static Vector3 Point(RobotGroup group, Vector3 point, float amount)
        {
            var rotation=Quaternion.Slerp(Quaternion.identity,Rotation(group.foldRotation),amount);
            var pivot=Vector(group.pivot);
            return pivot+rotation*(point-pivot)+Vector(group.foldTranslation)*amount;
        }
    }

    public partial class ReplayViewer
    {
        class RobotInstance
        {
            public RobotModel model;
            public Transform[] groups;
            public Bounds bounds;public Vector3 footExtents;
            public float fold, proximity, preview=-1, elevation, heading;
            public Bounds[] groupBounds;
            public Transform armorNode;
            public BoxCollider selection;
            public Renderer[] renderers;
            public Vector3 Eye => groups[model.eyeGroup].TransformPoint(PassagePose.Vector(model.eyePoint));
        }
        RobotCatalog robots;
        readonly Dictionary<string,RobotModel> robotModels=new Dictionary<string,RobotModel>();
        readonly Dictionary<string,Material> robotMaterials=new Dictionary<string,Material>();
        MaterialPropertyBlock unitColorBlock;

        void LoadRobotCatalog()
        {
            string path=Path.Combine(folder,"robots_catalog.json");
            if(!File.Exists(path)) return;
            robots=JsonUtility.FromJson<RobotCatalog>(File.ReadAllText(path));
            if(robots.schema!=1 || robots.bindings==null) throw new InvalidDataException("车型目录无效");
            foreach(var binding in robots.bindings)
            {
                var model=JsonUtility.FromJson<RobotModel>(File.ReadAllText(Path.Combine(folder,binding.model)));
                if(model.schema!=1 || model.groups==null || model.groups.Length==0 || model.eyeGroup<0 || model.eyeGroup>=model.groups.Length)
                    throw new InvalidDataException("车型结构无效："+binding.role);
                robotModels[binding.role]=model;
            }
        }
        Material RobotMaterial(string name,Color color,bool unlit=false)
        {
            if(robotMaterials.TryGetValue(name,out var cached)) return cached;
            Material material;
            if(name=="cad")
            {
                material=new Material(Resources.Load<Material>("ReplayRobot"));material.color=Color.white;owned.Add(material);
            }
            else material=Material(color,unlit);
            robotMaterials[name]=material;return material;
        }
        bool BuildRobot(Unit unit)
        {
            if(!robotModels.TryGetValue(unit.data.role,out var model)) return false;
            var instance=new RobotInstance {model=model,groups=new Transform[model.groups.Length]};unit.robot=instance;
            var renderers=new List<Renderer>[] {new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};
            bool first=true;
            for(int groupIndex=0;groupIndex<model.groups.Length;groupIndex++)
            {
                var group=model.groups[groupIndex];
                var node=new GameObject(model.id+" "+group.name).transform;node.SetParent(unit.root.transform,false);instance.groups[groupIndex]=node;
                if(instance.groupBounds==null)instance.groupBounds=new Bounds[model.groups.Length];
                for(int level=0;level<3;level++)
                {
                    var mesh=ReadMesh(group.meshes[level]);var obj=new GameObject("LOD "+level);obj.transform.SetParent(node,false);
                    obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=RobotMaterial("cad",Color.white);renderers[level].Add(renderer);
                    if(level==0) {instance.groupBounds[groupIndex]=mesh.bounds;if(first) {instance.bounds=mesh.bounds;first=false;} else instance.bounds.Encapsulate(mesh.bounds);}
                }
            }
            for(int g=0;g<model.groups.Length;g++)if(!model.groups[g].turret)
            foreach(float amount in new [] {0f,.5f,1f})
            for(int corner=0;corner<8;corner++)
            {
                var b=instance.groupBounds[g];var p=BallisticMath.Fold(model.groups[g],amount).MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                instance.footExtents=Vector3.Max(instance.footExtents,new Vector3(Mathf.Abs(p.x),0,Mathf.Abs(p.z)));
            }
            var identity=BuildRobotIdentity(unit,instance.bounds);
            var allRenderers=new List<Renderer>();foreach(var list in renderers) allRenderers.AddRange(list);
            allRenderers.AddRange(identity);instance.renderers=allRenderers.ToArray();
            foreach(var list in renderers) list.AddRange(identity);
            var lod=unit.root.AddComponent<LODGroup>();lod.size=Mathf.Max(.5f,instance.bounds.size.magnitude);lod.localReferencePoint=instance.bounds.center;
            // Four cars reuse the same meshes; only eleven/four motion nodes per
            // car are updated. No individual CAD screw has a runtime GameObject.
            lod.SetLODs(new [] {new LOD(.16f,renderers[0].ToArray()),new LOD(.045f,renderers[1].ToArray()),new LOD(.001f,renderers[2].ToArray())});
            unit.root.layer=9;instance.selection=unit.root.AddComponent<BoxCollider>();
            instance.selection.center=instance.bounds.center;instance.selection.size=instance.bounds.size+Vector3.one*.025f;
            return true;
        }
        Renderer[] BuildRobotIdentity(Unit unit,Bounds bounds)
        {
            string materialKey=unit.data.team=="red"?"armor-red":"armor-blue";
            if(!robotMaterials.TryGetValue(materialKey,out var material))
            {
                material=new Material(Resources.Load<Material>("ReplayArmor"));owned.Add(material);
                material.SetColor("_TeamColor",unit.data.team=="red"?new Color(1,.025f,.055f):new Color(.025f,.30f,1));
                robotMaterials[materialKey]=material;
            }
            string key="robot-armor/"+unit.robot.model.id+"/"+unit.data.role;
            if(!meshCache.TryGetValue(key,out var mesh))
            {
                var surface=ReadMesh("assets/robots/armor/am02-surface.tcrmesh");
                var digit=ReadMesh("assets/robots/armor/number-"+(unit.data.role=="infantry3"?"3":"4")+".tcrmesh");
                var combines=new List<CombineInstance>();
                foreach(var armor in unit.robot.model.armorPanels)
                {
                    var transform=Matrix4x4.TRS(PassagePose.Vector(armor.position),PassagePose.Rotation(armor.rotation),Vector3.one);
                    combines.Add(new CombineInstance {mesh=surface,transform=transform});
                    combines.Add(new CombineInstance {mesh=digit,transform=transform});
                }
                mesh=new Mesh {name=key};mesh.CombineMeshes(combines.ToArray(),true,true);mesh.RecalculateBounds();owned.Add(mesh);meshCache[key]=mesh;
            }
            var obj=new GameObject("CAD armor faces and printed number");obj.transform.SetParent(unit.root.transform,false);unit.robot.armorNode=obj.transform;
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            return new [] {renderer};
        }
        void UpdateRobotPose(Unit unit,Vector3 position)
        {
            var robot=unit.robot;if(robot==null) return;
            robot.proximity=PassagePose.Amount(field.tunnels,position);
            robot.fold=robot.preview>=0?robot.preview:Mathf.Lerp(robot.model.defaultFold,1,robot.proximity);
            bool known=BallisticMath.Heading(unit.data,playhead,ballisticConfig,out float heading);
            robot.heading=heading;robot.elevation=headings?CurrentElevation(unit,playhead):0;
            bool first=true;
            for(int i=0;i<robot.groups.Length;i++)
            {
                var matrix=BallisticMath.Group(robot.model,i,robot.fold,heading,robot.elevation,headings&&known);
                robot.groups[i].localPosition=matrix.GetColumn(3);
                robot.groups[i].localRotation=matrix.rotation;
                var b=robot.groupBounds[i];
                for(int corner=0;corner<8;corner++)
                {
                    var point=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                    if(first) {robot.bounds=new Bounds(point,Vector3.zero);first=false;} else robot.bounds.Encapsulate(point);
                }
            }
            robot.selection.center=robot.bounds.center;robot.selection.size=robot.bounds.size+Vector3.one*.025f;
        }
        float VehicleGroundHeight(Unit unit,Vector3 position)
        {
            float height=GroundHeight(position.x,position.z);
            if(unit.robot==null || unit.data.role=="air") return height;
            // Source vehicle origins were normalized to wheel contact. Sampling
            // the footprint also keeps wheels above slopes and step edges, without
            // editing historical XY or inventing a suspension telemetry channel.
            var extent=unit.robot.footExtents;
            foreach(float x in new [] {-extent.x*.8f,extent.x*.8f})
            foreach(float z in new [] {-extent.z*.8f,extent.z*.8f})
                height=Mathf.Max(height,GroundHeight(position.x+x,position.z+z));
            return height+BallisticMath.Support(unit.robot.model,unit.robot.fold)+.003f;
        }
        Vector3 RobotLabel(Unit unit)
        {
            if(unit.robot==null) return unit.root.transform.position+Vector3.up*.85f;
            return unit.root.transform.position+Vector3.up*(unit.robot.bounds.max.y+.12f);
        }
    }
}
