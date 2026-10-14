using System;
using System.Collections.Generic;
using UnityEngine;
using static TcrReplay.DeveloperMath;
namespace TcrReplay
{
    public struct RuneObservation
    {
        public string target;public int groupSerial;public float angle,time;public Vector3 center,axis;public DeveloperPlate[] plates;public bool[] lit,alreadyHit;
    }
    internal static class DeveloperMath
    {
        // Some native Unity Linux builds leave floating-point traps enabled.
        // libm range reduction can trap even near ordinary multiples of pi.
        // Reduce in managed code and evaluate on [-pi/2,pi/2] in double precision.
        static double Trig(double value,bool cosine)
        {
            if(!double.IsFinite(value))throw new InvalidOperationException("观测／滤波角度非有限："+value);
            value%=2*Math.PI;if(value>Math.PI)value-=2*Math.PI;else if(value< -Math.PI)value+=2*Math.PI;
            double sign=1;
            if(value>Math.PI/2){value=Math.PI-value;if(cosine)sign=-1;}
            else if(value< -Math.PI/2){value=-Math.PI-value;if(cosine)sign=-1;}
            if(Math.Abs(value)<1e-7)return sign*(cosine?1:value);
            double term=cosine?1:value,sum=term,square=value*value;
            for(int k=1;k<=12;k++){term*= -square/((2*k-(cosine?1:0))*(2*k+(cosine?0:1)));sum+=term;}
            return sign*sum;
        }
        internal static double Sin(double value)=>Trig(value,false);
        internal static double Cos(double value)=>Trig(value,true);
    }
    public sealed class DeveloperRuneTracker
    {
        public static float ObservedPhase(Vector3 reference,Vector3 observed,Vector3 axis)
        {
            // Front/back target centers have a nonzero axial offset from the
            // rotor pivot. A 3D SignedAngle is NOT the rotation around that axis.
            // Project both radii into the rotation plane before measuring phase.
            axis=axis.normalized;reference=Vector3.ProjectOnPlane(reference,axis);observed=Vector3.ProjectOnPlane(observed,axis);
            return Mathf.Atan2(Vector3.Dot(axis,Vector3.Cross(reference,observed)),Vector3.Dot(reference,observed));
        }
        sealed class Model
        {
            public bool sinusoid;public int sign;public double[] x=new double[4];public double[,] p=new double[4,4];public double weight;
            public void Predict(double dt)
            {
                var f=new double[4,4];for(int i=0;i<4;i++)f[i,i]=1;
                if(sinusoid)
                {
                    double a=x[1],w=x[2],phase=x[3],next=phase+w*dt,c=Cos(phase)-Cos(next);
                    x[0]+=sign*((2.09-a)*dt+a/w*c);x[3]=next;
                    f[0,1]=sign*(-dt+c/w);f[0,2]=sign*a*(-c/(w*w)+Sin(next)*dt/w);f[0,3]=sign*a/w*(-Sin(phase)+Sin(next));f[3,2]=dt;
                }
                // Small-rune speed is a rule constant, not a free velocity state.
                // Its direction and phase are still inferred from observations.
                else x[0]+=sign*Math.PI/3*dt;
                var nextP=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)for(int k=0;k<4;k++)for(int l=0;l<4;l++)nextP[i,j]+=f[i,k]*p[k,l]*f[j,l];
                if(sinusoid){nextP[0,0]+=1e-7*dt;nextP[1,1]+=1e-5*dt;nextP[2,2]+=1e-6*dt;nextP[3,3]+=1e-5*dt;}
                else nextP[0,0]+=1e-7*dt;
                p=nextP;
            }
            public double Correct(double angle,double variance)
            {
                double residual=angle-x[0],s=Math.Max(1e-10,p[0,0]+variance);var gain=new double[4];for(int i=0;i<4;i++)gain[i]=p[i,0]/s;
                var old=(double[,])p.Clone();for(int i=0;i<4;i++){x[i]+=gain[i]*residual;for(int j=0;j<4;j++)p[i,j]=old[i,j]-gain[i]*old[0,j];}
                for(int i=0;i<4;i++)for(int j=i;j<4;j++){double v=(p[i,j]+p[j,i])*.5;p[i,j]=p[j,i]=v;}
                if(sinusoid){x[1]=Math.Clamp(x[1],.780,1.045);x[2]=Math.Clamp(x[2],1.884,2.000);x[3]%=2*Math.PI;if(Math.Abs(x[3])<1e-9)x[3]=0;}
                return -.5*(residual*residual/s+Math.Log(s));
            }
            public double At(double dt)=>sinusoid?x[0]+sign*((2.09-x[1])*dt+x[1]/x[2]*(Cos(x[3])-Cos(x[3]+x[2]*dt))):x[0]+sign*Math.PI/3*dt;
        }
        readonly List<Model> models=new List<Model>();
        public readonly Dictionary<int,DeveloperPlate> layouts=new Dictionary<int,DeveloperPlate>();
        public readonly HashSet<int> lit=new HashSet<int>(),alreadyHit=new HashSet<int>();
        public Vector3 center,axis;public float time,lastSeen,started,lightSeen,uncertainty,largeProbability;public int updates;public bool ready;
        int lightMask;public int groupSerial=-1;Model best;bool largeMatched;
        public bool MotionMatched=>largeMatched||best!=null&&!best.sinusoid&&largeProbability<.08f&&time-started>.2f;
        public float FittedAmplitude=>best!=null&&best.sinusoid?(float)best.x[1]:0;
        public float FittedOmega=>best!=null&&best.sinusoid?(float)best.x[2]:0;
        public string Classification=>largeMatched?$"大机关函数匹配 a={FittedAmplitude:0.000} ω={FittedOmega:0.000}":MotionMatched?"小机关固定60°/s · 方向／相位已匹配":"规则转速函数识别中";
        public float Speed=>best==null?0:(float)((best.At(.01)-best.At(0))/.01*Mathf.Rad2Deg);
        public void Correct(RuneObservation o,DeveloperRules rules)
        {
            if(ready&&o.time<time-.0001f)return;
            if(!ready||o.time-time>1.5f)
            {
                models.Clear();largeMatched=false;
                for(int direction=-1;direction<=1;direction+=2)
                {var m=new Model {sign=direction,weight=.25};m.x[0]=o.angle;m.p[0,0]=.001;models.Add(m);}
                for(int direction=-1;direction<=1;direction+=2)for(int phase=0;phase<8;phase++)
                {
                    var m=new Model {sinusoid=true,sign=direction,weight=.5/16};m.x[0]=o.angle;m.x[1]=.9125;m.x[2]=1.942;m.x[3]=phase*Math.PI/4;m.p[0,0]=.001;m.p[1,1]=.03;m.p[2,2]=.002;m.p[3,3]=.35;models.Add(m);
                }
                time=started=o.time;ready=true;best=models[0];updates=0;layouts.Clear();lit.Clear();alreadyHit.Clear();lightMask=0;
            }
            double dt=o.time-time,reference=best.At(dt),angle=reference+Mathf.DeltaAngle((float)reference*Mathf.Rad2Deg,o.angle*Mathf.Rad2Deg)*Mathf.Deg2Rad;
            double radius=o.plates.Length>0?Math.Max(.1,(o.plates[0].center-o.center).magnitude):.7;
            double sigma=Math.Max(.0001,Math.Sqrt(rules.measurementSigma*rules.measurementSigma+rules.observationNoise*rules.observationNoise)/radius),maxLog=-1e30;var logs=new double[models.Count];
            for(int i=0;i<models.Count;i++){var m=models[i];m.Predict(dt);double prior=.97*m.weight+.03/(m.sinusoid?32:4);logs[i]=Math.Log(Math.Max(1e-12,prior))+m.Correct(angle,sigma*sigma);if(largeMatched&&!m.sinusoid)logs[i]=-1e30;maxLog=Math.Max(maxLog,logs[i]);}
            double total=0;for(int i=0;i<models.Count;i++){models[i].weight=Math.Exp(Math.Max(-60,logs[i]-maxLog));total+=models[i].weight;}
            var previousBest=best;best=models[0];double big=0;foreach(var m in models){m.weight/=total;if(m.sinusoid)big+=m.weight;if(m.weight>best.weight)best=m;}
            if(previousBest!=null&&(!largeMatched||previousBest.sinusoid)&&previousBest.weight*1.25>=best.weight)best=previousBest;
            largeProbability=(float)big;if(big>.9&&o.time-started>.35f&&best.sinusoid)largeMatched=true;
            uncertainty=(float)Math.Sqrt(Math.Max(0,best.p[0,0]));time=lastSeen=o.time;updates++;center=o.center;axis=o.axis.normalized;
            if(groupSerial!=o.groupSerial){groupSerial=o.groupSerial;lit.Clear();alreadyHit.Clear();lightMask=0;}
            int mask=0;for(int i=0;i<o.plates.Length;i++)
            {
                // Geometry is unwound by the measured phase, not the filtered
                // phase: estimator residuals must not move the learned rotor.
                var p=o.plates[i];p.local=Quaternion.AngleAxis(-o.angle*Mathf.Rad2Deg,axis)*(p.center-center);p.localRotation=Quaternion.AngleAxis(-o.angle*Mathf.Rad2Deg,axis)*p.rotation;
                if(layouts.TryGetValue(p.index,out var prior)){p.local=Vector3.Lerp(prior.local,p.local,.2f);p.localRotation=Quaternion.Slerp(prior.localRotation,p.localRotation,.2f);}layouts[p.index]=p;
                if(o.lit[i]){mask|=1<<p.index;lit.Add(p.index);}else lit.Remove(p.index);
                if(o.alreadyHit[i])alreadyHit.Add(p.index);else alreadyHit.Remove(p.index);
            }
            if(mask!=lightMask){lightSeen=o.time;lightMask=mask;}
        }
        public DeveloperPlate At(DeveloperPlate layout,float at)
        {
            float angle=(float)best.At(at-time),speed=(float)((best.At(at-time+.001)-best.At(at-time))/.001);
            Quaternion q=Quaternion.AngleAxis(angle*Mathf.Rad2Deg,axis);Vector3 radial=q*layout.local;
            layout.center=center+radial;layout.rotation=q*layout.localRotation;layout.velocity=Vector3.Cross(axis*speed,radial);layout.time=at;return layout;
        }
        public float UncertaintyAt(float at,float radius)
        {
            double dt=Math.Max(0,at-time),variance=best.p[0,0];
            if(!best.sinusoid)variance+=1e-7*dt;
            else {double phase=best.x[3],w=best.x[2],a=best.x[1],c=Cos(phase)-Cos(phase+w*dt);double[] j={1,best.sign*(-dt+c/w),best.sign*a*(-c/(w*w)+Sin(phase+w*dt)*dt/w),best.sign*a/w*(-Sin(phase)+Sin(phase+w*dt))};variance=0;for(int i=0;i<4;i++)for(int k=0;k<4;k++)variance+=j[i]*best.p[i,k]*j[k];}
            // Competing IMM models can each be locally confident while their
            // future phases still disagree. Include that disagreement in fire gating.
            double reference=best.At(dt);foreach(var model in models){double difference=model.At(dt)-reference;variance+=model.weight*difference*difference;}
            return radius*(float)Math.Sqrt(Math.Max(0,variance));
        }
        public static bool Solve(Vector3 muzzle,DeveloperPlate layout,DeveloperRuneTracker tracker,float now,float delay,DeveloperRules rules,out Vector3 direction,out float flight,out DeveloperPlate predicted)
        {
            direction=Vector3.forward;flight=0;predicted=layout;if(!tracker.ready||tracker.updates<5||now-tracker.lastSeen>1.5f)return false;
            Vector3 g=rules.gravityCompensation?Vector3.down*rules.gravity:Vector3.zero;
            Func<float,float> eq=t=>(tracker.At(layout,now+delay+t).center-muzzle-.5f*g*t*t).magnitude/t-rules.bulletSpeed;
            float lo=.002f,hi=0,old=eq(lo);for(float t=.025f;t<4;t+=.025f){float value=eq(t);if(value<=0&&old>=0){hi=t;break;}lo=t;old=value;}if(hi==0)return false;
            for(int i=0;i<20;i++){float mid=(lo+hi)*.5f;if(eq(mid)>0)lo=mid;else hi=mid;}flight=(lo+hi)*.5f;predicted=tracker.At(layout,now+delay+flight);
            direction=(predicted.center-muzzle-.5f*g*flight*flight).normalized;Vector3 impact=direction*rules.bulletSpeed+Vector3.down*rules.gravity*flight;
            return -Vector3.Dot(impact-predicted.velocity,predicted.Normal)>rules.armorMinVelocity&&Vector3.Dot(predicted.Normal,(muzzle-predicted.center).normalized)>.12f;
        }
    }
    public partial class ReplayViewer
    {
        readonly Dictionary<string,DeveloperRuneTracker> runeTrackers=new Dictionary<string,DeveloperRuneTracker>();
        readonly Queue<RuneObservation> runeObservations=new Queue<RuneObservation>();float nextRuneObservation;
        DeveloperRune observedRuneSource;float observedRuneStarted;
        void ObserveGameRunes()
        {
            if(!runeRotor)return;
            var source=game.RuneMotionSource();
            if(source!=observedRuneSource||source!=null&&source.started!=observedRuneStarted)
            {runeTrackers.Clear();runeObservations.Clear();nextRuneObservation=game.time;observedRuneSource=source;observedRuneStarted=source==null?0:source.started;}
            if(source==null)return;
            if(game.time>=nextRuneObservation)
            {
                nextRuneObservation=game.time+1/Mathf.Clamp(game.rules.observationHz,1,120);
                Vector3 center=runeRotor.parent.TransformPoint(PassagePose.Vector(mechanismConfig.rune.pivot)),axis=runeRotor.parent.TransformDirection(PassagePose.Vector(mechanismConfig.rune.axis)).normalized;
                foreach(string team in new[]{"red","blue"})
                {
                    var plates=new List<DeveloperPlate>();var lights=new List<bool>();var hitLights=new List<bool>();float angle=0;
                    foreach(var plate in gamePlates)
                    {
                        if(!plate.rune||plate.team!=team||!GamePlateVisible(plate)||observationRandom.NextDouble()<game.rules.observationDrop)continue;
                        var arm=runeArms.Find(a=>a.team==team&&a.index==plate.index);if(arm==null)continue;
                        var baseRadial=runeRotor.parent.TransformDirection(PassagePose.Vector(arm.geometry.targetCenter)-PassagePose.Vector(mechanismConfig.rune.pivot));
                        Vector3 observed=plate.center+new Vector3(Noise(),Noise(),Noise())*game.rules.observationNoise;
                        if(plates.Count==0)angle=DeveloperRuneTracker.ObservedPhase(baseRadial,observed-center,axis);
                        var copy=plate;copy.center=observed;plates.Add(copy);lights.Add(arm.targetPattern&&arm.targetPattern.enabled);hitLights.Add(arm.successDot&&arm.successDot.enabled);
                    }
                    if(plates.Count>0)runeObservations.Enqueue(new RuneObservation {target="rune_"+team,groupSerial=game.runes[team].groupSerial,time=game.time,angle=angle,center=center,axis=axis,plates=plates.ToArray(),lit=lights.ToArray(),alreadyHit=hitLights.ToArray()});
                }
            }
            while(runeObservations.Count>0&&runeObservations.Peek().time+game.rules.observationDelay<=game.time)
            {
                var observation=runeObservations.Dequeue();if(!runeTrackers.TryGetValue(observation.target,out var tracker))runeTrackers[observation.target]=tracker=new DeveloperRuneTracker();tracker.Correct(observation,game.rules);
            }
        }
    }
}
