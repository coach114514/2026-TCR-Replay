using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class AimRuneReport
        {
            public string gpu,error;public bool passed;public int rAnchorChecks,roiExclusionChecks,currentPointChecks,toggleChecks,shots,runeHits,greenFrames;
        }
        IEnumerator AimRuneSmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--aim-rune-smoke")+1];Directory.CreateDirectory(output);
            developerSmokeActive=true;allLabels=false;var report=new AimRuneReport {gpu=SystemInfo.graphicsDeviceName};
            yield return null;
            foreach(string team in new[]{"red","blue"})
            {
                try
                {
                    ResetDeveloper();game.actors[game.controlled].team=team;
                    var anchor=Array.Find(mechanismConfig.rune.selectionAnchors,a=>a.team==team);
                    Vector3 center=runeRotor.parent.TransformPoint(PassagePose.Vector(anchor.center));
                    Vector3 normal=runeRotor.parent.TransformDirection(PassagePose.Vector(anchor.normal));
                    viewCamera.transform.position=center+normal*3;viewCamera.transform.LookAt(center+Vector3.up*.12f);
                    gameAimPoint=center+Vector3.up*20;
                    DeveloperChecks.Require(TryGameAimCandidate(out var selected)&&selected.target=="rune_"+team&&selected.rune,"未通过对应面R标选择机关 "+team);
                    DeveloperChecks.Require(Vector3.Distance(selected.center,center)<.00001f,"候选连线使用了预测点而非R标当前点");report.currentPointChecks++;
                    foreach(float angle in new[]{0f,90f,211f})
                    {
                        game.runeAngle=angle;UpdateGameMechanisms();CollectGamePlates();
                        DeveloperChecks.Require(TryGameAimCandidate(out var rotated)&&rotated.target==selected.target&&Vector3.Distance(rotated.center,center)<.00001f,"R标候选随靶面旋转漂移");report.rAnchorChecks++;
                    }
                    ToggleGameAim();DeveloperChecks.Require(gameAutoAim&&gameLock==selected.target&&!gameAimValid,"右键锁定与关闭时提示候选不一致");report.toggleChecks++;
                    ToggleGameAim();DeveloperChecks.Require(!gameAutoAim&&gameLock==""&&!gameAimValid,"右键关闭保留预测框 / 锁定");report.toggleChecks++;
                    game.rules.aimRoiWidth=game.rules.aimRoiHeight=.05f;
                    var arm=gamePlates.Find(p=>p.rune&&p.team==team);viewCamera.transform.LookAt(arm.center);
                    DeveloperChecks.Require(!TryGameAimCandidate(out var wrong)||wrong.target!="rune_"+team,"旋转靶进入ROI、R标在外时仍选中机关");report.roiExclusionChecks++;
                    game.rules.aimRoiWidth=.6f;game.rules.aimRoiHeight=.5f;viewCamera.transform.LookAt(center+Vector3.up*.12f);
                }
                catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-r-candidate.png"));yield return null;
            }
            try
            {
                ResetDeveloper();UpdateGameCamera();CollectGamePlates();
                gameAimPoint=game.actors["target1"].position+Vector3.up*20;
                DeveloperChecks.Require(TryGameAimCandidate(out var candidate)&&candidate.target=="target1","车辆当前装甲没有ROI候选");
                DeveloperChecks.Require(gamePlates.Exists(p=>p.target==candidate.target&&p.index==candidate.plate&&Vector3.Distance(p.center,candidate.center)<.00001f),"车辆候选不是当前装甲中心");report.currentPointChecks++;
                ToggleGameAim();DeveloperChecks.Require(gameLock==candidate.target,"车辆候选与锁定不同");ToggleGameAim();report.toggleChecks+=2;
            }
            catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"vehicle-candidate.png"));yield return null;
            foreach(string team in new[]{"red","blue"})foreach(string kind in new[]{"small","large"})
            {
                try
                {
                    ResetDeveloper();game.running=true;game.rules.infiniteAmmo=game.rules.unlimitedHeat=game.rules.aimFireGate=true;
                    var actor=game.actors[game.controlled];actor.team=team;var face=gamePlates.Find(p=>p.rune&&p.team==team);
                    Vector3 center=runeRotor.parent.TransformPoint(PassagePose.Vector(mechanismConfig.rune.pivot));
                    actor.position=center+face.Normal*6;actor.position.y=0;SnapGameGround(gameVehicles[actor.id]);
                    Vector3 look=(center-(actor.position+Vector3.up*.29f)).normalized;
                    gameDesiredYaw=actor.gimbalYaw=Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg;gameDesiredPitch=actor.pitch=Mathf.Clamp(Mathf.Asin(look.y)*Mathf.Rad2Deg,-30,30);
                    PoseGameVehicle(gameVehicles[actor.id]);UpdateGameCamera();CollectGamePlates();
                    DeveloperChecks.Require(TryGameAimCandidate(out var initial)&&initial.target=="rune_"+team,"实际枪管视角未选中R标 "+team);
                    ToggleGameAim();game.runes[team].Training(game,kind,true);gameInput=new DeveloperInput {yaw=actor.gimbalYaw,pitch=actor.pitch,cameraRelative=true,fire=false};
                    for(int i=0;i<359;i++)GameStep();DeveloperChecks.Require(!gameAimValid&&game.runes[team].startPending,"启动等待保留旧预测框");
                    for(int i=0;i<720;i++){gameInput.fire=i>=120;GameStep();UpdateGameCamera();if(i>=360&&gameRuneHits>0&&gameAimValid)break;}
                    DeveloperChecks.Require(gameShotCount>0&&gameRuneHits>0,"机关实际自瞄发弹未命中 "+team+" "+kind+" "+aimStatus);
                    DeveloperChecks.Require(gameLock=="rune_"+team,"跟踪期间切换到其他对象");
                    report.shots+=gameShotCount;report.runeHits+=gameRuneHits;
                    DeveloperChecks.Require(AimPredictionFill.g>.9f&&AimPredictionFill.r<.2f&&AimPredictionFill.a>.1f&&AimPredictionFill.a<.5f,"预测框不是半透明绿色");
                    if(gameAimValid)report.greenFrames++;
                    game.running=false;
                }
                catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-"+kind+"-tracking.png"));yield return null;
            }
            gameEdit=true;editorScroll.y=630;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"rune-buttons.png"));yield return null;
            report.passed=string.IsNullOrEmpty(report.error);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("AIM_RUNE_SMOKE_"+(report.passed?"OK":"FAILED"));Application.Quit(report.passed?0:73);
        }
    }
}
