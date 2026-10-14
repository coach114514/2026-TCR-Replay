using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        IEnumerator PhysicsTerrainAudit(GameVehicle v,GameObject floor,PhysicsAuditReport report,string output)
        {
            var a=v.state;game.rules.powerLimiter=false;
            foreach(string name in new[]{"ramp-up","ramp-down","high-step","wall-slide","half-supported","fold-limits","power-off","snapshot","camera-muzzle"})
            {
                game.rules.vehiclePhysics=false;ConfigureGamePhysics();yield return null;
                var fixtures=new GameObject("Physics terrain "+name);float limit=StepLimit(v);
                if(name.StartsWith("ramp",StringComparison.Ordinal))
                {
                    var mesh=new Mesh();mesh.vertices=new[]{new Vector3(12,20,5.5f),new Vector3(12,20,9.5f),new Vector3(15,21.2f,9.5f),new Vector3(15,21.2f,5.5f)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();var n=new GameObject("Continuous slope");n.transform.SetParent(fixtures.transform);n.layer=8;n.AddComponent<MeshCollider>().sharedMesh=mesh;
                    CourseBox(fixtures,new Vector3(18,21.15f,7.5f),new Vector3(6,.1f,4),8);
                }
                if(name=="high-step")CourseBox(fixtures,new Vector3(14,20+limit*.7f,7.5f),new Vector3(1,limit*1.4f,4),8);
                if(name=="wall-slide"){CourseBox(fixtures,new Vector3(14,20.6f,7.5f),new Vector3(.15f,1.2f,8),10);fixtures.transform.GetChild(0).rotation=Quaternion.Euler(0,40,0);}
                if(name=="half-supported")CourseBox(fixtures,new Vector3(14,21.9f,7.5f),new Vector3(8,.2f,4),8);
                fixtures.transform.SetParent(floor.transform);game.rules.vehiclePhysics=true;gamePhysics=new DeveloperPhysicsWorld(floor);BuildVehiclePhysics(v);yield return null;
                a.fold=v.unit.robot.model.defaultFold;a.gimbalYaw=name=="ramp-down"?-90:90;a.motion="still";a.powerOffUntil=0;
                float support=BallisticMath.Support(v.unit.robot.model,a.fold);
                var start=new Vector3(name=="ramp-down"?17:name=="high-step"?12.5f:11,20+support+.025f,7.5f);if(name=="ramp-down")start.y+=1.2f;if(name=="half-supported")start=new Vector3(14,22+support+.025f,9.42f);
                var rotation=Quaternion.Euler(0,a.gimbalYaw,0);v.physics.Reset(start,rotation,name=="power-off"?Vector3.right*1.5f:Vector3.zero);
                var row=new PhysicsAuditRow {model=a.model,name=name,start=a.position};report.cases.Add(row);Debug.Log("PHYSICS_RESET "+name+" body="+v.physics.Body.position+" transform="+v.physics.Body.transform.position+" wheel="+v.physics.Wheels[0].Body.position);
                var command=new DeveloperInput {x=name=="ramp-up"||name=="high-step"||name=="wall-slide"?1:name=="ramp-down"?-1:0,yaw=a.gimbalYaw};
                if(name=="power-off")a.powerOffUntil=game.time+10;
                float min=100,max=0;var saved=default(VehiclePhysicsState);
                DevRaycast(start+Vector3.up,Vector3.down,out var floorHit,3,1<<8);Debug.Log("PHYSICS_TERRAIN_START "+a.model+" "+name+" surfaces="+gamePhysics.SurfaceCount+" ground="+(floorHit.collider?floorHit.point.ToString():"none"));
                for(int tick=0;tick<840;tick++)
                {
                    if(name=="ramp-up"&&a.position.x>16.6f||name=="ramp-down"&&a.position.x<10.5f)command.x=0;
                    if(name=="fold-limits")a.fold=tick<420?1:v.unit.robot.model.defaultFold;
                    game.time+=DeveloperWorld.Step;PreparePhysicsVehicle(v,command.Copy());if(name=="fold-limits"){a.fold=tick<420?1:v.unit.robot.model.defaultFold;v.physics.Submit(command);}gamePhysics.Step(game);PoseGameVehicle(v);
                    if(tick==1||tick==120){DevRaycast(new Vector3(a.position.x,25,a.position.z),Vector3.down,out var probe,10,1<<8);Debug.Log("PHYSICS_TERRAIN_TICK "+a.model+" "+name+" "+tick+" pos="+a.position+" wheels="+v.physics.Wheels[0].Center+" ground="+(probe.collider?probe.point.ToString():"none"));}
                    if(tick>600){min=Mathf.Min(min,a.position.y);max=Mathf.Max(max,a.position.y);}if(tick%120==0)yield return null;
                }
                row.end=a.position;row.velocity=a.velocity;row.angular=v.physics.Body.angularVelocity;row.yaw=a.yaw;row.heightVariation=max-min;row.contacts=v.physics.Grounded;
                try
                {
                    DeveloperChecks.Require(float.IsFinite(a.position.y)&&a.position.y>19.5f,"地面穿透/求解发散");
                    if(name=="ramp-up")DeveloperChecks.Require(a.position.x>15.2f&&a.position.y>21,"上坡未到达平台 "+a.position);
                    if(name=="ramp-down")DeveloperChecks.Require(a.position.x<11.8f&&a.position.y<20.25f,"下坡被接缝卡住 "+a.position);
                    if(name=="high-step")DeveloperChecks.Require(a.position.x<13.5f&&row.heightVariation<.025f,"越过高坎或顶墙持续抖动");
                    if(name=="wall-slide")DeveloperChecks.Require(Mathf.Abs(a.position.z-start.z)>.2f,"斜墙没有沿面滑移");
                    if(name=="half-supported")DeveloperChecks.Require(Vector3.Angle(v.physics.Body.rotation*Vector3.up,Vector3.up)>3||a.position.y<21.5f,"偏置支撑未产生重心姿态响应");
                    if(name=="fold-limits")foreach(var w in v.physics.Wheels)DeveloperChecks.Require(w.Config.kind=="slider"?w.JointValue>=w.Config.minTravel-.001f&&w.JointValue<=w.Config.maxTravel+.001f:w.JointValue>=w.Config.minAngle-1&&w.JointValue<=w.Config.maxAngle+1,"轮组超出限位");
                    if(name=="power-off")DeveloperChecks.Require(a.position.x>start.x+.1f&&a.position.y<start.y&&a.estimatedPower<.01f,"断电没有重力/惯性或仍在耗电");
                    if(name=="snapshot")
                    {
                        v.physics.CaptureSnapshot();saved=JsonUtility.FromJson<VehiclePhysicsState>(JsonUtility.ToJson(a.physics));var pos=a.position;var vel=a.velocity;v.physics.Reset(pos+Vector3.up,Quaternion.identity,Vector3.zero);v.physics.Reset(pos,Quaternion.identity,vel,saved);
                        DeveloperChecks.Require(Quaternion.Angle(saved.rotation,v.physics.Body.rotation)<.01f,"完整姿态存档未恢复");for(int i=0;i<4;i++)DeveloperChecks.Require(Vector3.Distance(saved.modules[i].localPosition,v.physics.Body.transform.InverseTransformPoint(v.physics.Wheels[i].Body.position))<.0001f,"轮组存档未恢复");
                    }
                    if(name=="camera-muzzle")
                    {
                        v.physics.Reset(a.position,Quaternion.Euler(8,90,6),Vector3.zero);a.pitch=12;PoseGameVehicle(v);UpdateGameCamera();GameMuzzle(v,out var muzzle,out var f);game.rules.infiniteAmmo=game.rules.unlimitedHeat=true;a.fold=v.unit.robot.model.defaultFold;
                        DeveloperChecks.Require(FireGameBullet(a),"挂点测试无法发弹");var b=gameBullets[gameBullets.Count-1];DeveloperChecks.Require(Vector3.Distance(b.position,muzzle)<.00001f&&Vector3.Angle(b.velocity,f)<.001f,"倾斜姿态枪口/初速度不一致");DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position,v.unit.robot.groups[v.eyeGroup].TransformPoint(v.eyePoint))<.00001f,"VT03绑定偏移");
                        foreach(var r in v.unit.renderers)DeveloperChecks.Require(!r.forceRenderingOff,"操控时隐藏了车体");
                    }
                    row.passed=true;
                }catch(Exception ex){row.error=ex.Message;}
                if(name=="fold-limits"||name=="half-supported")RenderPhysicsAudit(v,Path.Combine(output,a.model+"-"+name+".png"));
                fixtures.SetActive(false);Destroy(fixtures);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            }
        }
        IEnumerator PhysicsLifecycleAudit(PhysicsAuditReport report,string output)
        {
            var row=new PhysicsAuditRow {model="system",name="scene-lifecycle-save-record"};report.cases.Add(row);
            game.rules.vehiclePhysics=true;ConfigureGamePhysics();yield return null;
            string sceneName="__physics_runtime_acceptance",sceneFile=Path.Combine(GameStorage,sceneName+".json"),clipsFile=Path.Combine(GameStorage,"clips.json");
            string priorScene=File.Exists(sceneFile)?File.ReadAllText(sceneFile):null,priorClips=File.Exists(clipsFile)?File.ReadAllText(clipsFile):null;
            try
            {
                for(int i=0;i<20;i++){ResetDeveloper();DeveloperChecks.Require(gamePhysics.BodyCount==10,"重置后重复刚体");game.Add("audit","blue",new Vector3(8,0,8),"jiageng");DeveloperChecks.Require(gamePhysics.BodyCount==15,"加车数量错误");game.Remove("audit");DeveloperChecks.Require(gamePhysics.BodyCount==10,"删车残留刚体");}
                game.running=true;for(int i=0;i<120;i++)GameStep();game.running=false;SaveGameScene(sceneName);var pos=game.actors["player"].position;LoadGameScene(sceneName);DeveloperChecks.Require(gamePhysics.BodyCount==10&&Vector3.Distance(game.actors["player"].position,pos)<.0001f,"场景恢复丢失状态或重复物理车");
                game.BeginRecording("physics-audit");game.running=true;gameInput=new DeveloperInput {z=1,yaw=90,cameraRelative=true};for(int i=0;i<120;i++)GameStep();game.FinishRecording();game.running=false;var clip=game.clips["physics-audit"];DeveloperChecks.Require(clip.schema==3&&clip.frames.Count==120&&DeveloperWorld.ReadActor(clip.initialState).physics!=null,"录制没有物理初态/逐帧结果");
                row.passed=true;
            }catch(Exception ex){row.error=ex.ToString();}
            finally
            {
                if(priorScene!=null)File.WriteAllText(sceneFile,priorScene);else if(File.Exists(sceneFile))File.Delete(sceneFile);
                if(priorClips!=null)File.WriteAllText(clipsFile,priorClips);else if(File.Exists(clipsFile))File.Delete(clipsFile);
            }
            for(int i=0;i<20;i++){EnterMenu();yield return null;EnterDeveloper();game.rules.vehiclePhysics=true;ConfigureGamePhysics();yield return null;}
            int scenes=0;for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).name.StartsWith("Developer vehicle physics",StringComparison.Ordinal))scenes++;
            if(scenes!=1){row.passed=false;row.error+=" 重复独立物理场景 "+scenes;}
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
        }
    }
}
