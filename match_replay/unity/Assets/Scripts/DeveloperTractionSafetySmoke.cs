using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class TractionSafetyRow
        {public string model,name,error="";public bool passed;public float drift,lateral,stopDistance,stopSeconds,peakPower;public Vector3 start,end;}
        [Serializable] class TractionSafetyReport
        {public bool passed;public string gpu;public List<TractionSafetyRow> cases=new List<TractionSafetyRow>();}
        public void BeginTractionSafetyAudit(string output){EnterDeveloper();developerSmokeActive=true;StartCoroutine(TractionSafetyAudit(output));}
        void SaveTractionSafety(TractionSafetyReport r,string output)
        {r.passed=r.cases.TrueForAll(x=>x.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(r,true));}
        IEnumerator TractionSafetyAudit(string output)
        {
            Directory.CreateDirectory(output);yield return null;var report=new TractionSafetyReport {gpu=SystemInfo.graphicsDeviceName};
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                ResetDeveloper();game.Remove("target1");ChangeGameModel(game.actors["player"],model);var v=gameVehicles["player"];var a=v.state;game.running=true;
                game.rules.vehiclePhysics=false;ConfigureGamePhysics();yield return null;var course=new GameObject("Traction calibration surface");
                CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(50,.1f,50),8);game.rules.vehiclePhysics=true;gamePhysics=new DeveloperPhysicsWorld(course);BuildVehiclePhysics(v);yield return null;
                foreach(string mode in new[]{"release-brake","release-spin","power-off-coast"})
                {
                    var row=new TractionSafetyRow {model=model,name=mode};report.cases.Add(row);a.fold=v.unit.robot.model.defaultFold;a.gimbalYaw=a.yaw=0;a.capEnabled=false;a.powerOffUntil=0;game.rules.powerLimiter=true;
                    v.physics.Reset(new Vector3(14,20+BallisticMath.Support(v.unit.robot.model,a.fold)+.01f,7.5f),Quaternion.identity,Vector3.zero);v.physics.EnsureSpawnClearance();
                    for(int i=0;i<120;i++)TractionTick(v,new DeveloperInput());
                    for(int i=0;i<360;i++)TractionTick(v,new DeveloperInput {z=1,cameraRelative=true});row.start=a.position;
                    if(mode=="power-off-coast")a.powerOffUntil=game.time+20;
                    Vector3 settled=Vector3.zero;row.stopSeconds=12;
                    for(int i=0;i<1440;i++)
                    {
                        TractionTick(v,new DeveloperInput {spin=mode=="release-spin"});row.peakPower=Mathf.Max(row.peakPower,a.estimatedPower);
                        if(a.velocity.magnitude<.05f&&row.stopSeconds==12)row.stopSeconds=i*DeveloperWorld.Step;
                        if(i==240)settled=a.position;if(i>240)row.drift=Mathf.Max(row.drift,Vector3.ProjectOnPlane(a.position-settled,Vector3.up).magnitude);
                        if(i%240==0)yield return null;
                    }
                    row.end=a.position;row.stopDistance=Vector3.ProjectOnPlane(a.position-row.start,Vector3.up).magnitude;
                    try
                    {
                        if(mode=="power-off-coast")DeveloperChecks.Require(row.peakPower<.01f&&row.stopDistance>.1f,"断电仍主动制动/耗电");
                        else {DeveloperChecks.Require(row.stopSeconds<1.5f&&row.stopDistance<1.4f&&row.drift<.03f,"松键制动/停车漂移超标");DeveloperChecks.Require(row.peakPower<=game.DrivePowerBudget(a,false)+.2f,"制动/旋转耗电超限");}
                        if(mode=="release-spin")DeveloperChecks.Require(Mathf.Abs(a.angularVelocity)>100,"松键后小陀螺被停车逻辑取消");row.passed=true;
                    }catch(Exception e){row.error=e.Message;}
                    SaveTractionSafety(report,output);
                }
                game.rules.vehiclePhysics=false;ConfigureGamePhysics();course.SetActive(false);Destroy(course);yield return null;
                // Every approach heading on a known continuous 23-degree plane.
                foreach(int heading in new[]{0,45,90,135,180,225,270,315})
                {
                    var row=new TractionSafetyRow {model=model,name="cross-slope-"+heading};report.cases.Add(row);
                    var ramp=new GameObject("Traction continuous cross slope");var mesh=new Mesh();float tilt=Mathf.Tan(23*Mathf.Deg2Rad);
                    mesh.vertices=new[]{new Vector3(4,20-10*tilt,-2.5f),new Vector3(4,20-10*tilt,17.5f),new Vector3(24,20+10*tilt,17.5f),new Vector3(24,20+10*tilt,-2.5f)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();var collider=ramp.AddComponent<MeshCollider>();ramp.layer=8;collider.sharedMesh=mesh;
                    game.rules.vehiclePhysics=true;gamePhysics=new DeveloperPhysicsWorld(ramp);BuildVehiclePhysics(v);yield return null;
                    a.fold=v.unit.robot.model.defaultFold;a.gimbalYaw=heading;a.capEnabled=false;a.powerOffUntil=0;game.rules.powerLimiter=true;
                    var normal=new Vector3(-tilt,1,0).normalized;var rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,heading,0);
                    v.physics.Reset(new Vector3(14,20+BallisticMath.Support(v.unit.robot.model,a.fold)+.02f,7.5f),rotation,Vector3.zero);v.physics.EnsureSpawnClearance();
                    for(int i=0;i<240;i++)TractionTick(v,new DeveloperInput {yaw=heading});row.start=a.position;
                    Vector3 dir=Quaternion.Euler(0,heading,0)*Vector3.forward,side=Vector3.Cross(Vector3.up,dir);
                    for(int i=0;i<180;i++){TractionTick(v,new DeveloperInput {z=1,yaw=heading,cameraRelative=true,slow=true});row.lateral=Mathf.Max(row.lateral,Mathf.Abs(Vector3.Dot(a.position-row.start,side)));row.peakPower=Mathf.Max(row.peakPower,a.estimatedPower);}
                    Vector3 settled=Vector3.zero;
                    for(int i=0;i<1440;i++){TractionTick(v,new DeveloperInput {yaw=heading});if(i==240)settled=a.position;if(i>240)row.drift=Mathf.Max(row.drift,Vector3.ProjectOnPlane(a.position-settled,Vector3.up).magnitude);if(i%240==0)yield return null;}
                    row.end=a.position;
                    try{DeveloperChecks.Require(row.lateral<.12f&&row.drift<.03f&&a.velocity.magnitude<.05f,"连续斜坡异向偏离/驻车失败");DeveloperChecks.Require(row.peakPower<=game.DrivePowerBudget(a,false)+.2f,"坡上功率超限");row.passed=true;}catch(Exception e){row.error=e.Message;}
                    SaveTractionSafety(report,output);game.rules.vehiclePhysics=false;ConfigureGamePhysics();ramp.SetActive(false);Destroy(ramp);Destroy(mesh);yield return null;
                }
                game.rules.vehiclePhysics=true;ConfigureGamePhysics();yield return null;ResetDeveloper();game.Remove("target1");ChangeGameModel(game.actors["player"],model);v=gameVehicles["player"];a=v.state;game.running=true;
                Vector3[] seeds={new Vector3(.65f,0,3),new Vector3(field.width-.65f,0,3),new Vector3(4,0,.65f),new Vector3(4,0,field.depth-.65f),new Vector3(.65f,0,.65f),new Vector3(field.width-.65f,0,.65f),new Vector3(.65f,0,field.depth-.65f),new Vector3(field.width-.65f,0,field.depth-.65f)};
                Vector3[] directions={Vector3.left,Vector3.right,Vector3.back,Vector3.forward,new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1)};
                for(int wall=0;wall<seeds.Length;wall++)
                {
                    var row=new TractionSafetyRow {model=model,name="physical-boundary-"+wall};report.cases.Add(row);Vector3 seed=seeds[wall],dir=directions[wall].normalized;
                    // A test-only support pad isolates the actual perimeter
                    // collider from unrelated scenery at a field corner.
                    var pad=gamePhysics.NewObject("Boundary test support",8);pad.transform.position=seed+Vector3.down*.06f;var box=pad.AddComponent<BoxCollider>();box.size=new Vector3(3,.12f,3);box.sharedMaterial=gamePhysics.bodyMaterial;
                    a.fold=v.unit.robot.model.defaultFold;a.gimbalYaw=Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg;a.position=seed+Vector3.up;SnapGameGround(v);v.physics.Reset(a.position,Quaternion.Euler(0,a.gimbalYaw,0),Vector3.zero);v.physics.EnsureSpawnClearance();row.start=a.position;
                    for(int i=0;i<720;i++){TractionTick(v,new DeveloperInput {x=dir.x,z=dir.z,yaw=a.gimbalYaw,burst=true});if(i%240==0)yield return null;}
                    row.end=a.position;
                    try{DeveloperChecks.Require(a.position.x>=-.02f&&a.position.x<=field.width+.02f&&a.position.z>=-.02f&&a.position.z<=field.depth+.02f&&a.position.y>-.5f,"穿过可见边界/坠落");row.passed=true;}catch(Exception e){row.error=e.Message;}
                    pad.SetActive(false);Destroy(pad);SaveTractionSafety(report,output);
                }
                var recovery=new TractionSafetyRow {model=model,name="automatic-void-recovery"};report.cases.Add(recovery);int prior=recoveryCount;
                v.physics.Reset(new Vector3(-1,-2,3),Quaternion.Euler(20,45,20),Vector3.one);PhysicsBoundaryGuard(v);
                float deadline=Time.realtimeSinceStartup+20;while(v.recovering&&Time.realtimeSinceStartup<deadline)yield return null;
                try{DeveloperChecks.Require(recoveryCount==prior+1&&!v.recovering&&a.position.x>0&&a.position.y>-.1f&&a.velocity.magnitude<.001f,"异常越界未安全恢复");recovery.passed=true;}catch(Exception e){recovery.error=e.Message;}SaveTractionSafety(report,output);
            }
            var geometryRow=new TractionSafetyRow {model="system",name="visible-boundary-isolation"};report.cases.Add(geometryRow);
            try
            {
                int walls=0,rails=0;foreach(var c in safetyBoundary.GetComponentsInChildren<Collider>())if(c.enabled&&c.gameObject.layer==VehicleBoundaryLayer)walls++;
                foreach(var r in safetyBoundary.GetComponentsInChildren<Renderer>())if(r.name=="Boundary warning rail")rails++;
                DeveloperChecks.Require(walls==2*(Mathf.CeilToInt(field.width)+Mathf.CeilToInt(field.depth))&&rails==walls,"可见边界存在缺口/标识缺失");
                using(var g=new ReplayLidarGeometry()){g.Add(safetyBoundary);g.Freeze();DeveloperChecks.Require(!g.PhysicsScene.Raycast(new Vector3(-1,1,3),Vector3.right,out _,2,~0,QueryTriggerInteraction.Ignore),"点云包含辅助边界");}
                geometryRow.passed=true;
            }catch(Exception e){geometryRow.error=e.Message;}
            SaveTractionSafety(report,output);developerSmokeActive=false;game.running=false;Debug.Log("TRACTION_SAFETY_AUDIT "+report.passed);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(report.passed?0:1);
#endif
        }
    }
}
