using UnityEngine;

namespace TcrReplay
{
    public sealed class DeveloperWheelPhysics
    {
        public readonly VehicleRigWheel Config;public readonly Rigidbody Body;public readonly ConfigurableJoint Joint;public readonly Collider Tire;
        readonly DeveloperPhysicsWorld world;readonly VehicleRig rig;
        public float Omega,Angle,TargetOmega,RequestedTorque,Load,Slip,JointValue,DriveIntegral,Penetration,CarrierLoad,DriveForce;
        public bool Sticking {get;private set;}
        public bool Supported=>driveSupported;
        public Vector3 SurfaceNormal {get;private set;}=Vector3.up;
        public Vector3 Traction=>driveTraction;
        public Vector3 DrivePoint=>drivePoint;
        public float DriveRadius=>driveRadius;
        public bool Grounded {get;private set;}
        public Vector3 SurfaceForce;Vector3 surfaceImpulse;
        public string PenetrationObject {get;private set;}="";
        public Vector3 PenetrationPoint {get;private set;}
        Vector3 contactPoint,contactNormal=Vector3.up,pointSum,normalSum;float accumulatedImpulse,carrierImpulse,contactWeight,contactPenetration;bool contact;
        Vector3 drivePoint,driveAxle,driveTraction,driveCenter;float driveRadius,driveVelocity,driveLimit,staticLimit,effectiveInverseMass,rollerVelocity,rollerLimit;Vector3 rollerDirection;bool driveSupported;
        public Vector3 Center=>Body.transform.TransformPoint(Config.Center-Config.Pivot);
        public float RotorInertia=>Mathf.Max(.0004f,Config.rotorMass*Body.mass/Config.moduleMass*Config.radius*Config.radius*.6f);
        public DeveloperWheelPhysics(DeveloperPhysicsWorld world,VehicleRig rig,VehicleRigWheel config,Rigidbody body,ConfigurableJoint joint,Collider tire)
        {this.world=world;this.rig=rig;Config=config;Body=body;Joint=joint;Tire=tire;}
        public void Contact(Collision collision)
        {
            for(int i=0;i<collision.contactCount;i++)
            {
                var p=collision.GetContact(i);surfaceImpulse+=p.thisCollider.attachedRigidbody==Body?p.impulse:-p.impulse;if(p.thisCollider!=Tire&&p.otherCollider!=Tire)
                {Vector3 n=p.thisCollider.attachedRigidbody==Body?p.normal:-p.normal;var other=p.thisCollider.attachedRigidbody==Body?p.otherCollider:p.thisCollider;if(n.y>.35f&&(other.gameObject.layer==8||other.gameObject.layer==10))carrierImpulse+=Mathf.Abs(Vector3.Dot(p.impulse,n));continue;}
                if(-p.separation>contactPenetration){contactPenetration=-p.separation;PenetrationObject=(p.thisCollider==Tire?p.otherCollider:p.thisCollider).name;PenetrationPoint=p.point;}
                var normal=p.thisCollider==Tire?p.normal:-p.normal;
                Vector3 axle=Body.rotation*PassagePose.Vector(Config.axle);
                // Tread contacts on a legal riser also transmit motor torque.
                // Reject underside/end-cap contacts, not the front of a step.
                if(normal.y<-.05f||Mathf.Abs(Vector3.Dot(normal,axle))>.85f)continue;
                float impulse=Mathf.Abs(Vector3.Dot(p.impulse,normal)),weight=Mathf.Max(impulse,.00001f);accumulatedImpulse+=impulse;pointSum+=p.point*weight;normalSum+=normal*weight;contactWeight+=weight;contact=true;
            }
        }
        public void ClearContact(){contact=Grounded=false;Penetration=contactPenetration=Load=CarrierLoad=DriveForce=carrierImpulse=accumulatedImpulse=contactWeight=0;SurfaceForce=surfaceImpulse=pointSum=normalSum=Vector3.zero;}
        public float SuspensionTrim;
        public float MotorTorque(DeveloperRules rules,float dt,bool powered,float previousScale,float feedforward=0)
        {
            if(!powered){DriveIntegral=0;return 0;}
            float error=TargetOmega-Omega;
            if(previousScale<.98f)DriveIntegral*=Mathf.Exp(-8*dt);
            float candidate=DriveIntegral+error*rules.wheelIntegralGain*dt;
            float request=error*rules.wheelSpeedGain+candidate+feedforward;
            if(Mathf.Abs(request)<=rules.motorTorque||Mathf.Sign(error)!=Mathf.Sign(request))DriveIntegral=candidate;
            DriveIntegral=Mathf.Clamp(DriveIntegral,-rules.motorTorque,rules.motorTorque);
            return Mathf.Clamp(error*rules.wheelSpeedGain+DriveIntegral+feedforward,-rules.motorTorque,rules.motorTorque);
        }
        public void EndStep(float dt)
        {SurfaceForce=surfaceImpulse/dt;surfaceImpulse=Vector3.zero;Grounded=contact;Penetration=contactPenetration;CarrierLoad=carrierImpulse/dt;Load=contact?Mathf.Lerp(Load,accumulatedImpulse/dt,1-Mathf.Exp(-dt/.025f)):0;if(contactWeight>0){contactPoint=pointSum/contactWeight;contactNormal=normalSum.normalized;}accumulatedImpulse=carrierImpulse=contactWeight=contactPenetration=0;pointSum=normalSum=Vector3.zero;contact=false;}
        public float GoalSpeed(Vector3 velocity,float yawRate,Vector3 chassis,bool staticContact=false)
        {
            Vector3 normal=staticContact?SurfaceNormal:contactNormal;
            if(!Grounded)normal=world.Physics.Raycast(Center+Vector3.up*.02f,Vector3.down,out var hit,Config.radius+.1f,1<<8)?hit.normal:Vector3.up;
            DeveloperChassisControl.Basis(Config,Body.rotation,normal,out var traction,out _,out float coupling);
            return Vector3.Dot(traction,velocity+Vector3.Cross(Vector3.up*yawRate,Center-chassis))/(Config.radius*coupling);
        }
        public void ConfigureSuspension(DeveloperRules rules,Rigidbody chassis,float fold,float dt,float frameMinimumY,float clearance,float rideOffset=0)
        {
            if(Config.kind=="slider")
            {
                float sag=.003f,k=chassis.mass*rules.gravity/4/.012f*rules.suspensionScale;
                Joint.xDrive=new JointDrive {positionSpring=k,positionDamper=2*rules.suspensionDamping*Mathf.Sqrt(k*(Body.mass+chassis.mass/4)),maximumForce=chassis.mass*rules.gravity};
                float balance=rules.wheelStaticContact&&rules.activeSuspension&&Grounded?Mathf.Clamp(((chassis.mass/4+Body.mass)*rules.gravity-Load)/k*.6f,-.006f,.006f):0;
                Joint.targetPosition=new Vector3(sag+rideOffset+balance+SuspensionTrim+(Config.minTravel+Config.maxTravel)*.5f,0,0);JointValue=Vector3.Dot(chassis.transform.InverseTransformPoint(Body.position)-Config.Pivot,Config.Axis);
            }
            else
            {
                float desired=Config.foldAngle*fold;
                // The saved fully flattened pose puts the underside within
                // 2 mm of the tread. Keep bounded suspension compliance for
                // a crest instead of transferring the car's load to its frame.
                if(rules.activeSuspension&&clearance>rules.armGroundClearance+.001f||fold>.9f&&clearance>0)
                {
                    for(int i=0;i<4;i++)
                    {
                        Vector3 poseLever=Quaternion.AngleAxis(desired,Config.Axis)*(Config.Center-Config.Pivot);
                        float bottom=Config.Pivot.y+poseLever.y-Config.radius,excess=bottom-frameMinimumY+clearance;
                        float derivative=Vector3.Cross(Config.Axis,poseLever).y;
                        if(excess<=0||Mathf.Abs(derivative)<.03f)break;
                        desired-=Mathf.Clamp(excess/derivative*Mathf.Rad2Deg,0,4);
                    }
                }
                if(rules.activeSuspension&&fold<.95f&&clearance>rules.armGroundClearance)
                {
                    Vector3 crestLever=Quaternion.AngleAxis(desired,Config.Axis)*(Config.Center-Config.Pivot);
                    float derivative=Vector3.Cross(Config.Axis,crestLever).y;
                    if(Mathf.Abs(derivative)>.03f)desired-=Mathf.Clamp((clearance-rules.armGroundClearance)/derivative*Mathf.Rad2Deg,0,12);
                }
                // Correction changes each real arm; it never overwrites chassis attitude.
                // A continuous ramp legitimately pitches the whole chassis.
                // Do not cancel its world-space height change in each arm:
                // that flattens the stance against the terrain and unloads the
                // diagonal wheel pair. Real joint compliance and the bounded
                // measured-load trim below adapt to contact instead.
                if(rules.wheelStaticContact&&rules.activeSuspension)desired-=SuspensionTrim*500;
                desired=Mathf.Clamp(desired,Config.minAngle,Config.maxAngle);
                // Support load otherwise deflects a fully lowered arm beyond
                // the source pose and lifts the tire above the frame's bottom.
                // Compensate the estimated static moment through the servo;
                // all resulting forces remain internal to the real joint.
                Vector3 supportAxis=chassis.rotation*Config.Axis,lever=Center-Body.position;
                float support=(chassis.mass/4+Body.mass)*rules.gravity;
                bool near=world.Physics.Raycast(Center,Vector3.down,out _,Config.radius+.04f,(1<<8)|(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);
                float moment=Vector3.Dot(Vector3.Cross(lever,Vector3.up*(near?support:0))+Vector3.Cross(Body.worldCenterOfMass-Body.position,Vector3.down*Body.mass*rules.gravity),supportAxis);
                float target=desired-Mathf.Clamp(moment/rules.armSpring*Mathf.Rad2Deg,-6,6);
                Joint.angularXDrive=new JointDrive {positionSpring=rules.armSpring,positionDamper=rules.armDamper,maximumForce=rules.armTorque};Joint.targetRotation=Quaternion.Euler(-target,0,0);
                Quaternion relative=Quaternion.Inverse(chassis.rotation)*Body.rotation;relative.ToAngleAxis(out float degrees,out var axis);if(degrees>180)degrees-=360;JointValue=degrees*Mathf.Sign(Vector3.Dot(axis,Config.Axis));
            }
        }
        public void PrepareDrive(DeveloperRules rules,Rigidbody chassis)
        {
            Vector3 center=Center,axle=Body.rotation*PassagePose.Vector(Config.axle);float verticalRadius=Config.radius*Mathf.Sqrt(Mathf.Max(0,1-axle.y*axle.y))+Config.width*.5f*Mathf.Abs(axle.y);
            bool near=world.Physics.Raycast(center,Vector3.down,out var hit,verticalRadius+.012f,(1<<8)|(1<<10)|(1<<14),QueryTriggerInteraction.Ignore)&&hit.normal.y>.35f;
            bool supported=rules.wheelStaticContact?Grounded:near&&(Grounded||hit.distance<=verticalRadius+.004f);
            SurfaceNormal=rules.wheelStaticContact&&Grounded&&contactNormal.y>.35f?contactNormal:near?hit.normal:Grounded&&contactNormal.y>.35f?contactNormal:Vector3.up;
            Vector3 normal=Grounded?contactNormal:hit.normal;Vector3 point=Grounded?contactPoint:hit.point;
            // Rolling velocity is omega cross the contact radius. Projection
            // of a horizontal command misses its upward component on a riser.
            DeveloperChassisControl.Basis(Config,Body.rotation,normal,out var t,out _,out float coupling);
            driveCenter=center;driveAxle=axle;drivePoint=point;driveTraction=t;driveRadius=Config.radius*coupling;driveSupported=supported&&t.sqrMagnitude>.9f;
            driveVelocity=driveSupported?Vector3.Dot(Body.GetPointVelocity(point),t):0;
            float load=Grounded?Mathf.Clamp(Load,0,rules.mass*rules.gravity):chassis.mass*rules.gravity/4;
            driveLimit=rules.wheelGrip*load;staticLimit=Mathf.Max(rules.wheelStaticGrip,rules.wheelGrip)*load;
            Vector3 lever=point-chassis.worldCenterOfMass,moment=Vector3.Cross(lever,t),local=Quaternion.Inverse(chassis.rotation*chassis.inertiaTensorRotation)*moment;
            Vector3 tensor=chassis.inertiaTensor;
            effectiveInverseMass=1/Mathf.Max(1,rules.mass)+local.x*local.x/Mathf.Max(.01f,tensor.x)+local.y*local.y/Mathf.Max(.01f,tensor.y)+local.z*local.z/Mathf.Max(.01f,tensor.z);
            rollerDirection=Vector3.Cross(normal,t).normalized;rollerVelocity=driveSupported?Vector3.Dot(Body.GetPointVelocity(point),rollerDirection):0;rollerLimit=rules.wheelRollResistance*load;
        }
        float RotorStep(DeveloperRules rules,float torque,float dt,out float force)
        {
            float inertia=RotorInertia;force=0;
            if(driveSupported)
            {
                float r=driveRadius;
                if(rules.wheelStaticContact)
                {
                    // A finite tangential impulse couples rotor inertia and
                    // vehicle effective mass. Unlike a viscous slip spring,
                    // this transmits static torque without steady wheelspin.
                    float required=(r*Omega-driveVelocity+dt*r*torque/inertia)/(dt*(r*r/inertia+effectiveInverseMass));
                    force=Mathf.Abs(required)<=staticLimit?required:Mathf.Clamp(required,-driveLimit,driveLimit);
                }
                else
                {float k=rules.wheelSlip;float next=(inertia*Omega+dt*(torque+k*r*driveVelocity))/(inertia+dt*k*r*r);force=Mathf.Clamp(k*(r*next-driveVelocity),-driveLimit,driveLimit);}
            }
            return Omega+(torque-driveRadius*force)*dt/inertia;
        }
        public float PredictedPower(DeveloperRules rules,float torque,float dt)
        {
            float next=RotorStep(rules,torque,dt,out _);
            // Use the same contact reaction as the rotor integrator. Desired
            // speed is not mechanical work: including it prevents a stalled
            // wheel from delivering legal low-speed climbing torque.
            float work=rules.wheelStaticContact?Mathf.Max(0,Mathf.Max(torque*Omega,torque*next)):Mathf.Abs(torque)*Mathf.Max(Mathf.Abs(Omega),Mathf.Abs(next));
            return work/Mathf.Max(.1f,rules.efficiency)+rules.motorCopperLoss*torque*torque;
        }
        public float PredictedBrakePower(DeveloperRules rules,float torque,float dt)
        {float next=RotorStep(rules,torque,dt,out _);return Mathf.Max(0,Mathf.Max(-torque*Omega,-torque*next));}
        public void Drive(DeveloperRules rules,Rigidbody chassis,float torque,float dt)
        {
            float inertia=RotorInertia;Omega=RotorStep(rules,torque,dt,out float f);
            DriveForce=f;
            if(driveSupported)
            {
                Body.AddForceAtPosition(driveTraction*f,drivePoint);Slip=driveRadius*Omega-driveVelocity;
                Sticking=rules.wheelStaticContact&&Mathf.Abs(f)<staticLimit+.0001f&&Mathf.Abs(Slip)<.02f;
                if(rules.wheelStaticContact&&rollerDirection.sqrMagnitude>.9f)Body.AddForceAtPosition(-rollerDirection*rollerLimit*Mathf.Clamp(rollerVelocity/.04f,-1,1),drivePoint);
                // Axle friction torque already changes the virtual rotor's
                // momentum. Remove that same component from the rigid carrier
                // so it is not applied a second time through the force lever.
                float axleMoment=Vector3.Dot(Vector3.Cross(drivePoint-driveCenter,driveTraction*f),driveAxle);Body.AddTorque(-driveAxle*axleMoment);
            }
            else{Slip=0;Sticking=false;Grounded=false;Load=0;}
            float beforeDrag=Omega;Omega=Mathf.Clamp(Mathf.MoveTowards(Omega*Mathf.Exp(-.2f*dt),0,rules.wheelBearingTorque*dt/inertia),-rules.wheelMaxOmega*1.5f,rules.wheelMaxOmega*1.5f);
            Angle=Mathf.Repeat(Angle+Omega*Mathf.Rad2Deg*dt,360);
            // Rotor reaction is internal: the virtual rotor receives the motor
            // torque, while its housing receives the opposite axle torque.
            Body.AddTorque(driveAxle*(-torque+inertia*(beforeDrag-Omega)/dt)-Vector3.Cross(Body.angularVelocity,driveAxle*inertia*Omega));
        }
    }
    public sealed class DeveloperChassisContact : MonoBehaviour
    {
        public Rigidbody body;public float GroundLoad;public Vector3 WallNormal,SurfaceForce;float impulse,wallWeight;Vector3 wallSum,surfaceImpulse;
        void Collect(Collision collision)
        {
            for(int i=0;i<collision.contactCount;i++)
            {
                var p=collision.GetContact(i);var other=p.thisCollider.attachedRigidbody==body?p.otherCollider:p.thisCollider;
                if(other.gameObject.layer!=8&&other.gameObject.layer!=10&&other.gameObject.layer!=17)continue;
                surfaceImpulse+=p.thisCollider.attachedRigidbody==body?p.impulse:-p.impulse;
                Vector3 normal=p.thisCollider.attachedRigidbody==body?p.normal:-p.normal;
                if(normal.y>.6f&&p.point.y<body.worldCenterOfMass.y-.04f)impulse+=Mathf.Abs(Vector3.Dot(p.impulse,normal));
                if(Mathf.Abs(normal.y)<.35f){float weight=Mathf.Max(.00001f,Mathf.Abs(Vector3.Dot(p.impulse,normal)));wallSum+=Vector3.ProjectOnPlane(normal,Vector3.up).normalized*weight;wallWeight+=weight;}
            }
        }
        void OnCollisionEnter(Collision collision){Collect(collision);}
        void OnCollisionStay(Collision collision){Collect(collision);}
        public void EndStep(float dt){SurfaceForce=surfaceImpulse/dt;GroundLoad=impulse/dt;WallNormal=wallWeight>0?wallSum.normalized:Vector3.zero;impulse=wallWeight=0;wallSum=surfaceImpulse=Vector3.zero;}
        public void Clear(){GroundLoad=impulse=wallWeight=0;WallNormal=wallSum=SurfaceForce=surfaceImpulse=Vector3.zero;}
    }
    public sealed class DeveloperWheelContact : MonoBehaviour
    {
        public DeveloperWheelPhysics wheel;
        void OnCollisionEnter(Collision collision){wheel?.Contact(collision);}
        void OnCollisionStay(Collision collision){wheel?.Contact(collision);}
    }
}
