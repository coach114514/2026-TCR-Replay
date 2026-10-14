using System;
using UnityEngine;
namespace TcrReplay
{
    public static class DeveloperChecks
    {
        public static void Require(bool condition,string error){if(!condition)throw new InvalidOperationException("开发者验收："+error);}
        public static void Run()
        {
            var world=new DeveloperWorld();var a=world.Add("player","red",Vector3.zero);var target=world.Add("target","blue",Vector3.forward*5);
            Require(!world.Fire(a),"开局0允许发弹量仍可射击");a.ammo=a.allowedAmmo=100;Require(world.Fire(a),"合法射击未产生");Require(a.heat==10&&a.ammo==99&&a.xp==1,"发弹额度 / 热量 / 经验");
            world.Set("player","level","9");Require(a.heatLimit==114&&a.powerLimit==90&&a.maxHp==400,"手册9级属性错误");a.hp=100;a.heat=25;world.ApplyLevel(a,10);Require(a.hp==100&&a.heat==25,"升级重置血量或热量");
            float solo=DeveloperTables.Speed(a,world.rules,0),spin=DeveloperTables.Speed(a,world.rules,Mathf.PI*2);Require(solo>spin&&solo<=3.3f,"共享功率未降低自旋平移速度");
            world.Set("rules","gravity","4");Require(world.customRules&&world.rules.gravity==4,"自定义规则标签未设置");
            world.time=10;int damage=world.Hit(new DeveloperHit {target="target",shooter="player",panel=0,normalSpeed=25});Require(damage==20&&target.hp==180,"实际装甲伤害");Require(world.Hit(new DeveloperHit {target="target",shooter="player",panel=0,normalSpeed=25})==0,"50ms屏蔽间隔");
            var blueBase=world.buildings["blue_base"];world.time=11;Require(world.Hit(new DeveloperHit {target="blue_base",shooter="player",normalSpeed=25})==0,"前哨存活时基地保护");
            world.buildings["blue_outpost"].hp=0;world.time=12;Require(world.Hit(new DeveloperHit {target="blue_base",shooter="player",normalSpeed=25,local=Vector2.one*.03f})==5&&blueBase.shield==145,"基地上前板伤害 / 护盾");
            world.running=true;world.buildings["red_base"].armorTarget=1;world.Tick();Require(world.buildings["blue_outpost"].stopped,"对方基地展开未锁存前哨停转");world.buildings["red_base"].armorTarget=0;world.Tick();Require(world.buildings["blue_outpost"].stopped,"收拢后前哨错误恢复转动");
            Require(DeveloperRune.Ring(.001f)==10&&DeveloperRune.Ring(.15f)==1&&DeveloperRune.Ring(.151f)==0,"机关环数边界");
            var r=world.runes["red"];world.SetTime(181);Require(r.Trigger(world),"大机关机会未建立");world.time+=3;r.Tick(world);for(int i=0;i<5;i++){world.time+=.2f;r.Tick(world);Require(r.Hit(world,r.lit,.02f,25),"大机关首击");Require(r.Hit(world,r.optional,.02f,25),"大机关第二击");}Require(r.successes==1&&r.hits==10&&r.attack==2&&r.cooling==3,"大机关十击9环增益");
            RuneLifecycle();RuneSuccessAndFeedback();Tracking();Recording();Navigation();RuleBoundaries();Phase2();Debug.Log("DEVELOPER_LOGIC_CHECKS_OK levels heat ammo power armor shield EKF intercept rune recording navigation victory energy IMM immortality outpost");
        }
        static void Tracking()
        {
            var rules=new DeveloperRules();var tracker=new DeveloperTracker();Vector3 velocity=new Vector3(.8f,.03f,-.35f);float omega=2.6f;
            var layout=new DeveloperPlate {local=new Vector3(0,.19f,.266f),localRotation=Quaternion.identity,tilt=Quaternion.identity,half=new Vector3(.0675f,.0625f,.004f)};
            for(int i=0;i<=180;i++){float t=i/60f;var q=Quaternion.Euler(0,omega*t*Mathf.Rad2Deg,0);tracker.Correct(new DeveloperObservation {plate=layout,position=new Vector3(0,0,8)+velocity*t+q*layout.local,yaw=omega*t*Mathf.Rad2Deg,time=t},rules);}
            Require((tracker.Velocity-velocity).magnitude<.03f&&Mathf.Abs(tracker.Omega-omega*Mathf.Rad2Deg)<2,"平移+自旋EKF误差");float future=3.3f;var predicted=tracker.At(layout,future);var actual=new Vector3(0,0,8)+velocity*future+Quaternion.Euler(0,omega*future*Mathf.Rad2Deg,0)*layout.local;
            Require((predicted.center-actual).magnitude<.015f,"联合前向预测误差");
            var steady=new DeveloperTracker();layout.local=Vector3.zero;layout.localRotation=Quaternion.Euler(0,180,0);for(int i=0;i<60;i++)steady.Correct(new DeveloperObservation {plate=layout,position=new Vector3(0,.5f,8),yaw=0,time=i/60f},rules);
            Require(DeveloperIntercept.Solve(Vector3.up*.3f,layout,steady,.98f,0,rules,out var direction,out float flight,out var plate),"静态重力补偿无解");var impact=Vector3.up*.3f+direction*rules.bulletSpeed*flight+Vector3.down*.5f*rules.gravity*flight*flight;Require((impact-plate.center).magnitude<.002f&&direction.y>0,"枪管方向 / 重力弹着位置");
        }
        static void Recording()
        {
            var world=new DeveloperWorld();var a=world.Add("player","red",Vector3.zero);world.BeginRecording("sample");for(int i=0;i<120;i++){world.time=i/120f;world.Record(a,new DeveloperInput{x=i<60?1:-1,z=1,spin=true});}world.time=1;world.FinishRecording();a.clip="sample";a.pathLoop=false;
            for(int i=0;i<120;i++){var input=world.ClipInput(a);Require(input.x==(i<60?1:-1),"动作录制按固定步复现");}Require(world.clips["sample"].frames.Count==120,"暂停时间进入录制");
        }
        static void Navigation()
        {
            var path=DeveloperNavigation.Plan(Vector3.zero,Vector3.right*5,(a,b)=>b.x>2&&b.x<2.8f&&Mathf.Abs(b.z)<.85f?null:(Vector3?)b);Require(path.Count>2&&path.Exists(p=>Mathf.Abs(p.z)>=.85f),"路径没有绕过不可通行区域");
            Require(DeveloperNavigation.Plan(Vector3.zero,Vector3.right*5,(a,b)=>null).Count==0,"不可达目标产生穿墙路径");
            Require(DeveloperNavigation.Plan(Vector3.zero,new Vector3(.1f,0,0),(a,b)=>null).Count==0,"最后一段路径未检查仍然穿墙");
            var occupied=new DeveloperNavigation.Search(Vector3.zero,Vector3.right*5,(a,b)=>b.x<0||b.x>2||Mathf.Abs(b.z)>1?null:(Vector3?)b,400,true);while(!occupied.Done)occupied.Advance(256,1000);Require(occupied.Temporary&&occupied.Route.Count>0&&occupied.Route[occupied.Route.Count-1].x>1.5f&&occupied.Route[occupied.Route.Count-1].x<=2,"最近候选被隔断时未寻找最近同层可达替代点");
        }
        static void Phase2()
        {
            foreach(float phase in new[]{-170f,-90f,-10f,10f,90f,170f})foreach(float axial in new[]{-.12f,.12f})
            {
                Vector3 radius=new Vector3(.7f,0,axial),axis=Vector3.forward,rotated=Quaternion.AngleAxis(phase,axis)*radius;
                Require(Mathf.Abs(Mathf.DeltaAngle(DeveloperRuneTracker.ObservedPhase(radius,rotated,axis)*Mathf.Rad2Deg,phase))<.001f,"旋转相位被前后靶面轴向偏移污染");
            }
            Require(DeveloperWorld.ReadRules("{\"gravity\":4}").capacitance>8,"旧规则片段丢失新增电容默认值");Require(DeveloperWorld.ReadActor("{\"id\":\"target\"}").trainingImmortal,"旧车辆片段丢失训练默认值");
            var world=new DeveloperWorld();var player=world.Add("player","red",Vector3.zero);var target=world.Add("target","blue",Vector3.forward);
            for(int i=0;i<30;i++){world.time=i*.1f;world.Hit(new DeveloperHit {target="target",shooter="player",normalSpeed=25,panel=i%4});}
            Require(target.hp==1&&target.trainingDamage==600&&target.actualHpDamage==199&&player.kills==0&&target.deadAt<0,"靶车保留1HP但未独立累计有效训练伤害");
            target.model="jiageng";target.fold=.5f;target.ammo=target.allowedAmmo=100;Require(target.CanFire(world.time,world.rules),"麦轮正常半摊错误禁射");target.fold=1;Require(!target.CanFire(world.time,world.rules),"麦轮全摊过洞发弹联锁");
            float initial=player.capEnergy;float requested=world.PowerBudget(player)+100;world.EnergyStep(player,true,requested);
            Require(Mathf.Abs(initial-player.capEnergy-100*DeveloperWorld.Step/world.rules.capEfficiency)<.01f&&player.chassisInputPower<=world.PowerBudget(player)+.001f,"电容放电不守恒 / 挤占裁判底盘功率");
            player.capEnabled=false;Require(world.DrivePowerBudget(player,true)==world.PowerBudget(player),"关闭电容仍额外供电");player.capEnabled=true;
            player.capEnergy=0;for(int i=0;i<65;i++){world.time+=DeveloperWorld.Step;world.EnergyStep(player,true,world.PowerBudget(player));}Require(player.capProtected,"欠压未进入保护");
            player.capEnergy=world.rules.capacitance*11*11*.5f;for(int i=0;i<610;i++){world.time+=DeveloperWorld.Step;world.EnergyStep(player,true,world.PowerBudget(player));}Require(!player.capProtected,"充电后未按恢复延迟解锁");
            player.chassisEnergy=0;Require(world.PowerBudget(player)==35,"空底盘能量功率限制");player.chassisEnergy=26000;Require(world.PowerBudget(player)==player.powerLimit*1.25f,"高底盘能量倍率");
            var post=world.buildings["red_outpost"];post.hp=0;post.destroyedBefore=true;post.stopped=true;post.rotationStopAt=2;world.RestoreOutpost("red");Require(post.hp==1500&&post.destroyedBefore&&post.stopped&&post.rotationStopAt==2,"前哨复活丢失永久停转 / 首毁历史");
            foreach(bool large in new[]{false,true})foreach(int sign in new[]{-1,1})
            {
                var tracker=new DeveloperRuneTracker();float amplitude=.84f,omega=1.96f,phase=.7f,radius=.7f;
                Func<float,float> angle=t=>sign*(large?(2.09f-amplitude)*t+amplitude/omega*(Mathf.Cos(phase)-Mathf.Cos(phase+omega*t)):Mathf.PI/3*t);
                for(int i=0;i<=720;i++)
                {if(i%7==0)continue;float t=i/60f,theta=angle(t);Quaternion q=Quaternion.AngleAxis(theta*Mathf.Rad2Deg,Vector3.forward);var p=new DeveloperPlate {index=0,center=new Vector3(0,2,8)+q*(Vector3.up*radius),rotation=q*Quaternion.Euler(0,180,0),half=Vector3.one*.15f};
                    tracker.Correct(new RuneObservation {time=t,angle=theta+Mathf.Sin(i*1.7f)*.0005f,center=new Vector3(0,2,8),axis=Vector3.forward,plates=new[]{p},lit=new[]{true},alreadyHit=new[]{false}},world.rules);}
                float at=12.25f;var predicted=tracker.At(tracker.layouts[0],at);Vector3 actual=new Vector3(0,2,8)+Quaternion.AngleAxis(angle(at)*Mathf.Rad2Deg,Vector3.forward)*Vector3.up*radius;float error=(predicted.center-actual).magnitude;
                Debug.Log($"RUNE_TRACK_VERIFY large={large} sign={sign} futureError={error:F5} bigProbability={tracker.largeProbability:F3} speed={tracker.Speed:F2}");Require(error<.003f,"机关未来相位预测误差 "+error);
                if(!large)Require(Mathf.Abs(Mathf.Abs(tracker.Speed)-60)<.01f&&tracker.MotionMatched,"小机关没有使用固定60度每秒模型");
                Require(large?tracker.largeProbability>.8f:tracker.largeProbability<.8f,"机关变速分类错误");
                Require(DeveloperRuneTracker.Solve(new Vector3(0,.4f,0),tracker.layouts[0],tracker,12,0,world.rules,out var direction,out float flight,out var hit),"机关重力截获无解");
                Vector3 impact=new Vector3(0,.4f,0)+direction*world.rules.bulletSpeed*flight+Vector3.down*.5f*world.rules.gravity*flight*flight;Require(Vector3.Distance(impact,hit.center)<.003f,"机关枪管向量与弹道落点不一致");
            }
        }
        static void RuleBoundaries()
        {
            var world=new DeveloperWorld();var a=world.Add("a","red",Vector3.zero);a.trainingImmortal=false;world.Set("a","hp","0");world.ApplyLevel(a,8);Require(a.hp==0,"战亡状态升级导致无条件复活");
            world.Set("a","powerLimit","120");Require(world.customRules,"车辆性能覆盖未标记自定义规则");world.customRules=false;world.Set("rules","spinSpeed","120");Require(a.spinRate==120,"默认自旋转速覆盖未应用");
            var r=world.runes["red"];world.SetTime(1);Require(r.Trigger(world),"小机关机会");world.time+=3;r.Tick(world);for(int i=0;i<5;i++){world.time+=.2f;r.Tick(world);Require(r.Hit(world,r.lit,.05f,25),"小机关逐臂击打");}Require(r.successes==1&&r.smallBuffUntil>world.time,"小机关完成");world.SetTime(180);Require(r.successes==0&&r.opportunities==1,"小/大机关次数错误混用");
            var red=world.buildings["red_outpost"];red.destroyedBefore=true;red.hp=750;world.buildings["blue_outpost"].hp=100;world.End();Require(world.winner=="蓝方","重建前哨血量掩盖首次被毁的胜负优先级");
            world.buildings["blue_outpost"].destroyedBefore=true;world.teams["red"].damage=world.teams["blue"].damage=0;a.hp=80;world.Add("b","blue",Vector3.zero).hp=30;world.End();Require(world.winner=="红方","最终剩余机器人血量判胜");
        }
        static void RuneLifecycle()
        {
            foreach(string kind in new[]{"small","large"})foreach(bool loop in new[]{false,true})
            {
                var world=new DeveloperWorld();world.SetTime(kind=="small"?181:1);world.running=true;
                var r=world.runes["red"];r.smallLoop=r.largeLoop=loop;r.Training(world,kind,true);
                float angle=world.runeAngle,begin=world.time;for(int i=0;i<359;i++)world.Tick();
                Require(r.startPending&&!r.activating&&r.lit<0&&world.runeAngle==angle&&!r.Hit(world,0,0,25),"三秒等待期间转动/亮靶/结算");
                world.Tick();Require(r.activating&&!r.startPending&&Mathf.Abs(r.started-begin-3)<.0001f&&r.Large(world.time)==(kind=="large"),"大小模式受比赛时间干扰 / 三秒边界");
                float until=r.started+20;while(world.time<until-.004f)world.Tick();
                Require(!r.activating&&r.startPending==loop&&r.enabled==loop,"单次/循环在窗口结束后状态错误");
                if(loop){float pauseAngle=world.runeAngle;for(int i=0;i<359;i++)world.Tick();Require(r.startPending&&world.runeAngle==pauseAngle,"循环重启跳过三秒等待");world.Tick();Require(r.activating&&r.attempts==2,"循环未重新启动");}
                r.Training(world,kind,false);for(int i=0;i<400;i++)world.Tick();Require(!r.activating&&!r.startPending&&!r.enabled,"关闭后延迟启动复活");
            }
            var success=new DeveloperWorld();var small=success.runes["red"];small.smallLoop=true;small.Training(success,"small",true);success.time=3;small.Tick(success);
            for(int i=0;i<5;i++){success.time+=.2f;small.Tick(success);Require(small.Hit(success,small.lit,.02f,25),"循环小机关有效击打");}
            Require(small.successes==1&&small.startPending&&!small.activating&&Mathf.Abs(small.startsAt-success.time-3)<.0001f,"成功后的循环等待");
            small.Training(success,"large",true);Require(small.mode=="large"&&small.startPending&&small.successes==0,"待启动期间大小切换未取消旧轮");
            var restored=JsonUtility.FromJson<DeveloperRune>(JsonUtility.ToJson(small));Require(restored.mode=="large"&&restored.startPending&&restored.smallLoop&&restored.startsAt==small.startsAt,"机关训练配置未保存");
            var both=new DeveloperWorld();both.running=true;both.runes["red"].Training(both,"small",true);both.runes["blue"].Training(both,"large",true);
            for(int i=0;i<361;i++)both.Tick();Require(both.RuneMotionSource()==both.runes["blue"],"共轴没有采用正在激活的大机关变速");both.runes["blue"].Stop();Require(both.RuneMotionSource()==both.runes["red"],"关闭一面错误停止另一面的机关");
            both.runes["red"].Stop();float stopped=both.runeAngle;both.Tick();Require(both.runeAngle==stopped,"全关闭仍转动");
            Require(DeveloperWorld.ReadRules("{\"gravity\":4}").aimRoiWidth==.6f,"旧规则片段丢失ROI默认值");
            Debug.Log("RUNE_LIFECYCLE_VERIFY_OK delay=3 window=20 small/large single/loop cancel/switch success/restart save shared-rotor");
        }
        static void RuneSuccessAndFeedback()
        {
            var w=new DeveloperWorld();var r=w.runes["red"];r.Training(w,"large",true);w.time=3;r.Tick(w);
            for(int i=0;i<5;i++)
            {
                Require(r.Hit(w,r.lit,.002f,25),"大机关每组首击");int second=r.optional,oldHits=r.hits;
                w.time+=1.001f;r.Tick(w);Require(!r.Hit(w,second,.002f,25)&&r.hits==oldHits,"第二击超1秒仍结算");
                if(i<4){w.time+=.151f;r.Tick(w);}
            }
            Require(r.successes==1&&r.hits==5&&r.groups==5&&Mathf.Abs(r.buffUntil-w.time-30)<.001f,"五组首击、没有第二击应成功并增益30秒");
            r.Training(w,"large",true);w.time+=3;r.Tick(w);Require(r.MinimumRing(w.time)==4,"一次成功后有效4–10环");
            int serial=r.hitSerial;Require(!r.Hit(w,r.lit,.14f,25)&&r.hitSerial==serial,"无效环区被计入命中");
            r.successes=2;Require(r.MinimumRing(w.time)==7,"两次成功后有效7–10环");
            r.successes=0;int wrong=(r.lit+1)%5;while(wrong==r.optional)wrong=(wrong+1)%5;int generation=r.groupSerial;
            Require(!r.Hit(w,wrong,.01f,25)&&r.resets==1&&r.activating&&r.groupSerial>generation,"打暗靶应重置进度但不关闭激活窗口");
            generation=r.groupSerial;w.time+=2.5f;r.Tick(w);Require(r.resets==2&&r.activating&&r.groupSerial>generation,"首击超时重置");
            var a=new DeveloperRuneAimState();a.Group("rune_red",1);a.Pair(2,4);a.Issued(7,2,1,true);a.Tick(.99f);
            Require(a.Pending&&a.current==2&&!a.confirmed.Contains(2),"发弹即被当作命中／切靶");a.Tick(1);
            Require(!a.Pending&&a.current==2&&a.retries==1,"未命中不得换靶，需允许补打");a.Issued(8,2,2,true);a.Confirm(0,2);Require(a.Pending&&a.current==2,"旧灯组命中误推进");
            a.Confirm(1,2);Require(!a.Pending&&a.current==4&&a.confirmed.Contains(2),"实际首击确认后未按序选择第二靶");
            a.Group("rune_red",2);a.Pair(1,3);a.Confirm(2,3);Require(a.first==3&&a.current==1,"手动提前命中第二靶未更新实际顺序");
            Debug.Log("RUNE_SUCCESS_FEEDBACK_VERIFY_OK five-first-hits optional-second ring-shrink deadlines miss/retry confirmed-order");
        }
    }
}
