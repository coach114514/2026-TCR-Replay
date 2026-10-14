using System;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public static class ReplayRobotChecks
    {
        public static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("车型/地面验证失败："+message);}
        public static void Contracts()
        {
            string folder=Path.Combine(Application.streamingAssetsPath,"replay");
            var catalog=JsonUtility.FromJson<RobotCatalog>(File.ReadAllText(Path.Combine(folder,"robots_catalog.json")));
            Require(catalog.schema==1 && catalog.bindings.Length==2,"必须登记两个角色映射");
            foreach(var binding in catalog.bindings)
            {
                var model=JsonUtility.FromJson<RobotModel>(File.ReadAllText(Path.Combine(folder,binding.model)));
                Require((binding.role=="infantry3" && model.id=="sweeper") || (binding.role=="infantry4" && model.id=="jiageng"),"角色车型错绑");
                Require(model.schema==1 && model.groups.Length>0 && model.defaultFold>=0 && model.defaultFold<=1 && model.eyePoint.Length==3 && model.labelPoint.Length==3,"车型头部无效");
                Require(model.eyeGroup>=0 && model.eyeGroup<model.groups.Length,"视点分组无效");
                Require(model.armorPanels!=null && model.armorPanels.Length==4,"装甲身份点缺失或重复");
                foreach(var armor in model.armorPanels)
                    Require(armor.position.Length==3 && armor.rotation.Length==4 && PassagePose.Vector(armor.position).magnitude<1,"装甲身份变换无效");
                Require(model.gunGroup>=0 && model.gunGroup<model.groups.Length && model.groups[model.gunGroup].pitch,"枪口分组缺失");
                Require(model.yawPivot.Length==3 && model.pitchPivot.Length==3 && model.muzzlePoint.Length==3 && model.gunForward.Length==3 && model.groundOffsets.Length>=2,"瞄准挂点缺失");
                Require(Mathf.Abs(PassagePose.Vector(model.gunForward).magnitude-1)<.001f,"枪口方向不是单位向量");
                var triangles=new int[3];
                foreach(var group in model.groups)
                {
                    Require(group.pivot.Length==3 && group.foldTranslation.Length==3 && group.foldRotation.Length==4 && group.meshes.Length==3,"关节结构无效");
                    var q=PassagePose.Rotation(group.foldRotation);
                    Require(Mathf.Abs(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w-1)<.001f,"旋转非单位四元数");
                    for(int level=0;level<3;level++)
                    {
                        using(var reader=new BinaryReader(File.OpenRead(Path.Combine(folder,group.meshes[level]))))
                        {
                            string magic=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));int vertices=reader.ReadInt32(),indices=reader.ReadInt32();
                            Require(magic=="TCRMESH2" && vertices>0 && indices>0 && indices%3==0 && reader.BaseStream.Length==16L+vertices*16L+indices*4L,"机器人网格损坏");
                            for(int i=0;i<vertices*3;i++) Require(float.IsFinite(reader.ReadSingle()),"非有限模型坐标");
                            for(int i=0;i<indices;i++) {int index=reader.ReadInt32();Require(index>=0 && index<vertices,"模型索引越界");}
                            triangles[level]+=indices/3;
                        }
                    }
                }
                Require(triangles[0]<=105000 && triangles[1]<=30000 && triangles[2]<=6500,"显示预算超标");
                Require(triangles[0]>triangles[1] && triangles[1]>triangles[2],"LOD 没有降级");
                Debug.Log($"REPLAY_ROBOT_ASSET_OK role={binding.role} model={model.id} groups={model.groups.Length} triangles={triangles[0]}/{triangles[1]}/{triangles[2]}");
            }
            var fields=JsonUtility.FromJson<FieldCatalog>(File.ReadAllText(Path.Combine(folder,"fields_catalog.json")));
            foreach(var entry in fields.fields)
            {
                var field=JsonUtility.FromJson<FieldConfig>(File.ReadAllText(Path.Combine(folder,entry.file)));
                Require(!string.IsNullOrEmpty(field.collisionMesh) && File.Exists(Path.Combine(folder,field.collisionMesh)),"独立地面资产缺失");
                if(field.tunnels==null) continue;
                foreach(var p in field.tunnels)
                {
                    Require(p.approachMargin>p.holdMargin && p.holdMargin>=0 && p.halfX>0 && p.halfY>0 && p.clearance>0,"洞口参数无效");
                    var center=new Vector3(p.x,0,p.y);
                    Require(PassagePose.Amount(field.tunnels,center)==1,"洞内没有完全收缩");
                    Require(PassagePose.Amount(new [] {p},center+Vector3.right*(p.halfX+p.approachMargin+.1f))==0,"远离洞口未展开");
                    float half=PassagePose.Amount(new [] {p},center+Vector3.right*(p.halfX+(p.holdMargin+p.approachMargin)/2));
                    Require(Mathf.Abs(half-.5f)<.0001f,"过渡曲线不连续");
                    var counterpart=Array.Find(field.tunnels,t=>t.team!=p.team && Mathf.Abs(t.x+p.x-field.width)<.01f && Mathf.Abs(t.y+p.y-field.depth)<.01f);
                    Require(counterpart!=null,"洞口红蓝中心对称错位");
                }
            }
            var rig=new ReplayCameraRig();rig.Attach(Vector3.zero,Quaternion.identity,Vector3.up,true,Vector3.zero);
            rig.Move(Vector3.down,1);rig.ConstrainPosition(Vector3.up*.08f);rig.Track(true,Vector3.right);
            Require(Vector3.Distance(rig.Position,new Vector3(1,.08f,0))<.0001f,"下一帧跟随撤销地面约束");
            Debug.Log("REPLAY_ROBOT_CONTRACT_OK identity=3/sweeper,4/jiageng lod=3 pose=spatial camera_floor=persistent");
        }
    }
}
