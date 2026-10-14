using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcrReplay
{
    public sealed class DeveloperVehiclePhysics : IDisposable
    {
        public readonly DeveloperActor Actor;public readonly VehicleRig Rig;
        public readonly Rigidbody Body;public readonly DeveloperWheelPhysics[] Wheels=new DeveloperWheelPhysics[4];
        public readonly GameObject root;public readonly Transform display;
        readonly DeveloperPhysicsWorld world;readonly DeveloperChassisContact frameContact;readonly List<Collider> colliders=new List<Collider>();
        readonly List<Transform> bodyMechanical=new List<Transform>();
        readonly Transform[] moduleVisuals=new Transform[4],tireVisuals=new Transform[4],damperVisuals=new Transform[4];
        readonly Quaternion[] damperBasis=new Quaternion[4];readonly float[] damperLengths=new float[4];
        readonly RobotModel model;readonly Transform[] legacyGroups;
        readonly Bounds bodyBounds;Vector3 publishedPosition,publishedVelocity;float publishedYaw,mass=-1;
        Quaternion previousRotation,currentRotation;Vector3 previousPosition,currentPosition;
        DeveloperInput input=new DeveloperInput();bool frozen,alive=true;
        readonly float[] goals=new float[4],feedTorques=new float[4],supportTorques=new float[4],motionTorques=new float[4];readonly Vector3[] forceColumns=new Vector3[4];readonly float[] forceWeights=new float[4],boundedForces=new float[4],forceLimits=new float[4],trimDeltas=new float[4];Vector3 allocationDirection;
        public DeveloperInput Command=>input;
        public string ControlDiagnostic=>"ref="+driveReference+" integral="+driftIntegral+" ride="+rideReference+" frameLoad="+frameContact.GroundLoad+" frameForce="+frameContact.SurfaceForce+" net="+instantForce+" acceleration="+instantAcceleration+" underbody="+underbodyHold+" actuator="+actuatorPower+" scale="+torqueScale;
        Vector3 preMomentum,instantForce,instantAcceleration;
        public IEnumerable<Collider> Shapes=>colliders;
        Vector3 comOffset=new Vector3(100,100,100);
        Vector3 driveReference,driftIntegral;float yawReference,foldReference,rideReference;
        float actuatorPower,torqueScale,climbCooldown,underbodyHold;
        float electricalWork,brakeWork,holdTime,rampAssistHold;bool energyBurst;
        public bool RampAssistActive {get;private set;}
        public Vector3 SupportNormal {get;private set;}=Vector3.up;
        public float SlopeSpeedLimit {get;private set;}
        public float BrakePower {get;private set;}
        public string MotionState {get;private set;}="静止";
        readonly Vector3[] previousModules=new Vector3[4];readonly Quaternion[] previousModuleRotations=new Quaternion[4];readonly float[] previousAngles=new float[4];
        public float MotorPower {get;private set;}
        public int Grounded {get;private set;}
        public bool Frozen {get=>frozen;set {if(frozen==value)return;frozen=value;Body.isKinematic=value;foreach(var w in Wheels)w.Body.isKinematic=value;}}
        public DeveloperVehiclePhysics(DeveloperPhysicsWorld world,DeveloperActor actor,VehicleRig rig,Transform display,RobotModel model,Transform[] legacyGroups,Func<string,Mesh> mesh,Material paint)
        {
            this.world=world;Actor=actor;Rig=rig;this.display=display;this.model=model;this.legacyGroups=legacyGroups;
            root=world.NewObject(actor.id+" physical chassis",15);Body=MakeBody(root,rig.bodyMass);frameContact=root.AddComponent<DeveloperChassisContact>();frameContact.body=Body;
            Bounds bounds=new Bounds(new Vector3(0,.14f,0),new Vector3(.25f,.15f,.25f));bool first=true;
            foreach(var node in rig.nodes)
            {
                int i=node.module;var parent=i<0?display:(moduleVisuals[i]?moduleVisuals[i]:CreateModuleVisual(i));
                var obj=new GameObject(node.id).transform;obj.SetParent(parent,false);
                if(node.id.StartsWith("tire",StringComparison.Ordinal)){obj.localPosition=rig.wheels[i].Center-rig.wheels[i].Pivot;tireVisuals[i]=obj;}
                if(node.id.StartsWith("damper",StringComparison.Ordinal)){obj.SetParent(display,false);damperVisuals[i]=obj;}
                for(int lod=0;lod<3;lod++)
                {var item=new GameObject("Mechanical LOD "+lod);item.transform.SetParent(obj,false);item.AddComponent<MeshFilter>().sharedMesh=mesh(node.meshes[lod]);item.AddComponent<MeshRenderer>().sharedMaterial=paint;}
                if(i<0)foreach(var path in node.collisionMeshes)
                {var shape=mesh(path);AddCollider(root.transform,shape,Vector3.zero,Quaternion.identity,world.bodyMaterial);if(first){bounds=shape.bounds;first=false;}else bounds.Encapsulate(shape.bounds);}
            }
            bodyBounds=bounds;
            for(int i=0;i<4;i++)
            {
                var cfg=rig.wheels[i];var obj=world.NewObject(actor.id+" physical "+cfg.label,16);var rb=MakeBody(obj,cfg.moduleMass);
                obj.transform.position=cfg.Pivot;
                var joint=obj.AddComponent<ConfigurableJoint>();joint.connectedBody=Body;joint.autoConfigureConnectedAnchor=false;joint.anchor=Vector3.zero;joint.connectedAnchor=cfg.Pivot+(cfg.kind=="slider"?cfg.Axis*((cfg.minTravel+cfg.maxTravel)*.5f):Vector3.zero);
                joint.axis=cfg.Axis;joint.secondaryAxis=Mathf.Abs(Vector3.Dot(cfg.Axis,Vector3.up))>.95f?Vector3.forward:Vector3.up;
                joint.xMotion=cfg.kind=="slider"?ConfigurableJointMotion.Limited:ConfigurableJointMotion.Locked;joint.yMotion=joint.zMotion=ConfigurableJointMotion.Locked;
                joint.angularXMotion=cfg.kind=="hinge"?ConfigurableJointMotion.Limited:ConfigurableJointMotion.Locked;joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Locked;
                joint.linearLimit=new SoftJointLimit {limit=(cfg.maxTravel-cfg.minTravel)*.5f,bounciness=0,contactDistance=.001f};
                // PhysX twist limits use the opposite sign to our CAD arm angle.
                joint.lowAngularXLimit=new SoftJointLimit {limit=-cfg.maxAngle,contactDistance=.5f};joint.highAngularXLimit=new SoftJointLimit {limit=-cfg.minAngle,contactDistance=.5f};
                joint.rotationDriveMode=RotationDriveMode.XYAndZ;joint.enableCollision=false;joint.enablePreprocessing=true;joint.massScale=1;joint.connectedMassScale=1;
                var contact=new GameObject("Tire normal contact");contact.transform.SetParent(obj.transform,false);contact.layer=16;contact.transform.localPosition=cfg.Center-cfg.Pivot;contact.transform.localRotation=Quaternion.FromToRotation(Vector3.right,PassagePose.Vector(cfg.axle));
                var tire=contact.AddComponent<MeshCollider>();tire.sharedMesh=world.TireMesh(cfg);tire.convex=true;tire.sharedMaterial=world.tireMaterial;tire.contactOffset=.001f;colliders.Add(tire);
                foreach(var node in rig.nodes)if(node.id=="carrier-"+i)foreach(var path in node.collisionMeshes)AddCollider(obj.transform,mesh(path),Vector3.zero,Quaternion.identity,world.bodyMaterial);
                Wheels[i]=new DeveloperWheelPhysics(world,rig,cfg,rb,joint,tire);
                obj.AddComponent<DeveloperWheelContact>().wheel=Wheels[i];
                if(damperVisuals[i]&&cfg.damperFixed!=null)
                {
                    Vector3 fixedPoint=PassagePose.Vector(cfg.damperFixed),moving=PassagePose.Vector(cfg.damperMoving);var dir=moving-fixedPoint;damperLengths[i]=dir.magnitude;damperBasis[i]=Quaternion.LookRotation(dir,Vector3.right);
                    damperVisuals[i].localPosition=fixedPoint;damperVisuals[i].localRotation=damperBasis[i];
                    foreach(Transform child in damperVisuals[i]){child.localRotation=Quaternion.Inverse(damperBasis[i]);child.localPosition=Quaternion.Inverse(damperBasis[i])*(cfg.Pivot-fixedPoint);}
                }
            }
            // Only this vehicle's adjacent assembly is ignored. Other cars and
            // all field geometry remain physically collidable.
            for(int a=0;a<colliders.Count;a++)for(int b=a+1;b<colliders.Count;b++)Physics.IgnoreCollision(colliders[a],colliders[b],true);
            foreach(int group in rig.legacyTurretGroups)
            {
                var parent=new GameObject("Physical cloud "+group).transform;parent.SetParent(Body.transform,false);bodyMechanical.Add(parent);
            }
            Reset(actor.position,Quaternion.Euler(0,actor.yaw,0),actor.velocity,actor.physics);
            world.Add(this);
        }
        Transform CreateModuleVisual(int i)
        {var t=new GameObject("Independent "+Rig.wheels[i].label).transform;t.SetParent(display,false);t.localPosition=Rig.wheels[i].Pivot;return moduleVisuals[i]=t;}
        Rigidbody MakeBody(GameObject obj,float weight)
        {
            var body=obj.AddComponent<Rigidbody>();body.mass=weight;body.useGravity=false;body.linearDamping=.01f;body.angularDamping=.04f;body.maxAngularVelocity=30;body.maxDepenetrationVelocity=1.5f;body.solverIterations=12;body.solverVelocityIterations=6;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;body.interpolation=RigidbodyInterpolation.None;return body;
        }
        void AddCollider(Transform parent,Mesh shape,Vector3 position,Quaternion rotation,PhysicsMaterial material)
        {var node=new GameObject("Rigid contact "+shape.name);node.layer=parent.gameObject.layer;node.transform.SetParent(parent,false);node.transform.localPosition=position;node.transform.localRotation=rotation;var c=node.AddComponent<MeshCollider>();c.sharedMesh=shape;c.convex=true;c.sharedMaterial=material;c.contactOffset=.002f;foreach(var other in colliders)Physics.IgnoreCollision(c,other,true);colliders.Add(c);world.IgnoreDuplicateFloor(c);}
        public void AddCloudCollision(int group,Mesh mesh)
        {int i=Array.IndexOf(Rig.legacyTurretGroups,group);if(i>=0)AddCollider(bodyMechanical[i],mesh,Vector3.zero,Quaternion.identity,world.bodyMaterial);}
        public void Submit(DeveloperInput command){input=command;}
        public void EnsureSpawnClearance()
        {
            // Explicit spawn/ground placement only. Legacy footprint corners
            // do not coincide with the independently articulated tire centers.
            float lift=0;
            foreach(var wheel in Wheels)
            {
                var center=wheel.Center;
                if(!world.Physics.Raycast(center+Vector3.up*.02f,Vector3.down,out var floor,wheel.Config.radius+.25f,(1<<8)|(1<<10)|(1<<14),QueryTriggerInteraction.Ignore)||floor.normal.y<.35f)continue;
                var axle=wheel.Body.rotation*PassagePose.Vector(wheel.Config.axle);float axial=Mathf.Clamp(Vector3.Dot(axle,floor.normal),-1,1);
                float support=wheel.Config.radius*Mathf.Sqrt(1-axial*axial)+wheel.Config.width*.5f*Mathf.Abs(axial);
                lift=Mathf.Max(lift,(support+.004f-Vector3.Dot(center-floor.point,floor.normal))/floor.normal.y);
            }
            if(lift>.0001f)Reset(Body.position+Vector3.up*lift,Body.rotation,Actor.velocity,Actor.physics);
        }
        public void SyncEdits()
        {
            if((Actor.position-publishedPosition).sqrMagnitude>.0000001f||Mathf.Abs(Mathf.DeltaAngle(Actor.yaw,publishedYaw))>.001f)
            {var tilt=Quaternion.Inverse(Quaternion.Euler(0,publishedYaw,0))*Body.rotation;Reset(Actor.position,Quaternion.Euler(0,Actor.yaw,0)*tilt,Actor.velocity,null);}
            else if((Actor.velocity-publishedVelocity).sqrMagnitude>.000001f&&!Frozen){Body.linearVelocity=Actor.velocity;foreach(var w in Wheels)w.Body.linearVelocity=Actor.velocity;}
        }
        public void Reset(Vector3 position,Quaternion rotation,Vector3 velocity,VehiclePhysicsState state=null)
        {
            bool wasFrozen=Frozen;Frozen=false;
            if(ValidState(state,Rig.revisionHash))rotation=state.rotation.normalized;else state=null;
            Body.transform.SetPositionAndRotation(position,rotation);Body.position=position;Body.rotation=rotation;Body.linearVelocity=velocity;Body.angularVelocity=state?.angularVelocity??Vector3.zero;
            for(int i=0;i<4;i++)
            {
                var w=Wheels[i];var cfg=w.Config;var q=Quaternion.AngleAxis(cfg.foldAngle*Actor.fold,cfg.Axis);
                Vector3 point=position+rotation*(state!=null?state.modules[i].localPosition:cfg.Pivot);Quaternion orientation=rotation*(state!=null?state.modules[i].localRotation:q);
                w.Body.transform.SetPositionAndRotation(point,orientation);w.Body.position=point;w.Body.rotation=orientation;
                w.Body.linearVelocity=state!=null?state.modules[i].velocity:velocity;w.Body.angularVelocity=state!=null?state.modules[i].angularVelocity:Vector3.zero;
                w.ClearContact();w.SuspensionTrim=state?.modules[i].suspensionTrim??0;w.Omega=state?.modules[i].wheelOmega??0;w.Angle=state?.modules[i].wheelAngle??0;w.DriveIntegral=state?.modules[i].wheelIntegral??0;w.ClearContact();previousModules[i]=point;previousModuleRotations[i]=orientation;previousAngles[i]=w.Angle;
            }
            frameContact.Clear();rampAssistHold=state?.rampAssistHold??0;RampAssistActive=energyBurst=false;underbodyHold=state?.underbodyHold??0;publishedPosition=Actor.position=previousPosition=currentPosition=position;publishedVelocity=Actor.velocity=velocity;driveReference=state?.driveReference??velocity;publishedYaw=Actor.yaw=Yaw(rotation);previousRotation=currentRotation=rotation;yawReference=state?.yawReference??Body.angularVelocity.y;foldReference=state?.foldReference??Actor.fold;torqueScale=state?.torqueScale??1;holdTime=state?.holdTime??0;driftIntegral=state?.driftIntegral??Vector3.zero;rideReference=state?.rideReference??0;climbCooldown=state?.climbCooldown??0;Body.WakeUp();CaptureSnapshot();ApplyVisuals(1);Frozen=wasFrozen;
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        public static bool ValidState(VehiclePhysicsState s,string hash)
        {
            if(s==null||s.schema!=1||s.rigHash!=hash||s.modules?.Length!=4||!ValidRotation(s.rotation)||!Finite(s.angularVelocity)||!Finite(s.driveReference)||!float.IsFinite(s.foldReference)||!float.IsFinite(s.yawReference)||!float.IsFinite(s.torqueScale)||!float.IsFinite(s.holdTime)||!Finite(s.driftIntegral)||!float.IsFinite(s.rideReference)||Mathf.Abs(s.rideReference)>.0201f||!float.IsFinite(s.climbCooldown)||s.climbCooldown<0||s.climbCooldown>3.01f||!float.IsFinite(s.underbodyHold)||s.underbodyHold<0||s.underbodyHold>1.51f||!float.IsFinite(s.rampAssistHold)||s.rampAssistHold<0||s.rampAssistHold>1.21f)return false;
            foreach(var m in s.modules)if(m==null||!Finite(m.localPosition)||m.localPosition.magnitude>2||!ValidRotation(m.localRotation)||!Finite(m.velocity)||!Finite(m.angularVelocity)||!float.IsFinite(m.wheelOmega)||!float.IsFinite(m.wheelAngle)||!float.IsFinite(m.wheelIntegral)||!float.IsFinite(m.suspensionTrim)||Mathf.Abs(m.suspensionTrim)>.0301f)return false;
            return true;
        }
        public static bool ValidRotation(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.5f;
        public static float Yaw(Quaternion q){Vector3 f=q*Vector3.forward;return Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg;}
        void AllocateForce(Vector3 force,float[] torques,float yawMoment=0)
        {
            var matrix=Matrix4x4.zero;matrix.m00=matrix.m11=matrix.m22=.0001f;matrix.m33=1;
            for(int i=0;i<4;i++)
            {
                var w=Wheels[i];var t=w.Traction;var r=w.DrivePoint-Body.worldCenterOfMass;
                var column=new Vector3(t.x,t.z,Vector3.Cross(r,t).y/.3f);forceColumns[i]=column;
                float weight=w.Supported?Mathf.Clamp(w.Load/Mathf.Max(1,mass*9.81f),.0001f,.8f):0;forceWeights[i]=weight;
                for(int x=0;x<3;x++)for(int y=0;y<3;y++)matrix[x,y]+=weight*column[x]*column[y];
            }
            Vector3 solved=matrix.inverse.MultiplyVector(new Vector3(force.x,force.z,yawMoment/.3f));
            for(int i=0;i<4;i++)torques[i]=forceWeights[i]*Vector3.Dot(forceColumns[i],solved)*Wheels[i].DriveRadius;
        }
        public void PreStep(DeveloperWorld game,float dt)
        {
            var rules=game.rules;
            if(alive!=Actor.Alive){alive=Actor.Alive;foreach(var c in colliders)c.enabled=alive;Frozen=!alive;}
            if(Frozen)return;
            if(Mathf.Abs(mass-rules.mass)>.0001f||comOffset!=rules.physicsComOffset)
            {
                mass=rules.mass;comOffset=rules.physicsComOffset;float scale=mass/Rig.mass;Body.mass=Rig.bodyMass*scale;Body.centerOfMass=new Vector3(bodyBounds.center.x,.14f,bodyBounds.center.z)+comOffset;
                Vector3 size=Vector3.Max(bodyBounds.size,new Vector3(.15f,.15f,.15f));Body.inertiaTensor=new Vector3(size.y*size.y+size.z*size.z,size.x*size.x+size.z*size.z,size.x*size.x+size.y*size.y)*Body.mass/12;
                foreach(var w in Wheels){w.Body.mass=w.Config.moduleMass*scale;w.Body.ResetCenterOfMass();w.Body.ResetInertiaTensor();}
            }
            float yaw=Yaw(Body.rotation);Vector3 wish=DeveloperChassisControl.Wish(input,yaw,rules.spinBodyRelative);allocationDirection=wish.normalized;
            if(rules.wheelStaticContact&&Vector3.Dot(wish,frameContact.WallNormal)<0)
            {
                // A real wall constrains the normal direction. Keep its free
                // tangent instead of integrating a command back into the wall.
                wish=Vector3.ProjectOnPlane(wish,frameContact.WallNormal);allocationDirection=wish.normalized;
                driveReference=Vector3.ProjectOnPlane(driveReference,frameContact.WallNormal);driftIntegral=Vector3.ProjectOnPlane(driftIntegral,frameContact.WallNormal);
            }
            bool powered=Actor.Alive&&game.time>=Actor.powerOffUntil;
            int contacts=0;Vector3 normalSum=Vector3.zero;
            foreach(var w in Wheels){w.PrepareDrive(rules,Body);if(w.Supported){contacts++;normalSum+=w.SurfaceNormal*Mathf.Max(.1f,w.Load);}}
            SupportNormal=normalSum.sqrMagnitude>.01f?normalSum.normalized:Vector3.up;
            Vector3 gravityForce=rules.wheelStaticContact&&contacts>=2&&SupportNormal.y>.35f?-rules.mass*Vector3.ProjectOnPlane(Vector3.down*rules.gravity,SupportNormal):Vector3.zero;
            bool approachingSlope=false;
            if(rules.wheelStaticContact&&wish.sqrMagnitude>.01f)
                foreach(var wheel in Wheels)
                {
                    Vector3 probe=wheel.Center+wish.normalized*(rules.wheelRampAssist?.45f:.30f)+Vector3.up*.5f;
                    if(world.Physics.Raycast(probe,Vector3.down,out var approaching,1,1<<8,QueryTriggerInteraction.Ignore)&&approaching.normal.y>.6f&&approaching.normal.y<.975f&&Vector3.Dot(approaching.normal,wish)<-.05f){approachingSlope=true;break;}
                }
            bool uphillNow=SupportNormal.y<.975f&&Vector3.Dot(gravityForce,wish)>3;
            bool assistEligible=rules.wheelRampAssist&&rules.wheelStaticContact&&rules.powerLimiter&&powered&&!input.slow&&wish.sqrMagnitude>.01f&&contacts>=2&&game.CapOutputLimit(Actor)>.1f;
            rampAssistHold=assistEligible&&(approachingSlope||uphillNow)?1.2f:assistEligible?Mathf.Max(0,rampAssistHold-dt):0;
            RampAssistActive=assistEligible&&rampAssistHold>0;
            bool useCap=input.burst||RampAssistActive;energyBurst|=powered&&useCap;
            float budget=powered?game.DrivePowerBudget(Actor,useCap):0;
            float mechanical=useCap?rules.burstMechanicalSpeed:rules.mechanicalSpeed;
            if(!input.cameraRelative)mechanical=Mathf.Min(mechanical,Actor.requestedSpeed);else if(input.slow&&!input.burst)mechanical*=rules.slowRatio;
            SlopeSpeedLimit=mechanical;
            if(rules.wheelStaticContact&&rules.powerLimiter&&wish.sqrMagnitude>.01f&&contacts>=2)
            {
                AllocateForce(gravityForce,feedTorques);float copper=0;for(int i=0;i<4;i++)copper+=rules.motorCopperLoss*feedTorques[i]*feedTorques[i];
                float uphill=Mathf.Max(0,Vector3.Dot(gravityForce,DeveloperChassisControl.SurfaceVelocity(wish.normalized,SupportNormal)));
                if(uphill>3)SlopeSpeedLimit=Mathf.Min(mechanical,Mathf.Max(.05f,(budget-rules.idlePower-actuatorPower-copper)*rules.efficiency/(uphill+rules.mass*rules.gravity*rules.wheelRollResistance)));
                // This is a sustainable speed estimate, not a command to dump
                // existing kinetic energy before a ramp. Actual positive motor
                // work is limited below; gravity slows an unsupported climb.
            }
            float steadyClimb=SlopeSpeedLimit;
            bool uphillMotion=rules.wheelStaticContact&&SupportNormal.y<.975f&&Vector3.Dot(gravityForce,wish)>3;
            if(uphillMotion)climbCooldown=rules.wheelClimbExitDelay;else climbCooldown=Mathf.Max(0,climbCooldown-dt);
            if(rules.wheelStaticContact&&rules.powerLimiter&&uphillMotion)
                mechanical=input.slow?Mathf.Min(mechanical,steadyClimb):Mathf.Min(mechanical,Mathf.Max(steadyClimb,Vector3.Dot(Body.linearVelocity,wish.normalized)));
            else if(climbCooldown>0)mechanical=Mathf.Min(mechanical,rules.wheelClimbExitSpeed);
            if(rules.wheelStaticContact&&rules.powerLimiter&&approachingSlope)mechanical=Mathf.Min(mechanical,RampAssistActive&&Rig.wheels[0].kind=="hinge"?rules.wheelAssistApproachSpeed:rules.wheelApproachSpeed);
            // The articulated chassis keeps its approach ceiling through the
            // crest and its exit dwell: the
            // forward probe can already see the flat platform while the rear
            // wheels are still on the ramp, otherwise the command jumps to
            // burstMechanicalSpeed just as support loads are changing.
            if(RampAssistActive&&Rig.wheels[0].kind=="hinge")mechanical=Mathf.Min(mechanical,rules.wheelAssistApproachSpeed);
            Vector3 desired=wish*mechanical;float currentOmega=Vector3.Dot(Body.angularVelocity,Vector3.up)*Mathf.Rad2Deg;
            float yawRate=input.spin?Actor.spinRate*Mathf.Deg2Rad:DeveloperChassisControl.Follow(yaw,Actor.gimbalYaw,currentOmega,rules);
            if(rules.wheelStaticContact&&!input.spin&&wish.sqrMagnitude<.0001f&&Mathf.Abs(Mathf.DeltaAngle(yaw,Actor.gimbalYaw))<rules.followDeadzone)
                yawRate=(Mathf.DeltaAngle(yaw,Actor.gimbalYaw)*rules.followGain*.25f-currentOmega*rules.followDamping)*Mathf.Deg2Rad;
            if(!input.spin&&wish.sqrMagnitude>.01f)
            {
                float radius=.1f;foreach(var w in Wheels){var p=w.Center-Body.position;radius=Mathf.Max(radius,new Vector2(p.x,p.z).magnitude);}
                float turnLimit=mechanical*wish.magnitude*rules.followDriveFraction/radius;
                yawRate=Mathf.Clamp(yawRate,-turnLimit,turnLimit);
            }
            if(!powered){desired=Vector3.zero;yawRate=0;}
            Vector3 priorReference=driveReference;
            bool releasing=wish.sqrMagnitude<.0001f,decelerating=desired.sqrMagnitude<driveReference.sqrMagnitude&&Vector3.Dot(desired-driveReference,driveReference)<0;
            float rate=rules.wheelStaticContact&&(releasing||decelerating)?rules.wheelBrakeDeceleration:useCap?rules.burstAcceleration:rules.acceleration;
            driveReference=Vector3.MoveTowards(driveReference,desired,rate*dt);driveReference.y=0;
            yawReference=Mathf.MoveTowards(yawReference,yawRate,rules.angularAcceleration*Mathf.Deg2Rad*dt);
            // During a commanded spin, keep the chassis origin from orbiting
            // its offset centre of mass. Ordinary driving controls COM velocity
            // so passive pitch/roll at a ramp edge does not induce braking.
            Vector3 horizontalVelocity=input.spin?Body.GetPointVelocity(Body.position):Body.linearVelocity;horizontalVelocity.y=0;
            if(powered&&releasing&&contacts>=2&&driveReference.magnitude<rules.wheelHoldSpeed&&horizontalVelocity.magnitude<rules.wheelHoldSpeed)holdTime+=dt;
            else if(!powered||!releasing||contacts<2||horizontalVelocity.magnitude>rules.wheelHoldRelease)holdTime=0;
            MotionState=!powered?"断电":contacts==0?"离地":!releasing?"行驶":holdTime>=rules.wheelHoldDelay?"保持":"制动";
            // Wheel speed can match its command while the chassis is slipping
            // or resting on a crest. Feedback requests additional finite wheel
            // torque through the same speed/current/power limits.
            var velocityGoal=driveReference+(driveReference-horizontalVelocity)*rules.chassisVelocityGain;float max=0;
            if(rules.wheelStaticContact)velocityGoal=DeveloperChassisControl.SurfaceVelocity(velocityGoal,SupportNormal);
            if(rules.wheelStaticContact&&powered&&contacts>=2)AllocateForce(gravityForce+rules.mass*DeveloperChassisControl.SurfaceVelocity((driveReference-priorReference)/dt,SupportNormal),feedTorques);
            else for(int i=0;i<4;i++)feedTorques[i]=0;
            for(int i=0;i<4;i++)
            {var w=Wheels[i];goals[i]=w.GoalSpeed(velocityGoal,yawReference,Body.position,rules.wheelStaticContact);max=Mathf.Max(max,Mathf.Abs(goals[i]));}
            float speedScale=max>rules.wheelMaxOmega?rules.wheelMaxOmega/max:1;
            if(rules.wheelStaticContact&&powered&&contacts>=2)
            {
                // Resolve a chassis wrench first, so a shared power limit does
                // not independently starve one of the two translation axes.
                AllocateForce(gravityForce,supportTorques);
                if(wish.sqrMagnitude>.01f)
                {
                    // A bounded lateral velocity integral rejects a persistent
                    // cross-slope disturbance without overwriting rigid motion.
                    Vector3 sideError=Vector3.ProjectOnPlane(driveReference-horizontalVelocity,wish.normalized);
                    driftIntegral=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(driftIntegral,wish.normalized)+sideError*dt,.12f);
                }
                else if(holdTime>=rules.wheelHoldDelay)driftIntegral=Vector3.ClampMagnitude(driftIntegral-horizontalVelocity*dt,.12f);
                else driftIntegral=Vector3.MoveTowards(driftIntegral,Vector3.zero,dt);
                Vector3 referenceAcceleration=(driveReference-priorReference)/dt;
                
                if(uphillMotion&&!input.slow&&wish.sqrMagnitude>.01f&&Vector3.Dot(referenceAcceleration,wish)<0)referenceAcceleration=Vector3.ProjectOnPlane(referenceAcceleration,wish.normalized);
                float velocityGain=Mathf.Max(1,rules.chassisVelocityGain*(releasing?30:20));
                Vector3 velocityError=driveReference-horizontalVelocity;
                bool articulated=Rig.wheels[0].kind=="hinge";
                float lateralPositionGain=articulated?rules.wheelArmLateralPositionGain:rules.wheelLateralPositionGain;
                Vector3 acceleration=driftIntegral*lateralPositionGain+referenceAcceleration+velocityError*velocityGain;
                // Match lateral position feedback with at least critical
                // velocity damping. Raising position gain alone can amplify
                // the wheel-load transfer transient at a non-planar crest.
                if(articulated&&wish.sqrMagnitude>.01f)
                    acceleration+=Vector3.ProjectOnPlane(velocityError,wish.normalized)*Mathf.Max(0,2*Mathf.Sqrt(lateralPositionGain)-velocityGain);
                float maxAcceleration=releasing?rules.wheelBrakeDeceleration:useCap?rules.burstAcceleration:rules.acceleration;
                if(wish.sqrMagnitude>.01f)
                {
                    Vector3 direction=wish.normalized,transverse=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(acceleration,direction),maxAcceleration*rules.wheelLateralPriority);
                    float remaining=Mathf.Sqrt(Mathf.Max(0,maxAcceleration*maxAcceleration-transverse.sqrMagnitude));
                    acceleration=transverse+direction*Mathf.Clamp(Vector3.Dot(acceleration,direction),-remaining,remaining);
                }
                else acceleration=Vector3.ClampMagnitude(acceleration,maxAcceleration);
                if(wish.sqrMagnitude>.01f)
                {
                    Vector3 stable=Vector3.ProjectOnPlane(acceleration,wish.normalized);
                    AllocateForce(gravityForce+rules.mass*DeveloperChassisControl.SurfaceVelocity(stable,SupportNormal),supportTorques);
                    acceleration-=stable;
                }
                float yawInertia=Body.inertiaTensor.y;foreach(var w in Wheels)yawInertia+=w.Body.mass*Vector3.ProjectOnPlane(w.Center-Body.worldCenterOfMass,Vector3.up).sqrMagnitude;
                float yawAcceleration=Mathf.Clamp((yawReference-currentOmega*Mathf.Deg2Rad)*12,-rules.angularAcceleration*Mathf.Deg2Rad,rules.angularAcceleration*Mathf.Deg2Rad);
                if(releasing&&horizontalVelocity.magnitude>.15f)yawAcceleration*=.3f;
                AllocateForce(rules.mass*DeveloperChassisControl.SurfaceVelocity(acceleration,SupportNormal),motionTorques,yawInertia*yawAcceleration);
                for(int i=0;i<4;i++)
                {
                    var w=Wheels[i];w.TargetOmega=goals[i]*speedScale;
                    // Rotor tracking corrects the small finite inertia and
                    // bearing losses; chassis feedback supplies the road load.
                    float tracking=w.MotorTorque(rules,dt,powered,torqueScale)*.12f;
                    if(releasing){w.DriveIntegral=0;tracking=0;}
                    motionTorques[i]+=tracking;
                }
                SetContactTorques(rules,1);
            }
            else for(int i=0;i<4;i++)
            {var w=Wheels[i];w.TargetOmega=goals[i]*speedScale;w.RequestedTorque=w.MotorTorque(rules,dt,powered,torqueScale,feedTorques[i]);}
            // Folding/lifting is rate- and power-limited. Passive spring work
            // does not draw electrical energy; moving the arm's commanded rest
            // position does. Gravity/holding losses are included in idlePower.
            float clearance=ArmClearance(rules);
            if(frameContact.GroundLoad>.5f)underbodyHold=1.5f;else underbodyHold=Mathf.Max(0,underbodyHold-dt);
            bool underbody=underbodyHold>0;
            float rideGoal=rules.wheelStaticContact&&rules.activeSuspension&&Rig.wheels[0].kind=="slider"?(underbody?.02f:Mathf.Clamp(clearance-rules.armGroundClearance,0,.01f)):0;
            float rideDelta=Mathf.MoveTowards(rideReference,rideGoal,.02f*dt)-rideReference;
            float foldDelta=Mathf.MoveTowards(foldReference,Actor.fold,3*dt)-foldReference;
            actuatorPower=0;foreach(var w in Wheels)if(w.Config.kind=="hinge")actuatorPower+=rules.armTorque*w.Config.foldAngle*Mathf.Deg2Rad*Mathf.Abs(foldDelta)/dt/Mathf.Max(.1f,rules.efficiency);
            actuatorPower+=rules.mass*rules.gravity*Mathf.Abs(rideDelta)/dt/Mathf.Max(.1f,rules.efficiency);
            for(int i=0;i<4;i++)
            {
                var wheel=Wheels[i];float trim=wheel.SuspensionTrim,trimLimit=wheel.Config.kind=="hinge"?rules.wheelArmTrimMax:.012f,trimRate=wheel.Config.kind=="hinge"?rules.wheelArmTrimRate:.012f;
                if(rules.wheelStaticContact&&rules.activeSuspension&&powered&&contacts>=2)
                {
                    float ideal=rules.mass*rules.gravity*SupportNormal.y/4,error=ideal-wheel.Load;
                    // Do not integrate contact solver noise into a slow change
                    // of parked attitude or continuously spend actuator power.
                    error=Mathf.Sign(error)*Mathf.Max(0,Mathf.Abs(error)-ideal*.02f);
                    trim=underbody
                        ?Mathf.MoveTowards(trim,Mathf.Max(trim,0),.012f*dt)
                        :Mathf.Clamp(trim+error*.0008f*dt,-trimLimit,trimLimit);
                }
                else trim=Mathf.MoveTowards(trim,0,.008f*dt);
                trimDeltas[i]=Mathf.Clamp(trim-wheel.SuspensionTrim,-trimRate*dt,trimRate*dt);
                float force=wheel.Config.kind=="slider"?rules.mass*rules.gravity/4:rules.armTorque*500*Mathf.Deg2Rad;
                actuatorPower+=force*Mathf.Abs(trimDeltas[i])/dt/Mathf.Max(.1f,rules.efficiency);
            }
            float actuatorBudget=Mathf.Max(0,budget-rules.idlePower)*.35f;
            if(!powered){foldDelta=rideDelta=0;for(int i=0;i<4;i++)trimDeltas[i]=0;actuatorPower=0;driftIntegral=Vector3.zero;}else if(rules.powerLimiter&&actuatorPower>actuatorBudget){float scale=actuatorBudget/actuatorPower;foldDelta*=scale;rideDelta*=scale;for(int i=0;i<4;i++)trimDeltas[i]*=scale;actuatorPower=actuatorBudget;}
            for(int i=0;i<4;i++)Wheels[i].SuspensionTrim+=trimDeltas[i];
            foldReference+=foldDelta;rideReference+=rideDelta;
            float available=Mathf.Max(0,budget-rules.idlePower-actuatorPower);float powerScale=1;
            if(rules.powerLimiter&&PredictedPower(rules,dt,1)>available||rules.wheelStaticContact&&PredictedBrakePower(rules,dt,1)>rules.wheelBrakePower)
            {float low=0,high=1;for(int j=0;j<12;j++){float mid=(low+high)*.5f;if(rules.powerLimiter&&PredictedPower(rules,dt,mid)>available||rules.wheelStaticContact&&PredictedBrakePower(rules,dt,mid)>rules.wheelBrakePower)high=mid;else low=mid;}powerScale=low;}
            if(rules.wheelStaticContact&&powered&&contacts>=2&&rules.powerLimiter&&PredictedPower(rules,dt,1)>available)
            {
                // Preserve uphill support torque before reducing commanded
                // acceleration. It still obeys current, adhesion and power.
                float low=0,high=1;for(int j=0;j<12;j++)
                {float mix=(low+high)*.5f;SetContactTorques(rules,mix);if(PredictedPower(rules,dt,1)>available)high=mix;else low=mix;}
                SetContactTorques(rules,low);
                powerScale=1;if(PredictedPower(rules,dt,1)>available)
                {float a=0,b=1;for(int j=0;j<12;j++){float mid=(a+b)*.5f;if(PredictedPower(rules,dt,mid)>available)b=mid;else a=mid;}powerScale=a;}
                if(PredictedBrakePower(rules,dt,powerScale)>rules.wheelBrakePower)
                {float a=0,b=powerScale;for(int j=0;j<12;j++){float mid=(a+b)*.5f;if(PredictedBrakePower(rules,dt,mid)>rules.wheelBrakePower)b=mid;else a=mid;}powerScale=a;}
            }
            torqueScale=powerScale;
            MotorPower=powered?rules.idlePower:0;Grounded=0;
            Body.AddForce(Vector3.down*rules.gravity*Body.mass);
            for(int i=0;i<4;i++)
            {
                var w=Wheels[i];w.Body.AddForce(Vector3.down*rules.gravity*w.Body.mass);w.ConfigureSuspension(rules,Body,foldReference,dt,bodyBounds.min.y,clearance,rideReference);
                float previousOmega=w.Omega,torque=w.RequestedTorque*powerScale;w.Drive(rules,Body,torque,dt);if(w.Grounded)Grounded++;
                MotorPower+=Mathf.Max(0,torque*(previousOmega+w.Omega)*.5f)/Mathf.Max(.1f,rules.efficiency)+rules.motorCopperLoss*torque*torque;
                brakeWork+=Mathf.Max(0,-torque*(previousOmega+w.Omega)*.5f)*dt;
            }
            MotorPower+=actuatorPower;
            preMomentum=CenterOfMassVelocity();instantForce=Vector3.down*rules.gravity*rules.mass;foreach(var w in Wheels)instantForce+=w.Traction*w.DriveForce;
            for(int i=0;i<bodyMechanical.Count;i++){var source=legacyGroups[Rig.legacyTurretGroups[i]];bodyMechanical[i].localPosition=source.localPosition;bodyMechanical[i].localRotation=source.localRotation;}
        }
        void SetContactTorques(DeveloperRules rules,float mix)
        {
            // A saturated wheel changes all three chassis wrench components.
            // Re-distribute the residual using a bounded convex solve, rather
            // than clipping each wheel independently and inducing a yaw kick.
            // Prioritize heading/lateral stability; forward acceleration can
            // yield when tire load or motor current is insufficient.
            float forwardWeight=.8f;
            if(RampAssistActive&&Rig.wheels[0].kind=="hinge"&&allocationDirection.sqrMagnitude>.5f)
            {
                Vector3 sideVelocity=Vector3.ProjectOnPlane(Body.linearVelocity,allocationDirection);sideVelocity.y=0;
                // Reserve scarce contact authority for correcting an observed
                // lateral transient before it becomes a large path deviation.
                // This changes the wrench objective, not the tire force bounds.
                float instability=Mathf.Clamp01(sideVelocity.magnitude/.12f+driftIntegral.magnitude/.04f);
                forwardWeight=Mathf.Lerp(.8f,rules.wheelArmForwardWeightMin,instability);
            }
            Vector3 target=Vector3.zero,actual=Vector3.zero;
            for(int i=0;i<4;i++)
            {
                var w=Wheels[i];float grip=Mathf.Abs(w.Slip)>.15f?rules.wheelGrip:Mathf.Max(rules.wheelStaticGrip,rules.wheelGrip);
                float radius=Mathf.Max(.01f,w.DriveRadius);
                forceLimits[i]=w.Supported?Mathf.Min(rules.motorTorque/radius,grip*Mathf.Max(0,w.Load)):0;
                float f=(supportTorques[i]+motionTorques[i]*mix)/radius;
                if(!float.IsFinite(f))f=0;
                target+=forceColumns[i]*f;boundedForces[i]=Mathf.Clamp(f,-forceLimits[i],forceLimits[i]);actual+=forceColumns[i]*boundedForces[i];
            }
            Vector3 weighted(Vector3 value)
            {
                if(allocationDirection.sqrMagnitude<.5f)return value;
                Vector2 along=new Vector2(allocationDirection.x,allocationDirection.z),xy=new Vector2(value.x,value.y);
                xy-=along*(Vector2.Dot(xy,along)*(1-forwardWeight));
                return new Vector3(xy.x,xy.y,value.z);
            }
            // Coordinate descent has no singular matrix inversions and cannot
            // leave the finite tire/current bounds, even with only two wheels.
            for(int iteration=0;iteration<24;iteration++)for(int i=0;i<4;i++)
            {
                Vector3 c=forceColumns[i],wc=weighted(c),error=weighted(actual-target);
                float regularization=.0002f/Mathf.Max(.02f,forceWeights[i]);
                float change=-(Vector3.Dot(wc,error)+regularization*boundedForces[i])/(wc.sqrMagnitude+regularization);
                float next=Mathf.Clamp(boundedForces[i]+change,-forceLimits[i],forceLimits[i]);
                actual+=c*(next-boundedForces[i]);boundedForces[i]=next;
            }
            for(int i=0;i<4;i++)Wheels[i].RequestedTorque=boundedForces[i]*Wheels[i].DriveRadius;
        }
        float ArmClearance(DeveloperRules rules)
        {
            float baseClearance=rules.armGroundClearance;
            if(!rules.activeSuspension||!rules.wheelStaticContact&&foldReference<.9f||!world.Physics.Raycast(Body.position+Vector3.up*.15f,Vector3.down,out var center,.6f,1<<8))return baseClearance;
            float mean=0;foreach(var w in Wheels)
            {if(!world.Physics.Raycast(w.Center+Vector3.up*.15f,Vector3.down,out var hit,.6f,1<<8))return baseClearance;mean+=hit.point.y*.25f;}
            // A crest lies above the plane joining the wheel contacts. The
            // arms yield within their limits; flat tunnels retain the CAD pose.
            return Mathf.Lerp(baseClearance,Mathf.Max(baseClearance,rules.armCrestClearance),Mathf.Clamp01((center.point.y-mean-.002f)/.02f));
        }
        float PredictedPower(DeveloperRules rules,float dt,float scale)
        {
            float sum=0;foreach(var w in Wheels)
            {
                sum+=w.PredictedPower(rules,w.RequestedTorque*scale,dt);
            }
            return sum;
        }
        float PredictedBrakePower(DeveloperRules rules,float dt,float scale)
        {float sum=0;foreach(var w in Wheels)sum+=w.PredictedBrakePower(rules,w.RequestedTorque*scale,dt);return sum;}
        public void PostStep(float dt)
        {currentPosition=Body.position;currentRotation=Body.rotation;electricalWork+=MotorPower*dt;frameContact.EndStep(dt);foreach(var w in Wheels)w.EndStep(dt);instantAcceleration=(CenterOfMassVelocity()-preMomentum)/dt;instantForce+=frameContact.SurfaceForce;foreach(var w in Wheels)instantForce+=w.SurfaceForce;}
        public void BeginTick()
        {electricalWork=brakeWork=0;energyBurst=false;previousPosition=Body.position;previousRotation=Body.rotation;for(int i=0;i<4;i++){previousModules[i]=Wheels[i].Body.position;previousModuleRotations[i]=Wheels[i].Body.rotation;previousAngles[i]=Wheels[i].Angle;}}
        public void Publish(DeveloperWorld game)
        {
            if(Frozen)return;Actor.position=publishedPosition=Body.position;Actor.velocity=publishedVelocity=Body.linearVelocity;Actor.yaw=publishedYaw=Yaw(Body.rotation);Actor.angularVelocity=Vector3.Dot(Body.angularVelocity,Vector3.up)*Mathf.Rad2Deg;Actor.verticalVelocity=Body.linearVelocity.y;
            MotorPower=electricalWork/DeveloperWorld.Step;BrakePower=brakeWork/DeveloperWorld.Step;Actor.estimatedPower=MotorPower;Actor.blocked=input.x*input.x+input.z*input.z>.05f&&new Vector2(Actor.velocity.x,Actor.velocity.z).magnitude<.03f;
            game.EnergyStep(Actor,energyBurst,MotorPower);
            CaptureSnapshot();
        }
        public void CaptureSnapshot()
        {
            var state=Actor.physics;if(state==null||state.modules?.Length!=4){state=Actor.physics=new VehiclePhysicsState {rigHash=Rig.revisionHash,modules=new VehicleModuleState[4]};for(int i=0;i<4;i++)state.modules[i]=new VehicleModuleState();}
            state.rotation=Body.rotation;state.angularVelocity=Body.angularVelocity;state.driveReference=driveReference;state.yawReference=yawReference;state.foldReference=foldReference;state.torqueScale=torqueScale;state.holdTime=holdTime;state.rideReference=rideReference;state.climbCooldown=climbCooldown;state.underbodyHold=underbodyHold;state.rampAssistHold=rampAssistHold;state.driftIntegral=driftIntegral;
            for(int i=0;i<4;i++){var w=Wheels[i];var s=state.modules[i];s.localPosition=Body.transform.InverseTransformPoint(w.Body.position);s.localRotation=Quaternion.Inverse(Body.rotation)*w.Body.rotation;s.velocity=w.Body.linearVelocity;s.angularVelocity=w.Body.angularVelocity;s.wheelOmega=w.Omega;s.wheelAngle=w.Angle;s.wheelIntegral=w.DriveIntegral;s.suspensionTrim=w.SuspensionTrim;}
        }
        public Vector3 CenterOfMassVelocity()
        {Vector3 momentum=Body.linearVelocity*Body.mass;float sum=Body.mass;foreach(var w in Wheels){momentum+=w.Body.linearVelocity*w.Body.mass;sum+=w.Body.mass;}return momentum/sum;}
        public void ApplyVisuals(float alpha)
        {
            display.SetPositionAndRotation(Vector3.Lerp(previousPosition,currentPosition,alpha),Quaternion.Slerp(previousRotation,currentRotation,alpha));
            for(int i=0;i<4;i++)
            {
                var w=Wheels[i];moduleVisuals[i].SetPositionAndRotation(Vector3.Lerp(previousModules[i],w.Body.position,alpha),Quaternion.Slerp(previousModuleRotations[i],w.Body.rotation,alpha));
                tireVisuals[i].localRotation=Quaternion.AngleAxis(Mathf.LerpAngle(previousAngles[i],w.Angle,alpha),PassagePose.Vector(w.Config.axle));
                if(damperVisuals[i]&&damperLengths[i]>.001f)
                {
                    Vector3 fixedLocal=PassagePose.Vector(w.Config.damperFixed),movingWorld=w.Body.transform.TransformPoint(PassagePose.Vector(w.Config.damperMoving)-w.Config.Pivot);
                    Vector3 delta=Body.transform.InverseTransformPoint(movingWorld)-fixedLocal;damperVisuals[i].localPosition=fixedLocal;damperVisuals[i].localRotation=Quaternion.LookRotation(delta,Vector3.right);damperVisuals[i].localScale=new Vector3(1,1,delta.magnitude/damperLengths[i]);
                }
            }
        }
        public void Dispose(){if(root){root.SetActive(false);UnityEngine.Object.Destroy(root);}foreach(var w in Wheels)if(w!=null&&w.Body){w.Body.gameObject.SetActive(false);UnityEngine.Object.Destroy(w.Body.gameObject);}}
    }
}
