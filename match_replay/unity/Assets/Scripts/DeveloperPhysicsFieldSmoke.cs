using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        public void BeginFieldPhysicsAudit(string output){EnterDeveloper();developerSmokeActive=true;StartCoroutine(FieldPhysicsOnly(output));}
        IEnumerator FieldPhysicsOnly(string output){Directory.CreateDirectory(output);yield return null;var r=new PhysicsAuditReport {gpu=SystemInfo.graphicsDeviceName};yield return PhysicsFieldAudit(r,output);r.passed=r.cases.TrueForAll(x=>x.passed);SavePhysicsRow(r,output);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(r.passed?0:1);
#endif
        }
        void SavePhysicsRow(PhysicsAuditReport report,string output)=>File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
        string PhysicsObstruction(GameVehicle v,Vector3 intent)
        {
            string result="pose="+v.physics.Body.position+" rotation="+v.physics.Body.rotation.eulerAngles+" velocity="+v.physics.Body.linearVelocity+" power="+v.physics.MotorPower+" "+v.physics.ControlDiagnostic+"\n";
            foreach(var w in v.physics.Wheels)result+=w.Config.label+" center="+w.Center+" grounded="+w.Grounded+" load="+w.Load+" carrierLoad="+w.CarrierLoad+" force="+w.DriveForce+" surfaceForce="+w.SurfaceForce+" joint="+w.JointValue+" target="+w.TargetOmega+" omega="+w.Omega+" torque="+w.RequestedTorque+" slip="+w.Slip+" trim="+w.SuspensionTrim+" normal="+w.SurfaceNormal+" traction="+w.Traction+"\n";
            foreach(var c in v.physics.Shapes)
            {
                var bounds=c.bounds;int count=DevOverlapBox(bounds.center+intent*.025f,bounds.extents+Vector3.one*.03f,driveOverlap,Quaternion.identity,(1<<8)|(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++){var other=driveOverlap[i];if(Physics.ComputePenetration(c,c.transform.position+intent*.025f,c.transform.rotation,other,other.transform.position,other.transform.rotation,out var push,out float depth))result+=c.transform.parent.name+"/"+c.name+" -> "+other.name+" depth="+depth.ToString("F4")+" push="+push.ToString("F3")+"\n";}
            }
            return result;
        }
        IEnumerator PhysicsFieldAudit(PhysicsAuditReport report,string output)
        {
            game.rules.vehiclePhysics=true;ConfigureGamePhysics();yield return null;
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                ResetDeveloper();ChangeGameModel(game.actors["player"],model);game.Remove("target1");var v=gameVehicles["player"];var a=v.state;game.rules.powerLimiter=true;yield return null;
                foreach(int mirror in new[]{0,1})foreach(int seam in new[]{0,1})
                {
                    var row=new PhysicsAuditRow {model=model,name="official-seam-"+mirror+"-"+seam};report.cases.Add(row);
                    var seed=seam==0?new Vector3(8.6f,3,11.3f):new Vector3(9,3,11.9f);var dir=seam==0?Vector3.left:Vector3.forward;
                    if(mirror==1){seed.x=28-seed.x;seed.z=15-seed.z;dir=-dir;}
                    a.capEnergy=.5f*game.rules.capacitance*game.rules.capMaxVoltage*game.rules.capMaxVoltage;a.capVoltage=game.rules.capMaxVoltage;a.capProtected=false;a.chassisEnergy=20000;
                    a.fold=v.unit.robot.model.defaultFold;a.position=seed-dir*.8f;a.yaw=a.gimbalYaw=Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg;a.velocity=Vector3.zero;SnapGameGround(v);v.physics.Reset(a.position,Quaternion.Euler(0,a.yaw,0),Vector3.zero);v.physics.EnsureSpawnClearance();a.requestedSpeed=3.3f;
                    for(int settle=0;settle<120;settle++){game.time+=DeveloperWorld.Step;PreparePhysicsVehicle(v,new DeveloperInput {yaw=a.gimbalYaw});gamePhysics.Step(game);PoseGameVehicle(v);}row.start=a.position;Vector3 side=Vector3.Cross(Vector3.up,dir);
                    var input=new DeveloperInput {x=dir.x,z=dir.z,yaw=a.gimbalYaw,burst=Environment.GetEnvironmentVariable("TCR_PHYSICS_BURST")=="1"};
                    for(int tick=0;tick<1440;tick++){game.time+=DeveloperWorld.Step;PreparePhysicsVehicle(v,input.Copy());gamePhysics.Step(game);PoseGameVehicle(v);row.lateral=Mathf.Max(row.lateral,Mathf.Abs(Vector3.Dot(a.position-row.start,side)));row.power=Mathf.Max(row.power,a.estimatedPower);if(Vector3.Dot(a.position-seed,dir)>.35f)break;if(tick%120==0){if(Environment.GetEnvironmentVariable("TCR_PHYSICS_TRACE")=="1")Debug.Log("SEAM_TRACE "+model+" "+mirror+" "+seam+" tick="+tick+"\n"+PhysicsObstruction(v,dir));yield return null;}}
                    row.end=a.position;row.velocity=a.velocity;try{DeveloperChecks.Require(Vector3.Dot(a.position-seed,dir)>.3f&&a.position.y> -.1f,"官方CAD接缝没有通过 "+v.lastBlockReason);DeveloperChecks.Require(row.lateral<=.12f,"官方CAD接缝异向偏离超过12cm："+row.lateral);DeveloperChecks.Require(row.power<=game.DrivePowerBudget(a,true)+.2f,"接缝助跑功率超限");row.passed=true;}catch(Exception ex){row.error=ex.Message;RenderPhysicsAudit(v,Path.Combine(output,model+"-"+row.name+".png"));File.WriteAllText(Path.Combine(output,model+"-"+row.name+"-contacts.txt"),PhysicsObstruction(v,dir));}SavePhysicsRow(report,output);
                }
                foreach(var passage in field.tunnels)
                {
                    var row=new PhysicsAuditRow {model=model,name="official-tunnel-"+passage.id};report.cases.Add(row);bool across=passage.halfX<passage.halfY;var axis=across?Vector3.right:Vector3.forward;var center=new Vector3(passage.x,0,passage.y);
                    a.position=center+axis*1.1f;a.yaw=a.gimbalYaw=across?270:180;a.fold=v.unit.robot.model.defaultFold;a.velocity=Vector3.zero;a.requestedSpeed=.5f;SnapGameGround(v);v.physics.Reset(a.position,Quaternion.Euler(0,a.yaw,0),Vector3.zero);row.start=a.position;
                    var input=new DeveloperInput {x=-axis.x,z=-axis.z,yaw=a.gimbalYaw};bool crossed=false,fireBlocked=true;
                    for(int tick=0;tick<1680;tick++)
                    {
                        game.time+=DeveloperWorld.Step;PreparePhysicsVehicle(v,input.Copy());gamePhysics.Step(game);PoseGameVehicle(v);float along=Vector3.Dot(a.position-center,axis);
                        if(Mathf.Abs(along)<.09f&&a.fold>.94f)crossed=true;if(GameFolded(v))fireBlocked&=!a.CanFire(game.time,game.rules);if(along<-.65f)break;if(tick%120==0)yield return null;
                    }
                    row.end=a.position;row.contacts=v.physics.Grounded;row.joints=new float[4];row.loads=new float[4];for(int j=0;j<4;j++){row.joints[j]=v.physics.Wheels[j].JointValue;row.loads[j]=v.physics.Wheels[j].Load;}
                    try{DeveloperChecks.Require(crossed&&Vector3.Dot(a.position-center,axis)<-.35f&&fireBlocked,"物理过洞失败 "+a.position+" fold="+a.fold+" yaw="+a.yaw+" "+v.lastBlockReason);row.passed=true;}catch(Exception ex){row.error=ex.Message;RenderPhysicsAudit(v,Path.Combine(output,model+"-"+passage.id+".png"));File.WriteAllText(Path.Combine(output,model+"-"+passage.id+"-contacts.txt"),PhysicsObstruction(v,-axis));}SavePhysicsRow(report,output);
                }
                var recovery=new PhysicsAuditRow {model=model,name="physical-recovery"};report.cases.Add(recovery);
                a.position=new Vector3(6,2,9.4f);a.fold=v.unit.robot.model.defaultFold;SnapGameGround(v);v.physics.Reset(a.position,Quaternion.Euler(12,40,9),Vector3.one);recovery.start=a.position;int prior=recoveryCount;RequestRecovery(a.id);float deadline=Time.realtimeSinceStartup+30;
                while(v.recovering&&Time.realtimeSinceStartup<deadline)yield return null;
                try{DeveloperChecks.Require(!v.recovering&&prior+1==recoveryCount&&a.velocity.sqrMagnitude<1e-8f,"物理脱困未完成/未清理惯性");foreach(var w in v.physics.Wheels)DeveloperChecks.Require(Vector3.Distance(v.physics.Body.transform.InverseTransformPoint(w.Body.position),w.Config.Pivot)<.002f&&w.Body.linearVelocity.sqrMagnitude<1e-8f,"脱困遗留轮组");recovery.end=a.position;recovery.passed=true;}catch(Exception ex){recovery.error=ex.Message;}SavePhysicsRow(report,output);
                var rates=new PhysicsAuditRow {model=model,name="fixed-step-display-rates"};report.cases.Add(rates);
                try
                {
                    Vector3 origin=a.position,reference=Vector3.zero;var q=Quaternion.Euler(0,a.yaw,0);
                    foreach(int fps in new[]{60,120,144,165,240})
                    {
                        v.physics.Reset(origin,q,Vector3.zero);double accumulator=0;int ticks=0;
                        for(int frame=0;frame<fps*2;frame++){accumulator+=1d/fps;while(accumulator+1e-6>=DeveloperWorld.Step){game.time+=DeveloperWorld.Step;v.physics.Submit(new DeveloperInput {z=1,yaw=a.gimbalYaw,cameraRelative=true});gamePhysics.Step(game);accumulator-=DeveloperWorld.Step;ticks++;}v.physics.ApplyVisuals(Mathf.Clamp01((float)(accumulator/DeveloperWorld.Step)));}
                        DeveloperChecks.Require(ticks==240,"显示率改变物理tick数量");if(fps==60)reference=a.position;else DeveloperChecks.Require(Vector3.Distance(reference,a.position)<.01f,"显示率改变运动结果");
                    }
                    rates.passed=true;
                }catch(Exception ex){rates.error=ex.Message;}SavePhysicsRow(report,output);
            }
            var performance=new PhysicsAuditRow {model="system",name="seventeen-vehicle-physics"};report.cases.Add(performance);
            ResetDeveloper();yield return null;
            try
            {
                for(int i=2;i<17;i++)game.Add("performance"+i,"blue",new Vector3(4+(i%6)*1.4f,0,3+(i/6)*1.1f),i%2==0?"jiageng":"sweeper");double total=0;var costs=new List<float>();
                for(int tick=0;tick<360;tick++){game.time+=DeveloperWorld.Step;gamePhysics.Step(game);if(tick>20){total+=gamePhysics.LastMilliseconds;costs.Add((float)gamePhysics.LastMilliseconds);}}
                costs.Sort();performance.milliseconds=(float)(total/costs.Count);performance.p50=costs[(int)(costs.Count*.5f)];performance.p95=costs[(int)(costs.Count*.95f)];performance.p99=costs[(int)(costs.Count*.99f)];DeveloperChecks.Require(gamePhysics.BodyCount==85,"17车五刚体数量不符");foreach(var actor in game.actors.Values)DeveloperChecks.Require(float.IsFinite(actor.position.y),"多车求解发散");performance.passed=true;
            }catch(Exception ex){performance.error=ex.Message;}SavePhysicsRow(report,output);
            var lidar=new PhysicsAuditRow {model="system",name="avia-independent-geometry"};report.cases.Add(lidar);ResetDeveloper();yield return null;
            try
            {
                using(var geometry=new ReplayLidarGeometry())
                {
                    foreach(var vehicle in gameVehicles.Values)geometry.Add(vehicle.unit.root);geometry.Freeze();var v=gameVehicles["player"];var pos=v.state.position;
                    bool hit=geometry.PhysicsScene.Raycast(pos+Vector3.up*1.5f,Vector3.down,out _,2,~0,QueryTriggerInteraction.Ignore);
                    DeveloperChecks.Require(hit&&geometry.PhysicsScene!=gamePhysics.Physics,"物理车型未进入独立点云场景");lidar.passed=true;
                }
            }catch(Exception ex){lidar.error=ex.Message;}SavePhysicsRow(report,output);
            game.running=false;
        }
    }
}
