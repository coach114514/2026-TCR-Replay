using System;
using System.Collections.Generic;
using UnityEngine;
namespace TcrReplay
{
    [Serializable] public sealed class DeveloperRune
    {
        public string team,mode="auto"; public bool enabled,startPending,smallLoop,largeLoop,activating,secondPending;public int opportunities,groups,successes,lit=-1,optional=-1,hits,attempts;
        public float startsAt;
        public int groupSerial,hitSerial,lastHitPanel=-1,lastHitRing,resets;public float lastHitAt;
        public float started,groupAt,firstAt,nextAt,buffUntil,smallBuffUntil,attack=1,defense,cooling=1,a=.91f,omega=1.94f,average,lastRadius;
        public List<int> rings=new List<int>(); public List<int> available=new List<int>();
        public DeveloperRandom random;public int awarded=-1;
        public DeveloperRune(string team,int seed){this.team=team;random=new DeveloperRandom(seed);available.AddRange(new[]{0,1,2,3,4});}
        public bool Large(float time)=>mode=="large"||mode!="small"&&time>=180;
        public bool Selected(string kind)=>enabled&&mode==kind;
        public bool Loop=>mode=="large"?largeLoop:mode=="small"&&smallLoop;
        public int MinimumRing(float time)=>Large(time)?successes>=2?7:successes>=1?4:1:1;
        public void Stop(){enabled=startPending=activating=secondPending=false;lit=optional=-1;}
        public void ResetTimeline(float time){Stop();startsAt=0;groups=0;successes=0;opportunities=0;awarded=-1;TickChances(time);}
        void TickChances(float time)
        {
            if(mode!="auto"){if(awarded<0){opportunities=1;awarded=0;}return;}
            int slot=time>=330?4:time>=255?3:time>=180?2:time>=90?1:0;
            if(slot>=2&&awarded<2){opportunities=0;successes=0;Stop();awarded=1;}
            if(slot>awarded){opportunities+=slot-awarded;awarded=slot;}
        }
        public bool Trigger(DeveloperWorld world)
        {
            TickChances(world.time);if(startPending||activating||opportunities<=0||world.time<buffUntil||!Large(world.time)&&world.time<smallBuffUntil)return false;
            Schedule(world);return true;
        }
        public void Training(DeveloperWorld world,string kind,bool on)
        {
            if(kind!="small"&&kind!="large")throw new ArgumentException("机关模式 small / large");
            if(!on){if(mode==kind)Stop();world.Log(team+" "+kind+"机关关闭");return;}
            Stop();if(mode!=kind){successes=0;buffUntil=smallBuffUntil=0;}
            mode=kind;opportunities=1;awarded=0;world.customRules=true;Schedule(world);
        }
        void Schedule(DeveloperWorld world)
        {
            enabled=startPending=true;activating=secondPending=false;lit=optional=-1;groups=hits=0;
            startsAt=world.time+3;world.Log(team+" "+(Large(world.time)?"大":"小")+"机关等待3秒"+(Loop?" · 循环":" · 单次"));
        }
        void Begin(DeveloperWorld world)
        {
            startPending=false;opportunities=Mathf.Max(0,opportunities-1);activating=true;started=startsAt;attempts++;groups=hits=0;rings.Clear();available.Clear();available.AddRange(new[]{0,1,2,3,4});
            a=.780f+(float)random.NextDouble()*.265f;omega=1.884f+(float)random.NextDouble()*.116f;NewGroup(world.time);world.Log(team+" "+(Large(world.time)?"大":"小")+"能量机关开始20秒激活窗口");
        }
        void EndCycle(DeveloperWorld world)
        {
            Stop();if(Loop){opportunities=1;Schedule(world);}
        }
        void NewGroup(float time)
        {
            groupSerial++;
            if(available.Count==0)available.AddRange(new[]{0,1,2,3,4});lit=available[random.Next(available.Count)];optional=(lit+1+random.Next(4))%5;
            secondPending=false;groupAt=time;nextAt=time;firstAt=0;
        }
        void ResetAttempt(float time){resets++;groups=hits=0;rings.Clear();available.Clear();available.AddRange(new[]{0,1,2,3,4});NewGroup(time);}
        public void Tick(DeveloperWorld world)
        {
            TickChances(world.time);if(startPending&&world.time+1e-6f>=startsAt)Begin(world);if(!activating)return;
            if(world.time-started>=20){world.Log(team+" 机关激活超时");EndCycle(world);return;}
            if(nextAt>0&&world.time<nextAt)return;
            if(lit<0){NewGroup(world.time);return;}
            if(secondPending&&world.time-firstAt>=1){FinishGroup(world);return;}
            if(!secondPending&&world.time-groupAt>=2.5f){ResetAttempt(world.time);world.Log(team+" 首击超时，窗口内进度重置");}
        }
        public static int Ring(float radius){if(radius<0||radius>.15f)return 0;return Mathf.Clamp(10-Mathf.FloorToInt((Mathf.Max(0,radius-.000001f))/.015f),1,10);}
        public bool Hit(DeveloperWorld world,int panel,float radius,float normalSpeed)
        {
            lastRadius=radius;int ring=Ring(radius);if(!activating||world.time<nextAt||ring==0||normalSpeed<=world.rules.armorMinVelocity)return false;
            bool large=Large(world.time);int minRing=MinimumRing(world.time);if(ring<minRing){world.Log(team+" 机关 "+ring+"环，当前有效环区 "+minRing+"..10，忽略");return false;}
            if(panel!=lit && !(large&&panel==optional)){ResetAttempt(world.time);world.Log(team+" 命中未激活靶，进度重置");return false;}
            if(!secondPending)
            {
                int oldLit=lit;lit=panel;if(large){if(panel==optional)optional=oldLit;secondPending=true;firstAt=world.time;}else{available.Remove(panel);}
                Confirm(world,panel,ring);world.Log(team+" 机关 "+ring+"环");if(!large)FinishGroup(world);return true;
            }
            if(panel==lit || world.time-firstAt>=1)return false;Confirm(world,panel,ring);world.Log(team+" 大机关第二击 "+ring+"环");FinishGroup(world);return true;
        }
        void Confirm(DeveloperWorld world,int panel,int ring){rings.Add(ring);hits++;hitSerial++;lastHitPanel=panel;lastHitRing=ring;lastHitAt=world.time;}
        void FinishGroup(DeveloperWorld world)
        {
            groups++;secondPending=false;lit=optional=-1;
            if(groups<5){if(Large(world.time))nextAt=world.time+.15f;else NewGroup(world.time);return;}
            activating=false;successes++;float sum=0;foreach(int n in rings)sum+=n;average=rings.Count>0?sum/rings.Count:0;
            if(!Large(world.time)){smallBuffUntil=world.time+45;world.teams[team].smallXp=0;world.Log(team+" 小机关成功：25%防御，45秒");EndCycle(world);return;}
            attack=average>9?3:average>7?2:1.5f;defense=average>9?.5f:.25f;cooling=average>9?5:average>8?3:average>3?2:1;
            int[] durations={30,35,40,45,50,60};buffUntil=world.time+durations[Mathf.Clamp(hits-5,0,5)];
            int live=0;foreach(var actor in world.actors.Values)if(actor.team==team&&actor.Alive)live++;if(live>0)foreach(var actor in world.actors.Values)if(actor.team==team&&actor.Alive)world.Xp(actor,750/live);
            world.Log($"{team} 大机关成功：{hits}击，平均{average:0.00}环，攻击×{attack:0.0}，持续{buffUntil-world.time:0}秒");
            EndCycle(world);
        }
    }
}
