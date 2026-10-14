using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class RuneAutoCase
        {
            public string team,kind,status;public int direction,priorSuccesses,shots,hits,retries,resets,twoFrameSamples,confirmedTransitions,predictionDrops;
            public string[] events;
            public int[] rings;public float averageRing;public int minimumRing;
            public float distance,seconds,maxYawStep,maxPitchStep,maxFifthYawStep,maxFifthPitchStep,maxContinuousPredictionStep,maxFifthPredictionStep;public bool passed,missInjected,noisy;
        }
        [Serializable] class RuneAutoReport
        {
            public string gpu,error="";public bool passed,manualDuringWait,modalStopsTrigger,offStopsTrigger,confirmedOnly;
            public List<RuneAutoCase> cases=new List<RuneAutoCase>();
        }
        void PlaceRuneDriver(string team,float distance)
        {
            CopyGameFields(new DeveloperRules(),game.rules);ResetDeveloper();var actor=game.actors[game.controlled];actor.team=team;actor.ammo=actor.allowedAmmo=100;
            var face=gamePlates.Find(p=>p.rune&&p.team==team);Vector3 center=runeRotor.parent.TransformPoint(PassagePose.Vector(mechanismConfig.rune.pivot));
            actor.position=center+face.Normal*distance;actor.position.y=0;SnapGameGround(gameVehicles[actor.id]);
            Vector3 look=(center-(actor.position+Vector3.up*.29f)).normalized;
            gameDesiredYaw=actor.gimbalYaw=Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg;gameDesiredPitch=actor.pitch=Mathf.Clamp(Mathf.Asin(look.y)*Mathf.Rad2Deg,-30,30);
            PoseGameVehicle(gameVehicles[actor.id]);UpdateGameCamera();CollectGamePlates();
            DeveloperChecks.Require(TryGameAimCandidate(out var first)&&first.target=="rune_"+team,"实际枪管视角R标不可选 "+team+" distance="+distance);
            ToggleGameAim();gameInput=new DeveloperInput {yaw=actor.gimbalYaw,pitch=actor.pitch,cameraRelative=true,fire=false};
        }
        IEnumerator RuneAutoSmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--rune-auto-smoke")+1];Directory.CreateDirectory(output);developerSmokeActive=true;allLabels=false;
            var report=new RuneAutoReport {gpu=SystemInfo.graphicsDeviceName};yield return null;
            try
            {
                DeveloperChecks.Run();PlaceRuneDriver("red",8);game.running=true;game.runes["red"].Training(game,"small",true);
                gameInput.fire=true;GameStep();DeveloperChecks.Require(gameShotCount==1&&runeAim.autoShots==0&&game.runes["red"].startPending,"等待期禁止了手动发弹");report.manualDuringWait=true;
            }
            catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
            foreach(string team in new[]{"red","blue"})foreach(string kind in new[]{"small","large"})foreach(float distance in new[]{6f,9f,8f})
            {
                var test=new RuneAutoCase {team=team,kind=kind,distance=distance,direction=distance==6?1:-1,noisy=distance==8};report.cases.Add(test);
                try
                {
                    PlaceRuneDriver(team,distance);game.direction=test.direction;game.running=true;var r=game.runes[team];r.Training(game,kind,true);
                    if(test.noisy){game.rules.observationNoise=.003f;game.rules.observationDelay=.04f;game.rules.observationDrop=.08f;}
                    var actor=game.actors[game.controlled];for(int i=0;i<359;i++)GameStep();DeveloperChecks.Require(gameShotCount==0&&r.startPending,"等待期自动扳机提前发弹");
                }
                catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);continue;}
                bool captured=false,haveFrame=false;int priorPanel=-1,priorGroup=-1,priorHit=-1,priorAim=-1;Vector3 priorDirection=Vector3.forward;
                for(int i=0;i<2450;i++)
                {
                    var actor=game.actors[game.controlled];var r=game.runes[team];float yawBefore=actor.gimbalYaw,pitchBefore=actor.pitch;int groupBefore=r.groupSerial,hitBefore=r.hitSerial;
                    // Stale mouse commands used to snap the gimbal back whenever
                    // a prediction became temporarily unavailable.
                    gameDesiredYaw=90;gameDesiredPitch=0;GameStep();UpdateGameCamera();
                    float dy=Mathf.Abs(Mathf.DeltaAngle(yawBefore,actor.gimbalYaw)),dp=Mathf.Abs(actor.pitch-pitchBefore);
                    test.maxYawStep=Mathf.Max(test.maxYawStep,dy);test.maxPitchStep=Mathf.Max(test.maxPitchStep,dp);
                    if(kind=="small"&&r.groups==4){test.maxFifthYawStep=Mathf.Max(test.maxFifthYawStep,dy);test.maxFifthPitchStep=Mathf.Max(test.maxFifthPitchStep,dp);}
                    if(runePredictions.Count==2)test.twoFrameSamples++;
                    if(haveFrame&&gameAimValid&&r.groupSerial==priorGroup&&runeAim.current==priorPanel&&aimPlate==priorAim&&priorPanel>=0)
                    {
                        float change=Vector3.Angle(priorDirection,gameAimDirection);test.maxContinuousPredictionStep=Mathf.Max(test.maxContinuousPredictionStep,change);
                        if(kind=="small"&&r.groups==4)test.maxFifthPredictionStep=Mathf.Max(test.maxFifthPredictionStep,change);
                    }
                    if(haveFrame&&r.activating&&r.groupSerial==priorGroup&&runePredictions.Count==0)test.predictionDrops++;
                    if(r.activating&&r.groupSerial==priorGroup&&runeAim.current!=priorPanel&&runeAim.current>=0&&priorPanel>=0)
                    {
                        if(r.hitSerial>priorHit||runeAim.confirmed.Contains(priorPanel))test.confirmedTransitions++;
                        else report.error+=$"未命中前换靶 {team} {kind} distance={distance} current={runeAim.current} prior={priorPanel} first={runeAim.first} second={runeAim.second} hits={r.hitSerial} priorHits={priorHit} groups={r.groupSerial}/{priorGroup} aimGroup={runeAim.group} observed={string.Join(",",runeTrackers[gameLock].alreadyHit)} confirmed={string.Join(",",runeAim.confirmed)}\n";
                    }
                    haveFrame=runePredictions.Count>0;priorGroup=r.groupSerial;priorPanel=runeAim.current;priorHit=r.hitSerial;priorAim=aimPlate;priorDirection=gameAimDirection;
                    if(!captured&&haveFrame&&((kind=="large"&&runePredictions.Count==2)||(kind=="small"&&r.groups==4)))
                    {
                        captured=true;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-"+kind+"-"+distance+"-tracking.png"));yield return null;
                    }
                    if(r.successes>0||!r.activating&&!r.startPending&&i>120)break;
                }
                var result=game.runes[team];test.seconds=game.time-result.started;test.shots=gameShotCount;test.hits=gameRuneHits;test.retries=runeAim.retries;test.resets=result.resets;test.status=aimStatus;RuneRingMetrics(test,result);
                test.passed=result.successes==1&&result.groups==5&&test.hits>=5&&test.predictionDrops==0&&test.maxYawStep<=3.001f&&test.maxPitchStep<=3.001f&&test.maxFifthPredictionStep<1&&(kind!="large"||test.twoFrameSamples>0);
                if(!test.passed){test.events=game.messages.ToArray();report.error+="自动完成失败 "+JsonUtility.ToJson(test)+"\n";}
                Debug.Log("RUNE_AUTO_CASE "+JsonUtility.ToJson(test));File.WriteAllText(Path.Combine(output,"progress.json"),JsonUtility.ToJson(report,true));
                game.running=false;yield return null;
            }
            foreach(int prior in new[]{1,2})
            {
                var test=new RuneAutoCase {team="red",kind="large",distance=8,direction=1,priorSuccesses=prior};report.cases.Add(test);
                try
                {
                    PlaceRuneDriver("red",8);game.running=true;var r=game.runes["red"];r.Training(game,"large",true);r.successes=prior;game.rules.unlimitedHeat=true;
                    for(int i=0;i<2700&&r.successes==prior;i++)GameStep();test.shots=gameShotCount;test.hits=gameRuneHits;test.seconds=game.time-r.started;test.status=aimStatus;RuneRingMetrics(test,r);
                    test.passed=r.successes==prior+1&&r.hits>=5;DeveloperChecks.Require(test.passed,"收缩有效环区不能自动完成 "+prior);
                }
                catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
            }
            try
            {
                var test=new RuneAutoCase {team="red",kind="small",distance=8,direction=1};report.cases.Add(test);
                PlaceRuneDriver("red",8);game.rules.unlimitedHeat=true;game.running=true;var r=game.runes["red"];r.Training(game,"small",true);
                int panel=-1;float until=0;
                for(int i=0;i<2700&&r.successes==0;i++)
                {
                    GameStep();
                    if(gameShotCount==1&&!test.missInjected)
                    {var bullet=gameBullets.Find(b=>b.id==1);DeveloperChecks.Require(bullet!=null,"未找到实际弹丸");bullet.velocity+=Vector3.up*14;test.missInjected=true;panel=runeAim.current;until=runeAim.feedbackAt;}
                    if(test.missInjected&&game.time<until-.02f)
                        DeveloperChecks.Require(gameAutoAim&&gameLock=="rune_red"&&gameAimValid&&runePredictions.Count>0&&runeAim.current==panel&&gameRuneHits==0,"实际到弹前撤框、断自瞄或切靶");
                }
                test.shots=gameShotCount;test.hits=gameRuneHits;test.retries=runeAim.retries;RuneRingMetrics(test,r);test.passed=r.successes==1&&test.retries>0&&test.shots>test.hits;
                DeveloperChecks.Require(test.passed,"故意失弹后未补打完成");report.confirmedOnly=true;
                PlaceRuneDriver("red",8);game.running=true;game.runes["red"].Training(game,"small",true);for(int i=0;i<385;i++)GameStep();
                gameEdit=true;DeveloperChecks.Require(!RuneAutomaticTrigger(game.actors[game.controlled]),"弹窗没有阻止自动扳机");gameEdit=false;report.modalStopsTrigger=true;
                ToggleGameAim();int shots=gameShotCount;for(int i=0;i<120;i++)GameStep();DeveloperChecks.Require(gameShotCount==shots&&!gameAutoAim&&runePredictions.Count==0,"右键关闭后自动扳机仍运行");report.offStopsTrigger=true;
            }
            catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
            game.running=false;report.passed=report.error.Length==0;File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("RUNE_AUTO_SMOKE_"+(report.passed?"OK":"FAILED")+" "+report.error);Application.Quit(report.passed?0:74);
        }
        static void RuneRingMetrics(RuneAutoCase result,DeveloperRune rune)
        {
            result.rings=rune.rings.ToArray();result.minimumRing=10;float sum=0;
            foreach(int ring in result.rings){sum+=ring;result.minimumRing=Mathf.Min(result.minimumRing,ring);}
            result.averageRing=result.rings.Length>0?sum/result.rings.Length:0;
        }
    }
}
