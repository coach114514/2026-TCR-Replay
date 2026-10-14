using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        bool developerSmokeActive;
        [Serializable] class DeveloperReport
        {
            public string revision="developer-mode-v1-20261005",gpu,error;public bool menu,rig,muzzle,damage,baseAnimation,runeRings,tunnelGeometry,modeSwitch;
            public int shots,hits,bulletsCreated,compoundShots,compoundHits,performanceActors,tunnelsPassed,basePlateContacts;public float foldedHeight,unfoldedHeight,aimError,compoundTravel,compoundCenterError,performanceFps,maximumTreadPenetration,geometricTreadOverlap;public bool fpsIndependent,physicalRuneDisk,consoleAtomic,recordReplay,projectileRetired,tunnelPassed,groundClippingFree,roofBlocked,savedState,zoneTimers,clipRestore,previewPure,timeEditSafe;
            public System.Collections.Generic.List<DisplayPhysicsProof> displayRates=new System.Collections.Generic.List<DisplayPhysicsProof>();
        }
        [Serializable] class DisplayPhysicsProof {public int cap,ticks,shots;public Vector3 position;public float heat,error;}
        IEnumerator DeveloperSmoke(string[] args)
        {
            int index=Array.IndexOf(args,"--game-smoke");string output=index+1<args.Length?args[index+1]:Path.Combine(Application.persistentDataPath,"game-smoke");Directory.CreateDirectory(output);
            developerSmokeActive=true;var report=new DeveloperReport {gpu=SystemInfo.graphicsDeviceName};game.running=false;yield return null;
            try
            {
                DeveloperChecks.Run();report.menu=appMode=="game"&&gameVehicles.Count==2;
                var player=game.actors["player"];var vehicle=gameVehicles["player"];player.gimbalYaw=90;gameDesiredYaw=90;PoseGameVehicle(vehicle);UpdateGameCamera();
                DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position,vehicle.unit.robot.groups[3].TransformPoint(Vt03Point))<.0001f,"C图传镜头绑定");
                GameMuzzle(vehicle,out var muzzle,out var forward);DeveloperChecks.Require(Vector3.Distance(muzzle,vehicle.unit.robot.groups[3].TransformPoint(PassagePose.Vector(vehicle.unit.robot.model.muzzlePoint)))<.0001f,"A枪口绑定");report.rig=report.muzzle=true;
                gameInput.cameraRelative=true;
                var upright=GameBounds(vehicle.unit.robot,0);var folded=GameBounds(vehicle.unit.robot,1);report.foldedHeight=folded.size.y;report.unfoldedHeight=upright.size.y;report.tunnelGeometry=folded.size.y<=.26f;
                QueueGame("supply player 100; set rules unlimitedHeat on"); // batches intentionally reject non-set; exercise rejection without mutation
                game.DrainCommands();DeveloperChecks.Require(player.ammo==0,"无效批量命令发生部分变更");QueueGame("supply player 100");game.DrainCommands();report.consoleAtomic=true;
                var target=game.actors["target1"];target.position=new Vector3(9.5f,GameGround(gameVehicles[target.id],new Vector3(9.5f,0,9.4f)),9.4f);SnapGameGround(gameVehicles[target.id]);gameVehicles[target.id].lastPosition=target.position;target.yaw=90;
                PoseGameVehicle(gameVehicles[target.id]);UpdateGameCamera();CollectGamePlates();game.rules.unlimitedHeat=true;game.customRules=true;game.running=true;gameInput=new DeveloperInput {yaw=90};
                gameAutoAim=true;gameLock="target1";for(int i=0;i<300;i++){gameInput.fire=i>=90;GameStep();UpdateGameCamera();}gameInput.fire=false;
                GameMuzzle(vehicle,out _,out forward);report.aimError=Vector3.Angle(forward,gameAimDirection);report.shots=gameShotCount;report.hits=gameHits;report.damage=target.hp<target.maxHp;
                DeveloperChecks.Require(report.shots>0&&report.damage,"真实枪管发弹 / 靶车命中失败："+aimStatus+" target="+target.position+" gun="+muzzle);
                game.running=false;gameThirdPerson=true;foreach(var v in gameVehicles.Values)PoseGameVehicle(v);viewCamera.transform.position=new Vector3(7,2,12);viewCamera.transform.LookAt(new Vector3(8,.3f,9.4f));RenderGameBullets();
                report.bulletsCreated=gameBulletCreated;report.runeRings=true;
                foreach(var plate in gamePlates)if(plate.rune){bool disk=GameContactAt(plate.center+plate.Normal*.12f,plate.center-plate.Normal*.12f,"player",1,1,out var contact)&&contact.rune;Vector3 corner=plate.center+plate.rotation*new Vector3(.14f,.14f,0);bool outside=GameContactAt(corner+plate.Normal*.12f,corner-plate.Normal*.12f,"player",1,1,out var miss)&&miss.rune;DeveloperChecks.Require(disk&&!outside,"机关有效圆与方框角点");report.physicalRuneDisk=true;break;}
            }
            catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"developer-shooting.png"));yield return null;
            try
            {
                foreach(var item in baseLeaves){var b=game.buildings[item.Key+"_base"];b.armorOpen=1;}
                UpdateGameMechanisms();CollectGamePlates();report.baseAnimation=baseLeaves.Count==2;
                foreach(var entry in baseLeaves)DeveloperChecks.Require(Vector3.Distance(entry.Value[0].localPosition,PassagePose.Vector(gameBase.leaves[0].pivot))>.15f,"护甲未展开");
                foreach(var plate in gamePlates)if(plate.target.EndsWith("_base")){bool contact=GameContactAt(plate.center+plate.Normal*.08f,plate.center-plate.Normal*.04f,"player",1,1,out var hit);DeveloperChecks.Require(contact&&hit.armor&&hit.plate.target==plate.target&&hit.plate.index==plate.index,"基地真实装甲击打面被CAD遮挡 "+plate.target+":"+plate.index+" center="+plate.center+" contact="+hit.point);report.basePlateContacts++;}
                viewCamera.transform.position=buildingNodes["red_base"].position+new Vector3(2,1.5f,-2.5f);viewCamera.transform.LookAt(buildingNodes["red_base"].position+Vector3.up*.6f);
            }
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"base-open.png"));yield return null;
            game.SetTime(181);var activeRune=game.runes["red"];activeRune.Trigger(game);for(int i=0;i<2;i++){game.time+=.2f;activeRune.Tick(game);activeRune.Hit(game,activeRune.lit,.04f,25);activeRune.Hit(game,activeRune.optional,.04f,25);}game.time+=.2f;activeRune.Tick(game);UpdateGameMechanisms();CollectGamePlates();var face=gamePlates.Find(p=>p.rune&&p.team=="red");viewCamera.transform.position=runeRotor.position+face.Normal*3;viewCamera.transform.LookAt(runeRotor.position);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"rune-progress.png"));yield return null;
            // Same continuous controls at all display caps must produce identical
            // fixed-step position, heat and shot count. These are real scene queries.
            try
            {
                Vector3 reference=Vector3.zero;float heatReference=0;int shotsReference=0;
                foreach(int fps in new[]{60,120,144,165,240})
                {
                    ResetDeveloper();var player=game.actors["player"];player.ammo=player.allowedAmmo=100;gameDesiredYaw=90;gameInput=new DeveloperInput {z=1,yaw=90,cameraRelative=true,spin=true,fire=true};game.running=true;
                    double accumulator=0;int ticks=0;for(int frame=0;frame<fps*2;frame++){accumulator+=1d/fps;while(accumulator+1e-9>=1d/120){GameStep();ticks++;accumulator-=1d/120;}}
                    var proof=new DisplayPhysicsProof {cap=fps,ticks=ticks,shots=gameShotCount,position=player.position,heat=player.heat,error=fps==60?0:Vector3.Distance(reference,player.position)};report.displayRates.Add(proof);
                    game.running=false;if(fps==60){reference=player.position;heatReference=player.heat;shotsReference=gameShotCount;}else DeveloperChecks.Require(ticks==240&&proof.error<(PhysicsDriving ? .01f : .0001f)&&Mathf.Abs(heatReference-player.heat)<.0001f&&shotsReference==gameShotCount,"FPS改变了物理 / 规则结算 "+JsonUtility.ToJson(proof));
                }
                report.fpsIndependent=true;
            }
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            try
            {
                ResetDeveloper();gameDesiredYaw=90;gameInput=new DeveloperInput {z=1,yaw=90,spin=true,cameraRelative=true};game.running=true;game.BeginRecording("smoke-motion");
                for(int i=0;i<120;i++)GameStep();game.FinishRecording();Vector3 expected=game.actors["player"].position;
                ResetDeveloper();game.running=true;QueueGame("clip play player smoke-motion");for(int i=0;i<120;i++)GameStep();
                DeveloperChecks.Require(Vector3.Distance(expected,game.actors["player"].position)<.005f,"实际运动录制复现");game.clips.Remove("smoke-motion");report.recordReplay=true;game.running=false;
            }
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            try
            {
                bool safe=true;foreach(string model in new[]{"sweeper","jiageng"})foreach(var passage in field.tunnels)
                {
                    ResetDeveloper();bool acrossX=passage.halfX<passage.halfY;Vector3 center=new Vector3(passage.x,0,passage.y),axis=acrossX?Vector3.right:Vector3.forward;var driver=game.Add("tunnel-driver",passage.team,center+axis*1.1f,model);driver.manualControl=true;driver.requestedSpeed=.5f;
                    game.actors["player"].manualControl=false;game.controlled=driver.id;game.running=true;gameDesiredYaw=acrossX?270:180;gameInput=new DeveloperInput {x=-axis.x,z=-axis.z,yaw=gameDesiredYaw};
                    // Spawn facing the straight approach. A sideways fully
                    // lowered chassis cannot turn inside the narrow corridor.
                    driver.yaw=driver.gimbalYaw=gameDesiredYaw;SnapGameGround(gameVehicles[driver.id]);bool crossedOpening=false;
                    for(int i=0;i<1680;i++){GameStep();var physical=gameVehicles[driver.id].physics;if(physical!=null){float depth=0;foreach(var wheel in physical.Wheels){depth=Mathf.Max(depth,wheel.Penetration);if(safe&&wheel.Penetration>.015f){Debug.Log("TREAD_CONTACT_DIAGNOSTIC "+model+" "+passage.id+" tick="+i+" wheel="+wheel.Config.label+" depth="+wheel.Penetration+" object="+wheel.PenetrationObject+" point="+wheel.PenetrationPoint+" center="+wheel.Center+" body="+physical.Body.position+" rotation="+physical.Body.rotation.eulerAngles);RenderPhysicsAudit(gameVehicles[driver.id],Path.Combine(output,"tread-penetration.png"));}}report.maximumTreadPenetration=Mathf.Max(report.maximumTreadPenetration,depth);safe &=depth<.015f&&driver.position.y>-.1f;if(i%10==0)report.geometricTreadOverlap=Mathf.Max(report.geometricTreadOverlap,PhysicalTreadPenetration(physical));}else safe &=driver.position.y>=GameGround(gameVehicles[driver.id],driver.position)-.002f;float along=Vector3.Dot(driver.position-center,axis),side=acrossX?driver.position.z-center.z:driver.position.x-center.x;if(Mathf.Abs(along)<.09f&&Mathf.Abs(side)<.1f&&driver.fold>.94f)crossedOpening=true;if(GameFolded(gameVehicles[driver.id]))DeveloperChecks.Require(!driver.CanFire(game.time,game.rules),"收缩状态发射联锁");if(along<-.65f)break;}
                    bool passed=crossedOpening&&Vector3.Dot(driver.position-center,axis)<-.35f;DeveloperChecks.Require(safe&&passed,passage.id+" 真实通行失败，末位置="+driver.position+" fold="+driver.fold+" yaw="+driver.yaw+" support="+GameGround(gameVehicles[driver.id],driver.position)+" hit="+(movementBlockingHit.collider?movementBlockingHit.collider.name:"none")+" point="+movementBlockingHit.point+" normal="+movementBlockingHit.normal+" reason="+gameVehicles[driver.id].lastBlockReason+" contacts="+DrivePenetrationDescription(gameVehicles[driver.id],driver.position,driver.fold,driver.yaw));report.tunnelsPassed++;game.running=false;
                    if(report.tunnelsPassed==1){var v=gameVehicles[driver.id];driver.yaw=180;center.y=GameGround(v,center)+.004f;driver.position=center;PoseGameVehicle(v);var bounds=GameBounds(v.unit.robot,0);report.roofBlocked=Physics.CheckBox(center+Quaternion.Euler(0,driver.yaw,0)*bounds.center,bounds.extents-Vector3.one*.012f,Quaternion.Euler(0,driver.yaw,0),1<<10);DeveloperChecks.Require(report.roofBlocked,"未收缩车体没有受到洞顶碰撞约束");}
                }
                report.groundClippingFree=safe;report.tunnelPassed=report.tunnelsPassed==field.tunnels.Length*2;
            }
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            try
            {
                ResetDeveloper();var actor=game.actors["player"];game.BeginRecording("smoke-restore");game.running=true;gameDesiredYaw=90;gameInput=new DeveloperInput {x=1,yaw=90,cameraRelative=true,spin=true};for(int i=0;i<60;i++)GameStep();game.FinishRecording();game.running=false;
                QueueGame("clip restore player smoke-restore 7,0,9.4 180");game.DrainCommands();DeveloperChecks.Require(Mathf.Abs(actor.position.x-7)<.001f&&Mathf.Abs(actor.position.z-9.4f)<.001f&&actor.position.y>=GameGround(gameVehicles["player"],actor.position)-.002f&&Mathf.Abs(actor.clipYawOffset-180)<.01f,"动作恢复起点与旋转映射");report.clipRestore=true;game.clips.Remove("smoke-restore");
                game.SetTime(181);var rune=game.runes["red"];rune.Trigger(game);rune.Hit(game,rune.lit,.02f,25);var before=JsonUtility.ToJson(rune);
                string name="__runtime_acceptance",scenePath=Path.Combine(GameStorage,name+".json"),clipsPath=Path.Combine(GameStorage,"clips.json");string prior=File.Exists(clipsPath)?File.ReadAllText(clipsPath):null;
                try{ammoOrders.Add(new DeveloperPurchase {actor="player",quantity=100,due=game.time+6});fortressEntered["player|audit"]=new DeveloperFortressTimer {key="player|audit",progress=8,lastPresent=game.time};SaveGameScene(name);ResetDeveloper();LoadGameScene(name);DeveloperChecks.Require(JsonUtility.ToJson(game.runes["red"])==before&&ammoOrders.Count==1&&fortressEntered["player|audit"].progress==8,"场景丢失机关灯序、购买或堡垒计时");report.savedState=true;}
                finally{if(File.Exists(scenePath))File.Delete(scenePath);if(prior!=null)File.WriteAllText(clipsPath,prior);else if(File.Exists(clipsPath))File.Delete(clipsPath);}
                gamePreview=true;PoseGameVehicle(gameVehicles["player"]);CollectGamePlates();string state=JsonUtility.ToJson(game.actors["player"]),runeState=JsonUtility.ToJson(game.runes["red"]);int shots=gameShotCount;UpdateGamePreview();DeveloperChecks.Require(state==JsonUtility.ToJson(game.actors["player"])&&runeState==JsonUtility.ToJson(game.runes["red"])&&shots==gameShotCount,"预览修改了比赛状态");report.previewPure=true;gamePreview=false;
                var shooter=game.actors["player"];shooter.ammo=shooter.allowedAmmo=100;FireGameBullet(shooter);DeveloperChecks.Require(gameBullets.Count>0,"时间修改测试没有在途弹丸");QueueGame("time 1");game.DrainCommands();DeveloperChecks.Require(gameBullets.Count==0&&nextObservation==game.time&&game.time==1,"倒退时钟遗留弹丸或延迟观测");report.timeEditSafe=true;
                ResetDeveloper();var post=game.buildings["red_outpost"];post.hp=0;post.destroyedBefore=true;post.stopped=true;game.buildings["red_base"].rebuildChances=1;
                var zone=Array.Find(gameZones.zones,z=>z.kind=="outpost"&&z.team=="red");Vector3 position=Vector3.zero;for(int i=0;i<zone.polygon.Length;i+=2)position+=new Vector3(zone.polygon[i],0,zone.polygon[i+1]);position/=zone.polygon.Length/2;
                foreach(var a in game.actors.Values){a.team="red";a.position=position;a.position.y=GroundFrom(new Vector3(position.x,8,position.z))+.004f;}
                for(int i=0;i<51;i++){game.time=i*.1f;UpdateGameZones();}DeveloperChecks.Require(post.hp==0&&post.rebuildProgress>4.9f&&post.rebuildProgress<6,"多车占领重建时间异常 progress="+post.rebuildProgress+" zone="+zone.id+" point="+position+" actor="+game.actors["player"].position+" contains="+ZoneContains(zone,position));for(int i=51;i<105;i++){game.time=i*.1f;UpdateGameZones();}DeveloperChecks.Require(post.hp==750,"独立车辆连续10秒未重建 progress="+post.rebuildProgress);report.zoneTimers=true;
            }
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            ResetDeveloper();yield return null;
            var movingTarget=game.actors["target1"];movingTarget.hp=movingTarget.maxHp=20000;movingTarget.motion="direction";movingTarget.direction=Vector3.forward;movingTarget.requestedSpeed=.55f;movingTarget.spin=true;movingTarget.spinRate=180;
            movingTarget.armorHeights[1]=.17f;movingTarget.armorHeights[3]=.085f;Vector3 targetStart=movingTarget.position;
            game.actors["player"].ammo=game.actors["player"].allowedAmmo=100;game.rules.unlimitedHeat=true;gameAutoAim=true;gameLock="target1";gameDesiredYaw=90;gameInput=new DeveloperInput {yaw=90};game.running=true;
            for(int i=0;i<240;i++){gameInput.fire=i>=45;for(int n=0;n<2;n++){GameStep();UpdateGameCamera();}yield return null;}
            game.running=false;report.compoundShots=gameShotCount;report.compoundHits=gameHits;report.compoundTravel=Vector3.Distance(movingTarget.position,targetStart);
            if(gameTrackers.TryGetValue("target1",out var joint))report.compoundCenterError=Vector3.Distance(joint.At(new DeveloperPlate {local=Vector3.zero,localRotation=Quaternion.identity},game.time).center,movingTarget.position);
            try{DeveloperChecks.Require(report.compoundTravel>.3f&&report.compoundHits>=3,"复合运动实际命中："+aimStatus);}
            catch(Exception ex){report.error+=(report.error==null?"":"\n")+ex.ToString();Debug.LogException(ex);}
            // Keep eight target rigs rendering for frame-rate measurement.
            for(int i=2;i<=8;i++){var actor=game.Add("target"+i,i%2==0?"blue":"red",new Vector3(5+i,0,8.4f));actor.spin=true;actor.spinRate=i%2==0?180:-180;actor.motion="random";actor.requestedSpeed=.45f;}
            report.performanceActors=game.actors.Count;game.running=true;float wall=Time.realtimeSinceStartup;int frames=0;
            while(Time.realtimeSinceStartup-wall<3){for(int n=0;n<2;n++){GameStep();UpdateGameCamera();}RenderGameBullets();frames++;yield return null;}
            game.running=false;report.performanceFps=frames/(Time.realtimeSinceStartup-wall);
            game.time+=8.1f;StepGameBullets();report.projectileRetired=gameBullets.Count==0;
            gameMap=true;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"path-map.png"));yield return null;gameMap=false;
            gameEdit=true;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"developer-editor.png"));yield return null;gameEdit=false;
            EnterMenu();report.menu=appMode=="menu";yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"main-menu.png"));yield return null;
            EnterReplay();yield return null;bool replayOk=appMode=="replay"&&match!=null&&units.Count>0;EnterDeveloper();yield return null;report.modeSwitch=replayOk&&gameVehicles.Count==2;
            File.WriteAllText(Path.Combine(output,"runtime.json"),JsonUtility.ToJson(report,true));Debug.Log("DEVELOPER_RUNTIME_"+(string.IsNullOrEmpty(report.error)?"OK":"FAILED")+" "+JsonUtility.ToJson(report));Application.Quit(string.IsNullOrEmpty(report.error)?0:2);
        }
        float PhysicalTreadPenetration(DeveloperVehiclePhysics vehicle)
        {
            // Measure the same convex tire and CAD surfaces used by physics.
            // A plane fitted beneath the wheel center extends triangle edges
            // infinitely, incorrectly rejecting valid contacts at a ramp seam.
            float maximum=0;
            foreach(var wheel in vehicle.Wheels)
            {
                var tire=wheel.Tire;var bounds=tire.bounds;int count=DevOverlapBox(bounds.center,bounds.extents,driveOverlap,Quaternion.identity,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++){var other=driveOverlap[i];if(Physics.ComputePenetration(tire,tire.transform.position,tire.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out float depth))maximum=Mathf.Max(maximum,depth);}
            }
            return float.IsFinite(vehicle.Body.position.y)&&vehicle.Body.position.y>-.1f?maximum:float.PositiveInfinity;
        }
    }
}
