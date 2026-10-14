using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class DriveSeamCase
        {
            public string model,collider,penetration;public Vector3 start,wanted,normal;public float centerRise,supportRise,normalChange,hitDistance;public bool clear,startClear,endClear;
        }
        [Serializable] class DriveSeamReport
        {
            public string gpu,error="";public int checkedEdges,blockedEdges;public List<DriveSeamCase> cases=new List<DriveSeamCase>();
        }
        IEnumerator DriveProbe(string[] args)
        {
            string output=args[Array.IndexOf(args,"--drive-probe")+1];Directory.CreateDirectory(output);developerSmokeActive=true;game.running=false;
            var report=new DriveSeamReport{gpu=SystemInfo.graphicsDeviceName};yield return null;
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                var a=game.actors["player"];ChangeGameModel(a,model);var v=gameVehicles[a.id];a.fold=v.unit.robot.model.defaultFold;
                var target=gameVehicles["target1"];Debug.Log("DRIVE_SPAWN "+target.state.position+" "+DrivePenetrationDescription(target,target.state.position,target.state.fold,target.state.yaw));
                var next=target.state.position+Vector3.forward*.001f;bool pass=TraverseGame(target,target.state.position,next,out var solved);Debug.Log("DRIVE_SPAWN_MOVE "+pass+" from="+target.state.position+" solved="+solved+" ground="+GroundHit(next,out _)+" "+DrivePenetrationDescription(target,solved,target.state.fold,target.state.yaw));
                foreach(var testPoint in new[]{new Vector3(9.5f,0,9.4f),new Vector3(15.97f,.18f,1.54f)}){var p=testPoint;p.y=GameGround(v,p)+BallisticMath.Support(v.unit.robot.model,a.fold)+.004f;Debug.Log("DRIVE_CONTACT "+model+" "+p+" "+DrivePenetrationDescription(v,p,a.fold,0));}
                for(float x=3.4f;x<11;x+=.2f)for(float z=10.3f;z<14.6f;z+=.2f)foreach(int mirror in new[]{0,1})for(int dir=0;dir<8;dir++)
                {
                    Vector3 p=mirror==0?new Vector3(x,3,z):new Vector3(28-x,3,15-z);p.y=GroundFrom(p,0)+.004f;
                    a.position=p;a.yaw=dir*45;a.gimbalYaw=a.yaw;a.velocity=Vector3.zero;a.verticalVelocity=0;p.y=GameGround(v,p)+BallisticMath.Support(v.unit.robot.model,a.fold)+.004f;a.position=p;PoseGameVehicle(v);
                    Vector3 delta=Quaternion.Euler(0,a.yaw,0)*Vector3.forward*.08f,wanted=p+delta;
                    float centerRise=GroundFrom(wanted)-GroundFrom(p),change=Vector3.Angle(GameSlope(p)*Vector3.up,GameSlope(wanted)*Vector3.up);
                    if(Mathf.Abs(centerRise)>.075f||change<2)continue;
                    movementBlockingHit=default;bool clear=TraverseGame(v,p,wanted,out _);report.checkedEdges++;if(!clear)report.blockedEdges++;
                    if(!clear||report.cases.Count<100){var end=wanted;end.y=GameGround(v,end)+BallisticMath.Support(v.unit.robot.model,a.fold)+.004f;report.cases.Add(new DriveSeamCase{model=model,start=p,wanted=wanted,centerRise=centerRise,supportRise=GameGround(v,wanted)-GameGround(v,p),normalChange=change,clear=clear,collider=movementBlockingHit.collider?movementBlockingHit.collider.name:"support/step",normal=movementBlockingHit.normal,hitDistance=movementBlockingHit.distance,startClear=DrivePoseClear(v,p,a.fold,a.yaw),endClear=DrivePoseClear(v,end,a.fold,a.yaw),penetration=clear?"":DrivePenetrationDescription(v,p,a.fold,a.yaw)+" -> "+DrivePenetrationDescription(v,end,a.fold,a.yaw)});}
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("DRIVE_PROBE_OK edges="+report.checkedEdges+" blocked="+report.blockedEdges);Application.Quit(0);
        }
        string DrivePenetrationDescription(GameVehicle v,Vector3 origin,float fold,float yaw)
        {
            string result="";var robot=v.unit.robot;GroundSupport(v,origin,yaw,out _,out var tilt);Quaternion body=tilt*Quaternion.Euler(0,yaw,0);
            Vector3 local=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);float heading=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
            for(int g=0;g<robot.groups.Length;g++)
            {
                var bounds=FoldedDriveBounds(robot,g,fold);if(!robot.model.groups[g].turret){var min=bounds.min;min.y+=.05f;bounds.SetMinMax(min,bounds.max);}
                Vector3 half=Vector3.Max(Vector3.one*.006f,bounds.extents-Vector3.one*.008f);var transform=BallisticMath.Group(robot.model,g,fold,heading,fold>robot.model.defaultFold+.01f?0:v.state.pitch,true)*BallisticMath.Fold(robot.model.groups[g],fold).inverse;
                Vector3 center=origin+body*transform.MultiplyPoint3x4(bounds.center);Quaternion rotation=body*transform.rotation;int n=DevOverlapBox(center,half,driveOverlap,rotation,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);var box=DriveProbeBox();box.size=half*2;
                for(int i=0;i<n;i++){var o=driveOverlap[i];if(Physics.ComputePenetration(box,center,rotation,o,o.transform.position,o.transform.rotation,out var push,out float depth)&&depth>.002f)result+="g"+g+" "+o.name+" depth="+depth.ToString("F4")+" push="+push.ToString("F3")+"; ";}
            }
            return result;
        }
        [Serializable] class DriveTestCase {public string model,name,error="";public bool passed;public Vector3 start,end;public float variation;}
        [Serializable] class DriveRecoveryReport {public string gpu,error="";public bool passed;public List<DriveTestCase> cases=new List<DriveTestCase>();public int recoveries;}
        IEnumerator DriveRecoverySmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--drive-smoke")+1];Directory.CreateDirectory(output);developerSmokeActive=true;game.running=false;game.rules.vehiclePhysics=false;ConfigureGamePhysics();yield return null;
            var report=new DriveRecoveryReport{gpu=SystemInfo.graphicsDeviceName};GameObject course=null;
            foreach(string model in new[]{"sweeper","jiageng"})
            {
                ResetDeveloper();var a=game.actors["player"];ChangeGameModel(a,model);var v=gameVehicles[a.id];game.rules.powerLimiter=false;a.requestedSpeed=1.2f;a.pitch=0;
                foreach(int mirror in new[]{0,1})foreach(int edge in new[]{0,1})
                {
                    var test=new DriveTestCase{model=model,name="official-CAD-road-seam-"+mirror+"-"+edge};report.cases.Add(test);
                    try
                    {
                        var seed=edge==0?new Vector3(8.6f,3,11.3f):new Vector3(9,3,11.9f);Vector3 direction=edge==0?Vector3.left:Vector3.forward;
                        if(mirror==1){seed.x=28-seed.x;seed.z=15-seed.z;direction=-direction;}
                        seed.y=GroundFrom(seed,0)+.004f;a.position=seed;a.yaw=a.gimbalYaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;a.fold=v.unit.robot.model.defaultFold;a.velocity=Vector3.zero;a.verticalVelocity=0;SnapGameGround(v);test.start=a.position;
                        DeveloperChecks.Require(TraverseGame(v,a.position,a.position+direction*.08f,out var end),"实际CAD接缝仍阻挡");
                        DeveloperChecks.Require(DrivePoseClear(v,end,a.fold,a.yaw),"接缝终点有车体穿模");
                        for(int i=0;i<36;i++){game.time+=DeveloperWorld.Step;MoveGameVehicle(v,new DeveloperInput{x=direction.x,z=direction.z,yaw=a.yaw});PoseGameVehicle(v);}
                        test.end=a.position;DeveloperChecks.Require(Vector3.Dot(a.position-test.start,direction)>.08f,"实际输入未驶过接缝 "+v.lastBlockReason);test.passed=true;
                    }catch(Exception ex){test.error=ex.Message;report.error+=model+" "+test.name+": "+ex+"\n";}
                }
                course=new GameObject("Drive seam course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);
                var mesh=new Mesh();mesh.vertices=new[]{new Vector3(11.5f,20,6),new Vector3(11.5f,20,9),new Vector3(14,21.061f,9),new Vector3(14,21.061f,6)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();
                foreach(int layer in new[]{8,10}){var n=new GameObject("23 degree ramp");n.transform.SetParent(course.transform);n.layer=layer;n.AddComponent<MeshCollider>().sharedMesh=mesh;}
                foreach(int layer in new[]{8,10})CourseBox(course,new Vector3(17,21.011f,7.5f),new Vector3(6,.1f,3),layer);
                Physics.SyncTransforms();
                foreach(int sign in new[]{1,-1})
                {
                    var test=new DriveTestCase{model=model,name=sign>0?"flat-ramp-plateau":"plateau-ramp-flat"};report.cases.Add(test);
                    try
                    {
                        a.position=new Vector3(sign>0?10.5f:15.2f,sign>0?20.004f:21.065f,7.5f);a.yaw=a.gimbalYaw=sign>0?90:-90;a.fold=v.unit.robot.model.defaultFold;a.velocity=Vector3.zero;a.verticalVelocity=0;SnapGameGround(v);test.start=a.position;
                        for(int i=0;i<720;i++){game.time+=DeveloperWorld.Step;MoveGameVehicle(v,new DeveloperInput{x=sign,yaw=a.yaw});PoseGameVehicle(v);}
                        test.end=a.position;DeveloperChecks.Require(sign*(a.position.x-test.start.x)>3.5f,"坡面接缝通行不足 "+a.position);test.passed=true;
                    }catch(Exception ex){test.error=ex.Message;report.error+=model+" "+test.name+": "+ex+"\n";}
                }
                course.SetActive(false);Destroy(course);yield return null;
                course=new GameObject("Drive constraint course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);
                float high=StepLimit(v)*1.3f;foreach(int layer in new[]{8,10})CourseBox(course,new Vector3(13,20+high*.5f,7.5f),new Vector3(1,high,3),layer);Physics.SyncTransforms();
                var wall=new DriveTestCase{model=model,name="high-step-stable-contact"};report.cases.Add(wall);
                try
                {
                    a.position=new Vector3(11.6f,20.004f,7.5f);a.yaw=a.gimbalYaw=90;a.velocity=Vector3.zero;a.verticalVelocity=0;a.fold=v.unit.robot.model.defaultFold;SnapGameGround(v);wall.start=a.position;float lowY=100,highY=0;
                    for(int i=0;i<600;i++){game.time+=DeveloperWorld.Step;MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90,burst=true});if(i>350){lowY=Mathf.Min(lowY,a.position.y);highY=Mathf.Max(highY,a.position.y);}}
                    wall.end=a.position;wall.variation=highY-lowY;DeveloperChecks.Require(a.position.x<12.5f&&wall.variation<.005f&&Mathf.Abs(a.position.y-wall.start.y)<.01f,"高台阶越界、抬升或顶墙抖动 "+a.position+" variation="+wall.variation);wall.passed=true;
                }catch(Exception ex){wall.error=ex.Message;report.error+=model+" "+wall.name+": "+ex+"\n";}
                var spinWall=new DriveTestCase{model=model,name="high-step-spin-stable-contact"};report.cases.Add(spinWall);
                try
                {
                    a.position=new Vector3(11.6f,20.004f,7.5f);a.yaw=a.gimbalYaw=90;a.velocity=Vector3.zero;a.verticalVelocity=a.angularVelocity=0;a.fold=v.unit.robot.model.defaultFold;SnapGameGround(v);spinWall.start=a.position;float lowY=100,highY=0;
                    for(int i=0;i<720;i++){game.time+=DeveloperWorld.Step;MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90,spin=true,burst=true});if(i>450){lowY=Mathf.Min(lowY,a.position.y);highY=Mathf.Max(highY,a.position.y);}}
                    spinWall.end=a.position;spinWall.variation=highY-lowY;DeveloperChecks.Require(a.position.x<12.5f&&spinWall.variation<.005f&&Mathf.Abs(a.position.y-spinWall.start.y)<.01f,"小陀螺顶高台阶越界或抬升 "+a.position+" variation="+spinWall.variation);spinWall.passed=true;
                }catch(Exception ex){spinWall.error=ex.Message;report.error+=model+" "+spinWall.name+": "+ex+"\n";}
                course.SetActive(false);Destroy(course);yield return null;
                course=new GameObject("Diagonal sliding course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);CourseBox(course,new Vector3(13,20.8f,7.5f),new Vector3(.25f,1.6f,8),10);course.transform.GetChild(1).rotation=Quaternion.Euler(0,45,0);Physics.SyncTransforms();
                var sliding=new DriveTestCase{model=model,name="diagonal-wall-slide"};report.cases.Add(sliding);
                try{a.position=new Vector3(11.8f,20.004f,7.5f);a.yaw=a.gimbalYaw=90;a.fold=v.unit.robot.model.defaultFold;a.velocity=Vector3.zero;a.verticalVelocity=0;SnapGameGround(v);sliding.start=a.position;
                    for(int i=0;i<720;i++){game.time+=DeveloperWorld.Step;MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90});PoseGameVehicle(v);}
                    sliding.end=a.position;DeveloperChecks.Require(a.position.x-sliding.start.x>1.8f&&Mathf.Abs(a.position.z-sliding.start.z)>.5f&&DrivePoseClear(v,a.position,a.fold,a.yaw),"斜墙没有安全滑移 "+a.position+" "+v.lastBlockReason);sliding.passed=true;}
                catch(Exception ex){sliding.error=ex.Message;report.error+=model+" "+sliding.name+": "+ex+"\n";}
                course.SetActive(false);Destroy(course);yield return null;
                course=new GameObject("Rescue collision course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);CourseBox(course,new Vector3(13,20.6f,7.5f),new Vector3(.8f,1.2f,2),10);Physics.SyncTransforms();
                var rescue=new DriveTestCase{model=model,name="embedded-rescue-usable-plane"};report.cases.Add(rescue);
                a.position=new Vector3(13,20.004f,7.5f);a.velocity=Vector3.one*3;a.angularVelocity=180;PoseGameVehicle(v);rescue.start=a.position;
                float hp=a.hp,ammo=a.ammo,heat=a.heat;int prior=recoveryCount;RequestRecovery(a.id);float deadline=Time.realtimeSinceStartup+30;
                while(v.recovering&&Time.realtimeSinceStartup<deadline)yield return null;
                try
                {
                    rescue.end=a.position;DeveloperChecks.Require(!v.recovering&&recoveryCount==prior+1&&Vector3.Distance(rescue.start,a.position)>.3f,"实际脱困没有移动到安全点 "+recoveryStatus);
                    DeveloperChecks.Require(RecoveryLanding(v,a.position,out _,out _)&&a.velocity==Vector3.zero&&a.angularVelocity==0,"脱困落点不可驶离或未清理惯性");
                    DeveloperChecks.Require(a.hp==hp&&a.ammo==ammo&&a.heat==heat,"脱困改变实战血量／弹量／热量");rescue.passed=true;
                }catch(Exception ex){rescue.error=ex.Message;report.error+=model+" "+rescue.name+": "+ex+"\n";}
                course.SetActive(false);Destroy(course);yield return null;
                course=new GameObject("Rescue lower layer course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);CourseBox(course,new Vector3(14,20.9f,7.5f),new Vector3(8,.1f,6),8);Physics.SyncTransforms();
                var layerTest=new DriveTestCase{model=model,name="rescue-does-not-pick-overhead-deck"};report.cases.Add(layerTest);
                try {a.position=new Vector3(13,20.004f,7.5f);DeveloperChecks.Require(RecoveryLanding(v,a.position,out var landing,out _)&&landing.y<20.1f,"脱困错误选择头顶桥面");layerTest.end=landing;layerTest.passed=true;}
                catch(Exception ex){layerTest.error=ex.Message;report.error+=model+" "+layerTest.name+": "+ex+"\n";}
                var occupied=new DriveTestCase{model=model,name="rescue-rejects-occupied-pad"};report.cases.Add(occupied);
                try{var other=game.actors["target1"];other.position=a.position;PoseGameVehicle(gameVehicles[other.id]);DeveloperChecks.Require(!RecoveryLanding(v,a.position,out _,out _),"脱困接受了另一辆车占用的位置");occupied.passed=true;}
                catch(Exception ex){occupied.error=ex.Message;report.error+=model+" "+occupied.name+": "+ex+"\n";}
                course.SetActive(false);Destroy(course);yield return null;
                course=new GameObject("Cancelled recovery course");CourseBox(course,new Vector3(14,19.95f,7.5f),new Vector3(20,.1f,12),8);CourseBox(course,new Vector3(14,20.6f,7.5f),new Vector3(8,1.2f,8),10);Physics.SyncTransforms();a.position=new Vector3(14,20.004f,7.5f);PoseGameVehicle(v);
                var cancelled=new DriveTestCase{model=model,name="reset-cancels-pending-rescue"};report.cases.Add(cancelled);int countBefore=recoveryCount;RequestRecovery(a.id);bool pending=v.recovering;ResetDeveloper();yield return null;yield return null;
                try{DeveloperChecks.Require(pending&&recoveryCount==countBefore&&!gameVehicles["player"].recovering&&recoveryStatus=="","重置未取消旧车脱困任务");cancelled.passed=true;}
                catch(Exception ex){cancelled.error=ex.Message;report.error+=model+" "+cancelled.name+": "+ex+"\n";}
                course.SetActive(false);Destroy(course);yield return null;
            }
            ResetDeveloper();gameThirdPerson=true;UpdateGameCamera();yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"rescue-button.png"));yield return null;
            report.recoveries=recoveryCount;report.passed=report.error==""&&report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("DRIVE_RECOVERY_SMOKE "+JsonUtility.ToJson(report));Application.Quit(report.passed?0:76);
        }
    }
}
