using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public static class DeveloperPhysicsChecks
    {
        public static void Assets(string folder)
        {
            foreach(string id in new[]{"sweeper","jiageng"})
            {
                var rig=VehicleRig.Load(folder,id);var nodes=new HashSet<string>();
                foreach(var node in rig.nodes)
                {
                    Require(node!=null&&nodes.Add(node.id)&&node.module>=-1&&node.module<4&&node.meshes?.Length==3&&node.collisionMeshes!=null,"机械节点/LOD无效："+id);
                    foreach(var path in node.meshes)MeshFile(folder,path,false);
                    foreach(var path in node.collisionMeshes)MeshFile(folder,path,true);
                }
                Require(nodes.Contains("frame"),"缺少固定框架");
                for(int i=0;i<4;i++)Require(nodes.Contains("tire-"+i)&&nodes.Contains("carrier-"+i),"缺少独立轮组");
                // All three chassis degrees of freedom must be controllable.
                float[,] gram=new float[3,3];
                foreach(var w in rig.wheels)
                {
                    Require(Mathf.Abs(Vector3.Dot(PassagePose.Vector(w.axle),PassagePose.Vector(w.rollingDirection)))<.001f,"轮轴/滚动轴不垂直");
                    var q=Quaternion.AngleAxis(w.foldAngle*rig.defaultFold,w.Axis);var center=w.Pivot+q*(w.Center-w.Pivot);
                    var row=new[]{DeveloperChassisControl.WheelSpeed(w,center,q,Vector3.right,0),DeveloperChassisControl.WheelSpeed(w,center,q,Vector3.forward,0),DeveloperChassisControl.WheelSpeed(w,center,q,Vector3.zero,1)};
                    for(int a=0;a<3;a++)for(int b=0;b<3;b++)gram[a,b]+=row[a]*row[b];
                }
                float determinant=gram[0,0]*(gram[1,1]*gram[2,2]-gram[1,2]*gram[2,1])-gram[0,1]*(gram[1,0]*gram[2,2]-gram[1,2]*gram[2,0])+gram[0,2]*(gram[1,0]*gram[2,1]-gram[1,1]*gram[2,0]);
                Require(float.IsFinite(determinant)&&determinant>1,"四轮驱动矩阵退化："+id);
            }
            MeshFile(folder,"assets/developer/field/final_road_contact.tcrmesh",false);
            Debug.Log("DEVELOPER_PHYSICS_ASSETS_OK");
        }
        static void MeshFile(string folder,string path,bool convex)
        {
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(folder,path))))
            {
                string magic=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));int vertices=reader.ReadInt32(),indices=reader.ReadInt32();
                Require((magic=="TCRMESH1"||magic=="TCRMESH2")&&vertices>=4&&indices>=12&&indices%3==0&&(!convex||indices/3<255)&&reader.BaseStream.Length==16L+vertices*(magic=="TCRMESH2"?16L:12L)+indices*4L,"机械网格结构无效："+path);
                for(int i=0;i<vertices*3;i++)Require(float.IsFinite(reader.ReadSingle()),"机械网格坐标非有限："+path);
                // Painted meshes append RGBA values after triangle indices.
                for(int i=0;i<indices;i++){int index=reader.ReadInt32();Require(index>=0&&index<vertices,"机械网格索引无效："+path);}
            }
        }
        static void Require(bool valid,string error){if(!valid)throw new InvalidDataException(error);}
    }
}
