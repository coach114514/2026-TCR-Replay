using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        BallisticConfig ballisticConfig=new BallisticConfig();
        bool showProjectiles=true, allLabels=true;
        class Shot
        {
            public ReplayEvent source;public Unit shooter;public float time,speed,radius;
            public AimResult aim;public bool solved;
        }
        class AimResult
        {
            public string target="",reason="空间数据缺失";public float pitch,confidence,flight;
            public int panel=-1;public Vector3 muzzle,direction;public bool spatial,evidence;
            public string hitEvent="",alternative="",region="";public float hpDrop,hpTime=-1,pitchLow,pitchHigh;
            public int candidateCount;
        }
        struct ArmorBox {public Vector3 center,half;public Quaternion rotation;public Unit unit;public int panel;public bool armor;}
        class PathPoint {public float age;public Vector3 position;public PathPoint(float t,Vector3 p){age=t;position=p;}}
        class FlightPath
        {
            public readonly List<PathPoint> points=new List<PathPoint>();
            public int collisions,armorContacts,validArmorContacts;public string hitEntity="";
            public bool collisionLimit,settled;
            public Vector3 At(float age)
            {
                int lo=0,hi=points.Count;
                while(lo<hi){int m=(lo+hi)/2;if(points[m].age<=age)lo=m+1;else hi=m;}
                if(lo==0)return points[0].position;if(lo>=points.Count)return points[points.Count-1].position;
                var a=points[lo-1];var b=points[lo];return Vector3.Lerp(a.position,b.position,Mathf.InverseLerp(a.age,b.age,age));
            }
        }
        class ProjectileVisual {public GameObject root;public Renderer ball;public LineRenderer line;public MaterialPropertyBlock color=new MaterialPropertyBlock();}
        readonly List<Shot> shots=new List<Shot>();
        readonly Dictionary<Unit,List<Shot>> shooterShots=new Dictionary<Unit,List<Shot>>();
        readonly Dictionary<string,List<ReplayEvent>> hitEvents=new Dictionary<string,List<ReplayEvent>>();
        readonly Dictionary<Shot,FlightPath> pathCache=new Dictionary<Shot,FlightPath>();
        readonly Queue<Shot> cacheOrder=new Queue<Shot>();
        readonly Dictionary<Shot,ProjectileVisual> activeProjectiles=new Dictionary<Shot,ProjectileVisual>();
        readonly Stack<ProjectileVisual> projectilePool=new Stack<ProjectileVisual>();
        readonly List<Shot> retireProjectiles=new List<Shot>();
        readonly List<Shot> displayedShots=new List<Shot>();
        readonly HashSet<Shot> displayedSet=new HashSet<Shot>();
        Material projectileMaterial;int projectileVisualCount;
        const int ProjectileBudget=512,PathCacheBudget=1024;
        void LoadBallisticConfig()
        {
            string p=Path.Combine(folder,"ballistics_config.json");
            if(File.Exists(p))ballisticConfig=JsonUtility.FromJson<BallisticConfig>(File.ReadAllText(p));
            if(ballisticConfig.gravity<=0 || ballisticConfig.speed17<=0 || ballisticConfig.speed42<=0)throw new InvalidDataException("弹道参数无效");
        }
        void PrepareShots()
        {
            ClearProjectileVisuals();shots.Clear();shooterShots.Clear();hitEvents.Clear();pathCache.Clear();cacheOrder.Clear();collisionFrames.Clear();frameOrder.Clear();
            var lookup=new Dictionary<string,Unit>();foreach(var unit in units)lookup[unit.data.id]=unit;
            var buckets=new Dictionary<string,List<ReplayEvent>>();
            foreach(var e in match.events)
            {
                if(e.kind=="受击" && (e.category=="17mm" || e.category=="42mm"))
                {
                    if(!hitEvents.TryGetValue(e.entity,out var list))hitEvents[e.entity]=list=new List<ReplayEvent>();list.Add(e);
                }
                if(e.kind!="发弹" || !lookup.ContainsKey(e.entity) || (e.category!="17mm" && e.category!="42mm"))continue;
                string key=e.entity+"/"+e.category+"/"+e.t.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(!buckets.TryGetValue(key,out var bucket))buckets[key]=bucket=new List<ReplayEvent>();bucket.Add(e);
            }
            foreach(var bucket in buckets.Values)
            for(int i=0;i<bucket.Count;i++)
            {
                var e=bucket[i];var shot=new Shot {source=e,shooter=lookup[e.entity],time=(Mathf.Floor((e.t+(i+.5f)/bucket.Count)*120)+.5f)/120,speed=e.category=="42mm"?ballisticConfig.speed42:ballisticConfig.speed17,radius=e.category=="42mm"?.02125f:.0084f};
                shots.Add(shot);if(!shooterShots.TryGetValue(shot.shooter,out var list))shooterShots[shot.shooter]=list=new List<Shot>();list.Add(shot);
            }
            shots.Sort((a,b)=> {int c=a.time.CompareTo(b.time);return c!=0?c:a.source.row.CompareTo(b.source.row);});
            foreach(var list in shooterShots.Values)list.Sort((a,b)=>a.time.CompareTo(b.time));
        }
        float FoldAt(Unit u,Vector3 position) => u.robot==null?0:u.robot.preview>=0?u.robot.preview:Mathf.Lerp(u.robot.model.defaultFold,1,PassagePose.Amount(field.tunnels,position));
        bool PositionAt(Unit u,float time,out Vector3 position)
        {
            if(u.data.role=="base" || u.data.role=="outpost")
            {
                var a=Array.Find(field.buildings,x=>x.team==u.data.team && x.role==u.data.role);
                position=u.cadNode?u.cadNode.position:a==null?Vector3.zero:new Vector3(a.x,GroundHeight(a.x,a.y),a.y);return a!=null;
            }
            if(!Timeline.Pose(u.data,time,out position))return false;
            float h=GroundHeight(position.x,position.z);
            if(u.robot!=null)
            {
                var m=u.robot.model;float fold=FoldAt(u,position);var extent=u.robot.footExtents;
                foreach(float x in new [] {-extent.x*.8f,extent.x*.8f})foreach(float z in new [] {-extent.z*.8f,extent.z*.8f})h=Mathf.Max(h,GroundHeight(position.x+x,position.z+z));
                h+=BallisticMath.Support(m,fold)+.003f;
            }
            position.y=h+(u.data.role=="air"?1.8f:0);return true;
        }
        bool MuzzleAt(Unit u,float time,float elevation,out Vector3 point,out Vector3 forward)
        {
            point=forward=Vector3.zero;
            if(!PositionAt(u,time,out var position) || !BallisticMath.Heading(u.data,time,ballisticConfig,out float heading))return false;
            if(u.robot!=null)
            {
                var m=u.robot.model;float fold=FoldAt(u,position);
                var transform=BallisticMath.Group(m,m.gunGroup,fold,heading,elevation,true);
                point=position+transform.MultiplyPoint3x4(PassagePose.Vector(m.muzzlePoint));forward=transform.MultiplyVector(PassagePose.Vector(m.gunForward)).normalized;
            }
            else {forward=BallisticMath.Direction(heading,elevation);point=position+Vector3.up*(u.data.role=="air"?.07f:.53f)+forward*.6f;}
            return true;
        }
        ArmorBox PanelAt(Unit u,float time,int index)
        {
            if(MovingOutpostPanel(u,time,index,out var movingPanel))return movingPanel;
            PositionAt(u,time,out var position);
            if(u.robot!=null)
            {
                var a=u.robot.model.armorPanels[index];return new ArmorBox {unit=u,panel=index,armor=true,center=position+PassagePose.Vector(a.position)-PassagePose.Rotation(a.rotation)*Vector3.forward*.003f,rotation=PassagePose.Rotation(a.rotation),half=new Vector3(.0675f,.0625f,.006f)};
            }
            bool building=u.data.role=="base" || u.data.role=="outpost";
            // Coarse fallback only when a field has no split outpost CAD.
            float bearing=index*360f/PanelCount(u);float radius=building?(u.data.role=="base"?.29f:.145f):.31f;
            float height=building?(u.data.role=="base"?.62f:.73f):.28f;
            var q=Quaternion.Euler(-15,bearing,0);
            if(!building)q=Quaternion.Euler(0,bearing,0);
            return new ArmorBox {unit=u,panel=index,armor=true,center=position+Vector3.up*height+Quaternion.Euler(0,bearing,0)*Vector3.forward*radius,rotation=q,half=building?new Vector3(.08f,.08f,.01f):new Vector3(.0675f,.0625f,.006f)};
        }
        int PanelCount(Unit unit) => unit.data.role=="base"?6:unit.data.role=="outpost"?3:4;
        bool KnownUnavailable(Unit unit,float time)
        {
            int i=Timeline.StateIndex(unit.data.samples,time);
            if(i>=0 && unit.data.samples[i].hasHp && unit.data.samples[i].hp<=0)return true;
            if(unit.data.role=="base")
            {
                var outpost=units.Find(u=>u.data.team==unit.data.team && u.data.role=="outpost");
                if(outpost!=null)
                {
                    int state=Timeline.StateIndex(outpost.data.samples,time);
                    if(state>=0 && outpost.data.samples[state].hasHp && outpost.data.samples[state].hp>0)return true;
                }
            }
            return false;
        }
        bool Screened42(string attackingTeam,float time)
        {
            float last=-100;
            foreach(var entry in shooterShots)
            {
                if(entry.Key.data.team!=attackingTeam || entry.Key.data.role!="hero")continue;
                var list=entry.Value;int lo=0,hi=list.Count;
                while(lo<hi){int mid=(lo+hi)/2;if(list[mid].time<=time)lo=mid+1;else hi=mid;}
                for(int i=lo-1;i>=0 && time-list[i].time<=4;i--)
                    if(list[i].source.category=="42mm"){last=Mathf.Max(last,list[i].time);break;}
            }
            return time-last>4;
        }
        bool DamageEvidence(Unit u,Shot shot,float expectedFlight,out float error,out string eventId)
        {
            error=0;eventId="";if(!hitEvents.TryGetValue(u.data.id,out var list))return false;
            int lo=0,hi=list.Count;float start=shot.source.t;
            while(lo<hi){int mid=(lo+hi)/2;if(list[mid].t<start)lo=mid+1;else hi=mid;}
            bool found=false;
            for(int i=lo;i<list.Count && list[i].t<=shot.time+expectedFlight+1.1f;i++)
                if(list[i].category==shot.source.category)
                {
                    float distance=Mathf.Abs(list[i].t-(shot.time+expectedFlight));
                    if(!found || distance<error){error=distance;eventId=list[i].id;}found=true;
                }
            return found;
        }
        float HealthDropAfterShot(Unit unit,Shot shot,float flight,out float healthTime)
        {
            var samples=unit.data.samples;float drop=0,error=float.PositiveInfinity;healthTime=-1;
            float impactTime=shot.time+flight;
            int start=Mathf.Max(1,Timeline.StateIndex(samples,shot.source.t));
            for(int i=start;i<samples.Length && samples[i].t<=impactTime+1.1f;i++)
                if(samples[i].hasHp && samples[i-1].hasHp && samples[i].t-samples[i-1].t<=2)
                {
                    if(samples[i].t<shot.source.t || samples[i].hp>=samples[i-1].hp)continue;
                    // A 1s state row represents an uncertain impact interval.
                    // Retain the source firing bucket, but exclude older drops
                    // and any interval ending >1.1s before the expected impact.
                    float distance=Mathf.Abs(samples[i].t-impactTime);
                    if(samples[i].t<impactTime-1.1f || distance>=error)continue;
                    drop=samples[i-1].hp-samples[i].hp;healthTime=samples[i].t;error=distance;
                }
            // HP alone is never evidence of a projectile or a specific attacker.
            return drop;
        }
        bool OutpostAimRegion(Unit target,float time,Vector3 muzzle,Vector3 forward,out ArmorBox box,out float minHeight,out float maxHeight)
        {
            box=default;minHeight=float.PositiveInfinity;maxHeight=float.NegativeInfinity;
            Vector3 center=Vector3.zero;var panels=new ArmorBox[3];
            for(int i=0;i<3;i++){panels[i]=PanelAt(target,time,i);center+=panels[i].center;}
            // The pivot, rather than the mean of three unevenly phased centres,
            // defines the cylindrical orbit in the world horizontal plane.
            center=target.cadNode && mechanismConfig!=null?target.cadNode.TransformPoint(PassagePose.Vector(mechanismConfig.outpost.pivot)):center/3;
            float radius=0,height=0;
            foreach(var panel in panels)
            {
                radius=Mathf.Max(radius,Vector3.ProjectOnPlane(panel.center-center,Vector3.up).magnitude);
                minHeight=Mathf.Min(minHeight,panel.center.y-panel.half.y-.04f);
                maxHeight=Mathf.Max(maxHeight,panel.center.y+panel.half.y+.04f);height+=panel.center.y/3;
            }
            center.y=height;
            if(!BallisticMath.HorizontalRing(muzzle,forward,center,radius,.10f,out var point))return false;
            var normal=Vector3.ProjectOnPlane(point-center,Vector3.up).normalized;
            box=new ArmorBox {unit=target,panel=-1,armor=true,center=point,rotation=Quaternion.LookRotation(normal,Vector3.up),half=new Vector3(.10f,.0445f,.008f)};
            return true;
        }
        void PitchLimits(Shot shot,out float min,out float max)
        {
            min=-30;max=30;
            if(shot.shooter.robot==null)return;
            var model=shot.shooter.robot.model;min=model.pitchMin;max=model.pitchMax;
            PositionAt(shot.shooter,shot.time,out var center);if(FoldAt(shot.shooter,center)>.9f){min=-3;max=3;}
        }
        float HeightAtRange(Vector3 muzzle,Vector3 point,Shot shot,float pitch)
        {
            float d=Vector3.ProjectOnPlane(point-muzzle,Vector3.up).magnitude,r=pitch*Mathf.Deg2Rad;
            float t=d/(shot.speed*Mathf.Cos(r));return muzzle.y+d*Mathf.Tan(r)-.5f*ballisticConfig.gravity*t*t;
        }
        AimResult SolveAim(Shot shot)
        {
            if(shot.solved)return shot.aim;shot.solved=true;
            var result=new AimResult();shot.aim=result;
            if(!MuzzleAt(shot.shooter,shot.time,0,out var muzzle,out var forward))return result;
            result.spatial=true;result.muzzle=muzzle;result.direction=forward;result.reason="无可核对目标，水平射击示意";
            float yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,best=-1,second=-1;
            PitchLimits(shot,out float min,out float max);
            string alternative="";var candidateEntities=new HashSet<string>();
            foreach(var target in units)
            {
                if(target==shot.shooter || target.data.team==shot.shooter.data.team || target.data.role=="air" || !PositionAt(target,shot.time,out var initial))continue;
                bool ring=target.data.role=="outpost";
                for(int panel=0;panel<(ring?1:PanelCount(target));panel++)
                {
                    float minHeight=0,maxHeight=0;ArmorBox box;
                    if(ring)
                    {
                        if(!OutpostAimRegion(target,shot.time,muzzle,forward,out box,out minHeight,out maxHeight))continue;
                    }
                    else box=PanelAt(target,shot.time,panel);
                    Vector3 delta=box.center-muzzle;
                    float angle=Mathf.Abs(Mathf.DeltaAngle(yaw,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg));
                    if(angle>8 || Vector3.Dot(box.rotation*Vector3.forward,(muzzle-box.center).normalized)<.2f)continue;
                    float pitch=0,flight=delta.magnitude/shot.speed;Vector3 candidateMuzzle=muzzle;bool valid=true;
                    for(int j=0;j<3;j++)
                    {
                        if(!PositionAt(target,shot.time+flight,out _)) {valid=false;break;}
                        if(ring)
                        {
                            if(!OutpostAimRegion(target,shot.time+flight,candidateMuzzle,forward,out box,out minHeight,out maxHeight)){valid=false;break;}
                            float low=Mathf.Max(minHeight,HeightAtRange(candidateMuzzle,box.center,shot,min));
                            float high=Mathf.Min(maxHeight,HeightAtRange(candidateMuzzle,box.center,shot,max));
                            if(low>high){valid=false;break;}box.center.y=Mathf.Clamp(box.center.y,low,high);
                        }
                        else box=PanelAt(target,shot.time+flight,panel);
                        if(!BallisticMath.LowArc(box.center-candidateMuzzle,shot.speed,ballisticConfig.gravity,out pitch,out flight)) {valid=false;break;}
                        MuzzleAt(shot.shooter,shot.time,pitch,out candidateMuzzle,out _);
                    }
                    if(!valid || flight>3 || pitch<min || pitch>max)continue;
                    // An HP=0 row at impact can be the killing shot's outcome.
                    // Reject already-dead targets before its source bucket,
                    // rather than rejecting that outcome as an invalid target.
                    if(KnownUnavailable(target,target.data.role=="base"?shot.time+flight:shot.source.t-.001f))continue;
                    float miss=Mathf.Abs(Mathf.DeltaAngle(yaw,Mathf.Atan2((box.center-muzzle).x,(box.center-muzzle).z)*Mathf.Rad2Deg));
                    // Only pitch is reconstructed. Require the recorded horizontal
                    // ray to pass through the panel or armor orbit. One degree
                    // and the outpost's 10cm positional margin express uncertainty.
                    float tolerance=Mathf.Atan2(box.half.x+shot.radius,(box.center-muzzle).magnitude)*Mathf.Rad2Deg+1;
                    if(miss>tolerance)continue;
                    bool evidence=DamageEvidence(target,shot,flight,out float timeError,out string hitEvent);
                    float hpDrop=HealthDropAfterShot(target,shot,flight,out float hpTime);
                    float score=(evidence?3:0)+(hpDrop>0?(evidence?.85f:1):0)+1-miss/Mathf.Max(1,tolerance)-timeError*.1f;
                    var direction=BallisticMath.Direction(yaw,pitch);
                    bool occluded=false;Vector3 prev=candidateMuzzle;
                    for(int j=1;j<=10;j++)
                    {
                        float t=flight*j/10;Vector3 next=candidateMuzzle+direction*shot.speed*t-Vector3.up*.5f*ballisticConfig.gravity*t*t;
                        if(Physics.Linecast(prev,next,1<<10,QueryTriggerInteraction.Ignore)){occluded=true;break;}prev=next;
                    }
                    if(occluded)continue;candidateEntities.Add(target.data.id);
                    if(score<=best)
                    {
                        if(target.data.id!=result.target && score>second){second=score;alternative=target.data.id;}
                        continue;
                    }
                    if(result.target!="" && target.data.id!=result.target){second=best;alternative=result.target;}best=score;
                    result.target=target.data.id;result.panel=ring?-1:panel;result.region=ring?"outpost_armor_ring":"armor_panel";
                    result.pitch=pitch;result.flight=flight;result.evidence=evidence;
                    result.hitEvent=hitEvent;result.hpDrop=hpDrop;result.hpTime=hpTime;result.pitchLow=result.pitchHigh=pitch;
                    if(ring)
                    {
                        Vector3 lower=box.center-candidateMuzzle,upper=lower;lower.y=minHeight-candidateMuzzle.y;upper.y=maxHeight-candidateMuzzle.y;
                        if(BallisticMath.LowArc(lower,shot.speed,ballisticConfig.gravity,out float pLow,out _))result.pitchLow=Mathf.Max(min,pLow);
                        if(BallisticMath.LowArc(upper,shot.speed,ballisticConfig.gravity,out float pHigh,out _))result.pitchHigh=Mathf.Min(max,pHigh);
                    }
                    result.confidence=(evidence?(hpDrop>0?.75f:.65f):hpDrop>0?.4f:.25f)*(ballisticConfig.headingCalibrated?1:.5f)*(fieldMismatch?.5f:1);
                    result.reason=(ring?"前哨三装甲的环形高度区域；":"")+(evidence?(hpDrop>0?"发弹后同口径受击、掉血、云台方向与弹道推定":"发弹后同口径受击、云台方向与弹道推定"):hpDrop>0?"发弹后掉血与方向候选，伤害来源未确认":"方向候选，缺少发弹后受击证据");
                    MuzzleAt(shot.shooter,shot.time,pitch,out result.muzzle,out result.direction);
                }
            }
            result.candidateCount=candidateEntities.Count;result.alternative=alternative;
            if(second>=0 && best-second<.25f)
            {
                result.target="";result.region="";result.panel=-1;result.pitch=result.pitchLow=result.pitchHigh=0;result.confidence=0;result.evidence=false;
                result.hpDrop=0;result.hpTime=-1;result.hitEvent="";
                result.reason="候选分数接近，未锁定目标；水平射击示意";
                MuzzleAt(shot.shooter,shot.time,0,out result.muzzle,out result.direction);
            }
            return result;
        }
        float CurrentElevation(Unit u,float time)
        {
            if(robotDemo || !shooterShots.TryGetValue(u,out var list) || list.Count==0)return 0;
            int lo=0,hi=list.Count;while(lo<hi){int mid=(lo+hi)/2;if(list[mid].time<=time)lo=mid+1;else hi=mid;}
            int a=Mathf.Max(0,lo-1),b=Mathf.Min(list.Count-1,lo);
            if(Mathf.Min(Mathf.Abs(list[a].time-time),Mathf.Abs(list[b].time-time))>1.5f)return 0;
            float p=SolveAim(list[a]).pitch;
            if(a!=b && list[b].time-list[a].time<=2)p=Mathf.Lerp(p,SolveAim(list[b]).pitch,Mathf.InverseLerp(list[a].time,list[b].time,time));
            if(u.robot!=null) {p=Mathf.Clamp(p,u.robot.model.pitchMin,u.robot.model.pitchMax);if(u.robot.fold>.9f)p=Mathf.Clamp(p,-3,3);}
            return p;
        }
        void ClearProjectileVisuals()
        {
            foreach(var visual in activeProjectiles.Values){visual.root.SetActive(false);projectilePool.Push(visual);}activeProjectiles.Clear();
        }
        void InvalidateBallistics()
        {
            ClearProjectileVisuals();pathCache.Clear();cacheOrder.Clear();collisionFrames.Clear();frameOrder.Clear();foreach(var shot in shots){shot.solved=false;shot.aim=null;}
        }
        void BuildProjectileFieldCollision(Transform parent)
        {
            if(!terrain)return;var root=new GameObject("Independent projectile field collision");root.transform.SetParent(parent,false);
            foreach(var filter in terrain.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.GetComponent<ReplayMovingCad>())continue;
                var obj=new GameObject(filter.name+" projectile collision");obj.layer=10;obj.transform.SetParent(root.transform,false);
                obj.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);obj.transform.localScale=filter.transform.lossyScale;
                obj.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            }
        }
        void BuildArmorColliders(Unit unit)
        {
            if(unit.data.role=="air")return;
            unit.armorColliders=new BoxCollider[PanelCount(unit)];
            for(int i=0;i<PanelCount(unit);i++)
            {
                var box=PanelAt(unit,0,i);var obj=new GameObject("Projectile armor "+i);obj.layer=11;obj.transform.SetParent(unit.root.transform,false);
                PositionAt(unit,0,out var origin);obj.transform.localPosition=box.center-origin;obj.transform.localRotation=box.rotation;
                var collider=obj.AddComponent<BoxCollider>();collider.size=box.half*2;unit.armorColliders[i]=collider;
            }
        }
        class CollisionFrame {public ArmorBox[] boxes;public bool[] valid;}
        readonly Dictionary<int,CollisionFrame> collisionFrames=new Dictionary<int,CollisionFrame>();
        readonly Queue<int> frameOrder=new Queue<int>();
        CollisionFrame FrameAt(int frame)
        {
            if(collisionFrames.TryGetValue(frame,out var cached))return cached;
            int runeCount=runeRotor?runeArms.Count:0;
            float t=frame/120f;var value=new CollisionFrame {boxes=new ArmorBox[units.Count*7+runeCount],valid=new bool[units.Count*7+runeCount]};
            for(int j=0;j<units.Count;j++)
            {
                var target=units[j];if(target.data.role=="air" || !PositionAt(target,t,out var position))continue;
                int count=PanelCount(target);
                for(int i=0;i<=count;i++)
                {
                    ArmorBox box;
                    if(i<count)box=PanelAt(target,t,i);
                    else
                    {
                        bool building=target.data.role=="base" || target.data.role=="outpost";float width=building?.16f:.2f;
                        box=new ArmorBox {unit=target,panel=-1,armor=false,center=position+Vector3.up*.16f,rotation=Quaternion.identity,half=new Vector3(width,.1f,width)};
                    }
                    value.boxes[j*7+i]=box;value.valid[j*7+i]=true;
                }
            }
            for(int i=0;i<runeCount;i++){int slot=units.Count*7+i;value.boxes[slot]=RunePanelAt(i,t);value.valid[slot]=true;}
            collisionFrames[frame]=value;frameOrder.Enqueue(frame);
            while(frameOrder.Count>4096){int old=frameOrder.Dequeue();collisionFrames.Remove(old);}return value;
        }
        FlightPath ComputeFlight(Shot shot)
        {
            var aim=SolveAim(shot);var path=new FlightPath();path.points.Add(new PathPoint(0,aim.muzzle));if(!aim.spatial)return path;
            Vector3 p=aim.muzzle,v=aim.direction*shot.speed;float age=0;
            // Cache historical target positions once per time step, including both
            // endpoints, rather than moving live scene objects while seeking.
            for(int step=0;step<960;step++)
            {
                float dt=BallisticMath.Step;Vector3 next=p+v*dt-Vector3.up*(.5f*ballisticConfig.gravity*dt*dt);
                Vector3 nextV=v-Vector3.up*ballisticConfig.gravity*dt;
                float fraction=2;Vector3 normal=Vector3.zero,surfaceVelocity=Vector3.zero;ArmorBox contact=default;
                Vector3 motion=next-p;float length=motion.magnitude;
                if(length>1e-7f && Physics.SphereCast(p,shot.radius,motion/length,out var hit,length,(1<<8)|(1<<10),QueryTriggerInteraction.Ignore))
                {fraction=hit.distance/length;normal=hit.normal;}
                if(age>.025f)
                {
                    int frame=Mathf.FloorToInt((shot.time+age)*120);
                    var aFrame=FrameAt(frame);var bFrame=FrameAt(frame+1);var cFrame=FrameAt(frame+2);
                    float blend=(shot.time+age)*120-frame;
                    for(int i=0;i<aFrame.boxes.Length;i++)
                    {
                        if(!aFrame.valid[i] || !bFrame.valid[i] || !cFrame.valid[i])continue;
                        var a=aFrame.boxes[i];var b=bFrame.boxes[i];
                        a.center=Vector3.Lerp(a.center,b.center,blend);b.center=Vector3.Lerp(b.center,cFrame.boxes[i].center,blend);
                        if(a.unit==shot.shooter || (a.center-p).sqrMagnitude>4)continue;
                        if(BallisticMath.SweepBox(p,next,a.center,b.center,a.rotation,a.half,shot.radius,out float f,out var n) && f<fraction)
                        {
                            if(a.panel==-2)
                            {
                                var local=Quaternion.Inverse(a.rotation)*(Vector3.Lerp(p,next,f)-Vector3.Lerp(a.center,b.center,f));
                                if(local.x*local.x+local.y*local.y>(.15f+shot.radius)*(.15f+shot.radius))continue;
                            }
                            fraction=f;normal=n;surfaceVelocity=(b.center-a.center)/dt;contact=a;
                        }
                    }
                }
                if(fraction<=1)
                {
                    float impactAge=age+dt*fraction;Vector3 impact=p+motion*fraction;
                    v=Vector3.Lerp(v,nextV,fraction);path.collisions++;
                    if(contact.armor)
                    {
                        path.armorContacts++;float speed=-Vector3.Dot(v-surfaceVelocity,contact.rotation*Vector3.forward);
                        bool screened42=shot.source.category=="42mm" && Screened42(shot.shooter.data.team,shot.time+impactAge);
                        if(Vector3.Dot(normal,contact.rotation*Vector3.forward)>.5f && speed>(shot.source.category=="42mm"?10:12) && contact.unit.data.team!=shot.shooter.data.team && !KnownUnavailable(contact.unit,shot.time+impactAge) && !screened42)
                        {path.validArmorContacts++;path.hitEntity=contact.unit.data.id;}
                    }
                    path.points.Add(new PathPoint(impactAge,impact));
                    v=BallisticMath.Bounce(v,normal,surfaceVelocity,contact.armor?ballisticConfig.restitutionArmor:ballisticConfig.restitutionGround,ballisticConfig.tangentRetention);
                    float remaining=dt*(1-fraction);p=impact+normal*.001f+v*remaining-Vector3.up*.5f*ballisticConfig.gravity*remaining*remaining;
                    v-=Vector3.up*ballisticConfig.gravity*remaining;
                    if(normal.y>.65f && v.magnitude<.5f) {path.settled=true;path.points.Add(new PathPoint(age+dt,p));break;}
                    if(path.collisions>=24) {path.collisionLimit=true;break;}
                }
                else {p=next;v=nextV;}
                age+=dt;
                if(step%2==0 || fraction<=1)path.points.Add(new PathPoint(age,p));
                if(p.y<-.2f || p.x<-2 || p.z<-2 || p.x>field.width+2 || p.z>field.depth+2)break;
            }
            return path;
        }
        FlightPath CachedFlight(Shot shot)
        {
            if(pathCache.TryGetValue(shot,out var cached))return cached;
            var result=ComputeFlight(shot);pathCache[shot]=result;cacheOrder.Enqueue(shot);
            while(cacheOrder.Count>PathCacheBudget){var old=cacheOrder.Dequeue();pathCache.Remove(old);}return result;
        }
        ProjectileVisual AcquireProjectile()
        {
            if(projectilePool.Count>0)return projectilePool.Pop();
            if(projectileMaterial==null){projectileMaterial=new Material(Resources.Load<Material>("ReplayProjectile"));owned.Add(projectileMaterial);}
            var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name="Recorded projectile (inferred trajectory)";Destroy(obj.GetComponent<Collider>());
            var ball=obj.GetComponent<Renderer>();ball.sharedMaterial=projectileMaterial;ball.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var line=new GameObject("Short projectile trail").AddComponent<LineRenderer>();line.transform.SetParent(obj.transform,false);line.useWorldSpace=true;line.sharedMaterial=projectileMaterial;line.positionCount=8;line.numCapVertices=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;projectileVisualCount++;
            return new ProjectileVisual {root=obj,ball=ball,line=line};
        }
        void UpdateProjectiles()
        {
            if(!showProjectiles || robotDemo || match==null){ClearProjectileVisuals();return;}
            int start=ShotEnd(playhead-BallisticMath.Life),end=ShotEnd(playhead);
            displayedShots.Clear();displayedSet.Clear();
            for(int i=end-1;i>=start && displayedShots.Count<ProjectileBudget;i--)
                if(SolveAim(shots[i]).spatial){displayedShots.Add(shots[i]);displayedSet.Add(shots[i]);}
            // Retire everything outside the chosen window BEFORE acquiring any
            // objects, so dense backward seeks cannot grow the pool past budget.
            retireProjectiles.Clear();foreach(var pair in activeProjectiles)if(!displayedSet.Contains(pair.Key))retireProjectiles.Add(pair.Key);
            foreach(var shot in retireProjectiles){var v=activeProjectiles[shot];v.root.SetActive(false);projectilePool.Push(v);activeProjectiles.Remove(shot);}
            foreach(var shot in displayedShots)
            {
                var path=CachedFlight(shot);float age=playhead-shot.time;
                if(!activeProjectiles.TryGetValue(shot,out var visual))activeProjectiles[shot]=visual=AcquireProjectile();
                Vector3 p=path.At(age);bool onscreen=p.x>=-1 && p.z>=-1 && p.x<=field.width+1 && p.z<=field.depth+1 && p.y>=-.1f;
                visual.root.transform.position=p;visual.root.transform.localScale=Vector3.one*(shot.radius*2);
                visual.root.SetActive(onscreen);if(!onscreen)continue;
                float alpha=BallisticMath.Opacity(age);visual.color.SetColor("_Color",new Color(.6f,1,.045f,alpha));visual.ball.SetPropertyBlock(visual.color);visual.line.SetPropertyBlock(visual.color);
                visual.line.startWidth=shot.radius*.8f;visual.line.endWidth=shot.radius*.15f;
                for(int j=0;j<8;j++)visual.line.SetPosition(j,path.At(Mathf.Max(0,age-j*.012f)));
            }
        }
        int ShotEnd(float time)
        {
            int lo=0,hi=shots.Count;while(lo<hi){int mid=(lo+hi)/2;if(shots[mid].time<=time)lo=mid+1;else hi=mid;}return lo;
        }
        string SelectedAimSummary(Unit unit)
        {
            if(!shooterShots.TryGetValue(unit,out var list) || list.Count==0)return "瞄准推定：无发弹记录";
            int lo=0,hi=list.Count;while(lo<hi){int mid=(lo+hi)/2;if(list[mid].time<=playhead)lo=mid+1;else hi=mid;}
            if(lo==0 || playhead-list[lo-1].time>3)return "瞄准推定：当前无近期射击";
            var shot=list[lo-1];var aim=SolveAim(shot);
            var target=units.Find(u=>u.data.id==aim.target);
            string name=target==null?"未锁定":(target.data.team=="red"?"红 ":"蓝 ")+target.data.name;
            string evidence=aim.evidence?"同口径受击":aim.hpDrop>0?"掉血方向候选":"方向示意";
            if(aim.region=="outpost_armor_ring")
                return $"推定目标 {name} · 装甲环区\n俯仰 {aim.pitch:+0.0;-0.0;0.0}°（{aim.pitchLow:0.0}–{aim.pitchHigh:0.0}°）· 置信 {aim.confidence:P0}\n{evidence} · 发弹后掉血 {aim.hpDrop:0}";
            return $"推定目标 {name} · 俯仰 {aim.pitch:+0.0;-0.0;0.0}°\n{evidence} · 发弹后掉血 {aim.hpDrop:0} · 置信 {aim.confidence:P0}";
        }
        void ToggleAllLabels(){allLabels=!allLabels;}
    }
}
