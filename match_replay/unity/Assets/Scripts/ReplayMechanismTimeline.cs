using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public class MechanismData
    {
        public int schema;public string rules;
        public BaseArmorRecord[] baseArmor;public OutpostRecord[] outpost;
        public RuneProgressRecord[] runeProgress;public RuneSuccessRecord[] runeSuccess;
        public MatchPhaseRecord[] phases;public RuneStateRecord[] runeStates;
    }
    [Serializable] public class MatchPhaseRecord {public string state,evidence;public float t,ends;}
    [Serializable] public class RuneStateRecord {public string team,kind,state,evidence;public float t,ends;public int arms;public bool armSequenceKnown;}
    [Serializable] public class BaseArmorRecord {public string team,state,evidence,source;public float t;}
    [Serializable] public class OutpostRecord {public string team,state,evidence;public float t;}
    [Serializable] public class RuneProgressRecord {public string team,kind,evidence;public float t,rings,average;public int arms;}
    [Serializable] public class RuneSuccessRecord {public string team,kind,state,evidence;public float t,ends;public int arms;}
    public enum BaseArmorPose {Unknown,Closed,Open}
    public struct BaseArmorState
    {
        public BaseArmorPose pose;public float t;public string evidence,source;
        public static BaseArmorState Unknown => new BaseArmorState {pose=BaseArmorPose.Unknown,t=-1,source="unknown",evidence="当前记录缺少基地护甲状态"};
    }
    // Reserved source contract; no armor mesh, hinge, animation or pose-based
    // collision is instantiated in this release. Protocol invincibility is not
    // converted to a closed armor pose.
    public interface IBaseArmorSource {bool TryQuery(string gameId,string team,float time,out BaseArmorState state);}
    public interface IBaseArmorHistorySource:IBaseArmorSource {bool TryFirstOpened(string gameId,string team,float until,out float time);}
    public struct RuneState
    {
        public string state,evidence,kind;public int arms;public float since,ends;
        public bool confirmed,armSequenceKnown;
    }
    public sealed class ReplayMechanismTimeline
    {
        readonly MatchData data;
        readonly Dictionary<string,EntityData> entities=new Dictionary<string,EntityData>();
        readonly List<IBaseArmorSource> armorSources=new List<IBaseArmorSource>();
        readonly Dictionary<string,float> firstDeaths=new Dictionary<string,float>();
        readonly List<Vector2> largeIntervals=new List<Vector2>();
        public readonly int direction;
        public ReplayMechanismTimeline(MatchData match)
        {
            data=match;
            uint hash=2166136261;foreach(char c in match.gameId){hash^=c;hash*=16777619;}
            direction=(hash&1)==0?1:-1;
            foreach(var e in match.entities)
            {
                entities[e.id]=e;
                if(e.role=="outpost")
                    foreach(var s in e.samples)if(s.hasHp && s.hp<=0){firstDeaths[e.team]=s.t;break;}
            }
            BuildRuneMotion();
        }
        // Small/large are time stages of this one physical rotor, not separate
        // rotors. A surviving small buff must not change the stage after 180s.
        public static string RuneStage(float time) => time<180?"small":"large";
        static float DirectRuneEnd(RuneStateRecord record)
        {
            float end=record.ends;
            if(record.state=="activating")end=end<0?record.t+20:Mathf.Min(end,record.t+20);
            return end;
        }
        void BuildRuneMotion()
        {
            if(data.duration<=180 || data.mechanisms==null)return;
            var boundaries=new SortedSet<float> {180,data.duration};
            Action<float> add=t=>{if(float.IsFinite(t) && t>=180 && t<=data.duration)boundaries.Add(t);};
            if(data.mechanisms.runeProgress!=null)
                foreach(var r in data.mechanisms.runeProgress)
                    if(r.kind=="large"){add(r.t);if(r.arms>0)add(r.t+2);}
            if(data.mechanisms.runeSuccess!=null)
                foreach(var r in data.mechanisms.runeSuccess)if(r.kind=="large")add(r.t);
            if(data.mechanisms.runeStates!=null)
                foreach(var r in data.mechanisms.runeStates)
                    if(r.kind=="large"){add(r.t);add(DirectRuneEnd(r));}
            float previous=-1;
            foreach(float end in boundaries)
            {
                if(previous>=0 && end>previous)
                {
                    float middle=previous+(end-previous)*.5f;
                    var red=Rune("red",middle);var blue=Rune("blue",middle);
                    // The handbook specifies a shared axis and independent side
                    // states, but not speed arbitration for unequal trigger times.
                    // Union is a display assumption: never add two motor speeds.
                    if(red.kind=="large" && red.state=="activating" || blue.kind=="large" && blue.state=="activating")
                    {
                        int last=largeIntervals.Count-1;
                        if(last>=0 && largeIntervals[last].y==previous)
                            largeIntervals[last]=new Vector2(largeIntervals[last].x,end);
                        else largeIntervals.Add(new Vector2(previous,end));
                    }
                }
                previous=end;
            }
        }
        public void RegisterArmorSource(IBaseArmorSource source) {if(source!=null && !armorSources.Contains(source))armorSources.Add(source);}
        public void UnregisterArmorSource(IBaseArmorSource source) {armorSources.Remove(source);}
        public BaseArmorState BaseArmor(string team,float time)
        {
            for(int i=armorSources.Count-1;i>=0;i--)
                if(armorSources[i].TryQuery(data.gameId,team,time,out var state))return state;
            BaseArmorRecord last=null;
            if(data.mechanisms?.baseArmor!=null)
                foreach(var r in data.mechanisms.baseArmor)if(r.team==team && r.t<=time && (last==null || r.t>last.t))last=r;
            if(last==null)return BaseArmorState.Unknown;
            var pose=last.state=="open"?BaseArmorPose.Open:last.state=="closed"?BaseArmorPose.Closed:BaseArmorPose.Unknown;
            return new BaseArmorState {pose=pose,t=last.t,evidence=last.evidence,source=last.source??"recorded"};
        }
        public bool GuideLamp(string team,bool baseLamp,float time,out bool known)
        {
            known=false;
            if(data.mechanisms?.phases!=null)
                foreach(var phase in data.mechanisms.phases)
                    if(time>=phase.t && time<phase.ends && (phase.state=="preparation" || phase.state=="self_check")){known=true;return true;}
            if(!entities.TryGetValue(team+"_outpost",out var e))return false;
            int i=Timeline.StateIndex(e.samples,time);
            while(i>=0 && !e.samples[i].hasHp)i--;
            if(i<0)return false;
            // Confirmed destruction persists until a later positive HP record
            // proves rebuilding. A gap in telemetry cannot turn the base off.
            if(e.samples[i].hp>0 && time-e.samples[i].t>2)return false;
            known=true;bool alive=e.samples[i].hp>0;return baseLamp?!alive:alive;
        }
        public float OutpostStop(string team)
        {
            float stop=180;
            if(firstDeaths.TryGetValue(team,out float death))stop=Mathf.Min(stop,death);
            if(data.mechanisms?.outpost!=null)
                foreach(var r in data.mechanisms.outpost)if(r.team==team && r.state=="stopped")stop=Mathf.Min(stop,r.t);
            if(data.mechanisms?.baseArmor!=null)
                foreach(var r in data.mechanisms.baseArmor)if(r.team!=team && r.state=="open")stop=Mathf.Min(stop,r.t);
            // External state adapters may confirm an earlier open state.
            foreach(var source in armorSources)
            {
                string other=team=="red"?"blue":"red";
                if(source is IBaseArmorHistorySource history && history.TryFirstOpened(data.gameId,other,data.duration,out float opened))stop=Mathf.Min(stop,opened);
                else if(source.TryQuery(data.gameId,other,data.duration,out var state) && state.pose==BaseArmorPose.Open)stop=Mathf.Min(stop,state.t);
            }
            return stop;
        }
        public float OutpostAngle(string team,float time)
        {
            float stop=OutpostStop(team),t=Mathf.Clamp(time,0,stop);
            float angle=t<=5?14.4f*t*t:360+144*(t-5);
            if(time>stop)
            {
                float home=Mathf.Ceil((angle-.001f)/360)*360;
                angle=Mathf.Min(home,angle+144*(time-stop));
            }
            return direction*angle;
        }
        public RuneState Rune(string team,float time)
        {
            string kind=RuneStage(time);
            var state=new RuneState {state="unknown",kind=kind,since=-1,ends=-1,arms=-1,evidence="缺少完整机关状态流"};
            RuneStateRecord direct=null;
            if(data.mechanisms?.runeStates!=null)
                foreach(var r in data.mechanisms.runeStates)
                    if(r.team==team && r.kind==kind && r.t<=time && (direct==null || r.t>direct.t))direct=r;
            RuneProgressRecord progress=null;RuneSuccessRecord success=null;
            if(data.mechanisms?.runeProgress!=null)
                foreach(var r in data.mechanisms.runeProgress)
                    if(r.team==team && r.kind==kind && r.t<=time && (progress==null || r.t>=progress.t))progress=r;
            if(data.mechanisms?.runeSuccess!=null)
                foreach(var r in data.mechanisms.runeSuccess)
                    if(r.team==team && r.t<=time && (success==null || r.t>success.t))success=r;
            // A newer confirmed success ends a previously observed activation.
            // Otherwise the direct state remains authoritative over sparse hits.
            if(direct!=null && !(direct.state=="activating" && success!=null && success.kind==kind && success.t>=direct.t))
            {
                float end=DirectRuneEnd(direct);
                return end<0 || time<end?new RuneState {state=direct.state,kind=kind,since=direct.t,ends=end,arms=direct.arms,evidence=direct.evidence,confirmed=true,armSequenceKnown=direct.armSequenceKnown}:state;
            }
            if(success!=null && (progress==null || success.t>=progress.t))
            {
                float ends=success.ends;
                if(ends<0 && success.kind=="large" && success.arms>=5 && success.arms<=10)
                    ends=success.t+(success.arms==10?60:5*success.arms+5); // rule table5-18
                if(ends>=0 && time>=ends)
                    return time-ends<=2?new RuneState {state="inactive",kind=kind,since=ends,ends=-1,arms=0,evidence="成功记录及规则有效期推导",confirmed=true}:state;
                if(ends<0 && time-success.t>2)return state;
                return new RuneState {state="activated",kind=success.kind,since=success.t,ends=ends,arms=success.arms,evidence=success.evidence,confirmed=true};
            }
            if(progress!=null && progress.arms>0 && time-progress.t<=2)
                return new RuneState {state="activating",kind=progress.kind,since=progress.t,ends=-1,arms=progress.arms,evidence=progress.evidence,confirmed=true};
            return state;
        }
        public RuneState RuneMechanism(string team,float time)
        {
            var state=Rune(team,time);string kind=RuneStage(time);
            if(state.kind==kind)return state;
            // A still valid small buff is evidence about the robots' benefit,
            // not confirmation of the current large-stage mechanism lights.
            return new RuneState {kind=kind,state="unknown",since=-1,ends=-1,arms=-1,evidence="仅有小机关增益记录，当前大机关状态未知"};
        }
        // Deterministic illustrative phase, analytically integrated so seeking
        // and historical target collision use exactly the same shared rotation.
        // Actual phase, random parameters and bright arm IDs are not recorded.
        const float RuneA=.9f,RuneW=1.94f,RuneB=2.09f-RuneA;
        public float RuneAngle(float time)
        {
            float angle=60*Mathf.Max(0,time);
            if(time<=180)return direction*angle;
            foreach(var range in largeIntervals)
                if(time>range.x)angle+=RuneExtra(range.x,Mathf.Min(range.y,time));
            return direction*angle;
        }
        public bool RuneVariable(float time)
        {
            foreach(var range in largeIntervals)if(time>=range.x && time<range.y)return true;
            return false;
        }
        public float RuneSpeed(float time)
        {
            foreach(var range in largeIntervals)
                if(time>=range.x && time<range.y)
                    return direction*(RuneA*Mathf.Sin(RuneW*(time-range.x))+RuneB)*Mathf.Rad2Deg;
            return direction*60;
        }
        static float RuneExtra(float start,float end)
        {
            float elapsed=end-start;
            return (RuneA/RuneW*(1-Mathf.Cos(RuneW*elapsed))+RuneB*elapsed)*Mathf.Rad2Deg-60*elapsed;
        }
    }
}
