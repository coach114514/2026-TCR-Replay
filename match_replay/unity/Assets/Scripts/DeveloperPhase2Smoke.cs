using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class Phase2Report
        {
            public string revision="developer-phase2-20261005",gpu,error;
            public bool logic,models,ownBody,stepLimit,stableWall,ramp,navigation,occupiedGoal,outposts,lidarFirstReturn,lidarWindow,lidarBounded,lidarOff,runeObservation,capBurst,replayCloud,replayLifecycle;
            public float sweeperHeight,mecanumHeight,wallYVariation,noCloudFps,dualCloudFps,capUsed,actualScanHz,scanElapsedMs;
            public int redPoints,bluePoints,dualPoints,cloudPeak,navNodes,runeObservations;
        }
        IEnumerator Phase2Smoke(string[] args)
        {
            developerSmokeActive=true;int index=Array.IndexOf(args,"--phase2-smoke");
            string output=index+1<args.Length?args[index+1]:Path.Combine(Application.persistentDataPath,"phase2-smoke");Directory.CreateDirectory(output);game.running=false;yield return null;
            var report=new Phase2Report {gpu=SystemInfo.graphicsDeviceName};GameObject fixtures=null;
            try
            {
                DeveloperChecks.Run();report.logic=true;
                foreach(string model in new[]{"sweeper","jiageng"})
                {
                    var a=game.actors["player"];ChangeGameModel(a,model);var v=gameVehicles[a.id];a.gimbalYaw=90;a.pitch=10;PoseGameVehicle(v);UpdateGameCamera();GameMuzzle(v,out _,out var forward);
                    DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position,v.unit.robot.groups[v.eyeGroup].TransformPoint(v.eyePoint))<.0001f&&Vector3.Angle(viewCamera.transform.forward,forward)<.001f,"镜头 / 枪管姿态挂点错误 "+model);
                    foreach(var renderer in v.unit.renderers)DeveloperChecks.Require(!renderer.forceRenderingOff,"第一人称隐藏自身车体");
                    if(model=="sweeper")report.sweeperHeight=v.compressedHeight;else report.mecanumHeight=v.compressedHeight;
                    DeveloperChecks.Require(v.compressedHeight>.15f&&v.compressedHeight<.3f,"完全压缩高度测量错误 "+v.compressedHeight);report.models=report.ownBody=true;
                    fixtures=new GameObject("Phase2 physics course");CourseBox(fixtures,new Vector3(14,19.95f,7.5f),new Vector3(14,.1f,10),8);
                    float low=StepLimit(v)*.8f;CourseBox(fixtures,new Vector3(13,20+low*.5f,7.5f),new Vector3(1,low,2),8);CourseBox(fixtures,new Vector3(13,20+low*.5f,7.5f),new Vector3(1,low,2),10);
                    Physics.SyncTransforms();a.position=new Vector3(11.5f,20.004f,7.5f);a.yaw=a.gimbalYaw=90;a.velocity=Vector3.zero;a.verticalVelocity=0;a.requestedSpeed=1;a.fold=v.unit.robot.model.defaultFold;
                    for(int i=0;i<400;i++)MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90});DeveloperChecks.Require(a.position.x>13.55f,"低台阶无法通过 "+model+" "+a.position);fixtures.SetActive(false);Destroy(fixtures);
                    fixtures=new GameObject("Phase2 wall course");CourseBox(fixtures,new Vector3(14,19.95f,7.5f),new Vector3(14,.1f,10),8);float high=StepLimit(v)*1.3f;CourseBox(fixtures,new Vector3(13,20+high*.5f,7.5f),new Vector3(1,high,2),8);CourseBox(fixtures,new Vector3(13,20+high*.5f,7.5f),new Vector3(1,high,2),10);
                    Physics.SyncTransforms();a.position=new Vector3(11.5f,20.004f,7.5f);a.velocity=Vector3.zero;a.verticalVelocity=0;float minY=100,maxY=0;
                    for(int i=0;i<500;i++){MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90});if(i>250){minY=Mathf.Min(minY,a.position.y);maxY=Mathf.Max(maxY,a.position.y);}}
                    DeveloperChecks.Require(a.position.x<12.5f,"高台阶越界 "+model+" "+a.position);report.wallYVariation=Mathf.Max(report.wallYVariation,maxY-minY);DeveloperChecks.Require(maxY-minY<.005f,"顶墙时车体跳动");report.stepLimit=report.stableWall=true;
                    fixtures.SetActive(false);Destroy(fixtures);fixtures=null;
                    fixtures=new GameObject("Phase2 slope course");CourseBox(fixtures,new Vector3(14,19.95f,7.5f),new Vector3(14,.1f,10),8);
                    var rampObject=GameObject.CreatePrimitive(PrimitiveType.Cube);rampObject.transform.SetParent(fixtures.transform);rampObject.transform.position=new Vector3(13,20.45f,7.5f);rampObject.transform.localScale=new Vector3(3,.1f,2);rampObject.transform.rotation=Quaternion.Euler(0,0,18);rampObject.layer=8;rampObject.GetComponent<Renderer>().enabled=false;
                    a.position=new Vector3(11.7f,20.104f,7.5f);a.velocity=Vector3.zero;a.verticalVelocity=0;Physics.SyncTransforms();
                    float peak=a.position.y;for(int i=0;i<720;i++){MoveGameVehicle(v,new DeveloperInput{x=1,yaw=90});peak=Mathf.Max(peak,a.position.y);}DeveloperChecks.Require(a.position.x>12.5f&&peak>20.3f,"连续坡道无法通行 "+model+" "+a.position);report.ramp=true;fixtures.SetActive(false);Destroy(fixtures);fixtures=null;
                }
                ResetDeveloper();game.running=true;game.rules.infiniteAmmo=true;game.rules.unlimitedHeat=true;var driver=game.actors["player"];float cap=driver.capEnergy;gameDesiredYaw=90;gameInput=new DeveloperInput {z=1,yaw=90,cameraRelative=true,burst=true};for(int i=0;i<360;i++)GameStep();report.capUsed=cap-driver.capEnergy;DeveloperChecks.Require(report.capUsed>1,"Ctrl爆发未使用电容");report.capBurst=true;game.running=false;
                foreach(string team in new[]{"red","blue"}){var post=game.buildings[team+"_outpost"];post.hp=0;post.destroyedBefore=true;post.stopped=true;post.rotationStopAt=2;game.RestoreOutpost(team);DeveloperChecks.Require(post.hp==1500&&post.stopped&&post.destroyedBefore,"前哨复活状态错误");}report.outposts=true;
            }
            catch(Exception ex){Phase2Error(report,ex);}
            if(fixtures){fixtures.SetActive(false);Destroy(fixtures);}ResetDeveloper();
            // Navigation runs once per display frame. The test course is isolated
            // above the CAD field so actual wall/vehicle collision can be audited.
            fixtures=new GameObject("Phase2 navigation course");CourseBox(fixtures,new Vector3(14,19.95f,7.5f),new Vector3(14,.1f,10),8);CourseBox(fixtures,new Vector3(12,20.5f,7.5f),new Vector3(.4f,1,2.2f),10);
            var navActor=game.actors["target1"];navActor.position=new Vector3(10,20.004f,7.5f);navActor.velocity=Vector3.zero;navActor.yaw=90;navActor.motion="path";navActor.path=new List<Vector3>{new Vector3(14,20.004f,7.5f)};navActor.pathLoop=false;navActor.requestedSpeed=1.2f;PoseGameVehicle(gameVehicles[navActor.id]);Physics.SyncTransforms();game.running=true;
            for(int frame=0;frame<1500&&navActor.motion!="still";frame++){for(int step=0;step<4;step++)GameStep();yield return null;}
            try {DeveloperChecks.Require(Vector2.Distance(new Vector2(navActor.position.x,navActor.position.z),new Vector2(14,7.5f))<.3f,"导航绕墙未到达 "+navActor.position);report.navigation=true;}
            catch(Exception ex){Phase2Error(report,ex);}
            var blocker=game.actors["player"];blocker.position=new Vector3(14,20.004f,7.5f);blocker.manualControl=false;blocker.velocity=Vector3.zero;PoseGameVehicle(gameVehicles[blocker.id]);navActor.position=new Vector3(10,20.004f,7.5f);navActor.velocity=Vector3.zero;navActor.motion="path";navActor.pathIndex=0;navActor.pathLoop=false;navigation.Clear();
            for(int frame=0;frame<1500&&!(navigation.TryGetValue(navActor.id,out var parked)&&parked.temporary&&Vector3.Distance(navActor.position,parked.effective)<.16f);frame++){for(int step=0;step<4;step++)GameStep();yield return null;}
            try {DeveloperChecks.Require(navigation.TryGetValue(navActor.id,out var route)&&route.temporary&&!route.unreachable&&Vector3.Distance(navActor.position,route.effective)<.3f&&!VehicleOverlap(gameVehicles[navActor.id],navActor.position,gameVehicles[blocker.id]),"占用目标未停靠邻近可达点 "+navActor.position+" yaw="+navActor.yaw+" blocked="+navActor.blocked+" reason="+gameVehicles[navActor.id].lastBlockReason+(navigation.TryGetValue(navActor.id,out var debugRoute)?" effective="+debugRoute.effective+" temporary="+debugRoute.temporary+" unreachable="+debugRoute.unreachable+" index="+debugRoute.index+" route="+debugRoute.route.Count+" searching="+(debugRoute.search!=null):" no route"));report.occupiedGoal=true;}
            catch(Exception ex){Phase2Error(report,ex);}
            game.running=false;fixtures.SetActive(false);Destroy(fixtures);ResetDeveloper();gameThirdPerson=false;UpdateGameCamera();
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"first-person-body.png"));yield return null;
            // Display only: this diagnostic writes reports/screenshots, never point
            // clouds or a sensor recording. The application has no export endpoint.
            foreach(int source in new[]{0,1,2})
            {
                lidarSettings=new LidarSettings {source=source,viewMode=1,colorMode=2};SetLidar(true);
                float timeout=Time.realtimeSinceStartup+30;while(lidarFrame==0&&Time.realtimeSinceStartup<timeout)yield return null;
                // Frame index persists across toggles; wait explicitly for data.
                while(lidarPointCount==0&&Time.realtimeSinceStartup<timeout)yield return null;
                try
                {
                    DeveloperChecks.Require(lidarPointCount>1000,"机位没有有效点云 source="+source+" count="+lidarPointCount);
                    if(source==0)report.redPoints=lidarPointCount;else if(source==1)report.bluePoints=lidarPointCount;else report.dualPoints=lidarPointCount;
                    for(int i=0;i<lidarPointCount;i+=Mathf.Max(1,lidarPointCount/100))
                    {var point=lidarPointArray[i];Vector3 origin=LidarOrigin((int)point.meta.x),delta=point.position-origin;DeveloperChecks.Require(lidarGeometry.PhysicsScene.Raycast(origin,delta.normalized,out var hit,delta.magnitude+.02f,~0,QueryTriggerInteraction.Ignore)&&Mathf.Abs(hit.distance-delta.magnitude)<.025f,"第一回波被遮挡面穿透");}report.lidarFirstReturn=true;
                }
                catch(Exception ex){Phase2Error(report,ex);}
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"lidar-"+source+".png"));yield return null;
                if(source==2){try{DeveloperChecks.Require(lidarTexture&&lidarBuffer!=null,"独立点云视图未创建");report.lidarWindow=true;}catch(Exception ex){Phase2Error(report,ex);}}
                SetLidar(false);yield return null;
            }
            QualitySettings.vSyncCount=0;Application.targetFrameRate=240;game.running=true;gameInput=new DeveloperInput();game.actors["target1"].spin=true;
            float begin=Time.realtimeSinceStartup;double realtimeSteps=0;for(int frame=0;frame<180;frame++){realtimeSteps+=Time.unscaledDeltaTime;while(realtimeSteps>=DeveloperWorld.Step){GameStep();realtimeSteps-=DeveloperWorld.Step;}yield return null;}report.noCloudFps=180/(Time.realtimeSinceStartup-begin);
            lidarSettings=new LidarSettings {source=2,accumulation=1,viewMode=1};SetLidar(true);begin=Time.realtimeSinceStartup;
            for(int frame=0;frame<300;frame++){realtimeSteps+=Time.unscaledDeltaTime;while(realtimeSteps>=DeveloperWorld.Step){GameStep();realtimeSteps-=DeveloperWorld.Step;}report.cloudPeak=Math.Max(report.cloudPeak,lidarPointCount);yield return null;}
            report.dualCloudFps=300/(Time.realtimeSinceStartup-begin);report.actualScanHz=lidarActualHz;report.scanElapsedMs=lidarScanMs;
            try {DeveloperChecks.Require(report.cloudPeak>0&&report.cloudPeak<=LidarPointLimit,"累积点云未遵守内存上限");report.lidarBounded=true;SetLidar(false);DeveloperChecks.Require(!lidarPending&&!lidarCommands.IsCreated&&lidarBuffer==null&&lidarGeometry==null&&lidarPointCount==0,"关闭扫描没有释放资源");report.lidarOff=true;}
            catch(Exception ex){Phase2Error(report,ex);}
            SetLidar(false);ResetDeveloper();game.SetTime(181);game.running=true;game.rules.infiniteAmmo=game.rules.unlimitedHeat=true;game.rules.aimFireGate=true;
            var runeFace=gamePlates.Find(p=>p.rune&&p.team=="red");Vector3 runeCenter=runeRotor.parent.TransformPoint(PassagePose.Vector(mechanismConfig.rune.pivot));var runeDriver=game.actors["player"];runeDriver.position=runeCenter+runeFace.Normal*6;runeDriver.position.y=0;SnapGameGround(gameVehicles[runeDriver.id]);
            Vector3 look=(runeCenter-runeDriver.position).normalized;gameDesiredYaw=Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg;gameDesiredPitch=Mathf.Clamp(Mathf.Asin(look.y)*Mathf.Rad2Deg,-30,30);runeDriver.gimbalYaw=gameDesiredYaw;runeDriver.pitch=gameDesiredPitch;PoseGameVehicle(gameVehicles[runeDriver.id]);UpdateGameCamera();CollectGamePlates();game.runes["red"].Trigger(game);gameAutoAim=true;gameLock="rune_red";gameInput=new DeveloperInput {yaw=gameDesiredYaw,pitch=gameDesiredPitch,cameraRelative=true,fire=true};
            for(int frame=0;frame<480;frame++){for(int step=0;step<2;step++)GameStep();yield return null;}
            try {DeveloperChecks.Require(runeTrackers.TryGetValue("rune_red",out var tracked)&&tracked.ready&&tracked.updates>20,"实际机关未产生相位观测");report.runeObservations=tracked.updates;DeveloperChecks.Require(gameRuneHits>0,"实际机关自瞄未命中："+aimStatus+" pos="+runeDriver.position+" tracker="+tracked.Classification);report.runeObservation=true;}
            catch(Exception ex){Phase2Error(report,ex);}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"rune-autoaim.png"));yield return null;
            game.running=false;EnterReplay();Seek(93);lidarSettings=new LidarSettings {source=2,viewMode=1,colorMode=2};SetLidar(true);
            float replayTimeout=Time.realtimeSinceStartup+30;while(lidarPointCount==0&&Time.realtimeSinceStartup<replayTimeout)yield return null;
            try {DeveloperChecks.Require(lidarPointCount>1000&&appMode=="replay","历史回放模式没有有效点云");report.replayCloud=true;Seek(5);DeveloperChecks.Require(lidarPointCount==0&&!lidarPending,"回放跳转遗留旧扫描帧");LoadMatch(1);DeveloperChecks.Require(lidarPointCount==0&&!lidarPending,"换局遗留旧扫描帧");}
            catch(Exception ex){Phase2Error(report,ex);}
            replayTimeout=Time.realtimeSinceStartup+30;while(lidarPointCount==0&&Time.realtimeSinceStartup<replayTimeout)yield return null;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"replay-cloud.png"));yield return null;
            try {EnterMenu();DeveloperChecks.Require(!lidarEnabled&&lidarGeometry==null&&lidarBuffer==null,"返回菜单没有停止扫描");report.replayLifecycle=true;}
            catch(Exception ex){Phase2Error(report,ex);}
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("PHASE2_SMOKE "+JsonUtility.ToJson(report));Application.Quit(string.IsNullOrEmpty(report.error)?0:1);
        }
        void CourseBox(GameObject parent,Vector3 position,Vector3 size,int layer)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name="Physics course";o.transform.SetParent(parent.transform);o.transform.position=position;o.transform.localScale=size;o.layer=layer;o.GetComponent<Renderer>().enabled=false;}
        static void Phase2Error(Phase2Report report,Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
    }
}
