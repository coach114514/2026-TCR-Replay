using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class PhysicsAuditRow
        {
            public string model,name,error="";public bool passed;public Vector3 start,end,velocity,angular;
            public float yaw,heightVariation,power,milliseconds,positionDrift,attitudeDrift,supportForce,p50,p95,p99,lateral;public float[] joints,loads,omegas;public int contacts;
        }
        [Serializable] class PhysicsAuditReport {public bool passed;public string gpu,error="";public List<PhysicsAuditRow> cases=new List<PhysicsAuditRow>();}
        public void BeginPhysicsAudit(string output){EnterDeveloper();StartCoroutine(PhysicsSmoke(output));}
        IEnumerator PhysicsSmoke(string output)
        {
            Directory.CreateDirectory(output);developerSmokeActive=true;yield return null;
            var report=new PhysicsAuditReport {gpu=SystemInfo.graphicsDeviceName};
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                game.rules.vehiclePhysics=false;ConfigureGamePhysics();ResetDeveloper();game.Remove("target1");
                var a=game.actors["player"];ChangeGameModel(a,model);var v=gameVehicles[a.id];
                var course=new GameObject("Physical calibration floor");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(60,.1f,60),8);
                game.rules.vehiclePhysics=true;game.rules.powerLimiter=false;gamePhysics=new DeveloperPhysicsWorld(course);BuildVehiclePhysics(v);yield return null;
                foreach(string name in new[]{"drop-rest","forward","backward","strafe","strafe-left","diagonal","diagonal-left","back-diagonal","back-diagonal-left","follow-stationary","spin-stationary","brake","airborne","ordinary-power","burst-power","air-drive"})
                {
                    var row=new PhysicsAuditRow {model=model,name=name};report.cases.Add(row);
                    float support=BallisticMath.Support(v.unit.robot.model,v.unit.robot.model.defaultFold);
                    a.fold=v.unit.robot.model.defaultFold;a.gimbalYaw=0;a.angularVelocity=0;a.capEnabled=false;
                    bool air=name=="airborne"||name=="air-drive";
                    v.physics.Reset(new Vector3(14,20+support+(name=="drop-rest"?1:air?3:.015f),7.5f),Quaternion.identity,Vector3.zero);row.start=a.position;
                    float min=100,max=0,peak=0;game.running=true;
                    var command=new DeveloperInput {cameraRelative=true,yaw=0,x=name=="strafe"||name=="diagonal"||name=="back-diagonal"?1:name=="strafe-left"||name=="diagonal-left"||name=="back-diagonal-left"?-1:0,z=name=="forward"||name=="diagonal"||name=="diagonal-left"||name=="ordinary-power"||name=="burst-power"||name=="air-drive"?1:name=="backward"||name=="back-diagonal"||name=="back-diagonal-left"?-1:0,spin=name=="spin-stationary",burst=name=="burst-power"};
                    game.rules.powerLimiter=name.EndsWith("power",StringComparison.Ordinal);a.capEnabled=name=="burst-power";float capStart=a.capEnergy,allowed=game.DrivePowerBudget(a,command.burst);
                    if(name=="follow-stationary")a.gimbalYaw=90;
                    if(name=="brake"){v.physics.Reset(row.start,Quaternion.identity,Vector3.forward*2);}
                    Vector3 settledPosition=Vector3.zero;Quaternion settledRotation=Quaternion.identity;
                    for(int tick=0;tick<(air?48:name=="drop-rest"?3600:480);tick++)
                    {
                        game.time+=DeveloperWorld.Step;v.physics.Submit(command);gamePhysics.Step(game);PoseGameVehicle(v);
                        peak=Mathf.Max(peak,a.estimatedPower);if(tick>360){min=Mathf.Min(min,a.position.y);max=Mathf.Max(max,a.position.y);}
                        if(tick==1200){settledPosition=a.position;settledRotation=v.physics.Body.rotation;}
                        if(tick%60==0)yield return null;
                    }
                    row.end=a.position;row.velocity=a.velocity;row.yaw=a.yaw;row.angular=v.physics.Body.angularVelocity;row.power=peak;row.contacts=v.physics.Grounded;row.milliseconds=(float)gamePhysics.LastMilliseconds;row.heightVariation=max-min;
                    row.joints=new float[4];row.loads=new float[4];row.omegas=new float[4];for(int j=0;j<4;j++){row.joints[j]=v.physics.Wheels[j].JointValue;row.loads[j]=v.physics.Wheels[j].Load;row.omegas[j]=v.physics.Wheels[j].Omega;}
                    try
                    {
                        DeveloperChecks.Require(float.IsFinite(a.position.y)&&a.position.y>19.5f,"车辆穿过地面或发散");
                        if(name=="drop-rest")
                        {
                            row.positionDrift=Vector3.Distance(a.position,settledPosition);row.attitudeDrift=Quaternion.Angle(settledRotation,v.physics.Body.rotation);foreach(var w in v.physics.Wheels)row.supportForce+=w.Load;
                            DeveloperChecks.Require(row.heightVariation<.02f&&a.velocity.magnitude<.1f&&row.positionDrift<.005f&&row.attitudeDrift<.5f,"30秒静置未收敛");
                            DeveloperChecks.Require(Mathf.Abs(row.supportForce-game.rules.mass*game.rules.gravity)<game.rules.mass*game.rules.gravity*.1f,"稳态支持力与车重不符");
                        }
                        if(name=="forward")DeveloperChecks.Require(a.position.z-row.start.z>1&&Mathf.Abs(a.yaw)<10,"前进无驱动或朝向改变");
                        if(name=="strafe")DeveloperChecks.Require(a.position.x-row.start.x>1&&Mathf.Abs(a.yaw)<10,"横移无驱动或转向行进方向");
                        if(name=="diagonal")DeveloperChecks.Require(a.position.z-row.start.z>.7f&&a.position.x-row.start.x>.7f,"斜向驱动失败");
                        if(command.z<0)DeveloperChecks.Require(a.position.z-row.start.z< -.7f,"后退方向错误");
                        if(command.x<0)DeveloperChecks.Require(a.position.x-row.start.x< -.7f,"左移方向错误");
                        if(name=="follow-stationary")DeveloperChecks.Require(Mathf.Abs(Mathf.DeltaAngle(a.yaw,90))<5,"静止底盘未跟随云台");
                        if(name=="spin-stationary")DeveloperChecks.Require(Mathf.Abs(a.angularVelocity)>100,"小陀螺无旋转");
                        if(name=="brake")DeveloperChecks.Require(a.velocity.magnitude<.1f&&a.position.z-row.start.z>.01f,"惯性制动错误");
                        if(name=="airborne")DeveloperChecks.Require(a.velocity.y< -3&&v.physics.Grounded==0,"悬空未自然下落");
                        if(name=="air-drive"){var momentum=v.physics.CenterOfMassVelocity();DeveloperChecks.Require(new Vector2(momentum.x,momentum.z).magnitude<.01f&&v.physics.Grounded==0,"离地获得了外部地面牵引");}
                        if(name.EndsWith("power",StringComparison.Ordinal))DeveloperChecks.Require(peak<=allowed+.2f&&a.position.z-row.start.z>.3f,"功率超限或无驱动力");
                        if(name=="burst-power")DeveloperChecks.Require(a.capEnergy<capStart&&peak>game.PowerBudget(a),"爆发未使用电容");
                        row.passed=true;
                    }catch(Exception ex){row.error=ex.Message;}
                    File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
                    if(name=="drop-rest")RenderPhysicsAudit(v,Path.Combine(output,model+"-assembled.png"));
                }
                yield return PhysicsTerrainAudit(v,course,report,output);
                game.running=false;game.rules.vehiclePhysics=false;ConfigureGamePhysics();course.SetActive(false);Destroy(course);yield return null;
            }
            yield return PhysicsLifecycleAudit(report,output);
            yield return PhysicsFieldAudit(report,output);
            report.passed=report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("VEHICLE_PHYSICS_AUDIT "+report.passed);
            developerSmokeActive=false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(report.passed?0:1);
#endif
        }
        void RenderPhysicsAudit(GameVehicle v,string file)
        {
            var c=viewCamera;Vector3 p=c.transform.position;Quaternion q=c.transform.rotation;var target=c.targetTexture;var rect=c.rect;var rt=new RenderTexture(1000,750,24);var image=new Texture2D(1000,750,TextureFormat.RGB24,false);
            var active=RenderTexture.active;try{c.rect=new Rect(0,0,1,1);c.targetTexture=rt;c.transform.position=v.state.position+new Vector3(.85f,.6f,-.85f);c.transform.LookAt(v.state.position+Vector3.up*.17f);c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1000,750),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());}finally{c.targetTexture=target;c.rect=rect;c.transform.SetPositionAndRotation(p,q);RenderTexture.active=active;Destroy(rt);Destroy(image);}
        }
    }
}
