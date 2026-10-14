using System;
using System.Collections.Generic;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        readonly Dictionary<string,Bounds[][]> driveFoldBounds=new Dictionary<string,Bounds[][]>();
        Bounds FoldedDriveBounds(RobotInstance robot,int group,float fold)
        {
            if(!driveFoldBounds.TryGetValue(robot.model.id,out var poses))
            {
                poses=new Bounds[robot.groups.Length][];
                for(int g=0;g<poses.Length;g++){var vertices=ReadMesh(robot.model.groups[g].meshes[0]).vertices;poses[g]=new Bounds[21];
                    for(int pose=0;pose<=20;pose++){var matrix=BallisticMath.Fold(robot.model.groups[g],pose/20f);bool first=true;foreach(var vertex in vertices){var point=matrix.MultiplyPoint3x4(vertex);if(first){poses[g][pose]=new Bounds(point,Vector3.zero);first=false;}else poses[g][pose].Encapsulate(point);}}
                }driveFoldBounds[robot.model.id]=poses;
            }
            float f=Mathf.Clamp01(fold)*20;int lo=Mathf.FloorToInt(f),hi=Mathf.CeilToInt(f);var bounds=poses[group][lo];if(lo!=hi){bounds.Encapsulate(poses[group][hi].min);bounds.Encapsulate(poses[group][hi].max);}return bounds;
        }
        readonly RaycastHit[] movementHits=new RaycastHit[48];
        RaycastHit movementBlockingHit;
        Vector3 driveContactNormal;float driveContactDepth;
        string driveRejectReason="";
        bool DriveReject(string reason){driveRejectReason=reason;return false;}
        float GroundFrom(Vector3 p,float rise=.18f)
        {
            float top=p.y+rise;
            foreach(var t in field.tunnels??Array.Empty<PassageRegion>())if(Mathf.Abs(p.x-t.x)<t.halfX+.3f&&Mathf.Abs(p.z-t.y)<t.halfY+.3f)top=Mathf.Min(top,t.floorMax+.02f);
            return DevRaycast(new Vector3(p.x,Mathf.Max(.02f,top),p.z),Vector3.down,out var hit,12,1<<8)?Mathf.Max(0,hit.point.y):0;
        }
        Quaternion GameSlope(Vector3 p)
        {
            float top=p.y+.18f;foreach(var t in field.tunnels??Array.Empty<PassageRegion>())if(Mathf.Abs(p.x-t.x)<t.halfX+.25f&&Mathf.Abs(p.z-t.y)<t.halfY+.25f)top=Mathf.Min(top,t.floorMax+.02f);
            if(DevRaycast(new Vector3(p.x,Mathf.Max(.02f,top),p.z),Vector3.down,out var hit,12,1<<8)&&hit.normal.y>=.65f)return Quaternion.FromToRotation(Vector3.up,hit.normal);
            return Quaternion.identity;
        }
        float GameGround(GameVehicle v,Vector3 p)
        {
            GroundSupport(v,p,v.state.yaw,out float height,out _);return height;
        }
        bool GroundHit(Vector3 p,out RaycastHit hit)
        {
            float top=p.y+.18f;foreach(var t in field.tunnels??Array.Empty<PassageRegion>())if(Mathf.Abs(p.x-t.x)<t.halfX+.3f&&Mathf.Abs(p.z-t.y)<t.halfY+.3f)top=Mathf.Min(top,t.floorMax+.02f);
            return DevRaycast(new Vector3(p.x,Mathf.Max(.02f,top),p.z),Vector3.down,out hit,12,1<<8,QueryTriggerInteraction.Ignore)&&hit.normal.y>=.65f;
        }
        readonly RaycastHit[] driveWheelContacts=new RaycastHit[4];readonly Vector3[] driveWheelOffsets=new Vector3[4];readonly bool[] driveWheelSupported=new bool[4];
        bool GroundSupport(GameVehicle v,Vector3 p,float yaw,out float height,out Quaternion tilt)
        {
            height=0;tilt=Quaternion.identity;if(!GroundHit(p,out var center))return false;
            Vector3 ext=v.unit.robot.footExtents*.95f;Quaternion heading=Quaternion.Euler(0,yaw,0);
            float weight=2,sx=0,sz=0,sy=0,sxx=0,szz=0,sxz=0,sxy=0,szy=0;
            // Derive pose and support from the SAME wheel contacts. A center-only
            // normal plus a maximum corner height tips the chassis into seams.
            for(int i=0;i<4;i++)
            {
                Vector3 offset=heading*new Vector3((i&1)==0?-ext.x:ext.x,0,(i&2)==0?-ext.z:ext.z);
                driveWheelOffsets[i]=offset;bool valid=GroundHit(p+offset,out var wheel)&&SupportContact(v,center,wheel,offset.magnitude);driveWheelSupported[i]=valid;driveWheelContacts[i]=wheel;if(!valid)continue;
                float y=wheel.point.y-center.point.y;weight++;sx+=offset.x;sz+=offset.z;sy+=y;sxx+=offset.x*offset.x;szz+=offset.z*offset.z;sxz+=offset.x*offset.z;sxy+=offset.x*y;szy+=offset.z*y;
            }
            float xx=sxx-sx*sx/weight,zz=szz-sz*sz/weight,xz=sxz-sx*sz/weight,xy=sxy-sx*sy/weight,zy=szy-sz*sy/weight,det=xx*zz-xz*xz;
            Vector3 normal=center.normal;if(det>1e-8f){Vector2 gradient=new Vector2((xy*zz-zy*xz)/det,(zy*xx-xy*xz)/det);gradient=Vector2.ClampMagnitude(gradient,1.169f);normal=new Vector3(-gradient.x,1,-gradient.y).normalized;}
            tilt=Quaternion.FromToRotation(Vector3.up,normal);height=Mathf.Max(0,center.point.y);
            for(int i=0;i<4;i++)
            {
                if(driveWheelSupported[i])height=Mathf.Max(height,driveWheelContacts[i].point.y-(tilt*driveWheelOffsets[i]).y);
            }
            return true;
        }
        bool SupportContact(GameVehicle v,RaycastHit center,RaycastHit wheel,float radius)
        {
            float difference=Mathf.Abs(wheel.point.y-center.point.y);
            if(difference<=StepLimit(v)+.003f)return true;
            if((center.normal.y>=.999f&&wheel.normal.y>=.999f)||difference>radius*1.18f+StepLimit(v)||Vector3.Dot(center.normal,wheel.normal)<.65f)return false;
            // A sloping corner can belong to a separate upper deck. Require a
            // continuous surface from the center instead of adopting that deck.
            int count=Mathf.Max(2,Mathf.CeilToInt(radius/.04f));float previous=center.point.y;
            for(int i=1;i<=count;i++)
            {
                Vector3 sample=Vector3.Lerp(center.point,wheel.point,i/(float)count);sample.y=Mathf.Max(center.point.y,wheel.point.y)+StepLimit(v);
                if(!GroundHit(sample,out var hit)||Mathf.Abs(hit.point.y-previous)>StepLimit(v)+radius/count*1.18f+.003f)return false;
                previous=hit.point.y;
            }
            return Mathf.Abs(previous-wheel.point.y)<.01f;
        }
        float CompressedHeight(GameVehicle v)
        {
            float low=float.MaxValue,high=float.MinValue;var robot=v.unit.robot;
            for(int g=0;g<robot.groups.Length;g++)
            {
                var m=BallisticMath.Group(robot.model,g,1,0,0,true);
                foreach(var filter in robot.groups[g].GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter.name!="LOD 0")continue;
                    foreach(var vertex in filter.sharedMesh.vertices){float y=m.MultiplyPoint3x4(vertex).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
                }
            }
            foreach(var plate in v.plateNodes)foreach(var filter in plate.GetComponentsInChildren<MeshFilter>(true))
            foreach(var vertex in filter.sharedMesh.vertices){float y=plate.localPosition.y+(plate.localRotation*vertex).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
            return Mathf.Max(.1f,high-low);
        }
        bool GameFolded(GameVehicle v)=>v.state.fold>v.unit.robot.model.defaultFold+.01f;
        float StepLimit(GameVehicle v)=>v.compressedHeight*.6f;
        Bounds GameBounds(RobotInstance robot,float fold)
        {
            Bounds bounds=new Bounds();bool first=true;
            for(int g=0;g<robot.groups.Length;g++){var m=BallisticMath.Group(robot.model,g,fold,0,0,true);var b=robot.groupBounds[g];for(int k=0;k<8;k++){var p=m.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}}
            return bounds;
        }
        bool VehicleOverlap(GameVehicle v,Vector3 at,GameVehicle other,float margin=.025f,float yaw=float.NaN,float fold=float.NaN)
        {
            if(other==v||!other.state.Alive)return false;
            if(float.IsNaN(yaw))yaw=v.state.yaw;if(float.IsNaN(fold))fold=v.state.fold;
            float h1=GameBounds(v.unit.robot,fold).max.y,h2=GameBounds(other.unit.robot,other.state.fold).max.y;
            if(at.y>other.state.position.y+h2+.01f||other.state.position.y>at.y+h1+.01f)return false;
            Vector3 d=other.state.position-at;d.y=0;Quaternion q=Quaternion.Euler(0,yaw,0),o=Quaternion.Euler(0,other.state.yaw,0);
            Vector3 e=v.unit.robot.footExtents,oe=other.unit.robot.footExtents;
            for(int i=0;i<4;i++)
            {
                Vector3 axis=(i<2?q:o)*(i%2==0?Vector3.right:Vector3.forward);
                float a=Mathf.Abs(Vector3.Dot(axis,q*Vector3.right))*e.x+Mathf.Abs(Vector3.Dot(axis,q*Vector3.forward))*e.z;
                float b=Mathf.Abs(Vector3.Dot(axis,o*Vector3.right))*oe.x+Mathf.Abs(Vector3.Dot(axis,o*Vector3.forward))*oe.z;
                if(Mathf.Abs(Vector3.Dot(axis,d))>=a+b+margin)return false;
            }
            return true;
        }
        bool MovementClear(GameVehicle v,Vector3 origin,Vector3 delta,float fold,bool vehicles=true,float yaw=float.NaN)
        {
            movementBlockingHit=default;driveContactNormal=Vector3.zero;driveContactDepth=0;if(delta.sqrMagnitude<1e-10f)return true;if(float.IsNaN(yaw))yaw=v.state.yaw;
            var robot=v.unit.robot;GroundSupport(v,origin,yaw,out _,out var tilt);Quaternion body=tilt*Quaternion.Euler(0,yaw,0);
            Vector3 localDirection=Quaternion.Inverse(body)*BallisticMath.Direction(v.state.gimbalYaw,v.state.pitch);float heading=Mathf.Atan2(localDirection.x,localDirection.z)*Mathf.Rad2Deg;
            for(int g=0;g<robot.groups.Length;g++)
            {
                var bounds=FoldedDriveBounds(robot,g,fold);if(!robot.model.groups[g].turret){var min=bounds.min;min.y+=.05f;bounds.SetMinMax(min,bounds.max);}
                Vector3 half=Vector3.Max(Vector3.one*.008f,bounds.extents-Vector3.one*.008f);var transform=DriveGroupPose(v,g,fold,body)*BallisticMath.Fold(robot.model.groups[g],fold).inverse;
                Vector3 center=origin+body*transform.MultiplyPoint3x4(bounds.center);Quaternion rotation=body*transform.rotation;
                int n=DevBoxCast(center,half,delta.normalized,movementHits,rotation,delta.magnitude+.004f,(1<<10)|(1<<14),QueryTriggerInteraction.Ignore);
                if(n==movementHits.Length)return false;
                for(int i=0;i<n;i++)
                {
                    var hit=movementHits[i];if(hit.normal.y>=.6f||Vector3.Dot(hit.normal,delta.normalized)>=-.01f||hit.distance>=delta.magnitude+.002f)continue;
                    // BoxCast reports -castDirection at distance zero when its
                    // starting volume touches a mesh. Confirm the actual MTV:
                    // a supporting floor contact is not a wall in that direction.
                    if(hit.distance<=.001f)
                    {
                        var box=DriveProbeBox();box.size=half*2;
                        if(!DriveShapeContact(v,g,origin,fold,body,hit.collider,out var push,out float depth)||DriveContactWithinSkin(push,depth)||(push.y>=.65f&&depth<=StepLimit(v)*.5f)||Vector3.Dot(push,delta)>=0)continue;
                    }
                    movementBlockingHit=hit;driveContactNormal=hit.normal;return false;
                }
            }
            if(vehicles)foreach(var other in gameVehicles.Values)if(VehicleOverlap(v,origin+delta,other,.025f,yaw,fold))return false;
            return true;
        }
        bool TraverseGame(GameVehicle v,Vector3 from,Vector3 wanted,out Vector3 resolved,bool vehicles=true,bool navigationPose=false)
        {
            movementBlockingHit=default;driveContactNormal=Vector3.zero;driveContactDepth=0;driveRejectReason="";
            resolved=from;Vector3 distance=wanted-from;distance.y=0;int segments=Mathf.Max(1,Mathf.CeilToInt(distance.magnitude/.04f));
            for(int i=1;i<=segments;i++)if(!TraverseSegment(v,resolved,from+distance*(i/(float)segments),out resolved,vehicles,navigationPose))return false;
            return true;
        }
        bool TraverseSegment(GameVehicle v,Vector3 from,Vector3 wanted,out Vector3 resolved,bool vehicles,bool navigationPose)
        {
            resolved=from;if(wanted.x<.3f||wanted.z<.3f||wanted.x>field.width-.3f||wanted.z>field.depth-.3f)return false;
            wanted.y=from.y;float fold=Mathf.Lerp(v.unit.robot.model.defaultFold,1,Mathf.Max(PassagePose.Amount(field.tunnels,from),PassagePose.Amount(field.tunnels,wanted)));
            if(!navigationPose)fold=v.state.fold;
            Vector3 horizontal=wanted-from;horizontal.y=0;float yaw=navigationPose&&horizontal.sqrMagnitude>1e-8f?Mathf.Atan2(horizontal.x,horizontal.z)*Mathf.Rad2Deg:v.state.yaw;
            if(!GroundSupport(v,from,yaw,out float oldGround,out _)||!GroundSupport(v,wanted,yaw,out float newGround,out _))return DriveReject("missing support");
            // Test the actual short surface interval instead of requiring two
            // adjacent normals to be identical at a legitimate ramp/flat join.
            float centerRise=GroundFrom(wanted)-GroundFrom(from);
            if(centerRise>StepLimit(v)+.003f||newGround-oldGround>StepLimit(v)+horizontal.magnitude*1.18f+.003f)return DriveReject("rise center="+centerRise+" support="+(newGround-oldGround));
            float target=newGround+BallisticMath.Support(v.unit.robot.model,fold)+.004f;
            var landing=new Vector3(wanted.x,target,wanted.z);if(!DriveFloorClearance(v,ref landing,fold,yaw))return false;target=landing.y;
            Vector3 origin=from,delta=new Vector3(wanted.x-from.x,Mathf.Max(0,target-from.y),wanted.z-from.z);
            if(!MovementClear(v,origin,delta,fold,vehicles,yaw))
            {
                // Probe just beyond the contacted riser: a low legal step can
                // touch the leading chassis before the wheel samples reach it.
                if(!movementBlockingHit.collider)return DriveReject("movement or actor overlap");
                Vector3 probe=movementBlockingHit.point+horizontal.normalized*.018f;probe.y=oldGround+StepLimit(v)+.003f;
                float ledge=GroundFrom(probe,0),step=ledge-oldGround;
                if(step<=.005f||step>StepLimit(v)+.002f)return DriveReject("blocked riser step="+step+" hit="+movementBlockingHit.collider.name);
                origin.y=ledge+BallisticMath.Support(v.unit.robot.model,fold)+.004f;
                delta.y=0;if(!MovementClear(v,origin,delta,fold,vehicles,yaw))return false;target=Mathf.Max(target,origin.y);
            }
            var candidate=new Vector3(wanted.x,target,wanted.z);if(!DrivePoseClear(v,candidate,fold,yaw,vehicles))return false;
            resolved=candidate;return true;
        }
        void SnapGameGround(GameVehicle v)
        {
            var p=v.state.position;if(p.y<=0)p.y=8;
            p.y=GameGround(v,p)+BallisticMath.Support(v.unit.robot.model,v.state.fold)+.004f;float nominal=p.y;if(DriveFloorClearance(v,ref p,v.state.fold,v.state.yaw))v.state.position=p;else v.state.position.y=nominal;
            v.state.verticalVelocity=0;v.lastPosition=v.state.position;PoseGameVehicle(v);v.physics?.EnsureSpawnClearance();
        }
        void MoveGameVehicle(GameVehicle vehicle,DeveloperInput input)
        {
            var a=vehicle.state;float dt=DeveloperWorld.Step;
            if(vehicle.recovering){a.velocity=Vector3.zero;a.verticalVelocity=a.angularVelocity=0;return;}
            if(!a.Alive||game.time<a.powerOffUntil){a.velocity=Vector3.zero;a.estimatedPower=0;game.EnergyStep(a,false,0);return;}
            Vector3 wish=new Vector3(input.x,0,input.z);if(input.cameraRelative)wish=Quaternion.Euler(0,input.yaw,0)*wish;wish=Vector3.ClampMagnitude(wish,1);
            if(a.blocked&&(wish.sqrMagnitude>.01f||input.spin)&&TryResolveDriveOverlap(vehicle,out var corrected))a.position=corrected;
            float budget=game.DrivePowerBudget(a,input.burst),goalOmega=input.spin?a.spinRate:0;
            if(game.rules.powerLimiter){float max=Mathf.Sqrt(Mathf.Max(0,budget-game.rules.idlePower)/Mathf.Max(.001f,game.rules.spinPower))*Mathf.Rad2Deg;goalOmega=Mathf.Clamp(goalOmega,-max,max);}
            a.angularVelocity=Mathf.MoveTowards(a.angularVelocity,goalOmega,game.rules.angularAcceleration*dt);float omega=a.angularVelocity*Mathf.Deg2Rad;
            float previousYaw=a.yaw;
            if(Mathf.Abs(a.angularVelocity)>.01f)a.yaw+=a.angularVelocity*dt;else if(wish.sqrMagnitude>.02f)a.yaw=Mathf.MoveTowardsAngle(a.yaw,Mathf.Atan2(wish.x,wish.z)*Mathf.Rad2Deg,180*dt);
            if(Mathf.Abs(Mathf.DeltaAngle(previousYaw,a.yaw))>.01f)
            {
                // A turn changes the wheel support plane on a seam. Checking
                // the new orientation at the old root height falsely embeds a
                // wheel and can permanently prevent the chassis from turning.
                Vector3 turned=a.position;bool turnClear=GroundSupport(vehicle,turned,a.yaw,out float support,out _);
                if(turnClear)
                {
                    turned.y=Mathf.Max(turned.y,support+BallisticMath.Support(vehicle.unit.robot.model,a.fold)+.004f);
                    turnClear=DriveFloorClearance(vehicle,ref turned,a.fold,a.yaw)&&turned.y-a.position.y<=StepLimit(vehicle)&&DrivePoseClear(vehicle,turned,a.fold,a.yaw);
                }
                if(turnClear)a.position=turned;else {a.yaw=previousYaw;a.angularVelocity=0;}
            }
            float mechanical=input.burst?game.rules.burstMechanicalSpeed:game.rules.mechanicalSpeed;
            float speed=Mathf.Min(mechanical,Mathf.Sqrt(Mathf.Max(0,budget-game.rules.idlePower-game.rules.spinPower*omega*omega)/Mathf.Max(.01f,game.rules.speedPower)));
            if(!game.rules.powerLimiter)speed=mechanical;
            if(!input.cameraRelative)speed=Mathf.Min(speed,a.requestedSpeed);else if(input.slow&&!input.burst)speed*=game.rules.slowRatio;
            float climb=0;if(wish.sqrMagnitude>.01f){Vector3 query=a.position+wish.normalized*.1f;climb=Mathf.Clamp((GameGround(vehicle,query)-GameGround(vehicle,a.position))/.1f,0,2);}
            float climbPower=game.rules.mass*game.rules.gravity*climb/Mathf.Max(.1f,game.rules.efficiency);
            if(game.rules.powerLimiter){float spare=Mathf.Max(0,budget-game.rules.idlePower-game.rules.spinPower*omega*omega),kv=Mathf.Max(.01f,game.rules.speedPower);speed=Mathf.Min(speed,(-climbPower+Mathf.Sqrt(climbPower*climbPower+4*kv*spare))/(2*kv));}
            Vector3 old=new Vector3(a.velocity.x,0,a.velocity.z),horizontal=Vector3.MoveTowards(old,wish*speed,(input.burst?game.rules.burstAcceleration:game.rules.acceleration)*dt);
            float inertial=game.rules.mass*Mathf.Max(0,(horizontal.magnitude-old.magnitude)/dt)*horizontal.magnitude/Mathf.Max(.1f,game.rules.efficiency);
            if(game.rules.powerLimiter){float spare=Mathf.Max(0,budget-game.rules.idlePower-game.rules.spinPower*omega*omega-game.rules.speedPower*horizontal.sqrMagnitude-climbPower*horizontal.magnitude);if(inertial>spare&&inertial>0)horizontal=Vector3.Lerp(old,horizontal,spare/inertial);}
            Vector3 start=a.position,delta=horizontal*dt,wanted=start+delta;float proximity=PassagePose.Amount(field.tunnels,wanted);
            if(wish.sqrMagnitude>.01f)proximity=Mathf.Max(proximity,PassagePose.Amount(field.tunnels,wanted+wish.normalized*(Mathf.Max(vehicle.unit.robot.footExtents.x,vehicle.unit.robot.footExtents.z)+.18f)));
            float nextFold=Mathf.Lerp(vehicle.unit.robot.model.defaultFold,1,proximity);a.fold=Mathf.MoveTowards(a.fold,nextFold,dt*3);a.blocked=false;
            Vector3 separation=Vector3.zero;
            if(!TraverseGame(vehicle,start,wanted,out var resolved))
            {
                a.blocked=true;
                vehicle.lastBlockReason=driveRejectReason;
                Vector3 normal=driveContactNormal;normal.y=0;Vector3 tangent=normal.sqrMagnitude>.05f?Vector3.ProjectOnPlane(delta,normal.normalized):Vector3.zero;
                Vector3 push=normal.sqrMagnitude>.05f?normal.normalized*Mathf.Min(delta.magnitude*.5f,driveContactDepth+.002f):Vector3.zero;
                if(Vector3.Dot(tangent+push,delta)<=0)push=Vector3.zero;
                Vector3 slide=tangent+push;
                if(slide.sqrMagnitude>1e-8f&&TraverseGame(vehicle,start,start+slide,out resolved))separation=push;
                else
                {
                    bool xClear=TraverseGame(vehicle,start,start+Vector3.right*delta.x,out var x),zClear=TraverseGame(vehicle,start,start+Vector3.forward*delta.z,out var z);
                    resolved=xClear&&(!zClear||(x-start).sqrMagnitude>(z-start).sqrMagnitude)?x:zClear?z:start;
                }
            }
            a.position.x=resolved.x;a.position.z=resolved.z;
            float targetY=GameGround(vehicle,a.position)+BallisticMath.Support(vehicle.unit.robot.model,a.fold)+.004f;
            if(new Vector2(resolved.x-start.x,resolved.z-start.z).sqrMagnitude>1e-10f)targetY=Mathf.Max(resolved.y,targetY);
            var standing=new Vector3(a.position.x,targetY,a.position.z);if(DriveFloorClearance(vehicle,ref standing,a.fold,a.yaw))targetY=standing.y;
            if(a.position.y<=targetY+.012f){a.position.y=targetY;a.verticalVelocity=0;}
            else {a.verticalVelocity-=game.rules.gravity*dt;a.position.y=Mathf.Max(targetY,a.position.y+a.verticalVelocity*dt);if(a.position.y==targetY)a.verticalVelocity=0;}
            // Vertical support corrections are never recycled as driving input.
            Vector3 motor=Vector3.ClampMagnitude(new Vector3(a.position.x-start.x-separation.x,0,a.position.z-start.z-separation.z)/dt,horizontal.magnitude);
            if(Vector3.Dot(motor,horizontal)<0)motor=Vector3.zero;
            a.velocity=new Vector3(motor.x,a.verticalVelocity,motor.z);vehicle.lastPosition=a.position;
            vehicle.stuckSeconds=wish.sqrMagnitude>.04f&&delta.sqrMagnitude>1e-10f&&Vector3.Dot(a.position-start,delta)<delta.sqrMagnitude*.15f?vehicle.stuckSeconds+dt:0;
            float travelled=new Vector2(a.velocity.x,a.velocity.z).magnitude;
            inertial=game.rules.mass*Mathf.Max(0,(travelled-old.magnitude)/dt)*travelled/Mathf.Max(.1f,game.rules.efficiency);
            a.estimatedPower=game.rules.idlePower+game.rules.speedPower*travelled*travelled+game.rules.spinPower*omega*omega+inertial+game.rules.mass*game.rules.gravity*Mathf.Max(0,a.position.y-start.y)/dt/Mathf.Max(.1f,game.rules.efficiency);
            if(game.rules.powerLimiter)a.estimatedPower=Mathf.Min(budget,a.estimatedPower);
            game.EnergyStep(a,input.burst,a.estimatedPower);
        }
        void ChangeGameModel(DeveloperActor a,string model)
        {
            if(model!="sweeper"&&model!="jiageng")throw new ArgumentException("车型 sweeper / jiageng");
            if(a.model==model&&gameVehicles.TryGetValue(a.id,out var same)&&same.unit.robot.model.id==model)return;
            gamePhysics?.Remove(a.id);a.physics=null;
            if(gameVehicles.TryGetValue(a.id,out var old)){old.unit.root.SetActive(false);Destroy(old.unit.root);gameVehicles.Remove(a.id);}
            a.model=model;a.armorHeights.Clear();a.fold=model=="jiageng"?.5f:0;BuildGameVehicle(a);a.velocity=Vector3.zero;SnapGameGround(gameVehicles[a.id]);navigation.Remove(a.id);gameTrackers.Clear();observations.Clear();InvalidateLidar();
            game.Log(a.id+" 车型切换为 "+model+"，完全压缩高 "+gameVehicles[a.id].compressedHeight.ToString("0.000")+"m");
        }
    }
}
