using UnityEngine;

namespace TcrReplay
{
    public static class DeveloperChassisControl
    {
        public static Vector3 Wish(DeveloperInput input,float bodyYaw,bool spinBodyRelative)
        {
            var local=Vector3.ClampMagnitude(new Vector3(input.x,0,input.z),1);
            if(!input.cameraRelative)return local;
            return Quaternion.Euler(0,input.spin&&spinBodyRelative?bodyYaw:input.yaw,0)*local;
        }
        public static float Follow(float bodyYaw,float gimbalYaw,float actualOmega,DeveloperRules rules)
        {
            float error=Mathf.DeltaAngle(bodyYaw,gimbalYaw);
            if(Mathf.Abs(error)<rules.followDeadzone)error=0;
            return Mathf.Clamp(error*rules.followGain-actualOmega*rules.followDamping,-rules.followMaxSpeed,rules.followMaxSpeed)*Mathf.Deg2Rad;
        }
        public static float WheelSpeed(VehicleRigWheel wheel,Vector3 point,Quaternion moduleRotation,Vector3 localVelocity,float yawRate)
        {
            Basis(wheel,moduleRotation,Vector3.up,out var t,out _,out float coupling);
            return Vector3.Dot(t,localVelocity+Vector3.Cross(Vector3.up*yawRate,point))/(wheel.radius*coupling);
        }
        // Preserve the commanded horizontal direction on a continuous slope.
        // Projecting it onto the plane changes that direction on a cross slope.
        public static Vector3 SurfaceVelocity(Vector3 horizontal,Vector3 normal)
        {horizontal.y=normal.y>.35f?-(horizontal.x*normal.x+horizontal.z*normal.z)/normal.y:0;return horizontal;}
        public static void Basis(VehicleRigWheel wheel,Quaternion rotation,Vector3 normal,out Vector3 traction,out Vector3 rolling,out float coupling)
        {
            Vector3 axle=rotation*PassagePose.Vector(wheel.axle);rolling=Vector3.Cross(axle,normal).normalized;
            Vector3 localTraction=PassagePose.Vector(wheel.tractionDirection);
            traction=(rolling*Vector3.Dot(localTraction,PassagePose.Vector(wheel.rollingDirection))+Vector3.ProjectOnPlane(axle,normal).normalized*Vector3.Dot(localTraction,PassagePose.Vector(wheel.axle))).normalized;
            coupling=Mathf.Max(.3f,Mathf.Abs(Vector3.Dot(traction,rolling)));
        }
    }
}
