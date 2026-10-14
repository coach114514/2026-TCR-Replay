using System;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        // Opt-in observation only: the test drives actual X11 mouse/key events.
        [Serializable] class UiProbe
        {
            public string mode,panel,camera,selection,command,focus,error,query;
            public int frame,width,height,shots,actors,pathPoints,matchIndex,fps,keyboard,hotControl,lidarView,results;
            public bool focused,captured,backgroundAllowed,running,playing,preview,autoAim,allLabels,topView,lidar,fullscreen,targetSpin;
            public float time,gameTime,replayTime,ammo,allowedAmmo,hp,gimbalYaw,gimbalPitch,mapZoom,lidarYaw,lidarDistance,distance,speed;
            public Vector3 playerPosition,cameraPosition,cameraEuler;
            public bool recovering;public int recoveryCount;public float stuckSeconds;public string recoveryStatus;
            public Vector2 editorScroll,eventScroll,matchScroll,mapPan;
            public string aimLock,aimCandidate,redRuneMode,blueRuneMode;
            public bool aimValid,redRunePending,redRuneActive,redRuneEnabled,redSmallLoop,redLargeLoop;
            public float redRuneStartsAt,roiWidth,roiHeight;
            public Vector3 candidateCenter,predictionCenter,redRuneAnchor,redRuneNormal;
            public int runeFrames,runeFirst,runeSecond,runeCurrent,runeAutoShots,runeRetries,redRuneSuccesses,redRuneHits,redRuneLastRing;
            public bool runePendingShot;
            public float runePredictedMiss,runePredictedSigma,runeFittedAmplitude,runeFittedOmega,runeObservedSpeed;public bool runeMotionMatched;
        }
        string uiProbePath,uiProbeFocus="";float uiNextProbe;int uiProbeKeyboard,uiProbeHotControl;
        void InitializeUiProbe(string[] args)
        {
            int index=Array.IndexOf(args,"--ui-probe");
            if(index>=0&&index+1<args.Length)uiProbePath=args[index+1];
            int size=Array.IndexOf(args,"--ui-size");
            if(!string.IsNullOrEmpty(uiProbePath)&&size>=0&&size+2<args.Length&&int.TryParse(args[size+1],out int width)&&int.TryParse(args[size+2],out int height)&&width>=800&&height>=600)
                Screen.SetResolution(width,height,FullScreenMode.Windowed);
        }
        void WriteUiProbe()
        {
            if(string.IsNullOrEmpty(uiProbePath)||Time.unscaledTime<uiNextProbe)return;
            uiNextProbe=Time.unscaledTime+.04f;
            try
            {
                DeveloperActor player=null;if(game!=null)game.actors.TryGetValue(game.controlled,out player);
                var state=new UiProbe{mode=appMode,panel=ActiveUiPanel.ToString(),camera=cameraRig.Mode.ToString(),selection=gameSelection,
                    command=consoleLine,focus=uiProbeFocus,error=error,query=searchQuery,results=searchResults.Count,lidarView=lidarSettings.viewMode,
                    targetSpin=game!=null&&game.actors.TryGetValue("target1",out var target)&&target.spin,frame=Time.frameCount,width=Screen.width,height=Screen.height,
                    shots=gameShotCount,actors=game==null?0:game.actors.Count,pathPoints=game!=null&&game.actors.TryGetValue(gameSelection,out var actor)?actor.path.Count:0,
                    matchIndex=matchIndex,fps=fpsPreference,keyboard=uiProbeKeyboard,hotControl=uiProbeHotControl,
                    focused=Application.isFocused,captured=mouseCaptured,backgroundAllowed=BackgroundInputAllowed,running=game!=null&&game.running,
                    playing=playing,preview=gamePreview,autoAim=gameAutoAim,allLabels=allLabels,topView=topView,lidar=lidarEnabled,fullscreen=Screen.fullScreen,
                    time=Time.unscaledTime,gameTime=game==null?0:game.time,replayTime=playhead,ammo=player==null?0:player.ammo,
                    allowedAmmo=player==null?0:player.allowedAmmo,hp=player==null?0:player.hp,gimbalYaw=gameDesiredYaw,gimbalPitch=gameDesiredPitch,
                    mapZoom=mapZoom,lidarYaw=lidarViewYaw,lidarDistance=lidarViewDistance,distance=distance,speed=speed,
                    playerPosition=player==null?Vector3.zero:player.position,cameraPosition=viewCamera.transform.position,cameraEuler=viewCamera.transform.eulerAngles,
                    editorScroll=editorScroll,eventScroll=eventScroll,matchScroll=matchScroll,mapPan=mapPan};
                state.aimLock=gameLock;state.aimValid=gameAimValid;state.predictionCenter=gameAimPoint;
                state.recoveryCount=recoveryCount;state.recoveryStatus=recoveryStatus;if(player!=null&&gameVehicles.TryGetValue(player.id,out var drive)){state.recovering=drive.recovering;state.stuckSeconds=drive.stuckSeconds;}
                if(game!=null)
                {
                    state.roiWidth=game.rules.aimRoiWidth;state.roiHeight=game.rules.aimRoiHeight;
                    var r=game.runes["red"];state.redRuneMode=r.mode;state.blueRuneMode=game.runes["blue"].mode;
                    state.redRunePending=r.startPending;state.redRuneActive=r.activating;state.redRuneEnabled=r.enabled;
                    state.redSmallLoop=r.smallLoop;state.redLargeLoop=r.largeLoop;state.redRuneStartsAt=r.startsAt;
                    state.runeFrames=runePredictions.Count;state.runeFirst=runeAim.first;state.runeSecond=runeAim.second;state.runeCurrent=runeAim.current;
                    state.runeAutoShots=runeAim.autoShots;state.runeRetries=runeAim.retries;state.runePendingShot=runeAim.Pending;
                    state.runePredictedMiss=runeAim.predictedMiss;state.runePredictedSigma=runeAim.predictedSigma;
                    if(runeTrackers.TryGetValue(gameLock,out var motion)){state.runeMotionMatched=motion.MotionMatched;state.runeFittedAmplitude=motion.FittedAmplitude;state.runeFittedOmega=motion.FittedOmega;state.runeObservedSpeed=motion.Speed;}
                    state.redRuneSuccesses=r.successes;state.redRuneHits=r.hits;state.redRuneLastRing=r.lastHitRing;
                    if(TryGameAimCandidate(out var candidate)){state.aimCandidate=candidate.target;state.candidateCenter=candidate.center;}
                    if(runeRotor&&mechanismConfig.rune.selectionAnchors!=null)
                    {
                        var anchor=Array.Find(mechanismConfig.rune.selectionAnchors,a=>a.team=="red");
                        if(anchor!=null){state.redRuneAnchor=runeRotor.parent.TransformPoint(PassagePose.Vector(anchor.center));state.redRuneNormal=runeRotor.parent.TransformDirection(PassagePose.Vector(anchor.normal));}
                    }
                }
                File.WriteAllText(uiProbePath,JsonUtility.ToJson(state));
                if(File.Exists(uiProbePath+".capture")){File.Delete(uiProbePath+".capture");ScreenCapture.CaptureScreenshot(uiProbePath+".png");}
            }
            catch(Exception ex){Debug.LogWarning("UI输入诊断写入失败："+ex.Message);uiProbePath=null;}
        }
    }
}
