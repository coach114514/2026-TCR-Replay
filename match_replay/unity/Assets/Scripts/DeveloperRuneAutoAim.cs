using System;
using System.Collections.Generic;
using UnityEngine;
namespace TcrReplay
{
    // A shot is never treated as a hit. Only collision/referee feedback advances
    // the pair; expiration permits another shot at the same rotating target.
    public sealed class DeveloperRuneAimState
    {
        public string target="";public int group=-1,first=-1,second=-1,current=-1,pendingShot=-1,pendingPanel=-1,retries,autoShots;
            public float feedbackAt,predictedMiss,predictedSigma;public bool fireReady;public readonly HashSet<int> confirmed=new HashSet<int>();
        public bool Pending=>pendingShot>=0;
        public void Group(string key,int serial)
        {
            if(target==key&&group==serial)return;
            target=key;group=serial;first=second=current=pendingShot=pendingPanel=-1;fireReady=false;confirmed.Clear();
        }
        public void Pair(int a,int b){if(first>=0)return;first=current=a;second=b;}
        public void Confirm(int serial,int panel)
        {
            if(group!=serial)return;
            if(panel==second&&!confirmed.Contains(first)){int old=first;first=second;second=old;}
            confirmed.Add(panel);
            if(panel==pendingPanel)pendingShot=pendingPanel=-1;
            if(panel==first)current=second;
        }
        public void Issued(int shot,int panel,float at,bool automatic)
        {
            if(automatic)autoShots++;
            if(Pending)return;pendingShot=shot;pendingPanel=panel;feedbackAt=at;
        }
        public void Tick(float now){if(Pending&&now>=feedbackAt){pendingShot=pendingPanel=-1;retries++;}}
        public void Clear(){target="";group=first=second=current=pendingShot=pendingPanel=-1;fireReady=false;predictedMiss=predictedSigma=0;confirmed.Clear();retries=autoShots=0;}
    }
    public partial class ReplayViewer
    {
        struct RuneAimSolution
        {
            public int index,order;public Vector3 direction;public DeveloperPlate plate;public float flight,delay,score,uncertainty;public bool reachable;
        }
        readonly DeveloperRuneAimState runeAim=new DeveloperRuneAimState();
        readonly List<RuneAimSolution> runePredictions=new List<RuneAimSolution>(2);
        bool RuneLocked=>gameAutoAim&&gameLock.StartsWith("rune_",StringComparison.Ordinal);
        void ClearRuneAim(){runeAim.Clear();runePredictions.Clear();}
        bool TryRunePrediction(GameVehicle vehicle,Vector3 currentMuzzle,Vector3 forward,DeveloperRuneTracker tracker,int index,float extraDelay,out RuneAimSolution solution)
        {
            solution=default;if(!tracker.layouts.TryGetValue(index,out var layout))return false;
            Vector3 muzzle=currentMuzzle,direction=forward;float flight=0,delay=extraDelay;DeveloperPlate predicted=layout;
            bool reachable=true;
            for(int iter=0;iter<8;iter++)
            {
                if(!DeveloperRuneTracker.Solve(muzzle,layout,tracker,game.time,delay,game.rules,out direction,out flight,out predicted))return false;
                Vector3 local=vehicle.unit.root.transform.InverseTransformDirection(direction);
                float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg,
                    localYaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,localPitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
                reachable=localPitch>=-30&&localPitch<=30;
                // The gimbal is advanced later in THIS GameStep before firing.
                // Remaining turn time excludes that immediate movement; when
                // aligned the actual departure is now, not now+one tick.
                delay=extraDelay+Mathf.Max(0,Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(vehicle.state.gimbalYaw,yaw)),Mathf.Abs(vehicle.state.pitch-Mathf.Clamp(pitch,-30,30)))/Mathf.Max(1,game.rules.gimbalSpeed)-DeveloperWorld.Step);
                var model=vehicle.unit.robot.model;var m=BallisticMath.Group(model,model.gunGroup,vehicle.state.fold,localYaw,Mathf.Clamp(localPitch,-30,30),true);
                muzzle=vehicle.unit.root.transform.TransformPoint(m.MultiplyPoint3x4(PassagePose.Vector(model.muzzlePoint)));
            }
            float uncertainty=tracker.UncertaintyAt(game.time+delay+flight,layout.local.magnitude);
            solution=new RuneAimSolution {index=index,direction=direction,plate=predicted,flight=flight,delay=delay,uncertainty=uncertainty,
                reachable=reachable,score=flight+Vector3.Angle(forward,direction)*.015f+uncertainty};return true;
        }
        bool RuneTrajectoryClear(Vector3 muzzle,RuneAimSolution solution)
        {
            Vector3 prev=muzzle;
            for(int i=1;i<=24;i++)
            {
                float t=solution.flight*i/24;Vector3 next=muzzle+solution.direction*game.rules.bulletSpeed*t+Vector3.down*.5f*game.rules.gravity*t*t,delta=next-prev;
                if(DevSphereCast(prev,.0085f,delta.normalized,out _,Mathf.Max(0,delta.magnitude-(i==24?.025f:0)),(1<<10)|(1<<14),QueryTriggerInteraction.Ignore))return false;prev=next;
            }
            return true;
        }
        void SolveRuneAim(GameVehicle vehicle,Vector3 currentMuzzle,Vector3 forward)
        {
            runeAim.fireReady=false;
            if(gameLock!="rune_"+vehicle.state.team){aimStatus="机关自瞄仅选择己方有效面";runePredictions.Clear();return;}
            var rune=game.runes[vehicle.state.team];
            if(rune.startPending||!rune.activating)
            {
                runePredictions.Clear();runeAim.Group(gameLock,rune.groupSerial);
                aimStatus=rune.startPending?$"锁定 {gameLock} · 等待启动 {Mathf.Max(0,rune.startsAt-game.time):0.0}s":"锁定 "+gameLock+" · 本轮结束／未开启";return;
            }
            runeAim.Tick(game.time);runeAim.Group(gameLock,rune.groupSerial);
            if(!runeTrackers.TryGetValue(gameLock,out var tracker)||!tracker.ready||tracker.groupSerial!=rune.groupSerial)
            {aimStatus="锁定 "+gameLock+" · 等待当前灯序观测，保留跟踪";return;}
            foreach(int hit in tracker.alreadyHit)runeAim.Confirm(rune.groupSerial,hit);
            if(runeAim.first<0)
            {
                var choices=new List<RuneAimSolution>(2);
                foreach(int index in tracker.lit)if(!tracker.alreadyHit.Contains(index)&&TryRunePrediction(vehicle,currentMuzzle,forward,tracker,index,0,out var p))choices.Add(p);
                choices.Sort((a,b)=>{int score=a.score.CompareTo(b.score);return score!=0?score:a.index.CompareTo(b.index);});
                if(choices.Count>0)
                {
                    if(rune.Large(game.time)&&tracker.alreadyHit.Count>0)
                    {foreach(int hit in tracker.alreadyHit){runeAim.Pair(hit,choices[0].index);runeAim.Confirm(rune.groupSerial,hit);break;}}
                    else runeAim.Pair(choices[0].index,choices.Count>1?choices[1].index:-1);
                }
            }
            if(runeAim.first>=0&&runeAim.second<0&&rune.Large(game.time))
                foreach(int index in tracker.lit)if(index!=runeAim.first&&!runeAim.confirmed.Contains(index)){runeAim.second=index;if(runeAim.current<0)runeAim.current=index;break;}
            var current=new List<RuneAimSolution>(2);
            float secondDelay=0;
            foreach(int index in new[]{runeAim.first,runeAim.second})
            {
                if(index<0)continue;
                float extra=index==runeAim.second&&!runeAim.confirmed.Contains(runeAim.first)?secondDelay:0;
                if(!TryRunePrediction(vehicle,currentMuzzle,forward,tracker,index,extra,out var p))continue;
                p.order=index==runeAim.first?1:2;current.Add(p);
                if(p.order==1)secondDelay=(runeAim.Pending?Mathf.Max(0,runeAim.feedbackAt-game.time):p.flight)+.02f;
            }
            if(current.Count>0){runePredictions.Clear();runePredictions.AddRange(current);}
            foreach(var selected in current)
            {
                if(selected.index!=runeAim.current||runeAim.confirmed.Contains(selected.index))continue;
                gameAimValid=true;gameAimDirection=selected.direction;gameAimFlight=selected.flight;gameAimPoint=selected.plate.center;gameSelectedPlate=selected.plate;aimPlate=selected.index;
                float radius=.15f-(rune.MinimumRing(game.time)-1)*.015f;
                float deadline=rune.secondPending?rune.firstAt+1:rune.groupAt+2.5f;
                bool accurate=tracker.MotionMatched&&selected.uncertainty<=game.rules.runeAimMaxSigma;
                runeAim.fireReady=rune.lit>=0&&game.time>=rune.nextAt&&selected.reachable&&accurate&&game.time-tracker.lastSeen<=.35f&&game.time+selected.delay+selected.flight<Mathf.Min(deadline,rune.started+20)-.05f&&RuneTrajectoryClear(currentMuzzle,selected);
                string phase=runeAim.Pending?"等待实际命中反馈":!selected.reachable?"机械俯仰受限，持续预测":!accurate?"预测收敛中":runeAim.retries>0?"未命中快速补打":"自动扳机就绪";
                aimStatus=$"{tracker.Classification} · 靶{aimPlate} · {(selected.order==1?"①首击":"②补击")} · {phase} · 有效{rune.MinimumRing(game.time)}–10环";return;
            }
            aimStatus="锁定 "+gameLock+" · 灯序切换中，保持云台和预测框";
        }
        bool RuneAutomaticTrigger(DeveloperActor actor)
        {
            if(!RuneLocked||!runeAim.fireReady||runeAim.Pending||!gameAimValid||UiHasModal||!(developerSmokeActive||mouseCaptured&&BackgroundInputAllowed)||GameFolded(gameVehicles[actor.id]))return false;
            if(!actor.CanFire(game.time,game.rules))return false;
            // Avoid automatic thermal lockout while preserving manual fire.
            if(!game.rules.unlimitedHeat&&actor.heat+game.rules.heatPerShot>=actor.heatLimit-.05f)return false;
            GameMuzzle(gameVehicles[actor.id],out var muzzle,out var forward);
            if(!runeTrackers.TryGetValue(gameLock,out var tracker)||!tracker.layouts.TryGetValue(runeAim.current,out var layout))return false;
            if(!DeveloperRuneTracker.Solve(muzzle,layout,tracker,game.time,0,game.rules,out _,out float flight,out _))return false;
            // Evaluate the actual gun vector, not a camera ray or a direction
            // computed before posing the gimbal. Intersect its gravity trajectory
            // with the predicted moving target plane and aim for the 10-ring disk.
            Vector3 impact=Vector3.zero;DeveloperPlate plate=layout;
            for(int i=0;i<4;i++)
            {
                plate=tracker.At(layout,game.time+flight);
                impact=muzzle+forward*game.rules.bulletSpeed*flight+Vector3.down*.5f*game.rules.gravity*flight*flight;
                var velocity=forward*game.rules.bulletSpeed+Vector3.down*game.rules.gravity*flight-plate.velocity;
                float speed=Vector3.Dot(velocity,plate.Normal);if(speed>=-game.rules.armorMinVelocity)return false;
                flight-=Vector3.Dot(impact-plate.center,plate.Normal)/speed;if(flight<=0||flight>4)return false;
            }
            plate=tracker.At(layout,game.time+flight);impact=muzzle+forward*game.rules.bulletSpeed*flight+Vector3.down*.5f*game.rules.gravity*flight*flight;
            Vector3 local=Quaternion.Inverse(plate.rotation)*(impact-plate.center);
            runeAim.predictedMiss=new Vector2(local.x,local.y).magnitude;
            runeAim.predictedSigma=tracker.UncertaintyAt(game.time+flight,layout.local.magnitude);
            bool precise=runeAim.predictedMiss<=game.rules.runeAimMaxMiss&&runeAim.predictedSigma<=game.rules.runeAimMaxSigma&&runeAim.predictedMiss+2*runeAim.predictedSigma<=.014f;
            if(precise)gameAimFlight=flight;return precise;
        }
        void RuneShotIssued(GameBullet bullet,bool automatic)
        {
            if(!RuneLocked||bullet.shooter!=game.controlled||runeAim.current<0||!game.actors.TryGetValue(bullet.shooter,out var actor))return;
            bullet.runeTarget=gameLock;bullet.runePanel=runeAim.current;bullet.runeGroup=game.runes[actor.team].groupSerial;
            runeAim.Issued(bullet.id,bullet.runePanel,game.time+gameAimFlight+Mathf.Max(.025f,game.rules.observationDelay),automatic);
        }
        void RuneHitFeedback(GameBullet bullet,DeveloperPlate plate)
        {
            if(RuneLocked&&gameLock==plate.target)runeAim.Confirm(bullet.runeGroup,plate.index);
        }
        string RuneRingHud()
        {
            if(!RuneLocked||game==null||!game.actors.TryGetValue(game.controlled,out var actor))return "";
            var r=game.runes[actor.team];string last=r.hitSerial>0?r.lastHitRing+"环":"尚无有效命中";
            float sum=0;foreach(int ring in r.rings)sum+=ring;string average=r.rings.Count>0?(sum/r.rings.Count).ToString("0.00")+"环":"—";
            return $"{(r.Large(game.time)?"大":"小")}机关 · 第{Mathf.Min(5,r.groups+1)}/5组 · 有效{r.MinimumRing(game.time)}–10环 · 最近{last} · 本轮均值{average}";
        }
    }
}
