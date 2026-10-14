using System;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public class BallisticConfig
    {
        public float headingOffset=90, headingSign=-1, gravity=9.81f, speed17=25, speed42=12;
        public float restitutionArmor=.28f, restitutionGround=.34f, tangentRetention=.68f;
        public bool headingCalibrated=false;
        public string ruleVersion="V2.0.1 (20260629)", source="规则上限初速；俯仰/目标/碰撞为推定；英雄部署状态缺失时常规 12m/s";
    }
    public static class BallisticMath
    {
        public const float Step=1f/120, FadeStart=7, Life=8;
        public static float Opacity(float age) => age<0 || age>=Life?0:1-Mathf.Clamp01(age-FadeStart);
        public static bool Heading(EntityData data,float time,BallisticConfig config,out float angle)
        {
            angle=0;int i=Timeline.StateIndex(data.samples,time);if(i<0) return false;
            var a=data.samples[i];if(!a.hasGun || time-a.t>2) return false;
            float value=a.gun;
            if(i+1<data.samples.Length)
            {
                var b=data.samples[i+1];if(b.hasGun && b.t>a.t && b.t-a.t<=2)
                    value=Mathf.LerpAngle(a.gun,b.gun,(time-a.t)/(b.t-a.t));
            }
            angle=config.headingOffset+config.headingSign*value;return true;
        }
        public static Matrix4x4 Around(Vector3 pivot,Quaternion rotation) => Matrix4x4.TRS(pivot-rotation*pivot,rotation,Vector3.one);
        public static Matrix4x4 Fold(RobotGroup group,float amount)
        {
            var rotation=Quaternion.Slerp(Quaternion.identity,PassagePose.Rotation(group.foldRotation),amount);
            var pivot=PassagePose.Vector(group.pivot);
            return Matrix4x4.TRS(pivot-rotation*pivot+PassagePose.Vector(group.foldTranslation)*amount,rotation,Vector3.one);
        }
        public static float Support(RobotModel model,float fold)
        {
            var values=model.groundOffsets;if(values==null || values.Length<2) return 0;
            float p=Mathf.Clamp01(fold)*(values.Length-1);int i=Mathf.Min(values.Length-2,(int)p);return Mathf.Lerp(values[i],values[i+1],p-i);
        }
        public static Matrix4x4 Group(RobotModel model,int i,float fold,float heading,float elevation,bool useHeading)
        {
            var group=model.groups[i];var matrix=Fold(group,fold);
            if(group.pitch)
            {
                Vector3 axis=Vector3.Cross(Vector3.up,PassagePose.Vector(model.gunForward)).normalized;
                matrix=matrix*Around(PassagePose.Vector(model.pitchPivot),Quaternion.AngleAxis(-elevation,axis));
            }
            if(group.turret && useHeading)
            {
                Vector3 nativeForward=Fold(model.groups[model.gunGroup],fold).MultiplyVector(PassagePose.Vector(model.gunForward));
                float nativeYaw=Mathf.Atan2(nativeForward.x,nativeForward.z)*Mathf.Rad2Deg;
                matrix=Around(PassagePose.Vector(model.yawPivot),Quaternion.Euler(0,heading-nativeYaw,0))*matrix;
            }
            return matrix;
        }
        public static Vector3 Direction(float yaw,float pitch) => Quaternion.Euler(-pitch,yaw,0)*Vector3.forward;
        public static bool LowArc(Vector3 delta,float speed,float gravity,out float degrees,out float flight)
        {
            degrees=flight=0;float range=new Vector2(delta.x,delta.z).magnitude;if(range<.025f || speed<=0 || gravity<=0) return false;
            float v2=speed*speed,disc=v2*v2-gravity*(gravity*range*range+2*delta.y*v2);if(disc<0) return false;
            // Rational form avoids catastrophic cancellation for a short flat shot.
            float tangent=(gravity*range*range+2*delta.y*v2)/(range*(v2+Mathf.Sqrt(disc)));
            degrees=Mathf.Atan(tangent)*Mathf.Rad2Deg;flight=range/(speed*Mathf.Cos(degrees*Mathf.Deg2Rad));return float.IsFinite(flight);
        }
        // A horizontal yaw ray aimed at an armor orbit, independent of the
        // unavailable actual rotor phase. Tolerance never changes recorded yaw.
        public static bool HorizontalRing(Vector3 origin,Vector3 forward,Vector3 center,float radius,float positionTolerance,out Vector3 point)
        {
            point=Vector3.zero;forward.y=0;
            if(radius<=0 || forward.sqrMagnitude<.0001f)return false;forward.Normalize();
            Vector3 delta=center-origin;delta.y=0;float along=Vector3.Dot(delta,forward);
            if(along<=radius)return false;
            Vector3 side=delta-forward*along;float offset=side.magnitude;
            if(offset>radius+positionTolerance+along*Mathf.Tan(Mathf.Deg2Rad))return false;
            if(offset<=radius)point=origin+forward*(along-Mathf.Sqrt(Mathf.Max(0,radius*radius-offset*offset)));
            else point=center-side.normalized*radius;
            point.y=center.y;return true;
        }
        // Relative swept sphere against a translating oriented box. Expanded box
        // is conservative at corners; interpolation uses the historical endpoints.
        public static bool SweepBox(Vector3 a,Vector3 b,Vector3 centerA,Vector3 centerB,Quaternion rotation,Vector3 half,float radius,out float fraction,out Vector3 normal)
        {
            fraction=0;normal=Vector3.zero;Quaternion inv=Quaternion.Inverse(rotation);
            Vector3 p=inv*(a-centerA),d=inv*((b-centerB)-(a-centerA));half+=Vector3.one*radius;
            bool inside=Mathf.Abs(p.x)<half.x && Mathf.Abs(p.y)<half.y && Mathf.Abs(p.z)<half.z;
            // Spawn/remaining-time contact inside a shell must not loop indefinitely.
            if(inside) return false;
            float enter=0,leave=1;Vector3 hit=Vector3.zero;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(d[axis])<1e-8f) {if(Mathf.Abs(p[axis])>half[axis])return false;continue;}
                float near=(-half[axis]-p[axis])/d[axis],far=(half[axis]-p[axis])/d[axis];float sign=-1;
                if(near>far) {float tmp=near;near=far;far=tmp;sign=1;}
                if(near>=enter) {enter=near;hit=Vector3.zero;hit[axis]=sign;}
                leave=Mathf.Min(leave,far);if(enter>leave)return false;
            }
            if(enter<0 || enter>1 || hit.sqrMagnitude==0) return false;
            fraction=enter;normal=rotation*hit;return true;
        }
        public static Vector3 Bounce(Vector3 velocity,Vector3 normal,Vector3 surfaceVelocity,float restitution,float tangent)
        {
            Vector3 relative=velocity-surfaceVelocity;float vn=Vector3.Dot(relative,normal);
            return surfaceVelocity+(relative-vn*normal)*tangent-Mathf.Min(0,vn)*normal*restitution;
        }
    }
}
