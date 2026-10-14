using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class TractionRow
        {
            public string model,team,name,error="";public bool passed,reached,burst,assist;
            public float bodyOffset,allowedPower,angle,runup,lateral,holdDrift,driveSeconds,stopDistance,peakPower,finalSpeed,finalYaw;
            public Vector3 start,end,stop,settled;public int contacts;public float[] loads,slips,omegas;
        }
        [Serializable] class TractionReport
        {public bool passed;public int angleStep;public string gpu;public List<TractionRow> cases=new List<TractionRow>();}
        public void BeginTractionAudit(string output){EnterDeveloper();developerSmokeActive=true;StartCoroutine(TractionAudit(output));}
        void TractionTick(GameVehicle v,DeveloperInput input)
        {game.time+=DeveloperWorld.Step;PreparePhysicsVehicle(v,input.Copy());gamePhysics.Step(game);PoseGameVehicle(v);}
        void SaveTraction(TractionReport report,string output)
        {report.passed=report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));}
        IEnumerator TractionAudit(string output)
        {
            Directory.CreateDirectory(output);yield return null;
            int step=int.TryParse(Environment.GetEnvironmentVariable("TCR_TRACTION_ANGLE_STEP"),out int configured)?Mathf.Clamp(configured,5,90):10;
            var report=new TractionReport {gpu=SystemInfo.graphicsDeviceName,angleStep=step};
            float offset=float.TryParse(Environment.GetEnvironmentVariable("TCR_TRACTION_BODY_OFFSET"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float parsedOffset)?parsedOffset:0;
            bool burstMode=Environment.GetEnvironmentVariable("TCR_TRACTION_BURST")=="1",assistMode=Environment.GetEnvironmentVariable("TCR_TRACTION_ASSIST")=="1";
            float[] runups={.5f,.9f};string configuredRunups=Environment.GetEnvironmentVariable("TCR_TRACTION_RUNUPS");
            if(!string.IsNullOrEmpty(configuredRunups))runups=Array.ConvertAll(configuredRunups.Split(','),s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture));
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                ResetDeveloper();game.Remove("target1");var a=game.actors["player"];ChangeGameModel(a,model);var v=gameVehicles[a.id];a.capEnabled=false;game.rules.wheelRampAssist=assistMode;game.running=true;
                foreach(string team in new[]{"red","blue"})foreach(float runup in runups)for(int angle=0;angle<360;angle+=step)
                {
                    string only=Environment.GetEnvironmentVariable("TCR_TRACTION_CASE");if(!string.IsNullOrEmpty(only)&&Array.IndexOf(only.Split(','),model+"/"+team+"/"+angle+"/"+runup.ToString(System.Globalization.CultureInfo.InvariantCulture))<0)continue;
                    var center=new Vector3(team=="red"?6.6f:21.4f,0,7.5f);Vector3 dir=Quaternion.Euler(0,angle,0)*Vector3.forward,side=Vector3.Cross(Vector3.up,dir);
                    var row=new TractionRow {model=model,team=team,name="fortress-approach-hold",angle=angle,runup=runup,bodyOffset=offset,burst=burstMode,assist=assistMode};report.cases.Add(row);
                    a.position=center-dir*(1.2f+runup);a.position.y=2;a.yaw=a.gimbalYaw=angle+offset;a.fold=v.unit.robot.model.defaultFold;a.velocity=Vector3.zero;a.powerOffUntil=0;a.chassisEnergy=20000;a.buffer=60;a.capEnabled=burstMode||assistMode;if(burstMode||assistMode){a.capEnergy=.5f*game.rules.capacitance*game.rules.capMaxVoltage*game.rules.capMaxVoltage;a.capVoltage=game.rules.capMaxVoltage;a.capProtected=false;a.capUnderSince=a.capRecoverSince=-1;}row.allowedPower=game.DrivePowerBudget(a,burstMode||assistMode);
                    SnapGameGround(v);v.physics.Reset(a.position,Quaternion.Euler(0,angle+offset,0),Vector3.zero);v.physics.EnsureSpawnClearance();
                    var input=new DeveloperInput {x=Mathf.Sin(-offset*Mathf.Deg2Rad),z=Mathf.Cos(-offset*Mathf.Deg2Rad),yaw=angle+offset,cameraRelative=true,burst=burstMode};
                    for(int i=0;i<120;i++)TractionTick(v,new DeveloperInput {yaw=angle+offset});row.start=a.position;
                    for(int tick=0;tick<3000;tick++)
                    {
                        TractionTick(v,input);row.driveSeconds=(tick+1)*DeveloperWorld.Step;row.peakPower=Mathf.Max(row.peakPower,a.estimatedPower);row.lateral=Mathf.Max(row.lateral,Mathf.Abs(Vector3.Dot(a.position-row.start,side)));
                        if(tick%60==0&&!string.IsNullOrEmpty(only))Debug.Log("TRACTION_TRACE tick="+tick+" p="+a.position+" v="+a.velocity+" yaw="+a.yaw+" normal="+v.physics.SupportNormal+" limit="+v.physics.SlopeSpeedLimit+" input="+v.physics.Command.x+","+v.physics.Command.z+" reason="+v.lastBlockReason+"\n"+PhysicsObstruction(v,dir));
                        if(GroundHit(a.position,out var h)&&h.point.y>.445f&&new Vector2(a.position.x-center.x,a.position.z-center.z).magnitude<.16f){row.reached=true;break;}
                        if(a.position.y<-.5f||Vector3.Dot(a.position-center,dir)>1.5f)break;
                        if(tick%180==0)yield return null;
                    }
                    row.stop=a.position;Vector3 settled=Vector3.zero;
                    for(int tick=0;tick<1320;tick++)
                    {TractionTick(v,new DeveloperInput {yaw=angle+offset});if(tick==120)settled=a.position;if(tick>120)row.holdDrift=Mathf.Max(row.holdDrift,Vector3.ProjectOnPlane(a.position-settled,Vector3.up).magnitude);if(tick%60==0&&!string.IsNullOrEmpty(only))Debug.Log("TRACTION_STOP_TRACE tick="+tick+" state="+v.physics.MotionState+"\n"+PhysicsObstruction(v,dir));if(tick%240==0)yield return null;}
                    row.settled=settled;row.end=a.position;row.finalSpeed=a.velocity.magnitude;row.finalYaw=a.yaw;row.contacts=v.physics.Grounded;row.stopDistance=Vector3.Distance(row.stop,a.position);
                    row.loads=new float[4];row.slips=new float[4];row.omegas=new float[4];for(int i=0;i<4;i++){row.loads[i]=v.physics.Wheels[i].Load;row.slips[i]=v.physics.Wheels[i].Slip;row.omegas[i]=v.physics.Wheels[i].Omega;}
                    try
                    {
                        DeveloperChecks.Require(row.reached,"未上堡垒："+v.lastBlockReason);
                        DeveloperChecks.Require(row.lateral<=.12f,"加速上坡异向偏离超过12cm："+row.lateral);
                        DeveloperChecks.Require(row.holdDrift<=.03f&&row.finalSpeed<.05f,"松键后漂移/未停稳："+row.holdDrift);
                        DeveloperChecks.Require(GroundHit(a.position,out var h)&&h.point.y>.44f&&new Vector2(a.position.x-center.x,a.position.z-center.z).magnitude<.60f,"松键后驶离堡垒");
                        DeveloperChecks.Require(row.peakPower<=row.allowedPower+.2f,"爬坡功率超限");row.passed=true;
                    }catch(Exception e){row.error=e.Message;}
                    if(!row.passed){RenderPhysicsAudit(v,Path.Combine(output,model+"-"+team+"-"+angle+"-"+runup+"-failed.png"));File.WriteAllText(Path.Combine(output,model+"-"+team+"-"+angle+"-"+runup+"-contacts.txt"),PhysicsObstruction(v,dir));}
                    SaveTraction(report,output);
                }
            }
            SaveTraction(report,output);Debug.Log("TRACTION_AUDIT "+report.passed+" cases="+report.cases.Count);developerSmokeActive=false;game.running=false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(report.passed?0:1);
#endif
        }
    }
}
