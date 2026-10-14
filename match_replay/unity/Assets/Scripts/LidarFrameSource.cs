using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using UnityEngine;

namespace TcrReplay
{
    public enum LidarResetReason { Enabled, Disabled, TimelineJump, MatchChanged, FieldChanged, SceneChanged, SettingsChanged, Cleared, Disposed }

    /// <summary>Unity world metres: X right, Y up, Z forward. SensorId: 0 red, 1 blue.</summary>
    public readonly struct LidarSample
    {
        public readonly Vector3 WorldPosition;
        public readonly float RangeMetres, TimeOffsetSeconds;
        public readonly int SensorId;
        internal LidarSample(LidarPoint point)
        { WorldPosition=point.position;RangeMetres=point.range;SensorId=(int)point.meta.x;TimeOffsetSeconds=point.meta.y; }
    }

    /// <summary>Pose frozen when the scan starts, never the current UI settings.</summary>
    public readonly struct LidarSensorPose
    {
        public readonly int SensorId;
        public readonly Vector3 WorldPosition;
        public readonly Quaternion WorldRotation;
        internal LidarSensorPose(int id,Vector3 position,Quaternion rotation)
        { SensorId=id;WorldPosition=position;WorldRotation=rotation; }
        public Vector3 WorldToSensor(Vector3 point)=>Quaternion.Inverse(WorldRotation)*(point-WorldPosition);
        public Vector3 SensorToWorld(Vector3 point)=>WorldPosition+WorldRotation*point;
    }

    /// <summary>
    /// Owned immutable raw single scan, independent of display accumulation.
    /// Safe to retain/read on a worker thread; no Unity scene APIs on that thread.
    /// Time is simulation time, not UTC. Offsets are approximate scan scheduling;
    /// geometry is frozen for the frame, not per-point motion distortion.
    /// </summary>
    public sealed class LidarScanFrame
    {
        public const int SchemaVersion=1;
        public const string CoordinateConvention="Unity world metres; X right, Y up, Z forward; left-handed";
        public long Sequence {get;}
        public long Epoch {get;}
        public double SimulationTimeSeconds {get;}
        public double DurationSeconds {get;}
        public int RequestedHz {get;}
        public int QualityPercent {get;}
        public int AttemptedRays {get;}
        public string Mode {get;}
        public string FieldId {get;}
        public string GameId {get;}
        public bool HasReflectivity=>false;
        public bool HasLineNumber=>false;
        public bool GeometryFrozenForFrame=>true;
        public bool IsApproximateScan=>true;
        public IReadOnlyList<LidarSample> Points {get;}
        public IReadOnlyList<LidarSensorPose> Sensors {get;}
        internal LidarScanFrame(long sequence,long epoch,double time,int hz,int quality,int attempted,
            string mode,string field,string game,LidarPoint[] points,int count,LidarSensorPose[] sensors)
        {
            Sequence=sequence;Epoch=epoch;SimulationTimeSeconds=time;DurationSeconds=1d/hz;
            RequestedHz=hz;QualityPercent=quality;AttemptedRays=attempted;Mode=mode;FieldId=field;GameId=game;
            var copy=new LidarSample[count];for(int i=0;i<count;i++)copy[i]=new LidarSample(points[i]);
            Points=new ReadOnlyCollection<LidarSample>(copy);
            Sensors=new ReadOnlyCollection<LidarSensorPose>((LidarSensorPose[])sensors.Clone());
        }
    }

    public readonly struct LidarStreamReset
    {
        public readonly long Epoch;
        public readonly LidarResetReason Reason;
        internal LidarStreamReset(long epoch,LidarResetReason reason){Epoch=epoch;Reason=reason;}
    }

    /// <summary>Subscribe/configure on Unity main thread. TryGetLatestFrame is worker-safe.</summary>
    public interface ILidarFrameSource
    {
        event Action<LidarScanFrame> FrameReady;
        event Action<LidarStreamReset> StreamReset;
        bool RetainLatestFrame {get;set;}
        long Epoch {get;}
        bool TryGetLatestFrame(out LidarScanFrame frame);
        IDisposable Subscribe(Action<LidarScanFrame> onFrame,Action<LidarStreamReset> onReset=null);
    }

    internal sealed class LidarFrameHub:ILidarFrameSource
    {
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        readonly Action<Exception> fault;
        Action<LidarScanFrame> frames;Action<LidarStreamReset> resets;
        LidarScanFrame latest;long epoch;bool retain,closed;
        public LidarFrameHub(Action<Exception> fault){this.fault=fault;}
        void MainThread(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Configure lidar subscriptions on Unity main thread");}
        void Alive(){if(closed)throw new ObjectDisposedException(nameof(LidarFrameHub));}
        public event Action<LidarScanFrame> FrameReady
        {add{MainThread();Alive();frames+=value;}remove{MainThread();frames-=value;}}
        public event Action<LidarStreamReset> StreamReset
        {add{MainThread();Alive();resets+=value;}remove{MainThread();resets-=value;}}
        public bool RetainLatestFrame
        {get=>retain;set{MainThread();Alive();retain=value;if(!value)Volatile.Write(ref latest,null);}}
        public long Epoch=>Interlocked.Read(ref epoch);
        internal bool NeedsFrame=>!closed&&(retain||frames!=null);
        internal long SnapshotCopies {get;private set;}
        public bool TryGetLatestFrame(out LidarScanFrame frame)
        {frame=Volatile.Read(ref latest);return frame!=null;}
        public IDisposable Subscribe(Action<LidarScanFrame> onFrame,Action<LidarStreamReset> onReset=null)
        {
            if(onFrame==null)throw new ArgumentNullException(nameof(onFrame));
            FrameReady+=onFrame;if(onReset!=null)StreamReset+=onReset;
            return new Subscription(this,onFrame,onReset);
        }
        sealed class Subscription:IDisposable
        {
            LidarFrameHub owner;readonly Action<LidarScanFrame> frame;readonly Action<LidarStreamReset> reset;
            public Subscription(LidarFrameHub owner,Action<LidarScanFrame> frame,Action<LidarStreamReset> reset)
            {this.owner=owner;this.frame=frame;this.reset=reset;}
            public void Dispose(){if(owner==null)return;owner.MainThread();owner.FrameReady-=frame;owner.StreamReset-=reset;owner=null;}
        }
        internal LidarScanFrame Capture(long sequence,double time,int hz,int quality,int attempted,string mode,string field,string game,
            LidarPoint[] points,int count,LidarSensorPose[] sensors)
        {
            if(!NeedsFrame)return null;
            SnapshotCopies++;return new LidarScanFrame(sequence,Epoch,time,hz,quality,attempted,mode,field,game,points,count,sensors);
        }
        internal void Publish(LidarScanFrame frame)
        {
            MainThread();if(frame==null||closed||frame.Epoch!=Epoch)return;
            if(retain)Volatile.Write(ref latest,frame);
            var listeners=frames?.GetInvocationList();if(listeners==null)return;
            foreach(Action<LidarScanFrame> listener in listeners)
            {
                if(closed||frame.Epoch!=Epoch)break;
                try{listener(frame);}catch(Exception ex){fault?.Invoke(ex);}
            }
        }
        internal void Reset(LidarResetReason reason)
        {
            MainThread();if(closed)return;
            Volatile.Write(ref latest,null);var change=new LidarStreamReset(Interlocked.Increment(ref epoch),reason);
            var listeners=resets?.GetInvocationList();if(listeners==null)return;
            foreach(Action<LidarStreamReset> listener in listeners)
            {if(change.Epoch!=Epoch)break;try{listener(change);}catch(Exception ex){fault?.Invoke(ex);}}
        }
        internal void Close(){if(closed)return;Reset(LidarResetReason.Disposed);closed=true;frames=null;resets=null;retain=false;Volatile.Write(ref latest,null);}
    }
}
