using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
namespace TcrReplay
{
    [Serializable] public class LidarSettings
    {
        public int source,hz=10,quality=100,colorMode,viewMode;public float size=2.2f,alpha=.9f,accumulation;
        public float redHeight=3.925f,blueHeight=3.925f,redPitch=15,bluePitch=15,redYaw=90,blueYaw=270;
        public Vector2 redOffset=new Vector2(.165f,.15f),blueOffset=new Vector2(-.165f,-.15f);public bool cone;
    }
    [StructLayout(LayoutKind.Sequential)] public struct LidarPoint
    {
        public Vector3 position;public float range;public Vector4 meta;
    }
    public partial class ReplayViewer
    {
        const int LidarDrawLayer=15,LidarPointLimit=240000;
        LidarSettings lidarSettings=new LidarSettings();bool lidarEnabled,lidarPending,lidarDirty=true,lidarSceneDirty=true;
        ReplayLidarGeometry lidarGeometry;int lidarSceneKey,lidarFrame,lidarSkipped,lidarValidPoints,lidarQueries;
        float lidarLastTime=-1,lidarNextTime,lidarSnapshotTime,lidarScanMs,lidarActualHz,lidarWallStart,lidarRateStart;int lidarRateFrames;
        NativeArray<RaycastCommand> lidarCommands;NativeArray<RaycastHit> lidarHits;JobHandle lidarJob;
        LidarFrameHub lidarApi;long lidarSequence;int lidarScanSources,lidarScanSource,lidarScanHz,lidarScanQuality;
        string lidarScanMode,lidarScanField,lidarScanGame;LidarSensorPose[] lidarScanPoses;
        /// <summary>Access/subscribe on main thread. Opt-in raw frame snapshots; no recorder or transport.</summary>
        public ILidarFrameSource LidarFrames=>lidarApi??(lidarApi=new LidarFrameHub(ex=>Debug.LogWarning("Lidar frame subscriber failed: "+ex.Message)));
        LidarPoint[] lidarPointArray=new LidarPoint[LidarPointLimit];int lidarPointCount;
        Vector3[] lidarOrigins=new Vector3[2];readonly Queue<LidarFrame> lidarFrames=new Queue<LidarFrame>();
        readonly Stack<LidarFrame> lidarFramePool=new Stack<LidarFrame>();
        class LidarFrame {public LidarPoint[] points;public int count;public float time;}
        void LoadLidarPreferences(){try{string json=PlayerPrefs.GetString("replay.lidar","");if(json.Length>0){lidarSettings=JsonUtility.FromJson<LidarSettings>(json);ValidateLidarSettings();}}catch{lidarSettings=new LidarSettings();}}
        void ValidateLidarSettings(){if(lidarSettings==null)lidarSettings=new LidarSettings();var s=lidarSettings;s.source=Mathf.Clamp(s.source,0,2);s.hz=s.hz==20?20:10;s.quality=Mathf.Clamp(s.quality,10,100);s.viewMode=Mathf.Clamp(s.viewMode,0,1);s.colorMode=Mathf.Clamp(s.colorMode,0,2);s.accumulation=Mathf.Clamp(s.accumulation,0,1);s.size=Mathf.Clamp(s.size,1,8);s.alpha=Mathf.Clamp(s.alpha,.1f,1);}
        float LidarTime=>appMode=="game"&&game!=null?game.time:playhead;
        int LidarSources=>lidarSettings.source==2?2:1;
        void SetLidar(bool enabled)
        {
            var previousOwner=ActiveUiPanel;
            lidarEnabled=enabled;
            if(!enabled){StopLidarQueries();ClearLidarCloud(LidarResetReason.Disabled);ReleaseLidarDisplay();if(lidarGeometry!=null){lidarGeometry.Dispose();lidarGeometry=null;}lidarSceneKey=0;lidarFramePool.Clear();}
            else {lidarApi?.Reset(LidarResetReason.Enabled);lidarDirty=true;lidarLastTime=-1;lidarRateStart=Time.realtimeSinceStartup;lidarRateFrames=0;}
            if(previousOwner!=ActiveUiPanel)UiFocusChanged();
        }
        void StopLidarQueries()
        {
            if(lidarPending){lidarJob.Complete();lidarPending=false;}
            if(lidarCommands.IsCreated)lidarCommands.Dispose();if(lidarHits.IsCreated)lidarHits.Dispose();
        }
        void InvalidateLidar(LidarResetReason reason=LidarResetReason.SceneChanged)
        {
            StopLidarQueries();ClearLidarCloud(reason);lidarDirty=lidarSceneDirty=true;lidarSceneKey=0;lidarLastTime=-1;
        }
        void ClearLidarCloud(LidarResetReason reason=LidarResetReason.Cleared){while(lidarFrames.Count>0)lidarFramePool.Push(lidarFrames.Dequeue());lidarPointCount=lidarValidPoints=0;lidarNextTime=0;lidarApi?.Reset(reason);}
        int CurrentLidarSceneKey()
        {
            int key=fieldRoot?fieldRoot.GetInstanceID():0;
            if(appMode=="game"){foreach(var v in gameVehicles.Values)if(v.unit.root)key^=v.unit.root.GetInstanceID();}
            else {foreach(var u in units)if(u.root)key^=u.root.GetInstanceID();}
            return key;
        }
        void PrepareLidarGeometry(int key)
        {
            if(lidarSceneKey!=0&&lidarSceneKey!=key)ClearLidarCloud(LidarResetReason.SceneChanged);
            if(lidarGeometry==null)lidarGeometry=new ReplayLidarGeometry();lidarGeometry.Reset(fieldRoot);
            if(appMode=="game")foreach(var v in gameVehicles.Values){var vehicle=v;lidarGeometry.Add(v.unit.root,()=>vehicle.state.Alive);}
            else foreach(var u in units){var unit=u;lidarGeometry.Add(u.root,()=>unit.visible);}
            lidarSceneKey=key;lidarDirty=true;lidarSceneDirty=false;
        }
        Vector3 LidarOrigin(int source)
        {
            string team=source==0?"red":"blue";Vector3 p=buildingNodes.TryGetValue(team+"_radar_platform",out var platform)?platform.position:new Vector3(source==0?-1.51f:field.width+1.51f,0,field.depth*.5f);
            Vector2 offset=source==0?lidarSettings.redOffset:lidarSettings.blueOffset;p+=new Vector3(offset.x,source==0?lidarSettings.redHeight:lidarSettings.blueHeight,offset.y);return p;
        }
        Quaternion LidarRotation(int source)=>Quaternion.Euler(source==0?lidarSettings.redPitch:lidarSettings.bluePitch,source==0?lidarSettings.redYaw:lidarSettings.blueYaw,0);
        // Deterministic irrational phases approximate the expanding Avia scan
        // distribution; this is explicitly not its proprietary optical formula.
        public static Vector3 AviaDirection(int sample,int frame,int samples)
        {
            if(samples<=0||sample<0||sample>=samples)throw new ArgumentOutOfRangeException(nameof(sample),"Invalid Avia scan sample");
            double t=(sample+frame*(double)samples)/240000d;
            double a=t*431.728+sample*.61803398875,b=t*113.417+sample*.41421356237;
            float horizontal=AviaSineTurns(a)*AviaSineTurns(b+.25),vertical=AviaSineTurns(b)*AviaSineTurns(a*.37+.25);
            return Quaternion.Euler(vertical*38.6f,horizontal*35.2f,0)*Vector3.forward;
        }
        internal static float AviaSineTurns(double turns)
        {
            // Linux Mono/libm double-precision Cos raised SIGFPE in the native
            // FMA argument reducer during sustained replay scans. Mathf.Sin/
            // Cos also delegate to System.Math, so keep this hot path managed.
            // All integer scan inputs keep turns safely inside Int64 range.
            double phase=turns-(long)turns;
            if(phase<0)phase+=1;
            if(phase>.5)phase-=1;
            if(phase>.25)phase=.5-phase;else if(phase<-.25)phase=-.5-phase;
            double x=phase*6.2831853071795864769,z=x*x;
            // Degree-13 sine on [-pi/2, pi/2]; truncation error < 7e-10,
            // followed by a single float rounding. No native trig or remainder.
            double p=1d/6227020800;
            p=-1d/39916800+z*p;p=1d/362880+z*p;p=-1d/5040+z*p;
            p=1d/120+z*p;p=-1d/6+z*p;
            return (float)(x*(1+z*p));
        }
        void UpdateLidar()
        {
            if(!lidarEnabled||appMode=="menu"||!fieldRoot)return;
            if(lidarPending&&lidarJob.IsCompleted)FinishLidarFrame();
            if(lidarPending)return;
            float now=LidarTime;
            if(lidarLastTime>=0&&(now<lidarLastTime-.001f||now-lidarLastTime>1)){ClearLidarCloud(LidarResetReason.TimelineJump);lidarDirty=true;}
            lidarLastTime=now;int key=CurrentLidarSceneKey();if(lidarSceneDirty||lidarGeometry==null||key!=lidarSceneKey)PrepareLidarGeometry(key);
            bool paused=appMode=="game"?!game.running:!playing;
            if(!lidarDirty&&(paused||now<lidarNextTime))return;
            if(!lidarDirty&&now-lidarNextTime>1f/lidarSettings.hz)lidarSkipped+=Mathf.Max(0,Mathf.FloorToInt((now-lidarNextTime)*lidarSettings.hz));
            StartLidarFrame(now);
        }
        void StartLidarFrame(float now)
        {
            lidarSnapshotTime=now;lidarWallStart=Time.realtimeSinceStartup;lidarGeometry.Freeze();
            int samples=240000/Mathf.Clamp(lidarSettings.hz,10,20)*Mathf.Clamp(lidarSettings.quality,10,100)/100;
            int total=samples*LidarSources;
            lidarScanSources=LidarSources;lidarScanSource=lidarSettings.source;lidarScanHz=lidarSettings.hz;lidarScanQuality=lidarSettings.quality;
            lidarScanMode=appMode;lidarScanField=field.id;lidarScanGame=appMode=="replay"?match?.gameId:"";
            if(lidarApi!=null&&lidarApi.NeedsFrame)lidarScanPoses=new LidarSensorPose[lidarScanSources];else lidarScanPoses=null;
            if(!lidarCommands.IsCreated||lidarCommands.Length!=total){StopLidarQueries();lidarCommands=new NativeArray<RaycastCommand>(total,Allocator.Persistent);lidarHits=new NativeArray<RaycastHit>(total,Allocator.Persistent);}
            var query=new QueryParameters {layerMask=~0,hitBackfaces=true,hitMultipleFaces=false,hitTriggers=QueryTriggerInteraction.Ignore};
            for(int slot=0;slot<LidarSources;slot++)
            {
                int source=lidarSettings.source==2?slot:lidarSettings.source;Vector3 origin=LidarOrigin(source);Quaternion rot=LidarRotation(source);lidarOrigins[slot]=origin;
                if(lidarScanPoses!=null)lidarScanPoses[slot]=new LidarSensorPose(source,origin,rot);
                for(int i=0;i<samples;i++)lidarCommands[slot*samples+i]=new RaycastCommand(lidarGeometry.PhysicsScene,origin,rot*AviaDirection(i,lidarFrame,samples),query,80);
            }
            lidarQueries=total;lidarJob=RaycastCommand.ScheduleBatch(lidarCommands,lidarHits,64,1);lidarPending=true;lidarDirty=false;lidarNextTime=now+1f/lidarSettings.hz;
        }
        void FinishLidarFrame()
        {
            lidarJob.Complete();lidarPending=false;lidarScanMs=(Time.realtimeSinceStartup-lidarWallStart)*1000;
            int samples=lidarQueries/lidarScanSources,count=0;
            for(int i=0;i<lidarQueries;i++)if(lidarHits[i].collider&&lidarHits[i].distance>=1)
            {
                var hit=lidarHits[i];int slot=i/samples,source=lidarScanSource==2?slot:lidarScanSource;
                lidarPointArray[count++]=new LidarPoint {position=hit.point,range=hit.distance,meta=new Vector4(source,(i%samples)/(float)samples/lidarScanHz,lidarSnapshotTime,0)};
            }
            lidarValidPoints=count;
            // Capture before display accumulation overwrites the reusable raw buffer.
            lidarSequence++;
            var exported=lidarScanPoses==null?null:lidarApi?.Capture(lidarSequence,lidarSnapshotTime,lidarScanHz,lidarScanQuality,lidarQueries,
                lidarScanMode,lidarScanField,lidarScanGame,lidarPointArray,count,lidarScanPoses);
            if(lidarSettings.accumulation>.001f)
            {
                var frame=lidarFramePool.Count>0?lidarFramePool.Pop():new LidarFrame {points=new LidarPoint[lidarQueries]};if(frame.points.Length<count)frame.points=new LidarPoint[lidarQueries];frame.count=count;frame.time=lidarSnapshotTime;Array.Copy(lidarPointArray,frame.points,count);lidarFrames.Enqueue(frame);
                int sum=0;foreach(var f in lidarFrames)sum+=f.count;
                while(lidarFrames.Count>1&&(lidarSnapshotTime-lidarFrames.Peek().time>lidarSettings.accumulation||sum>LidarPointLimit)){var retired=lidarFrames.Dequeue();sum-=retired.count;lidarFramePool.Push(retired);}
                count=0;foreach(var f in lidarFrames){int n=Math.Min(f.count,LidarPointLimit-count);Array.Copy(f.points,0,lidarPointArray,count,n);count+=n;}
            }
            else while(lidarFrames.Count>0)lidarFramePool.Push(lidarFrames.Dequeue());
            lidarPointCount=count;UploadLidarPoints();lidarFrame++;lidarRateFrames++;
            float elapsed=Time.realtimeSinceStartup-lidarRateStart;if(elapsed>=1){lidarActualHz=lidarRateFrames/elapsed;lidarRateStart=Time.realtimeSinceStartup;lidarRateFrames=0;}
            // Dispatch only after display and scan state are consistent. Consumer
            // errors cannot break scanning; retained frames never alias our arrays.
            lidarApi?.Publish(exported);
        }
        void LateUpdate(){UpdateLidar();DrawLidarCloud();WriteUiProbe();}
    }
}
