using System;
using UnityEngine;
namespace TcrReplay
{
    public struct DeveloperPlate
    {
        public string target,team;public int index;public Vector3 center,half,local,velocity;public Quaternion rotation,localRotation,tilt;public bool rune,critical;public float time;
        public Vector3 Normal=>rotation*Vector3.forward;
    }
    public struct DeveloperObservation {public DeveloperPlate plate;public Vector3 position;public float yaw,time;}
    // Joint translation + spin EKF. Only timestamped visible panel observations
    // enter Correct; target commands, waypoints and ground-truth velocities do not.
    public sealed class DeveloperTracker
    {
        public readonly double[] x=new double[8];readonly double[,] p=new double[8,8];
        public float time,lastSeen;public bool ready;public Quaternion tilt=Quaternion.identity;public int updates;public float residual;
        public Vector3 Center=>new Vector3((float)x[0],(float)x[1],(float)x[2]);
        public Vector3 Velocity=>new Vector3((float)x[3],(float)x[4],(float)x[5]);
        public float Omega=>(float)x[7]*Mathf.Rad2Deg;
        public void Clear(){ready=false;updates=0;}
        public void Predict(float at,DeveloperRules rules)
        {
            if(!ready||at<=time)return;float dt=Mathf.Min(1,at-time);var f=new double[8,8];for(int i=0;i<8;i++)f[i,i]=1;for(int i=0;i<3;i++)f[i,i+3]=dt;f[6,7]=dt;
            for(int i=0;i<3;i++)x[i]+=x[i+3]*dt;x[6]+=x[7]*dt;
            var next=new double[8,8];for(int i=0;i<8;i++)for(int j=0;j<8;j++)for(int k=0;k<8;k++)for(int l=0;l<8;l++)next[i,j]+=f[i,k]*p[k,l]*f[j,l];
            for(int i=0;i<4;i++){int pos=i==3?6:i,vel=i==3?7:i+3;double q=i==3?25:i==1?rules.processVertical*rules.processVertical:rules.processAcceleration*rules.processAcceleration;next[pos,pos]+=q*dt*dt*dt*dt/4;next[pos,vel]+=q*dt*dt*dt/2;next[vel,pos]+=q*dt*dt*dt/2;next[vel,vel]+=q*dt*dt;}
            Array.Copy(next,p,next.Length);time=at;
        }
        public void Correct(DeveloperObservation observation,DeveloperRules rules)
        {
            if(ready&&observation.time<time-.0001f)return;var local=observation.plate.local;tilt=observation.plate.tilt;float angle=observation.yaw*Mathf.Deg2Rad;
            if(!ready){for(int i=0;i<8;i++)x[i]=0;x[6]=angle;Vector3 c=observation.position-Quaternion.Euler(0,observation.yaw,0)*tilt*local;x[0]=c.x;x[1]=c.y;x[2]=c.z;Array.Clear(p,0,p.Length);for(int i=0;i<8;i++)p[i,i]=i<3?.01:i==6?.001:10;ready=true;time=observation.time;}
            Predict(observation.time,rules);float yaw=(float)x[6]*Mathf.Rad2Deg;Vector3 offset=Quaternion.Euler(0,yaw,0)*tilt*local;
            Vector3 derivative=Vector3.Cross(Vector3.up,offset);double[] measurement={observation.position.x,observation.position.y,observation.position.z,x[6]+Mathf.DeltaAngle(yaw,observation.yaw)*Mathf.Deg2Rad};
            double[] estimated={x[0]+offset.x,x[1]+offset.y,x[2]+offset.z,x[6]};var h=new double[4,8];for(int i=0;i<3;i++)h[i,i]=1;h[0,6]=derivative.x;h[1,6]=derivative.y;h[2,6]=derivative.z;h[3,6]=1;
            var s=new double[4,4];double variance=Math.Max(.000001,rules.measurementSigma*rules.measurementSigma+rules.observationNoise*rules.observationNoise);
            for(int i=0;i<4;i++)for(int j=0;j<4;j++){for(int k=0;k<8;k++)for(int l=0;l<8;l++)s[i,j]+=h[i,k]*p[k,l]*h[j,l];if(i==j)s[i,j]+=i==3?.00007615:variance;}
            if(!Inverse(s,out var inverse))return;var gain=new double[8,4];for(int i=0;i<8;i++)for(int j=0;j<4;j++)for(int k=0;k<8;k++)for(int l=0;l<4;l++)gain[i,j]+=p[i,k]*h[l,k]*inverse[l,j];
            residual=(observation.position-(Center+offset)).magnitude;
            if(updates>3&&(residual>2||Mathf.Abs(Mathf.DeltaAngle(yaw,observation.yaw))>120)){Clear();Correct(observation,rules);return;}
            for(int i=0;i<8;i++)for(int j=0;j<4;j++)x[i]+=gain[i,j]*(measurement[j]-estimated[j]);
            // Joseph form keeps covariance symmetric and positive under nearly noiseless measurements.
            var ikh=new double[8,8];for(int i=0;i<8;i++)for(int j=0;j<8;j++){ikh[i,j]=i==j?1:0;for(int k=0;k<4;k++)ikh[i,j]-=gain[i,k]*h[k,j];}
            var next=new double[8,8];for(int i=0;i<8;i++)for(int j=0;j<8;j++){for(int k=0;k<8;k++)for(int l=0;l<8;l++)next[i,j]+=ikh[i,k]*p[k,l]*ikh[j,l];for(int k=0;k<4;k++)next[i,j]+=gain[i,k]*gain[j,k]*(k==3?.00007615:variance);}
            for(int i=0;i<8;i++)for(int j=0;j<8;j++)p[i,j]=(next[i,j]+next[j,i])*.5;lastSeen=observation.time;updates++;
        }
        public DeveloperPlate At(DeveloperPlate layout,float at)
        {
            float dt=at-time;Vector3 center=Center+Velocity*dt;Quaternion rotation=Quaternion.Euler(0,(float)(x[6]+x[7]*dt)*Mathf.Rad2Deg,0)*tilt;
            layout.center=center+rotation*layout.local;layout.rotation=rotation*layout.localRotation;layout.velocity=Velocity+Vector3.Cross(Vector3.up*(float)x[7],rotation*layout.local);layout.time=at;return layout;
        }
        public float Uncertainty=>Mathf.Sqrt((float)Math.Max(0,p[0,0]+p[1,1]+p[2,2]));
        public float UncertaintyAt(float at,DeveloperRules rules)
        {
            double dt=Math.Max(0,at-time),variance=0;for(int i=0;i<3;i++){double q=i==1?rules.processVertical*rules.processVertical:rules.processAcceleration*rules.processAcceleration;variance+=p[i,i]+2*dt*p[i,i+3]+dt*dt*p[i+3,i+3]+q*dt*dt*dt*dt/4;}
            return Mathf.Sqrt((float)Math.Max(0,variance));
        }
        static bool Inverse(double[,] input,out double[,] inverse)
        {
            int n=input.GetLength(0);var a=new double[n,n*2];inverse=new double[n,n];for(int i=0;i<n;i++)for(int j=0;j<n;j++){a[i,j]=input[i,j];a[i,j+n]=i==j?1:0;}
            for(int col=0;col<n;col++){int pivot=col;for(int row=col+1;row<n;row++)if(Math.Abs(a[row,col])>Math.Abs(a[pivot,col]))pivot=row;if(Math.Abs(a[pivot,col])<1e-12)return false;
                if(pivot!=col)for(int j=0;j<2*n;j++){double swap=a[pivot,j];a[pivot,j]=a[col,j];a[col,j]=swap;}double scale=a[col,col];for(int j=0;j<2*n;j++)a[col,j]/=scale;
                for(int row=0;row<n;row++)if(row!=col){double factor=a[row,col];for(int j=0;j<2*n;j++)a[row,j]-=factor*a[col,j];}}
            for(int i=0;i<n;i++)for(int j=0;j<n;j++)inverse[i,j]=a[i,j+n];return true;
        }
    }
    public static class DeveloperIntercept
    {
        public static bool Solve(Vector3 muzzle,DeveloperPlate layout,DeveloperTracker tracker,float now,float delay,DeveloperRules rules,out Vector3 direction,out float flight,out DeveloperPlate predicted)
        {
            direction=Vector3.forward;flight=0;predicted=layout;if(!tracker.ready||now-tracker.lastSeen>.75f||tracker.UncertaintyAt(now,rules)>.5f)return false;
            Vector3 g=rules.gravityCompensation?Vector3.down*rules.gravity:Vector3.zero;
            Func<float,float> equation=t=>{var plate=tracker.At(layout,now+delay+t);Vector3 velocity=(plate.center-muzzle-.5f*g*t*t)/t;return velocity.magnitude-rules.bulletSpeed;};
            float lo=.002f,hi=lo,value=equation(lo);bool bracket=false;
            for(float t=.025f;t<=4;t+=.025f){float next=equation(t);if(next<=0&&value>=0){hi=t;bracket=true;break;}lo=t;value=next;}if(!bracket)return false;
            for(int i=0;i<18;i++){float mid=(lo+hi)*.5f;if(equation(mid)>0)lo=mid;else hi=mid;}flight=(lo+hi)*.5f;predicted=tracker.At(layout,now+delay+flight);direction=((predicted.center-muzzle-.5f*g*flight*flight)/flight).normalized;
            Vector3 arrival=direction*rules.bulletSpeed+Vector3.down*rules.gravity*flight;float approach=-Vector3.Dot(arrival-predicted.velocity,predicted.Normal);
            return approach>rules.armorMinVelocity && Vector3.Dot(predicted.Normal,(muzzle-predicted.center).normalized)>.12f;
        }
    }
}
