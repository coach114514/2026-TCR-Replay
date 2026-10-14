using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public sealed class DeveloperRandom
    {
        public int state;
        public DeveloperRandom(int seed){state=seed==0?2026:seed;}
        public double NextDouble(){uint n=(uint)state;n^=n<<13;n^=n>>17;n^=n<<5;state=(int)n;return n/4294967296d;}
        public int Next(int count)=>Math.Min(count-1,(int)(NextDouble()*count));
    }
    [Serializable] public class DeveloperRules
    {
        public float gravity=9.81f, bulletSpeed=25, damage17=20, baseFrontDamage=5, armorMinVelocity=12, shotPeriod=.1f, armorInterval=.05f;
        public float idlePower=8, speedPower=8, spinPower=.55f, mechanicalSpeed=3.3f, acceleration=3, mass=18, efficiency=.7f;
        public float slowRatio=.35f, spinSpeed=360, angularAcceleration=720, gimbalSpeed=360, coolingMultiplier=1, heatPerShot=10, armorRestitution=.28f, groundRestitution=.25f;
        public float observationHz=60, observationDelay=0, observationNoise=0, observationDrop=0, processAcceleration=4, processVertical=.5f, measurementSigma=.002f;
        public float aimRoiWidth=.6f,aimRoiHeight=.5f;
        public float runeAimMaxMiss=.004f,runeAimMaxSigma=.004f;
        public float ordinaryPowerFraction=.85f,burstMechanicalSpeed=6,burstAcceleration=6,capacitance=60f/7,capMaxVoltage=21,capMinVoltage=8,capRecoverVoltage=10,capMaxPower=150,capEfficiency=.96f,capInterfaceCurrent=15,capUnderDelay=.5f,capRecoverDelay=5,wirelessPower=90;
        public bool powerLimiter=true, gravityCompensation=true, aimFireGate=false, infiniteAmmo=false, unlimitedHeat=false;
        public bool vehiclePhysics=true,spinBodyRelative=false,activeSuspension=true;
        public int physicsSubsteps=2;
        public Vector3 physicsComOffset=Vector3.zero;
        public float followDeadzone=1.5f,followGain=5,followDamping=.25f,followMaxSpeed=180;
        public float chassisVelocityGain=.8f,followDriveFraction=.4f,wheelBearingTorque=.02f;
        public float wheelGrip=.9f,wheelSlip=140,motorTorque=3.6f,wheelSpeedGain=.08f,wheelIntegralGain=.6f,wheelMaxOmega=140,motorCopperLoss=1.2f;
        public bool wheelStaticContact=true,wheelRampAssist=true;
        public float wheelStaticGrip=1.1f,wheelRollResistance=.02f,wheelBrakeDeceleration=4.5f,wheelBrakePower=300;
        public float wheelHoldSpeed=.04f,wheelHoldRelease=.08f,wheelHoldDelay=.15f;
        public float wheelApproachSpeed=1.2f,wheelAssistApproachSpeed=1.8f,wheelClimbExitSpeed=1,wheelClimbExitDelay=1.2f,wheelLateralPriority=.8f,wheelArmTrimMax=.03f,wheelArmTrimRate=.025f;
        public float wheelLateralPositionGain=100,wheelArmLateralPositionGain=125;
        public float wheelArmForwardWeightMin=.2f;
        public float suspensionScale=1,suspensionDamping=1,armSpring=90,armDamper=6,armTorque=18,armGroundClearance=.008f,armCrestClearance=.045f;
    }
    [Serializable] public class DeveloperTeam
    {
        public string id; public int coins=400, levelCap=5, core=1, earnedCoins, remoteAmmo;
        public float attack=1, defense, cooling=1, buffUntil, damage, smallXp;
        public DeveloperTeam(string id){this.id=id;}
    }
    [Serializable] public class DeveloperActor
    {
        public string model="sweeper"; public bool trainingImmortal,capEnabled=true,wirelessCharging; public float trainingDamage,actualHpDamage,verticalVelocity,chassisEnergy=20000,capEnergy=1890,capVoltage=21,capPower,capChargePower,chassisInputPower,capUnderSince=-1,capRecoverSince=-1; public bool capProtected;
        public string id, team="red", role="infantry", chassis="health", shooter="cooling", motion="still", clip="";
        public Vector3 position, velocity, direction; public float yaw, pitch, gimbalYaw, spinRate=360,angularVelocity, requestedSpeed=2, fold, hp=200, maxHp=200, shield;
        public int level=1, xp, ammo, allowedAmmo, shots, kills, instantRevives, pathIndex, loops=-1, replayIndex,fortressReserve,fortressReserveMax;
        public float powerLimit=45, heatLimit=40, cooling=12, heat, buffer=60, estimatedPower, nextShot, lastDamage=-100, deadAt=-1, respawnPoints, weakUntil, invulnerableUntil, powerOffUntil, fireLockUntil;
        public bool spin, slow, permanentFireLock, heatLocked, levelOverride, blocked, clipFire, pathLoop=true,inFortress,manualControl;
        public float zoneDefense,zoneVulnerability,zoneCooling,tunnelCoolingUntil,revivedAt,revivePowerUntil,clipYawOffset;
        public float pathDwell, dwellUntil, clipTime,clipDwell,clipWaitUntil, defense, vulnerability, attack=1, coolingBuff=1, terrainBuffUntil, defenseBuff;
        public List<Vector3> path=new List<Vector3>(); public List<float> armorHeights=new List<float>();
        public VehiclePhysicsState physics;
        public DeveloperActor(string id,string team,Vector3 position){this.id=id;this.team=team;this.position=position;}
        public bool Alive=>hp>0;
        public bool CanFire(float time,DeveloperRules rules)=>Alive && fold<=(model=="jiageng"?.51f:.01f) && !permanentFireLock && (!heatLocked || rules.unlimitedHeat) && time>=fireLockUntil && time>=weakUntil && time>=nextShot && (rules.infiniteAmmo || ammo>0 && (allowedAmmo>0||fortressReserve>0));
    }
    [Serializable] public class DeveloperBuilding
    {
        public string id, team, role; public float hp,maxHp,shield,armorOpen,armorTarget,angle,deadAt=-1,damage,rotationStopAt=-1,rebuildProgress;
        public bool destroyedBefore,stopped; public int rebuildChances;
        public DeveloperBuilding(string team,string role){this.team=team;this.role=role;id=team+"_"+role;hp=maxHp=role=="base"?5000:1500;shield=role=="base"?150:0;}
    }
    [Serializable] public class DeveloperInput
    {
        public float x,z,yaw,pitch; public bool spin,slow,fire,cameraRelative,burst;
        public DeveloperInput Copy()=>new DeveloperInput {x=x,z=z,yaw=yaw,pitch=pitch,spin=spin,slow=slow,fire=fire,cameraRelative=cameraRelative,burst=burst};
    }
    [Serializable] public class DeveloperFrame {public float t,spinRate,requestedSpeed; public DeveloperInput input; public Vector3 actual;}
    [Serializable] public class DeveloperClip
    {
        public int schema=3;public string name,field,model="sweeper",parameters,initialState;public Vector3 origin;public float initialYaw,duration;public List<DeveloperFrame> frames=new List<DeveloperFrame>();
    }
    [Serializable] public class DeveloperClipStore {public int schema=1;public List<DeveloperClip> clips=new List<DeveloperClip>();}
    public struct DeveloperHit {public string target,shooter;public int panel;public Vector2 local;public float normalSpeed;}
    public sealed partial class DeveloperWorld
    {
        public const float Step=1f/120;
        public readonly DeveloperRules rules=new DeveloperRules();
        public readonly Dictionary<string,DeveloperActor> actors=new Dictionary<string,DeveloperActor>();
        public readonly Dictionary<string,DeveloperTeam> teams=new Dictionary<string,DeveloperTeam>();
        public readonly Dictionary<string,DeveloperBuilding> buildings=new Dictionary<string,DeveloperBuilding>();
        public readonly Dictionary<string,DeveloperRune> runes=new Dictionary<string,DeveloperRune>();
        public readonly Dictionary<string,DeveloperClip> clips=new Dictionary<string,DeveloperClip>();
        public readonly Queue<Action> commands=new Queue<Action>();public readonly Queue<string> messages=new Queue<string>();
        public float time,runeAngle;public int tick,seed=2026,direction=1;public bool running,ended,customRules;public string winner="",controlled="player";
        public DeveloperClip recording;public string recordingActor;float recordStart;int economyMinute;
        public float RecordingElapsed=>recording==null?0:time-recordStart;
        public void RestoreRecording(DeveloperClip clip,string actor,float elapsed){recording=clip!=null&&!string.IsNullOrEmpty(clip.name)&&actors.ContainsKey(actor??"")?clip:null;recordingActor=recording!=null?actor:null;recordStart=time-elapsed;}
        readonly Dictionary<string,float> lastArmorHit=new Dictionary<string,float>();
        public event Action<DeveloperActor> Added; public event Action<DeveloperActor> Removed;public event Action ResetRequested;
        public DeveloperWorld(){ResetState();}
        public void Log(string message){messages.Enqueue($"[{time:0.00}] {message}");while(messages.Count>100)messages.Dequeue();}
        public void ResetState()
        {
            time=0;tick=0;runeAngle=0;running=false;ended=false;winner="";economyMinute=0;recording=null;lastArmorHit.Clear();
            customRules=JsonUtility.ToJson(rules)!=JsonUtility.ToJson(new DeveloperRules());
            teams.Clear();buildings.Clear();runes.Clear();foreach(string team in new[]{"red","blue"}){teams[team]=new DeveloperTeam(team);buildings[team+"_base"]=new DeveloperBuilding(team,"base");buildings[team+"_outpost"]=new DeveloperBuilding(team,"outpost");runes[team]=new DeveloperRune(team,seed+(team=="red"?0:1));}
            actors.Clear();controlled="player";direction=new System.Random(seed).Next(2)==0?1:-1;
        }
        public DeveloperActor Add(string id,string team,Vector3 p,string model="sweeper")
        {
            if(string.IsNullOrWhiteSpace(id)||id.Contains(" ")||actors.ContainsKey(id))throw new ArgumentException("车辆名称重复、为空或含空格");if(actors.Count>=17)throw new ArgumentException("最多16靶车 +1主车");if(!teams.ContainsKey(team))throw new ArgumentException("阵营必须为 red / blue");
            if(model!="sweeper"&&model!="jiageng")throw new ArgumentException("车型 sweeper / jiageng");
            var actor=new DeveloperActor(id,team,p){model=model,trainingImmortal=id!="player",spinRate=rules.spinSpeed,capEnergy=rules.capacitance*rules.capMaxVoltage*rules.capMaxVoltage*.5f,capVoltage=rules.capMaxVoltage};actors.Add(id,actor);Added?.Invoke(actor);return actor;
        }
        public void Remove(string id){if(id=="player")throw new ArgumentException("保留主玩家车");if(!actors.TryGetValue(id,out var a))throw new ArgumentException("车辆不存在");Removed?.Invoke(a);actors.Remove(id);if(controlled==id)controlled="player";}
        public void ApplyLevel(DeveloperActor a,int level,bool forced=false)
        {
            float old=a.maxHp;bool alive=a.Alive;a.level=Mathf.Clamp(level,1,10);if(forced){a.levelOverride=true;a.xp=DeveloperTables.Xp[a.level-1];}
            int i=a.level-1;bool health=a.chassis=="health",cool=a.shooter=="cooling";
            a.maxHp=(health?DeveloperTables.Health:DeveloperTables.PowerHealth)[i];a.powerLimit=(health?DeveloperTables.HealthPower:DeveloperTables.Power)[i];
            a.heatLimit=(cool?DeveloperTables.CoolHeat:DeveloperTables.BurstHeat)[i];a.cooling=(cool?DeveloperTables.CoolRate:DeveloperTables.BurstRate)[i];
            a.hp=alive?Mathf.Clamp(a.hp+Mathf.Max(0,a.maxHp-old),0,a.maxHp):0;if(a.heat>=a.heatLimit)a.heatLocked=true;
        }
        public void Xp(DeveloperActor a,int amount)
        {
            int cap=a.levelOverride?10:teams[a.team].levelCap;if(a.level>=cap)return;
            if(time<runes[a.team].smallBuffUntil){int extra=Mathf.Min(amount,1200-Mathf.RoundToInt(teams[a.team].smallXp));amount+=extra;teams[a.team].smallXp+=extra;}
            a.xp=Mathf.Min(a.xp+Mathf.Max(0,amount),DeveloperTables.Xp[cap-1]);
            int level=a.level;while(level<cap && a.xp>=DeveloperTables.Xp[level])level++;if(level!=a.level){ApplyLevel(a,level);Log(a.id+" 升至 "+level+" 级");}
        }
        public bool Fire(DeveloperActor a)
        {
            if(!a.CanFire(time,rules))return false;
            a.nextShot=time+Mathf.Max(.02f,rules.shotPeriod);a.shots++;if(!rules.infiniteAmmo){a.ammo--;if(a.fortressReserve>0)a.fortressReserve--;else a.allowedAmmo--;}
            if(!rules.unlimitedHeat){a.heat+=rules.heatPerShot;if(a.heat>=a.heatLimit)a.heatLocked=true;if(a.heat>=a.heatLimit+100)a.permanentFireLock=true;}
            if(rules.bulletSpeed>25){float excess=rules.bulletSpeed-25;if(excess>=10)a.permanentFireLock=true;else a.fireLockUntil=time+(excess<=5?15:20);}
            Xp(a,1);return true;
        }
        public int Hit(DeveloperHit hit)
        {
            if(hit.normalSpeed<=rules.armorMinVelocity || !actors.TryGetValue(hit.shooter,out var shooter))return 0;
            if(buildings.TryGetValue(hit.target,out var covered)&&covered.role=="base"&&hit.panel>=3&&covered.armorOpen<.99f)return 0;
            string key=hit.target+":"+hit.panel;if(lastArmorHit.TryGetValue(key,out float t)&&time-t<rules.armorInterval)return 0;lastArmorHit[key]=time;
            float raw=rules.damage17,defense=0,vulnerability=0,shield=0,hp=0;string team;DeveloperActor actor=null;DeveloperBuilding building=null;
            if(actors.TryGetValue(hit.target,out actor))
            {
                team=actor.team;if(!actor.Alive || time<actor.invulnerableUntil)return 0;hp=actor.hp;shield=actor.shield;defense=Mathf.Max(actor.zoneDefense,Mathf.Max(actor.defense,Mathf.Max(TeamDefense(team),time<actor.terrainBuffUntil?actor.defenseBuff:0)));vulnerability=Mathf.Max(actor.zoneVulnerability,actor.vulnerability);
            }
            else if(buildings.TryGetValue(hit.target,out building))
            {
                team=building.team;if(building.hp<=0)return 0;if(building.role=="base" && buildings[team+"_outpost"].hp>0)return 0;
                raw=building.role=="base" && hit.panel==0?rules.baseFrontDamage:rules.damage17;hp=building.hp;shield=building.shield;defense=TeamDefense(team);
            }
            else return 0;
            float critical=building!=null && Mathf.Abs(hit.local.x)<=.005f && Mathf.Abs(hit.local.y)<=.005f?1.5f:1;
            float multiplier=Mathf.Max(critical,Mathf.Max(shooter.attack,TeamAttack(shooter.team)));int damage=Mathf.Max(0,Mathf.RoundToInt(raw*multiplier*(1-Mathf.Clamp01(defense)+Mathf.Max(0,vulnerability))));
            float actual=Mathf.Min(Mathf.Max(0,hp-(actor!=null&&actor.trainingImmortal?1:0))+shield,damage),shieldLoss=Mathf.Min(shield,actual);hp-=actual-shieldLoss;
            if(actor!=null){float oldHp=actor.hp;actor.trainingDamage+=damage;actor.hp=Mathf.Max(actor.trainingImmortal?1:0,hp);actor.actualHpDamage+=oldHp-actor.hp;actor.shield-=shieldLoss;actor.lastDamage=time;if(actor.hp==0&&actor.deadAt<0){actor.deadAt=time;actor.respawnPoints=0;actor.heat=0;actor.buffer=60;shooter.kills++;Xp(shooter,Mathf.RoundToInt(50*actor.level*(1+.2f*Mathf.Max(0,actor.level-shooter.level))));}}
            else{float old=building.hp;building.hp=Mathf.Max(0,hp);building.shield-=shieldLoss;building.damage+=actual;if(building.role=="base")building.rebuildChances+=Mathf.FloorToInt((5000-building.hp)/1000)-Mathf.FloorToInt((5000-old)/1000);if(building.hp==0&&!building.destroyedBefore){building.destroyedBefore=true;building.deadAt=time;building.stopped=true;building.angle=0;building.rotationStopAt=time;}}
            teams[shooter.team].damage+=actual;Xp(shooter,actor!=null?Mathf.RoundToInt(actual*4):building.role=="outpost"?Mathf.RoundToInt(actual*2):Mathf.CeilToInt(actual/2));
            Log($"{hit.shooter} → {hit.target} 装甲 {hit.panel} -{(actor!=null&&actor.trainingImmortal?damage:actual):0} {(critical>1?"中心暴击":"")}");return Mathf.RoundToInt(actor!=null&&actor.trainingImmortal?damage:actual);
        }
        public void Tick()
        {
            // Console mutations are applied even when the simulation is paused.
            DrainCommands();
            if(!running || ended)return;tick++;time=tick/120f;
            foreach(var b in buildings.Values)
            {
                b.armorOpen=Mathf.MoveTowards(b.armorOpen,b.armorTarget,Step);
                if(b.role!="outpost")continue;
                if(!b.stopped && (b.hp<=0 || time>=180 || buildings[(b.team=="red"?"blue":"red")+"_base"].armorTarget>0)){b.stopped=true;b.angle=0;b.rotationStopAt=time;}
                if(!b.stopped)b.angle+=direction*144*Mathf.Clamp01(time/5)*Step;
            }
            foreach(var r in runes.Values)r.Tick(this);
            var source=RuneMotionSource();float runeSpeed=0;if(source!=null){float elapsed=time-source.started;runeSpeed=source.Large(time)?(source.a*Mathf.Sin(source.omega*elapsed)+2.09f-source.a)*Mathf.Rad2Deg:60;}
            runeAngle+=direction*runeSpeed*Step;
            if(tick%12==0)RuleTick();
            if(time>=420 || buildings["red_base"].hp<=0 || buildings["blue_base"].hp<=0)End();
        }
        void RuleTick()
        {
            int minute=Mathf.FloorToInt(Mathf.Max(0,time-1)/60);if(minute>economyMinute){for(int k=economyMinute+1;k<=Mathf.Min(minute,6);k++)foreach(var t in teams.Values){int gain=k==6?150:50;t.coins+=gain;t.earnedCoins+=gain;}economyMinute=minute;}
            foreach(var team in teams.Values)if(team.buffUntil>0&&time>=team.buffUntil){team.attack=1;team.defense=0;team.cooling=1;team.buffUntil=0;}
            foreach(var a in actors.Values)
            {
                if(a.Alive){a.heat=Mathf.Max(0,a.heat-Mathf.Max(a.cooling*rules.coolingMultiplier*Mathf.Max(a.coolingBuff,TeamCooling(a.team)),a.cooling+ a.zoneCooling)*.1f);if(a.heat<=0)a.heatLocked=false;
                    a.buffer=Mathf.Clamp(a.buffer-(a.chassisInputPower-PowerBudget(a))*.1f,0,60);if(a.buffer<=0&&time>=a.powerOffUntil){a.powerOffUntil=time+5;if(!rules.vehiclePhysics)a.velocity=Vector3.zero;a.buffer=60;Log(a.id+" 底盘断电5秒");}}
                else if(a.deadAt>=0){a.respawnPoints+=.1f;float needed=Mathf.Round(10+a.deadAt/10+20*a.instantRevives);if(a.respawnPoints>=needed){a.hp=a.maxHp*.1f;a.deadAt=-1;a.revivedAt=time;a.invulnerableUntil=time+30;a.weakUntil=float.MaxValue;Log(a.id+" 自然复活");}}
            }
        }
        public void DrainCommands(){while(commands.Count>0){try{commands.Dequeue()();}catch(Exception ex){Log("命令失败："+ex.Message);}}}
        public DeveloperRune RuneMotionSource()
        {
            DeveloperRune source=null;foreach(var rune in runes.Values)if(rune.activating){if(rune.Large(time))return rune;source=rune;}return source;
        }
        public float TeamDefense(string team){var r=runes[team];return Mathf.Max(teams[team].defense,Mathf.Max(time<r.smallBuffUntil?.25f:0,time<r.buffUntil?r.defense:0));}
        public float TeamAttack(string team){var r=runes[team];return Mathf.Max(teams[team].attack,time<r.buffUntil?r.attack:1);}
        public float TeamCooling(string team){var r=runes[team];return Mathf.Max(teams[team].cooling,time<r.buffUntil?r.cooling:1);}
        public float PowerBudget(DeveloperActor actor){float p=actor.chassisEnergy<=0?35:actor.powerLimit*(actor.chassisEnergy>=25000?1.25f:1);p=Mathf.Min(200,p);if(time<actor.revivePowerUntil)p*=2;return p;}
        public void End()
        {
            running=false;ended=true;int compare=buildings["red_base"].hp.CompareTo(buildings["blue_base"].hp);var redPost=buildings["red_outpost"];var bluePost=buildings["blue_outpost"];
            if(compare==0){if(redPost.destroyedBefore!=bluePost.destroyedBefore)compare=redPost.destroyedBefore?-1:1;else if(!redPost.destroyedBefore)compare=redPost.hp.CompareTo(bluePost.hp);}
            if(compare==0)compare=teams["red"].damage.CompareTo(teams["blue"].damage);
            if(compare==0){float redHp=0,blueHp=0;foreach(var a in actors.Values){if(a.team=="red")redHp+=a.hp;else blueHp+=a.hp;}compare=redHp.CompareTo(blueHp);}
            winner=compare==0?"平局":compare>0?"红方":"蓝方";Log("比赛结束："+winner);
        }
        public void BeginRecording(string name){if(recording!=null)throw new ArgumentException("请先结束当前录制");if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("片段名不能为空");var a=actors[controlled];recording=new DeveloperClip {name=name,origin=a.position,initialYaw=a.yaw,initialState=JsonUtility.ToJson(a),field="rmuc_2026_final",parameters=JsonUtility.ToJson(rules),model=a.model};recordingActor=controlled;recordStart=time;Log("开始录制 "+name);}
        public void Record(DeveloperActor a,DeveloperInput input){if(recording!=null&&a.id==recordingActor)recording.frames.Add(new DeveloperFrame {t=time-recordStart,input=input.Copy(),actual=a.position,spinRate=a.spinRate,requestedSpeed=a.requestedSpeed});}
        public void FinishRecording(){if(recording==null)throw new ArgumentException("当前没有录制");recording.duration=Mathf.Max(Step,time-recordStart);clips[recording.name]=recording;Log("已保存片段 "+recording.name+" · "+recording.frames.Count+" 步");recording=null;}
        public DeveloperInput ClipInput(DeveloperActor actor)
        {
            if(time<actor.clipWaitUntil)return new DeveloperInput {yaw=actor.gimbalYaw,pitch=actor.pitch};
            string[] names=actor.clip.Split(',');if(names.Length==0||!clips.TryGetValue(names[actor.replayIndex%names.Length],out var clip)||clip.frames.Count==0)return new DeveloperInput();
            if(actor.clipTime>=clip.duration){actor.clipTime-=clip.duration;actor.replayIndex++;if(actor.replayIndex>=names.Length){if(!actor.pathLoop||actor.loops==0){actor.motion="still";return new DeveloperInput {yaw=actor.gimbalYaw,pitch=actor.pitch};}actor.replayIndex=0;if(actor.loops>0)actor.loops--;}actor.clipWaitUntil=time+actor.clipDwell;return ClipInput(actor);}
            int i=Mathf.Min(clip.frames.Count-1,Mathf.RoundToInt(actor.clipTime/Step));actor.clipTime+=Step;var input=clip.frames[i].input.Copy();
            if(clip.schema>=2){actor.spinRate=clip.frames[i].spinRate;actor.requestedSpeed=clip.frames[i].requestedSpeed;}input.yaw+=actor.clipYawOffset;if(!input.cameraRelative){var direction=Quaternion.Euler(0,actor.clipYawOffset,0)*new Vector3(input.x,0,input.z);input.x=direction.x;input.z=direction.z;}
            if(!actor.clipFire)input.fire=false;return input;
        }
        public void SetTime(float value){if(recording!=null){FinishRecording();Log("时间调整前结束当前录制，保留片段");}time=Mathf.Clamp(value,0,420);tick=Mathf.RoundToInt(time*120);economyMinute=Mathf.FloorToInt(Mathf.Max(0,time-1)/60);foreach(var r in runes.Values)r.ResetTimeline(time);lastArmorHit.Clear();Log("时间已修改；机关机会按新时间重建，经济/血量不回滚");}
        public object Find(string name){if(name=="rules")return rules;if(name=="game")return this;if(actors.TryGetValue(name,out var a))return a;if(teams.TryGetValue(name,out var t))return t;if(buildings.TryGetValue(name,out var b))return b;if(name.StartsWith("rune_")&&runes.TryGetValue(name.Substring(5),out var r))return r;throw new ArgumentException("未知对象："+name);}
        public void Set(string objectName,string member,string value)
        {
            object obj=Find(objectName);var field=obj.GetType().GetField(member,BindingFlags.Public|BindingFlags.Instance);if(field==null)throw new ArgumentException("未知字段 "+member);
            if(field.IsInitOnly || obj==this || member=="id")throw new ArgumentException("此字段使用专用命令修改");object parsed=Parse(field.FieldType,value);
            if(parsed is float number && (!float.IsFinite(number)||Mathf.Abs(number)>1000000))throw new ArgumentException("数值无效");
            Validate(obj,member,parsed);object old=field.GetValue(obj);field.SetValue(obj,parsed);
            if(obj==rules || obj is DeveloperActor && (member=="maxHp"||member=="powerLimit"||member=="heatLimit"||member=="cooling"))customRules=true;
            if(obj is DeveloperRune&&member=="mode")customRules=true;
            if(obj==rules&&member=="spinSpeed")foreach(var a in actors.Values)if(Mathf.Approximately(a.spinRate,(float)old))a.spinRate=(float)parsed;
            if(obj is DeveloperActor actor){if(member=="level")ApplyLevel(actor,(int)parsed,true);else if(member=="xp"){int level=1;while(level<10&&actor.xp>=DeveloperTables.Xp[level])level++;ApplyLevel(actor,Mathf.Min(level,actor.levelOverride?10:teams[actor.team].levelCap));}else if(member=="chassis"||member=="shooter")ApplyLevel(actor,actor.level);else if(member=="maxHp")actor.hp=Mathf.Min(actor.hp,actor.maxHp);else if(member=="hp"){actor.hp=Mathf.Clamp(actor.hp,actor.trainingImmortal?1:0,actor.maxHp);if(actor.hp==0){actor.deadAt=time;actor.heat=0;actor.buffer=60;}else actor.deadAt=-1;}else if(member=="heat")actor.heatLocked=actor.heat>=actor.heatLimit;}
            if(obj is DeveloperTeam team){if(member=="core")team.levelCap=team.core>=3?10:team.core==2?7:5;}
            if(obj is DeveloperBuilding building && member=="hp" && building.hp==0){building.destroyedBefore=true;building.stopped=true;building.angle=0;building.deadAt=time;}
            if(obj is DeveloperActor energyActor&&(member=="capEnergy"||member=="capVoltage")){if(member=="capVoltage")energyActor.capEnergy=rules.capacitance*energyActor.capVoltage*energyActor.capVoltage*.5f;else energyActor.capVoltage=Mathf.Sqrt(2*energyActor.capEnergy/rules.capacitance);}
            Log(objectName+"."+member+" "+old+" → "+parsed+(obj==rules?" · 自定义规则":""));
        }
        public static DeveloperRules ReadRules(string json){var rules=new DeveloperRules();JsonUtility.FromJsonOverwrite(json,rules);if(!json.Contains("\"wheelStaticContact\""))rules.wheelStaticContact=false;foreach(var f in typeof(DeveloperRules).GetFields())Validate(rules,f.Name,f.GetValue(rules));return rules;}
        public static DeveloperActor ReadActor(string json){var actor=new DeveloperActor("","red",Vector3.zero);JsonUtility.FromJsonOverwrite(json,actor);if(!json.Contains("\"trainingImmortal\""))actor.trainingImmortal=actor.id!="player";return actor;}
        public static object Parse(Type type,string value)
        {
            if(type==typeof(string))return value;if(type==typeof(bool))return value=="1"||value=="on"?true:value=="0"||value=="off"?false:bool.Parse(value);
            if(type==typeof(float))return float.Parse(value,CultureInfo.InvariantCulture);if(type==typeof(int))return int.Parse(value,CultureInfo.InvariantCulture);
            if(type==typeof(Vector3)){var c=value.Split(',');if(c.Length!=3)throw new ArgumentException("向量格式 x,y,z");return new Vector3(float.Parse(c[0],CultureInfo.InvariantCulture),float.Parse(c[1],CultureInfo.InvariantCulture),float.Parse(c[2],CultureInfo.InvariantCulture));}
            throw new ArgumentException("使用 path / armor / clip 等专用命令编辑集合");
        }
        public static void Validate(object obj,string key,object parsed)
        {
            if((key=="wheelLateralPositionGain"||key=="wheelArmLateralPositionGain")&&parsed is float lateralGain&&(!float.IsFinite(lateralGain)||lateralGain<0||lateralGain>400))throw new ArgumentException("横向位置反馈增益范围0..400");
            if(key=="wheelArmForwardWeightMin"&&parsed is float forwardWeight&&(!float.IsFinite(forwardWeight)||forwardWeight<.05f||forwardWeight>1))throw new ArgumentException("坡道方向保护前进权重范围0.05..1");
            if(key=="physicsSubsteps"&&parsed is int substeps&&(substeps<1||substeps>4))throw new ArgumentException("物理子步范围1..4");
            if(key=="physicsComOffset"&&parsed is Vector3 offset&&offset.magnitude>.5f)throw new ArgumentException("质心偏移不得超过0.5m");
            if(parsed is float parameter)
            {
                if((key=="wheelApproachSpeed"||key=="wheelAssistApproachSpeed")&&(parameter<=0||parameter>3)||key=="wheelClimbExitSpeed"&&(parameter<=0||parameter>6)||key=="wheelClimbExitDelay"&&(parameter<0||parameter>3)||key=="wheelLateralPriority"&&(parameter<0||parameter>.95f))throw new ArgumentException("越顶速度0..6、延时0..3、横向牵引优先比例0..0.95");
                if((key=="wheelArmTrimMax"||key=="wheelArmTrimRate")&&(parameter<0||parameter>.03f))throw new ArgumentException("轮臂支撑修正与速率范围0..0.03");
                if((key=="wheelStaticGrip"&&(parameter<=0||parameter>3))||(key=="wheelRollResistance"&&(parameter<0||parameter>.15f))||(key=="wheelBrakeDeceleration"&&(parameter<=0||parameter>15))||(key=="wheelBrakePower"&&(parameter<=0||parameter>2000))||(key=="wheelHoldSpeed"||key=="wheelHoldRelease")&&(parameter<=0||parameter>.3f)||(key=="wheelHoldDelay"&&(parameter<0||parameter>2)))throw new ArgumentException("轮地制动/保持参数超出有效范围");
                if((key=="wheelGrip"||key=="wheelSlip"||key=="motorTorque"||key=="wheelSpeedGain"||key=="wheelMaxOmega"||key=="suspensionScale"||key=="suspensionDamping"||key=="armSpring"||key=="armDamper"||key=="armTorque"||key=="followGain"||key=="followMaxSpeed")&&parameter<=0)throw new ArgumentException("物理参数必须大于0");
                if((key=="followDeadzone"||key=="followDamping"||key=="motorCopperLoss"||key=="wheelIntegralGain"||key=="armGroundClearance"||key=="armCrestClearance"||key=="chassisVelocityGain"||key=="wheelBearingTorque")&&parameter<0)throw new ArgumentException("物理参数不能为负");
                if((key=="followDriveFraction"&&(parameter<.1f||parameter>1))||(key=="wheelBearingTorque"&&parameter>.2f))throw new ArgumentException("移动跟随比例范围0.1..1，轮轴阻力范围0..0.2Nm");
                if(key=="chassisVelocityGain"&&parameter>2)throw new ArgumentException("底盘速度反馈范围0..2");
                if(key=="armGroundClearance"&&parameter>.025f||key=="armCrestClearance"&&parameter>.06f)throw new ArgumentException("基础净空范围0..0.025m，凸坡顶净空范围0..0.06m");
                if((key=="wheelIntegralGain"&&parameter>10)||(key=="wheelGrip"&&parameter>3)||(key=="motorTorque"&&parameter>20)||(key=="wheelMaxOmega"&&parameter>500)||(key=="wheelSlip"&&parameter>2000)||(key=="armSpring"&&parameter>5000)||(key=="armTorque"&&parameter>100)||(key=="suspensionScale"&&parameter>10)||(key=="suspensionDamping"&&parameter>5)||(key=="followMaxSpeed"&&parameter>720)||(key=="followDeadzone"&&parameter>30)||(key=="mass"&&(parameter<1||parameter>100)))throw new ArgumentException("超出车辆物理稳定参数范围");
            }
            if((key=="runeAimMaxMiss"||key=="runeAimMaxSigma")&&parsed is float accuracy&&(accuracy<=0||accuracy>.014f))throw new ArgumentException("机关精度参数范围(0,0.014]米");
            if((key=="aimRoiWidth"||key=="aimRoiHeight")&&parsed is float roi&&(roi<.05f||roi>1))throw new ArgumentException("ROI宽高比例范围0.05..1");
            if(obj is DeveloperRune&&key=="mode"&&(string)parsed!="auto"&&(string)parsed!="small"&&(string)parsed!="large")throw new ArgumentException("机关模式 auto / small / large");
            if(parsed is float f){if(!float.IsFinite(f)||Mathf.Abs(f)>1000000)throw new ArgumentException("数值必须有限且在±1000000以内");if((key=="maxHp"||key=="heatLimit"||key=="bulletSpeed"||key=="shotPeriod"||key=="measurementSigma"||key=="observationHz"||key=="speedPower"||key=="efficiency"||key=="mass")&&f<=0)throw new ArgumentException("此参数必须大于0");if((key=="gravity"||key=="hp"||key=="shield"||key=="heat"||key=="buffer"||key=="requestedSpeed"||key=="armorTarget"||key=="armorOpen"||key=="observationDelay"||key=="observationNoise"||key=="powerLimit"||key=="cooling"||key=="damage17"||key=="baseFrontDamage"||key=="idlePower"||key=="spinPower"||key=="mechanicalSpeed"||key=="acceleration"||key=="angularAcceleration"||key=="gimbalSpeed"||key=="coolingMultiplier"||key=="heatPerShot"||key=="armorInterval"||key=="armorMinVelocity"||key=="processAcceleration"||key=="processVertical"||key=="pathDwell")&&f<0)throw new ArgumentException("此参数不能为负");if((key=="armorTarget"||key=="armorOpen"||key=="observationDrop"||key=="slowRatio"||key=="armorRestitution"||key=="groundRestitution")&&(f<0||f>1))throw new ArgumentException("此参数范围0..1");}
            if(parsed is Vector3 p&&(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)))throw new ArgumentException("坐标必须有限");
            if(parsed is int n && ((key=="level"&&(n<1||n>10))||(key=="levelCap"&&(n<1||n>10))||((key=="coins"||key=="ammo"||key=="allowedAmmo"||key=="xp")&&n<0)))throw new ArgumentException("整数超出有效范围");
            if(key=="model"&&(string)parsed!="sweeper"&&(string)parsed!="jiageng")throw new ArgumentException("车型 sweeper / jiageng");
            if(parsed is float energy){if((key=="capacitance"||key=="capMaxVoltage"||key=="capEfficiency"||key=="capInterfaceCurrent")&&energy<=0)throw new ArgumentException("电容参数必须大于0");if((key.StartsWith("cap",StringComparison.Ordinal)||key=="wirelessPower"||key=="chassisEnergy"||key=="burstMechanicalSpeed"||key=="burstAcceleration")&&energy<0)throw new ArgumentException("能源参数不能为负");if((key=="capEfficiency"||key=="ordinaryPowerFraction")&&(energy<=0||energy>1))throw new ArgumentException("效率及普通功率比例范围(0,1]");}
            if(key=="team"&& (string)parsed!="red" && (string)parsed!="blue")throw new ArgumentException("阵营 red / blue");
            if(obj is DeveloperRules capRules&&parsed is float cv){if(key=="capMaxVoltage"&&cv<=capRules.capRecoverVoltage||key=="capRecoverVoltage"&&(cv<=capRules.capMinVoltage||cv>=capRules.capMaxVoltage)||key=="capMinVoltage"&&cv>=capRules.capRecoverVoltage)throw new ArgumentException("电压须满足 0 ≤ 欠压 < 恢复 < 满电电压");}
            if(key=="chassis"&&(string)parsed!="health"&&(string)parsed!="power")throw new ArgumentException("底盘 health / power");if(key=="shooter"&&(string)parsed!="cooling"&&(string)parsed!="burst")throw new ArgumentException("发射机构 cooling / burst");
        }
    }
    public static class DeveloperTables
    {
        public static readonly int[] Xp={0,550,1100,1650,2200,2750,3300,3850,4400,5000};
        public static readonly int[] Health={200,225,250,275,300,325,350,375,400,400},HealthPower={45,50,55,60,65,70,75,80,90,100};
        public static readonly int[] PowerHealth={150,175,200,225,250,275,300,325,350,400},Power={60,65,70,75,80,85,90,95,100,100};
        public static readonly int[] CoolHeat={40,48,56,64,72,80,88,96,114,120},CoolRate={12,14,16,18,20,22,24,26,28,30};
        public static readonly int[] BurstHeat={170,180,190,200,210,220,230,240,250,260},BurstRate={5,7,9,11,12,13,14,16,18,20};
        public static float Speed(DeveloperActor a,DeveloperRules r,float omega,float budget=-1)=>Mathf.Min(r.mechanicalSpeed,Mathf.Sqrt(Mathf.Max(0,(budget<0?a.powerLimit:budget)-r.idlePower-r.spinPower*omega*omega)/Mathf.Max(.01f,r.speedPower)));
    }
}
