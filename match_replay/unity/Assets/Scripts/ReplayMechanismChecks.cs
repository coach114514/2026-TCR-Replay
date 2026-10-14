using System;
using UnityEngine;

namespace TcrReplay
{
    public static class ReplayMechanismChecks
    {
        static void Require(bool condition,string message){if(!condition)throw new Exception("机构规则验证失败："+message);}
        public static void Geometry(MechanismConfig config)
        {
            var rune=config?.rune;var partition=rune?.shellPartition;
            Require(config!=null && config.geometrySplitVersion>=4 && partition!=null,"机关仍使用R标随动的旧配置");
            Require(rune.hubMotion=="fixed_upright" && rune.hubSolidGroups!=null && rune.hubSolidGroups.Length==36 && Array.IndexOf(rune.hubSolidGroups,30)>=0 && Array.IndexOf(rune.hubSolidGroups,31)>=0 && Array.IndexOf(rune.hubSolidGroups,49)>=0 && Array.IndexOf(rune.hubSolidGroups,50)>=0 && Array.IndexOf(rune.hubSolidGroups,32)<0 && Array.IndexOf(rune.hubSolidGroups,51)<0,"R标及中心盖板缺少固定正向配置");
            Require(rune.solidGroups!=null && rune.solidGroups.Length==100 && Array.TrueForAll(rune.solidGroups,s=>s>=58 && s<158),"固定R标实体仍被划入活动灯臂");
            Require(config.outpost.axis[0]==0 && config.outpost.axis[1]==0 && config.outpost.axis[2]==1,"前哨源CAD竖直轴定义错误");
            var post=config.outpost.shellPartition;
            Require(post!=null && post.freeShellCount==393 && post.movingFreeShellCount==177 && post.unassignedTriangles==0 && post.movingShellGroups?.Length==177 && post.fixedShellGroups!=null,"前哨装甲外壳没有完整划入转子");
            var postOwners=new bool[393];
            foreach(int shell in post.movingShellGroups){Require(shell>=91 && shell<=267 && !postOwners[shell],"前哨活动外壳归属错误");postOwners[shell]=true;}
            foreach(int shell in post.fixedShellGroups){Require(shell>=0 && shell<393 && !postOwners[shell],"前哨固定外壳重复/无效");postOwners[shell]=true;}
            Require(Array.TrueForAll(postOwners,x=>x),"前哨外壳有遗漏");
            Require(partition.freeShellCount==1063 && partition.movingFreeShellCount==950 && partition.unassignedTriangles==0,"机关外壳归属不完整");
            Require(rune.arms!=null && rune.arms.Length==10 && partition.fixedShellGroups!=null,"机关缺少固定/活动外壳分类");
            var owners=new bool[partition.freeShellCount];var armIds=new System.Collections.Generic.HashSet<string>();int moving=0;
            foreach(var arm in rune.arms)
            {
                Require((arm.team=="red" || arm.team=="blue") && arm.index>=0 && arm.index<5 && armIds.Add(arm.team+arm.index),"机关灯臂身份重复");
                Require(arm.solidGroups!=null && arm.solidGroups.Length==10 && arm.shellGroups!=null && arm.shellGroups.Length==95,"机关灯臂缺少完整实体/灰色外壳");
                foreach(int shell in arm.shellGroups)
                {Require(shell>=0 && shell<owners.Length && !owners[shell],"活动外壳重复分配");owners[shell]=true;moving++;}
            }
            foreach(int shell in partition.fixedShellGroups)
            {Require(shell>=0 && shell<owners.Length && !owners[shell],"旋转外壳残留在固定网格分类中");owners[shell]=true;}
            Require(moving==950 && Array.TrueForAll(owners,x=>x),"机关外壳分类存在遗漏");
        }
        public static void Rules()
        {
            var data=new MatchData {gameId="rule-fixture",duration=420,entities=new[]{
                new EntityData {id="red_outpost",role="outpost",team="red",samples=new[]{
                    new Sample {t=1,hp=1500,hasHp=true},new Sample {t=121,hp=0,hasHp=true},new Sample {t=190,hp=500,hasHp=true}}},
                new EntityData {id="blue_outpost",role="outpost",team="blue",samples=new[]{new Sample {t=1,hp=1500,hasHp=true}}}
            },mechanisms=new MechanismData {baseArmor=Array.Empty<BaseArmorRecord>()}};
            var timeline=new ReplayMechanismTimeline(data);
            Require(timeline.BaseArmor("red",1).pose==BaseArmorPose.Unknown,"缺少护甲记录没有保持未知");
            Require(!timeline.GuideLamp("red",true,1,out bool known) && known,"前哨存活时基地亮灯");
            Require(timeline.GuideLamp("red",true,121,out known) && known,"前哨归零后基地没有亮灯");
            Require(timeline.GuideLamp("red",true,189,out known) && known && !timeline.GuideLamp("red",false,189,out known),"已确认前哨死亡因日志间隔被遗忘");
            Require(!timeline.GuideLamp("red",true,190,out known) && known,"重建后引导灯没有切回");
            timeline.GuideLamp("red",true,195,out known);Require(!known,"过期状态没有标记未知");
            Require(timeline.OutpostStop("red")==121 && timeline.OutpostStop("blue")==180,"首次被毁或180秒停转条件错误");
            Require(Mathf.Abs(timeline.OutpostAngle("red",5))==360 && timeline.OutpostAngle("red",5)==timeline.OutpostAngle("blue",5),"启动加速或两方旋向不一致");
            Require(Mathf.Abs(timeline.OutpostAngle("red",125)-timeline.OutpostAngle("red",240))<.001f,"重建后前哨重新转动");
            data.mechanisms.baseArmor=new[]{new BaseArmorRecord {team="blue",state="open",t=115,source="fixture"}};
            Require(timeline.OutpostStop("red")==115,"接口没有接入对方开甲停转条件");
            data.mechanisms.baseArmor=new[]{new BaseArmorRecord {team="red",state="invincible",t=0}};
            Require(timeline.BaseArmor("red",1).pose==BaseArmorPose.Unknown,"无敌枚举被误当闭合");
            data.mechanisms.phases=new[]{new MatchPhaseRecord {state="self_check",t=0,ends=15}};
            Require(timeline.GuideLamp("red",true,0,out known) && known && timeline.GuideLamp("red",false,0,out known),"可信自检阶段没有双灯点亮");
            data.mechanisms.runeSuccess=new[]{new RuneSuccessRecord {team="red",kind="small",t=100,ends=145,arms=5},new RuneSuccessRecord {team="blue",kind="large",t=200,ends=-1,arms=-1}};
            Require(timeline.Rune("red",144).state=="activated" && timeline.Rune("red",145).state=="inactive","小机关45秒有效期错误");
            Require(timeline.Rune("blue",200).state=="activated" && timeline.Rune("blue",203).state=="unknown","未知大机关有效期被补造");
            data.mechanisms.runeSuccess[1].arms=7;
            Require(timeline.Rune("blue",239).state=="activated" && timeline.Rune("blue",240).state=="inactive","已知七灯臂没有采用40秒有效期");
            foreach(int arms in new[]{5,6,7,8,9,10})
            {
                data.mechanisms.runeSuccess[1].arms=arms;
                Require(timeline.Rune("blue",200).ends==200+new[]{0,0,0,0,0,30,35,40,45,50,60}[arms],"大机关有效期不符合表5-18");
            }
            data.mechanisms.runeSuccess[0].t=170;data.mechanisms.runeSuccess[0].ends=215;
            Require(timeline.Rune("red",200).state=="activated" && timeline.Rune("red",200).kind=="small" && timeline.Rune("red",215).state=="inactive","小机关45秒增益被3分钟边界截断");
            Require(timeline.RuneMechanism("red",200).kind=="large" && timeline.RuneMechanism("red",200).state=="unknown","残留小机关增益被当作当前大机关已激活灯效");
            Require(timeline.Rune("red",220).state=="unknown","没有完整状态流时过期后的状态仍被确认");
            data.mechanisms.runeProgress=new[]{new RuneProgressRecord {team="red",kind="large",t=230,arms=0}};
            Require(timeline.Rune("red",230).state=="unknown","进度零值被误当正在激活");
            data.mechanisms.runeStates=new[]{new RuneStateRecord {team="blue",kind="large",state="failed",t=250,ends=251,evidence="fixture"}};
            Require(timeline.Rune("blue",250).state=="failed" && timeline.Rune("red",250).state!="failed","明确失败接口发生红蓝串线");
            RuneRotation();
            Debug.Log("REPLAY_MECHANISM_RULES_OK lamps=alive/dead/rebuild/check phase=unknown-aware outpost=5s/144deg/180s/no-restart armor=interface-only rune=45s/known-large-expiry/shared-rotor/180s-stage/20s-window");
        }
        static MatchData RuneFixture() => new MatchData {gameId="rune-motion-fixture",duration=420,entities=Array.Empty<EntityData>(),mechanisms=new MechanismData()};
        static void RuneRotation()
        {
            var data=RuneFixture();var timeline=new ReplayMechanismTimeline(data);
            Require(ReplayMechanismTimeline.RuneStage(179.999f)=="small" && ReplayMechanismTimeline.RuneStage(180)=="large","大小机关没有在180秒切换同体阶段");
            Require(Mathf.Abs(timeline.RuneAngle(6))==360 && Mathf.Abs(timeline.RuneSpeed(300))==60 && !timeline.RuneVariable(180),"π/3 rad/s匀速或激活机会被误当变速");
            data.mechanisms.runeProgress=new[]{new RuneProgressRecord {team="red",kind="small",t=179,arms=1}};
            timeline=new ReplayMechanismTimeline(data);
            Require(timeline.Rune("red",180).state!="activating" && !timeline.RuneVariable(180),"小机关击打窗口跨阶段驱动大机关靶面");
            data=RuneFixture();
            data.mechanisms.runeStates=new[]{new RuneStateRecord {team="red",kind="large",state="activating",t=200,ends=-1}};
            var redOnly=new ReplayMechanismTimeline(data);
            Require(redOnly.RuneVariable(201) && redOnly.Rune("blue",201).state=="unknown","直接状态没有驱动共轴变速，或串入另一方激活状态");
            Require(!redOnly.RuneVariable(220) && Mathf.Abs(redOnly.RuneSpeed(221))==60 && redOnly.Rune("red",220).state=="unknown","20秒激活窗口后仍无限变速/确认");
            data.mechanisms.runeStates[0].team="blue";var blueOnly=new ReplayMechanismTimeline(data);
            Require(Mathf.Abs(redOnly.RuneAngle(205)-blueOnly.RuneAngle(205))<.001f,"相同激活窗口仅因阵营不同改变公共转速");
            data.mechanisms.runeStates=new[]{new RuneStateRecord {team="red",kind="large",state="activating",t=200,ends=212},new RuneStateRecord {team="blue",kind="large",state="activating",t=200,ends=212}};
            var both=new ReplayMechanismTimeline(data);
            Require(Mathf.Abs(both.RuneAngle(210)-redOnly.RuneAngle(210))<.001f,"双侧激活把两个转速相加");
            data.mechanisms.runeStates=new[]{new RuneStateRecord {team="red",kind="large",state="activating",t=200,ends=205},new RuneStateRecord {team="blue",kind="large",state="activating",t=203,ends=210}};
            timeline=new ReplayMechanismTimeline(data);
            Require(timeline.RuneVariable(207) && !timeline.RuneVariable(210) && timeline.Rune("red",207).state=="unknown" && timeline.Rune("blue",207).state=="activating","两方不同起止窗口没有保持共轴转动及独立状态");
            data.mechanisms.runeSuccess=new[]{new RuneSuccessRecord {team="blue",kind="large",t=208,ends=-1,arms=-1}};
            timeline=new ReplayMechanismTimeline(data);
            Require(!timeline.RuneVariable(208) && timeline.Rune("blue",208).state=="activated","成功记录没有终止先前直接激活状态及变速");
            data=RuneFixture();data.mechanisms.runeProgress=new[]{new RuneProgressRecord {team="blue",kind="large",t=200,arms=1},new RuneProgressRecord {team="blue",kind="large",t=201,arms=2},new RuneProgressRecord {team="blue",kind="large",t=201.5f,arms=0}};
            timeline=new ReplayMechanismTimeline(data);
            Require(timeline.RuneVariable(201.25f) && !timeline.RuneVariable(201.5f),"持续命中窗口没有合并，或零进度后仍外推变速");
            data.mechanisms.runeProgress[2].t=201;timeline=new ReplayMechanismTimeline(data);
            Require(timeline.Rune("blue",201).state=="unknown" && !timeline.RuneVariable(201),"同秒后续零进度没有按源记录顺序更新");
            data.mechanisms.runeProgress[2]=new RuneProgressRecord {team="blue",kind="large",t=210,arms=1};
            timeline=new ReplayMechanismTimeline(data);
            Require(Mathf.Abs(timeline.RuneSpeed(200)-timeline.RuneSpeed(210))<.001f,"新的非连续激活窗口没有重置示意参数时间");
            // Independent numerical integration checks analytic angles, phase
            // continuity, direction and historical seeking at both boundaries.
            double numeric=timeline.RuneAngle(199);float last=(float)numeric;
            for(int i=0;i<1500;i++)
            {
                float t=199+i*.01f;numeric+=timeline.RuneSpeed(t+.005f)*.01f;
                float actual=timeline.RuneAngle(t+.01f);
                Require((actual-last)*timeline.direction>0,"共轴机关在切换状态时跳角或反向");last=actual;
            }
            Require(Math.Abs(numeric-timeline.RuneAngle(214))<.15,"解析角度与独立速度积分不一致");
            float saved=timeline.RuneAngle(212.5f);timeline.RuneAngle(0);timeline.RuneAngle(300);
            Require(saved==timeline.RuneAngle(212.5f),"倒退/跳转没有复现同一共轴姿态");
        }
    }
}
