using System;
using System.Collections.Generic;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        class GameBullet
        {
            public GameObject root;public Renderer renderer;public MaterialPropertyBlock color=new MaterialPropertyBlock();
            public Vector3 position,velocity,previous;public float born;public string shooter,runeTarget;public int id,runePanel,runeGroup,bounces;public bool settled;
        }
        readonly List<GameBullet> gameBullets=new List<GameBullet>(256);readonly Stack<GameBullet> gameBulletPool=new Stack<GameBullet>();
        Material gameBulletMaterial;int gameBulletCreated;
        string TrackerKey(DeveloperPlate plate)=>plate.target.EndsWith("_base")?plate.target+":"+plate.index:plate.target;
        DeveloperPlate TrackingLayout(DeveloperPlate plate)
        {
            if(plate.target.EndsWith("_base")){plate.local=Vector3.zero;plate.localRotation=plate.rotation;plate.tilt=Quaternion.identity;}return plate;
        }
        bool GamePlateVisible(DeveloperPlate plate,bool checkFov=true)
        {
            Vector3 eye=viewCamera.transform.position,delta=plate.center-eye;if(Vector3.Dot(plate.Normal,-delta.normalized)<.12f)return false;
            if(checkFov){var projected=viewCamera.WorldToViewportPoint(plate.center);if(projected.z<=.03f||projected.x<0||projected.x>1||projected.y<0||projected.y>1)return false;}
            if(DevRaycast(eye,delta.normalized,out var hit,Mathf.Max(0,delta.magnitude-.025f),(1<<10)|(1<<14)))return false;
            if(gamePhysics!=null)return !PhysicalVehicleSweep(eye,plate.center,.001f,game.controlled,plate.target,out _);
            foreach(var v in gameVehicles.Values)
            {
                if(v.state.id==plate.target||v.state.id==game.controlled||!v.state.Alive)continue;
                if(BallisticMath.SweepBox(eye,plate.center,v.state.position+Vector3.up*.12f,v.state.position+Vector3.up*.12f,Quaternion.Euler(0,v.state.yaw,0),new Vector3(.215f,.1f,.215f),0,out _,out _))return false;
            }
            return true;
        }
        void ObserveGameTargets()
        {
            if(game.time>=nextObservation)
            {
                nextObservation=game.time+1/Mathf.Clamp(game.rules.observationHz,1,120);
                foreach(var plate in gamePlates)
                {
                    if(plate.rune||plate.target==game.controlled||!GamePlateVisible(plate)||observationRandom.NextDouble()<game.rules.observationDrop)continue;
                    var layout=TrackingLayout(plate);Quaternion body=plate.rotation*Quaternion.Inverse(plate.localRotation)*Quaternion.Inverse(plate.tilt);
                    float yaw=plate.target.EndsWith("_base")?0:Mathf.Atan2((body*Vector3.forward).x,(body*Vector3.forward).z)*Mathf.Rad2Deg;
                    Vector3 noise=new Vector3(Noise(),Noise(),Noise())*game.rules.observationNoise;
                    observations.Enqueue(new DeveloperObservation {plate=layout,position=plate.center+noise,yaw=yaw,time=game.time});
                }
            }
            while(observations.Count>0&&observations.Peek().time+game.rules.observationDelay<=game.time)
            {
                var observation=observations.Dequeue();string key=TrackerKey(observation.plate);if(!gameTrackers.TryGetValue(key,out var tracker))gameTrackers[key]=tracker=new DeveloperTracker();tracker.Correct(observation,game.rules);
            }
        }
        float Noise(){double u=Math.Max(1e-8,observationRandom.NextDouble());return (float)(Math.Sqrt(-2*Math.Log(u))*DeveloperMath.Cos(2*Math.PI*observationRandom.NextDouble()));}
        void AcquireGameTarget()
        {
            gameLock="";aimPlate=-1;
            if(TryGameAimCandidate(out var candidate)){gameLock=candidate.target;aimPlate=candidate.plate;}
            aimStatus=gameLock.Length>0?"锁定 "+gameLock:"ROI内没有可见有效目标；右键关闭后重新锁定";game.Log(aimStatus);
        }
        bool GameAimReady(DeveloperActor actor){if(!gameAimValid)return false;GameMuzzle(gameVehicles[actor.id],out _,out var forward);return Vector3.Angle(forward,gameAimDirection)<.35f;}
        void SolveGameAim()
        {
            gameAimValid=false;if(!gameAutoAim||gameLock.Length==0||!gameVehicles.TryGetValue(game.controlled,out var vehicle))return;
            if(GameFolded(vehicle)){aimStatus="锁定 "+gameLock+" · 过洞收缩，禁止发弹";return;}
            GameMuzzle(vehicle,out var currentMuzzle,out var forward);if(gameLock.StartsWith("rune_",StringComparison.Ordinal)){SolveRuneAim(vehicle,currentMuzzle,forward);return;}float best=float.MaxValue;string reason="无可击中的板 / 观测暂失";
            foreach(var plate in gamePlates)
            {
                if(plate.target!=gameLock||plate.rune)continue;var layout=TrackingLayout(plate);if(!gameTrackers.TryGetValue(TrackerKey(plate),out var tracker)||!tracker.ready||tracker.updates<3)continue;
                Vector3 muzzle=currentMuzzle,direction=forward;float flight=0,delay=DeveloperWorld.Step;DeveloperPlate predicted=plate;bool valid=true;
                for(int iter=0;iter<4;iter++)
                {
                    if(!DeveloperIntercept.Solve(muzzle,layout,tracker,game.time,delay,game.rules,out direction,out flight,out predicted)){valid=false;break;}
                    float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(direction.y)*Mathf.Rad2Deg;
                    Vector3 local=vehicle.unit.root.transform.InverseTransformDirection(direction);float localYaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,localPitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
                    if(localPitch< -30||localPitch>30){valid=false;reason="枪管俯仰超出机械范围";break;}
                    float turn=Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(vehicle.state.gimbalYaw,yaw)),Mathf.Abs(vehicle.state.pitch-pitch));delay=DeveloperWorld.Step+turn/Mathf.Max(1,game.rules.gimbalSpeed);
                    var model=vehicle.unit.robot.model;var m=BallisticMath.Group(model,model.gunGroup,0,localYaw,localPitch,true);
                    muzzle=vehicle.unit.root.transform.TransformPoint(m.MultiplyPoint3x4(PassagePose.Vector(model.muzzlePoint)));
                }
                if(!valid)continue;
                Vector3 prev=muzzle;bool blocked=false;for(int i=1;i<=16;i++){float t=flight*i/16;Vector3 next=muzzle+direction*game.rules.bulletSpeed*t+Vector3.down*.5f*game.rules.gravity*t*t;Vector3 delta=next-prev;
                    if(DevRaycast(prev,delta.normalized,out var obstruction,Mathf.Max(0,delta.magnitude-(i==16?.02f:0)),(1<<10)|(1<<14))){blocked=true;break;}prev=next;}
                if(blocked){reason="预计弹道被场地或机构遮挡";continue;}
                float score=Vector3.Angle(forward,direction)*.02f+flight+(plate.index==aimPlate?-.09f:0)+tracker.UncertaintyAt(game.time,game.rules);
                if(score>=best)continue;best=score;gameAimValid=true;gameAimDirection=direction;gameAimFlight=flight;gameAimPoint=predicted.center;gameSelectedPlate=predicted;aimPlate=plate.index;
            }
            aimStatus=gameAimValid?$"EKF {gameLock} · 板{aimPlate} · 到弹{gameAimFlight:0.000}s · 重力{(game.rules.gravityCompensation?"补偿":"关闭")}":"锁定 "+gameLock+" · "+reason;
        }
        bool FireGameBullet(DeveloperActor actor,bool automaticRune=false)
        {
            if(gameBullets.Count>=512||!gameVehicles.TryGetValue(actor.id,out var vehicle)||vehicle.recovering)return false;if(!game.Fire(actor))return false;
            GameMuzzle(gameVehicles[actor.id],out var muzzle,out var forward);GameBullet bullet;
            if(gameBulletPool.Count>0)bullet=gameBulletPool.Pop();else
            {
                if(!gameBulletMaterial){gameBulletMaterial=new Material(Resources.Load<Material>("ReplayProjectile"));owned.Add(gameBulletMaterial);}
                var root=GameObject.CreatePrimitive(PrimitiveType.Sphere);root.name="Live 17mm projectile";root.transform.SetParent(gameRoot.transform,false);var collider=root.GetComponent<Collider>();collider.enabled=false;Destroy(collider);root.transform.localScale=Vector3.one*.017f;
                var renderer=root.GetComponent<Renderer>();renderer.sharedMaterial=gameBulletMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                bullet=new GameBullet {root=root,renderer=renderer};gameBulletCreated++;
            }
            bullet.position=bullet.previous=muzzle;bullet.velocity=forward*game.rules.bulletSpeed;bullet.born=game.time;bullet.shooter=actor.id;bullet.bounces=0;bullet.settled=false;bullet.root.SetActive(true);gameBullets.Add(bullet);bullet.id=++gameShotCount;
            bullet.runeTarget="";bullet.runePanel=bullet.runeGroup=-1;RuneShotIssued(bullet,automaticRune);return true;
        }
        struct GameContact {public bool armor,rune;public DeveloperPlate plate;public Vector3 point,normal,surface;public float fraction;public Vector2 local;}
        DeveloperPlate OldPlate(DeveloperPlate plate){foreach(var old in previousPlates)if(old.target==plate.target&&old.index==plate.index)return old;return plate;}
        bool GameContactAt(Vector3 start,Vector3 end,string shooter,float partStart,float partEnd,out GameContact result)
        {
            result=new GameContact {fraction=2};bool found=false;Vector3 delta=end-start;
            if(delta.sqrMagnitude>1e-10f&&DevSphereCast(start,.0085f,delta.normalized,out var hit,delta.magnitude,(1<<8)|(1<<10)|(1<<14),QueryTriggerInteraction.Ignore))
            {result=new GameContact {point=start+delta*(hit.distance/delta.magnitude),normal=hit.normal,fraction=hit.distance/delta.magnitude};found=true;}
            foreach(var plate in gamePlates)
            {
                if(plate.target==shooter)continue;var old=OldPlate(plate);Vector3 ca=Vector3.Lerp(old.center,plate.center,partStart),cb=Vector3.Lerp(old.center,plate.center,partEnd);Quaternion rotation=Quaternion.Slerp(old.rotation,plate.rotation,(partStart+partEnd)*.5f);
                float fraction;Vector3 normal;Vector2 local;
                if(plate.rune)
                {
                    normal=rotation*Vector3.forward;float d0=Vector3.Dot(start-ca,normal),d1=Vector3.Dot(end-cb,normal);if(d0<=0||d1>=0||Mathf.Abs(d0-d1)<1e-8f)continue;
                    fraction=d0/(d0-d1);var contact=Vector3.Lerp(start,end,fraction);var q=Quaternion.Inverse(rotation)*(contact-Vector3.Lerp(ca,cb,fraction));local=new Vector2(q.x,q.y);if(local.magnitude>.15f)continue;
                }
                else
                {
                    if(!BallisticMath.SweepBox(start,end,ca,cb,rotation,plate.half,.0085f,out fraction,out normal))continue;
                    var q=Quaternion.Inverse(rotation)*(Vector3.Lerp(start,end,fraction)-Vector3.Lerp(ca,cb,fraction));local=new Vector2(Mathf.Clamp(q.x,-plate.half.x,plate.half.x),Mathf.Clamp(q.y,-plate.half.y,plate.half.y));
                }
                if(fraction>=result.fraction)continue;
                result=new GameContact {point=Vector3.Lerp(start,end,fraction),fraction=fraction,normal=normal,plate=plate,armor=!plate.rune&&Vector3.Dot(normal,rotation*Vector3.forward)>.75f,rune=plate.rune,local=local,surface=(plate.center-old.center)/DeveloperWorld.Step};found=true;
            }
            if(gamePhysics!=null)
            {
                if(PhysicalVehicleSweep(start,end,.0085f,shooter,null,out var bodyHit)&&bodyHit.distance/delta.magnitude<result.fraction)
                {result=new GameContact {point=start+delta*(bodyHit.distance/delta.magnitude),fraction=bodyHit.distance/delta.magnitude,normal=bodyHit.normal};found=true;}
                return found;
            }
            foreach(var vehicle in gameVehicles.Values)
            {
                if(vehicle.state.id==shooter||!vehicle.state.Alive)continue;Vector3 center=vehicle.state.position+Vector3.up*.12f;
                if(BallisticMath.SweepBox(start,end,center,center,vehicle.unit.root.transform.rotation,new Vector3(.21f,.09f,.21f),.0085f,out float f,out var normal)&&f<result.fraction)
                {result=new GameContact {point=Vector3.Lerp(start,end,f),fraction=f,normal=normal};found=true;}
            }
            return found;
        }
        void StepGameBullets()
        {
            for(int i=gameBullets.Count-1;i>=0;i--)
            {
                var bullet=gameBullets[i];float age=game.time-bullet.born;
                if(age>=8||Mathf.Abs(bullet.position.x)>150||Mathf.Abs(bullet.position.z)>150||bullet.position.y< -15){bullet.root.SetActive(false);gameBulletPool.Push(bullet);gameBullets.RemoveAt(i);continue;}
                // Spawn is at this tick's posed muzzle. Integrating a new shot
                // against the previous-to-current target interval would advance
                // the bullet one physics tick ahead of the moving target.
                bullet.previous=bullet.position;if(bullet.settled||age<DeveloperWorld.Step*.5f)continue;const int steps=3;float dt=DeveloperWorld.Step/steps;
                for(int s=0;s<steps;s++)
                {
                    Vector3 gravity=Vector3.down*game.rules.gravity,next=bullet.position+bullet.velocity*dt+.5f*gravity*dt*dt;
                    if(GameContactAt(bullet.position,next,bullet.shooter,(float)s/steps,(float)(s+1)/steps,out var contact))
                    {
                        Vector3 impact=bullet.velocity+gravity*dt*contact.fraction;float normalSpeed=-Vector3.Dot(impact-contact.surface,contact.plate.Normal);
                        if(contact.armor){int damage=game.Hit(new DeveloperHit {shooter=bullet.shooter,target=contact.plate.target,panel=contact.plate.index,local=contact.local,normalSpeed=normalSpeed});if(damage>0)gameHits++;}
                        if(contact.rune && game.actors.TryGetValue(bullet.shooter,out var actor)&&actor.team==contact.plate.team)
                            if(game.runes[actor.team].Hit(game,contact.plate.index,contact.local.magnitude,normalSpeed)){gameRuneHits++;RuneHitFeedback(bullet,contact.plate);}
                        bullet.position=contact.point+contact.normal*.002f;bullet.velocity=BallisticMath.Bounce(impact,contact.normal,contact.surface,contact.armor?game.rules.armorRestitution:game.rules.groundRestitution,.68f);bullet.bounces++;
                        if(bullet.bounces>12||bullet.velocity.magnitude<.7f){bullet.settled=true;break;}
                    }
                    else{bullet.position=next;bullet.velocity+=gravity*dt;}
                }
            }
        }
        void RenderGameBullets()
        {
            foreach(var bullet in gameBullets){bullet.root.transform.position=bullet.position;bullet.color.SetColor("_Color",new Color(.6f,1,.045f,BallisticMath.Opacity(game.time-bullet.born)));bullet.renderer.SetPropertyBlock(bullet.color);}
        }
        void UpdateGamePreview()
        {
            if(!gamePreview){if(gamePreviewLine)gamePreviewLine.enabled=false;if(gameAimMarker)gameAimMarker.SetActive(false);return;}
            if(game.time<nextPreview&&game.running)return;nextPreview=game.time+.05f;if(!gameVehicles.TryGetValue(game.controlled,out var v))return;
            if(!gamePreviewLine){var root=new GameObject("Pure query trajectory preview");root.transform.SetParent(gameRoot.transform,false);gamePreviewLine=root.AddComponent<LineRenderer>();gamePreviewLine.sharedMaterial=SharedPrimitiveMaterial(new Color(.45f,1,.2f),true);gamePreviewLine.startWidth=.01f;gamePreviewLine.endWidth=.008f;}
            gamePreviewLine.enabled=true;GameMuzzle(v,out var muzzle,out var forward);var points=new List<Vector3>(80){muzzle};Vector3 previous=muzzle;
            for(int i=1;i<=80;i++){float t=i*.025f;Vector3 next=muzzle+forward*game.rules.bulletSpeed*t+Vector3.down*.5f*game.rules.gravity*t*t;
                if(GameContactAt(previous,next,game.controlled,1,1,out var hit)){points.Add(hit.point);break;}
                points.Add(next);previous=next;}
            gamePreviewLine.positionCount=points.Count;gamePreviewLine.SetPositions(points.ToArray());
            if(!gameAimMarker){gameAimMarker=Shape(PrimitiveType.Sphere,gameRoot.transform,Vector3.zero,Vector3.one*.03f,SharedPrimitiveMaterial(Color.yellow,true));var collider=gameAimMarker.GetComponent<Collider>();collider.enabled=false;Destroy(collider);}
            gameAimMarker.SetActive(gameAutoAim&&gameAimValid);gameAimMarker.transform.position=gameAimPoint;
        }
    }
}
