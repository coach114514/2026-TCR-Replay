using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TcrReplay
{
    public static class LidarFrameChecks
    {
        public static void Run()
        {
            int faults=0;var hub=new LidarFrameHub(_=>faults++);
            var raw=new[]{new LidarPoint {position=new Vector3(3,2,4),range=5,meta=new Vector4(1,.012f,7,0)}};
            var pose=new[]{new LidarSensorPose(1,new Vector3(1,2,3),Quaternion.Euler(0,90,0))};
            LidarScanFrame Capture()=>hub.Capture(42,7,20,100,24000,"replay","test-field","test-match",raw,1,pose);
            Require(Capture()==null&&hub.SnapshotCopies==0,"Unobserved scan allocated a snapshot");
            hub.RetainLatestFrame=true;var frame=Capture();hub.Publish(frame);
            Require(hub.TryGetLatestFrame(out var latest)&&ReferenceEquals(frame,latest),"Pull retention unavailable");
            raw[0].position=Vector3.zero;pose[0]=new LidarSensorPose(0,Vector3.zero,Quaternion.identity);
            Require(frame.Points[0].WorldPosition==new Vector3(3,2,4)&&frame.Sensors[0].SensorId==1,"Snapshot aliases reusable arrays");
            bool immutable=false;try{((IList<LidarSample>)frame.Points)[0]=default;}catch(NotSupportedException){immutable=true;}
            Require(immutable,"Consumer can mutate shared snapshot");
            var sensor=frame.Sensors[0];Require((sensor.SensorToWorld(sensor.WorldToSensor(frame.Points[0].WorldPosition))-frame.Points[0].WorldPosition).magnitude<1e-5f,"Sensor/world transform mismatch");
            Require(Task.Run(()=>hub.TryGetLatestFrame(out var f)&&f.Points.Count==1).GetAwaiter().GetResult(),"Worker cannot read retained snapshot");
            bool wrongThread=Task.Run(()=>{try{hub.RetainLatestFrame=false;return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();
            Require(wrongThread,"Subscription configuration allowed off main thread");
            int good=0,resets=0;var bad=hub.Subscribe(_=>throw new Exception("expected consumer failure"));
            var subscriber=hub.Subscribe(_=>good++,_=>resets++);hub.Publish(frame);
            Require(good==1&&faults==1,"A failing consumer interrupted other consumers");
            hub.Reset(LidarResetReason.TimelineJump);Require(!hub.TryGetLatestFrame(out _)&&resets==1,"Reset kept stale latest frame");
            hub.Publish(frame);Require(good==1,"Old epoch frame escaped reset");
            bad.Dispose();subscriber.Dispose();subscriber.Dispose();hub.RetainLatestFrame=false;
            Require(!hub.NeedsFrame&&Capture()==null,"Unsubscribe kept snapshot work alive");
            using(hub.Subscribe(_=>good++)){hub.Publish(Capture());}
            Require(good==2&&!hub.TryGetLatestFrame(out _),"Event-only subscription retained latest frame");
            hub.Close();bool closed=false;try{hub.Subscribe(_=>{});}catch(ObjectDisposedException){closed=true;}
            Require(closed&&!hub.NeedsFrame,"Disposed stream accepts new consumers");
            Debug.Log("LIDAR_FRAME_CONTRACT_OK");
        }
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
