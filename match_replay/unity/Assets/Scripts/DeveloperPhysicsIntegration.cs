using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        DeveloperPhysicsWorld gamePhysics;
        readonly Dictionary<string,VehicleRig> vehicleRigs=new Dictionary<string,VehicleRig>();
        bool PhysicsDriving=>gamePhysics!=null;
        bool physicsDebug;
        readonly RaycastHit[] physicalVehicleHits=new RaycastHit[128];
        static bool IsVehiclePhysicsParameter(string key)=>key=="vehiclePhysics"||key=="spinBodyRelative"||key=="activeSuspension"||key=="chassisVelocityGain"||key.StartsWith("physics",StringComparison.Ordinal)||key.StartsWith("follow",StringComparison.Ordinal)||key.StartsWith("wheel",StringComparison.Ordinal)||key.StartsWith("motor",StringComparison.Ordinal)||key.StartsWith("arm",StringComparison.Ordinal)&&key!="armorInterval"&&key!="armorMinVelocity"&&key!="armorRestitution"||key.StartsWith("suspension",StringComparison.Ordinal);
        static void RotatePhysicsState(VehiclePhysicsState s,float yaw)
        {if(s==null)return;var q=Quaternion.Euler(0,yaw,0);s.rotation=q*s.rotation;s.angularVelocity=q*s.angularVelocity;s.driveReference=q*s.driveReference;s.driftIntegral=q*s.driftIntegral;if(s.modules!=null)foreach(var m in s.modules){m.velocity=q*m.velocity;m.angularVelocity=q*m.angularVelocity;}}
        void DrawPhysicsDebug(DeveloperActor a)
        {
            if(!physicsDebug||!gameVehicles.TryGetValue(a.id,out var v)||v.physics==null)return;
            var p=v.physics;Panel(new Rect(18,155,540,220));Text(30,165,515,24,$"5刚体 · 接地{p.Grounded}/4 · 物理{gamePhysics.LastMilliseconds:0.00}ms · COM{p.Body.worldCenterOfMass:F2}",small);
            Text(30,190,515,24,$"朝向{a.yaw:0}° · 云台{a.gimbalYaw:0}° · v{a.velocity.magnitude:0.00}m/s · ω{a.angularVelocity:0}°/s",small);
            for(int i=0;i<4;i++){var w=p.Wheels[i];Text(30,215+i*27,515,25,$"{w.Config.label} {(w.Grounded?w.Sticking?"抓地":"滑动":"离地")} · Ω {w.TargetOmega:0.0}/{w.Omega:0.0} · N {w.Load:0.0} · 滑移{w.Slip:0.000}m/s",small);}
            Text(30,325,515,40,$"{p.MotionState}{(p.RampAssistActive?" / 坡道电容辅助":"")} · 稳态坡速估计{p.SlopeSpeedLimit:0.00}m/s · 耗电{p.MotorPower:0.0}W / 制动耗散{p.BrakePower:0.0}W",small);
        }
        void ConfigureGamePhysics()
        {
            if(game==null||game.rules.vehiclePhysics==PhysicsDriving)return;
            game.running=false;gameAccumulator=0;
            if(game.rules.vehiclePhysics)
            {
                try
                {
                    gamePhysics=new DeveloperPhysicsWorld(fieldRoot,ReadMesh);
                    foreach(var v in gameVehicles.Values)BuildVehiclePhysics(v);
                    game.Log("车辆物理已启用 · 120Hz · 底盘 + 四独立轮组");
                }
                catch(Exception ex){gamePhysics?.Dispose();gamePhysics=null;game.rules.vehiclePhysics=false;foreach(var v in gameVehicles.Values)RestoreLegacyVehicle(v);game.Log("物理初始化失败，已恢复旧版驾驶："+ex.Message);Debug.LogException(ex);}
            }
            else
            {
                gamePhysics.Dispose();gamePhysics=null;
                foreach(var v in gameVehicles.Values)RestoreLegacyVehicle(v);
                game.Log("已切换旧版驾驶 · 比赛保持暂停");
            }
            InvalidateLidar();gameTrackers.Clear();observations.Clear();previousPlates.Clear();
        }
        void RestoreLegacyVehicle(GameVehicle v)
        {
            v.physics=null;var children=new List<GameObject>();foreach(Transform child in v.unit.root.transform)if(child.name.StartsWith("Independent ",StringComparison.Ordinal)||child.name=="frame"||child.name.StartsWith("damper-",StringComparison.Ordinal))children.Add(child.gameObject);
            foreach(var child in children){child.SetActive(false);Destroy(child);}
            foreach(var group in v.unit.robot.groups)group.gameObject.SetActive(true);
            var lod=v.unit.root.GetComponent<LODGroup>();if(v.legacyLods!=null)lod.SetLODs(v.legacyLods);v.legacyLods=null;
            v.state.physics=null;SnapGameGround(v);
        }
        void BuildVehiclePhysics(GameVehicle v)
        {
            if(gamePhysics==null||v.physics!=null)return;
            if(!vehicleRigs.TryGetValue(v.state.model,out var rig))vehicleRigs[v.state.model]=rig=VehicleRig.Load(folder,v.state.model);
            v.legacyLods=v.unit.root.GetComponent<LODGroup>().GetLODs();
            v.physics=new DeveloperVehiclePhysics(gamePhysics,v.state,rig,v.unit.root.transform,v.unit.robot.model,v.unit.robot.groups,ReadMesh,RobotMaterial("cad",Color.white));
            foreach(var node in v.unit.robot.groups)if(!v.unit.robot.model.groups[Array.IndexOf(v.unit.robot.groups,node)].turret)node.gameObject.SetActive(false);
            var lod=v.unit.root.GetComponent<LODGroup>();var levels=lod.GetLODs();var renderers=v.unit.root.GetComponentsInChildren<Renderer>();
            for(int i=0;i<levels.Length;i++)
            {
                var list=new List<Renderer>();foreach(var r in levels[i].renderers)if(r&&r.gameObject.activeInHierarchy)list.Add(r);
                foreach(var r in renderers)if(r.gameObject.name=="Mechanical LOD "+i)list.Add(r);levels[i].renderers=list.ToArray();
            }
            lod.SetLODs(levels);v.unit.renderers=v.unit.root.GetComponentsInChildren<Renderer>(true);
            if(driveShapes==null)driveShapes=JsonUtility.FromJson<DriveShapeCatalog>(File.ReadAllText(Path.Combine(folder,"assets/robots/drive-collision/catalog.json")));
            foreach(var entry in driveShapes.groups)foreach(int group in rig.legacyTurretGroups)if(entry.key==rig.id+"-"+group)foreach(var mesh in entry.meshes)v.physics.AddCloudCollision(group,ReadMesh(mesh));
            v.physics.EnsureSpawnClearance();PoseGameVehicle(v);
        }
        void PreparePhysicsVehicle(GameVehicle v,DeveloperInput input)
        {
            var a=v.state;v.physics.Frozen=v.recovering||!a.Alive;v.physics.SyncEdits();
            Vector3 wish=DeveloperChassisControl.Wish(input,a.yaw,game.rules.spinBodyRelative),wanted=a.position+a.velocity*DeveloperWorld.Step;
            v.physicsWish=wish;float approach=PassagePose.Amount(field.tunnels,wanted);
            if(wish.sqrMagnitude>.01f)approach=Mathf.Max(approach,PassagePose.Amount(field.tunnels,wanted+wish.normalized*(v.unit.robot.footExtents.magnitude+.2f)));
            float goal=Mathf.Lerp(v.unit.robot.model.defaultFold,1,approach),fold=Mathf.MoveTowards(a.fold,goal,3*DeveloperWorld.Step);
            if(fold>=a.fold||DrivePoseClear(v,a.position,fold,a.yaw))a.fold=fold;
            if(wish.sqrMagnitude>.01f&&PhysicsHighStep(v,wish)) {input.x=input.z=0;a.blocked=true;v.lastBlockReason="高于压缩高度0.6倍的陡坎";}
            v.physics.Submit(input);
        }
        bool PhysicsHighStep(GameVehicle v,Vector3 wish)
        {
            float limit=StepLimit(v);var forward=wish.normalized;
            foreach(var w in v.physics.Wheels)
            {
                Vector3 from=w.Center;if(!DevRaycast(from+Vector3.up*.02f,Vector3.down,out var start,w.Config.radius+.08f,1<<8))continue;
                Vector3 to=from+forward*(w.Config.radius+.035f);float top=Mathf.Max(from.y,start.point.y)+limit+.03f;
                if(!DevRaycast(new Vector3(to.x,top,to.z),Vector3.down,out var end,limit+.4f,1<<8))continue;
                if(end.point.y-start.point.y<=limit+.002f)continue;
                if(start.normal.y>.999f&&end.normal.y>.999f)return true;
                float previous=start.point.y;
                for(int j=1;j<=5;j++)
                {Vector3 p=Vector3.Lerp(start.point,end.point,j/5f);p.y=top;if(!DevRaycast(p,Vector3.down,out var hit,limit+.4f,1<<8)||hit.point.y-previous>limit/5+.015f)return true;previous=hit.point.y;}
            }
            return false;
        }
        void RenderPhysicsFrame(float alpha)
        {
            foreach(var v in gameVehicles.Values)
            {
                if(v.physics==null)continue;v.physics.ApplyVisuals(alpha);
                float yaw=Mathf.LerpAngle(v.previousAimYaw,v.state.gimbalYaw,alpha),pitch=Mathf.Lerp(v.previousAimPitch,v.state.pitch,alpha),fold=Mathf.Lerp(v.previousFold,v.state.fold,alpha);
                var root=v.unit.root.transform;Vector3 local=root.InverseTransformDirection(BallisticMath.Direction(yaw,pitch));float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,elevation=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
                var robot=v.unit.robot;for(int g=0;g<robot.groups.Length;g++)if(robot.model.groups[g].turret)
                {var matrix=BallisticMath.Group(robot.model,g,fold,heading,GameFolded(v)?0:Mathf.Clamp(elevation,-30,30),true);robot.groups[g].SetLocalPositionAndRotation(matrix.GetColumn(3),matrix.rotation);}
            }
        }
        bool PhysicalVehicleSweep(Vector3 start,Vector3 end,float radius,string ignored,string alsoIgnored,out RaycastHit nearest)
        {
            nearest=default;if(gamePhysics==null)return false;var delta=end-start;if(delta.sqrMagnitude<1e-12f)return false;
            int count=gamePhysics.Physics.SphereCast(start,radius,delta.normalized,physicalVehicleHits,delta.magnitude,(1<<15)|(1<<16),QueryTriggerInteraction.Ignore);float distance=float.MaxValue;bool found=false;
            for(int i=0;i<count;i++){var h=physicalVehicleHits[i];if(!gamePhysics.TryOwner(h.collider,out var owner)||owner==ignored||owner==alsoIgnored||h.distance>=distance)continue;distance=h.distance;nearest=h;found=true;}
            return found;
        }
        bool DevRaycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction trigger=QueryTriggerInteraction.Ignore)
        =>gamePhysics!=null?gamePhysics.Physics.Raycast(origin,direction,out hit,distance,mask,trigger):Physics.Raycast(origin,direction,out hit,distance,mask,trigger);
        bool DevSphereCast(Vector3 origin,float radius,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction trigger)
        =>gamePhysics!=null?gamePhysics.Physics.SphereCast(origin,radius,direction,out hit,distance,mask,trigger):Physics.SphereCast(origin,radius,direction,out hit,distance,mask,trigger);
        int DevBoxCast(Vector3 center,Vector3 half,Vector3 direction,RaycastHit[] results,Quaternion rotation,float distance,int mask,QueryTriggerInteraction trigger)
        =>gamePhysics!=null?gamePhysics.Physics.BoxCast(center,half,direction,results,rotation,distance,mask,trigger):Physics.BoxCastNonAlloc(center,half,direction,results,rotation,distance,mask,trigger);
        int DevOverlapBox(Vector3 center,Vector3 half,Collider[] results,Quaternion rotation,int mask,QueryTriggerInteraction trigger)
        =>gamePhysics!=null?gamePhysics.Physics.OverlapBox(center,half,results,rotation,mask,trigger):Physics.OverlapBoxNonAlloc(center,half,results,rotation,mask,trigger);
    }
}
