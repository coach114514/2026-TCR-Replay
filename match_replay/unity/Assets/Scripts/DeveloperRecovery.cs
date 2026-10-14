using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        BoxCollider driveProbeBox;readonly Collider[] driveOverlap=new Collider[96];
        [Serializable] class DriveShapeGroup {public string key;public string[] meshes;}
        [Serializable] class DriveShapeCatalog {public int schema;public DriveShapeGroup[] groups;}
        DriveShapeCatalog driveShapes;readonly Dictionary<string,MeshCollider[]> driveShapeProbes=new Dictionary<string,MeshCollider[]>();
        int recoveryCount;string recoveryStatus="";
        // A finite wall skin is standard for kinematic character/vehicle motion;
        // keep it separate from floor/roof clearance and strict recovery pads.
        const float DriveWallSkin=.03f;
        bool DriveContactWithinSkin(Vector3 push,float depth,float margin=0)=>depth<=.002f||(margin<=0&&Mathf.Abs(push.y)<.2f&&depth<=DriveWallSkin);
        BoxCollider DriveProbeBox()
        {
            if(!driveProbeBox){var node=new GameObject("Drive clearance probe");node.transform.SetParent(gameRoot.transform,false);node.layer=2;driveProbeBox=node.AddComponent<BoxCollider>();driveProbeBox.isTrigger=true;}
            return driveProbeBox;
        }
        Matrix4x4 DriveGroupPose(GameVehicle v,int g,float fold,Quaternion body)
        {
            Vector3 local=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);
            float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg,-30,30);
            return BallisticMath.Group(v.unit.robot.model,g,fold,heading,fold>v.unit.robot.model.defaultFold+.01f?0:pitch,true);
        }
        bool DriveShapeContact(GameVehicle v,int group,Vector3 origin,float fold,Quaternion body,Collider obstacle,out Vector3 push,out float depth,bool floorOnly=false)
        {
            string key=v.unit.robot.model.id+"-"+group;
            if(!driveShapeProbes.TryGetValue(key,out var probes))
            {
                if(driveShapes==null)driveShapes=JsonUtility.FromJson<DriveShapeCatalog>(File.ReadAllText(Path.Combine(folder,"assets/robots/drive-collision/catalog.json")));
                var entry=Array.Find(driveShapes.groups,g=>g.key==key);if(driveShapes.schema!=1||entry==null||entry.meshes.Length==0)throw new InvalidDataException("车型碰撞轮廓缺失 "+key);
                probes=new MeshCollider[entry.meshes.Length];for(int i=0;i<probes.Length;i++)
                {
                    var node=new GameObject("Drive shape "+key+" part "+i);node.transform.SetParent(gameRoot.transform,false);node.layer=2;
                    var probe=node.AddComponent<MeshCollider>();probe.convex=true;probe.sharedMesh=ReadMesh(entry.meshes[i]);probe.isTrigger=true;probes[i]=probe;
                }
                driveShapeProbes[key]=probes;
            }
            var matrix=DriveGroupPose(v,group,fold,body);
            push=Vector3.zero;depth=0;bool found=false,hard=false;
            foreach(var probe in probes)
            {
                if(!Physics.ComputePenetration(probe,origin+body*(Vector3)matrix.GetColumn(3),body*matrix.rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out var direction,out float distance)||distance<=.002f||(floorOnly&&direction.y<.65f))continue;
                bool bad=direction.y<.65f||distance>.025f;
                if(!found||bad&&!hard||bad==hard&&distance>depth){push=direction;depth=distance;hard=bad;found=true;}
            }
            return found;
        }
        bool DriveFloorClearance(GameVehicle v,ref Vector3 origin,float fold,float yaw)
        {
            float originalY=origin.y,limit=StepLimit(v);var robot=v.unit.robot;
            for(int pass=0;pass<3;pass++)
            {
                if(!GroundSupport(v,origin,yaw,out _,out var tilt))return false;Quaternion body=tilt*Quaternion.Euler(0,yaw,0);
                Vector3 local=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,lift=0;
                for(int g=0;g<robot.groups.Length;g++)
                {
                    var bounds=FoldedDriveBounds(robot,g,fold);if(!robot.model.groups[g].turret){var min=bounds.min;min.y+=.05f;bounds.SetMinMax(min,bounds.max);}
                    Vector3 half=Vector3.Max(Vector3.one*.006f,bounds.extents-Vector3.one*.008f);var transform=DriveGroupPose(v,g,fold,body)*BallisticMath.Fold(robot.model.groups[g],fold).inverse;
                    Vector3 center=origin+body*transform.MultiplyPoint3x4(bounds.center);Quaternion rotation=body*transform.rotation;int n=DevOverlapBox(center,half,driveOverlap,rotation,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);if(n==driveOverlap.Length)return false;
                    if(n==0)continue;var box=DriveProbeBox();box.size=half*2;
                    for(int i=0;i<n;i++){var o=driveOverlap[i];if(Physics.ComputePenetration(box,center,rotation,o,o.transform.position,o.transform.rotation,out _,out _)&&DriveShapeContact(v,g,origin,fold,body,o,out var push,out float depth,true)&&depth>.002f)
                        {if(push.y<.65f)continue;if(depth>limit)return DriveReject("floor depth="+depth+" g="+g+" push="+push);lift=Mathf.Max(lift,depth/push.y+.003f);}}
                }
                if(lift==0)return true;if(origin.y+lift-originalY>limit)return DriveReject("floor cumulative lift="+(origin.y+lift-originalY));origin.y+=lift;
            }
            return DriveReject("floor unresolved after three passes");
        }
        bool TryResolveDriveOverlap(GameVehicle v,out Vector3 corrected)
        {
            corrected=v.state.position;Vector3 start=corrected;float limit=StepLimit(v)*.5f;var robot=v.unit.robot;
            for(int pass=0;pass<8;pass++)
            {
                GroundSupport(v,corrected,v.state.yaw,out _,out var tilt);Quaternion body=tilt*Quaternion.Euler(0,v.state.yaw,0);
                Vector3 local=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;bool touched=false;
                for(int g=0;g<robot.groups.Length;g++)
                {
                    var bounds=FoldedDriveBounds(robot,g,v.state.fold);if(!robot.model.groups[g].turret){var min=bounds.min;min.y+=.05f;bounds.SetMinMax(min,bounds.max);}
                    Vector3 half=Vector3.Max(Vector3.one*.006f,bounds.extents-Vector3.one*.008f);var transform=DriveGroupPose(v,g,v.state.fold,body)*BallisticMath.Fold(robot.model.groups[g],v.state.fold).inverse;
                    Vector3 center=corrected+body*transform.MultiplyPoint3x4(bounds.center);Quaternion rotation=body*transform.rotation;int n=DevOverlapBox(center,half,driveOverlap,rotation,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);if(n==driveOverlap.Length)return false;
                    if(n==0)continue;var box=DriveProbeBox();box.size=half*2;
                    for(int i=0;i<n;i++){var o=driveOverlap[i];if(!Physics.ComputePenetration(box,center,rotation,o,o.transform.position,o.transform.rotation,out _,out _)||!DriveShapeContact(v,g,corrected,v.state.fold,body,o,out var push,out float depth)||DriveContactWithinSkin(push,depth))continue;
                        if(depth>limit)return false;Vector3 delta;
                        if(push.y>=.65f)delta=Vector3.up*(depth/push.y+.003f);
                        else {push.y=0;if(push.sqrMagnitude<.05f)return false;delta=push.normalized*(depth+.003f);}
                        corrected+=delta;
                        if(!GroundSupport(v,corrected,v.state.yaw,out float support,out _))return false;
                        corrected.y=Mathf.Max(corrected.y,support+BallisticMath.Support(robot.model,v.state.fold)+.004f);
                        touched=true;if(Vector3.Distance(start,corrected)>limit)return false;break;}
                    if(touched)break;
                }
                if(!touched)break;
            }
            if((corrected-start).sqrMagnitude<1e-8f||!DrivePoseClear(v,corrected,v.state.fold,v.state.yaw))return false;
            if(!GroundSupport(v,corrected,v.state.yaw,out float height,out _)||corrected.y<height+BallisticMath.Support(robot.model,v.state.fold)-.002f)return false;
            return true;
        }
        bool DrivePoseClear(GameVehicle v,Vector3 origin,float fold,float yaw,bool vehicles=true,float margin=0)
        {
            var robot=v.unit.robot;if(!GroundSupport(v,origin,yaw,out _,out var tilt))return false;Quaternion body=tilt*Quaternion.Euler(0,yaw,0);
            Vector3 local=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
            for(int g=0;g<robot.groups.Length;g++)
            {
                var bounds=FoldedDriveBounds(robot,g,fold);if(!robot.model.groups[g].turret){var min=bounds.min;min.y+=.05f;bounds.SetMinMax(min,bounds.max);}
                Vector3 half=Vector3.Max(Vector3.one*.006f,bounds.extents+Vector3.one*(margin-.008f));
                var transform=DriveGroupPose(v,g,fold,body)*BallisticMath.Fold(robot.model.groups[g],fold).inverse;
                Vector3 center=origin+body*transform.MultiplyPoint3x4(bounds.center);Quaternion rotation=body*transform.rotation;
                int count=DevOverlapBox(center,half,driveOverlap,rotation,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);if(count==driveOverlap.Length)return false;
                if(count==0)continue;var box=DriveProbeBox();box.size=half*2;
                for(int i=0;i<count;i++)
                {
                    var obstacle=driveOverlap[i];if(Physics.ComputePenetration(box,center,rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out _)&&DriveShapeContact(v,g,origin,fold,body,obstacle,out var direction,out float depth)&&depth>.002f)
                    {if(DriveContactWithinSkin(direction,depth,margin)||direction.y>=.65f&&depth<=.005f)continue;driveContactNormal=direction;driveContactDepth=depth;return DriveReject("pose g="+g+" depth="+depth+" push="+direction+" collider="+obstacle.name);}
                }
            }
            if(vehicles)foreach(var other in gameVehicles.Values)if(VehicleOverlap(v,origin,other,margin+.025f,yaw,fold))return false;
            return true;
        }
        bool RecoveryLanding(GameVehicle v,Vector3 seed,out Vector3 landing,out float fold)
        {
            landing=seed;fold=Mathf.Lerp(v.unit.robot.model.defaultFold,1,PassagePose.Amount(field.tunnels,seed));
            float inset=Mathf.Max(v.unit.robot.footExtents.x,v.unit.robot.footExtents.z)+.08f;
            if(seed.x<inset||seed.x>field.width-inset||seed.z<inset||seed.z>field.depth-inset)return false;
            if(!GroundSupport(v,seed,v.state.yaw,out float height,out var tilt)||(tilt*Vector3.up).y<.99f)return false;
            landing.y=height+BallisticMath.Support(v.unit.robot.model,fold)+.006f;
            // Every wheel must be supported on this local plane; neither a roof
            // overhead nor the far side of a cliff is accepted as an escape pad.
            Vector3 ext=v.unit.robot.footExtents*.95f;Quaternion body=tilt*Quaternion.Euler(0,v.state.yaw,0);
            for(int i=0;i<4;i++)
            {
                Vector3 offset=body*new Vector3((i&1)==0?-ext.x:ext.x,0,(i&2)==0?-ext.z:ext.z);
                if(!GroundHit(landing+offset,out var hit)||Mathf.Abs(hit.point.y-(height+offset.y))>.025f)return false;
            }
            if(!DrivePoseClear(v,landing,fold,v.state.yaw,true,.012f))return false;
            // A pad is useful only if there is a real way to drive away from it.
            for(int i=0;i<4;i++)
            {
                Vector3 delta=Quaternion.Euler(0,i*45,0)*Vector3.forward*.35f;
                if(TraverseGame(v,landing,landing+delta,out _,true,true)&&TraverseGame(v,landing,landing-delta,out _,true,true))return true;
            }
            return false;
        }
        void RequestRecovery(string id,bool automatic=false)
        {
            if(game==null||!gameVehicles.TryGetValue(id,out var v)||!v.state.Alive){game?.Log("脱困失败：车辆不存在或已战亡");return;}
            if(v.recovering){game.Log(id+" 正在搜索安全脱困点");return;}
            if(game.recording!=null&&game.recordingActor==id){game.FinishRecording();SaveGameClips();game.Log("脱困前已结束当前动作录制，传送不写入片段");}
            v.automaticRecovery=automatic;StartCoroutine(RecoverVehicle(v));
        }
        IEnumerator RecoverVehicle(GameVehicle v)
        {
            var world=game;var actor=v.state;Vector3 start=actor.position;v.recovering=true;actor.velocity=Vector3.zero;actor.verticalVelocity=actor.angularVelocity=0;if(v.physics!=null){v.physics.Reset(actor.position,v.physics.Body.rotation,Vector3.zero);v.physics.Frozen=true;}
            recoveryStatus=actor.id+" 正在寻找附近安全平面";var timer=System.Diagnostics.Stopwatch.StartNew();Vector3 chosen=start;float chosenFold=actor.fold;bool found=false;
            Vector3 searchOrigin=start;
            if(v.automaticRecovery)
            {
                if(v.hasSafePosition&&RecoveryLanding(v,v.lastSafePosition,out chosen,out chosenFold))found=true;
                float inset=Mathf.Max(v.unit.robot.footExtents.x,v.unit.robot.footExtents.z)+.15f;
                searchOrigin=new Vector3(Mathf.Clamp(start.x,inset,field.width-inset),2,Mathf.Clamp(start.z,inset,field.depth-inset));
            }
            try
            {
                for(int ring=0;ring<=54&&!found;ring++)
                {
                    float radius=ring*.15f;int samples=ring==0?1:Mathf.Clamp(Mathf.CeilToInt(2*Mathf.PI*radius/.12f),16,180);float score=float.MaxValue;
                    for(int i=0;i<samples;i++)
                    {
                        if(game!=world||!gameVehicles.TryGetValue(actor.id,out var current)||current!=v||!actor.Alive)yield break;
                        float angle=i*2*Mathf.PI/samples;Vector3 seed=searchOrigin+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                        if(RecoveryLanding(v,seed,out var p,out float fold))
                        {float value=(p-start).sqrMagnitude;if(value<score){chosen=p;chosenFold=fold;score=value;found=true;}}
                        if(timer.Elapsed.TotalMilliseconds>2.5){yield return null;timer.Restart();}
                    }
                    recoveryStatus=actor.id+" 脱困搜索 · 半径 "+radius.ToString("0.00")+"m";
                }
                if(game!=world||!gameVehicles.TryGetValue(actor.id,out var finalVehicle)||finalVehicle!=v||!actor.Alive)yield break;
                if((actor.position-start).sqrMagnitude>1e-8f){recoveryStatus=actor.id+" 脱困取消：车辆位置已编辑";world.Log(recoveryStatus);yield break;}
                if(!found||!RecoveryLanding(v,chosen,out chosen,out chosenFold)){recoveryStatus=actor.id+" 附近未找到安全平面，保留原位置";world.Log(recoveryStatus);yield break;}
                actor.position=chosen;actor.fold=chosenFold;actor.velocity=Vector3.zero;actor.verticalVelocity=actor.angularVelocity=0;actor.blocked=false;v.stuckSeconds=0;v.lastPosition=chosen;actor.dwellUntil=Mathf.Max(actor.dwellUntil,world.time+.5f);
                navigation.Remove(actor.id);gameTrackers.Clear();observations.Clear();nextObservation=world.time;
                if(world.controlled==actor.id){gameInput=new DeveloperInput();gameAutoAim=gameAimValid=false;gameLock="";ClearRuneAim();gameDesiredYaw=actor.gimbalYaw;gameDesiredPitch=actor.pitch;}
                if(v.physics!=null){GroundSupport(v,chosen,actor.yaw,out _,out var tilt);v.physics.Reset(chosen,tilt*Quaternion.Euler(0,actor.yaw,0),Vector3.zero);v.physics.EnsureSpawnClearance();}v.lastSafePosition=actor.position;v.hasSafePosition=true;
                world.customRules=true;PoseGameVehicle(v);CollectGamePlates();previousPlates.Clear();previousPlates.AddRange(gamePlates);UpdateGameCamera();InvalidateLidar();recoveryCount++;
                recoveryStatus=actor.id+" 已脱困 · 移动 "+Vector3.Distance(start,chosen).ToString("0.00")+"m";world.Log(recoveryStatus+" → "+chosen.ToString("F3"));
            }
            finally {v.recovering=false;if(v.physics!=null&&game==world)v.physics.Frozen=!actor.Alive;}
        }
    }
}
