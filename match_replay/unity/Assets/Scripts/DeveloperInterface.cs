using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace TcrReplay
{
    [Serializable] public class DeveloperScene
    {
        public LidarSettings lidar;public int schema=5,seed,direction,observationState,motionState,shots,hits,runeHits;public float time,runeAngle,recordElapsed;public bool customRules,ended;public string controlled,winner,recordActor;public DeveloperRules rules;public DeveloperZoneSnapshot zones;public DeveloperClip recording;
        public List<DeveloperActor> actors=new List<DeveloperActor>();public List<DeveloperTeam> teams=new List<DeveloperTeam>();public List<DeveloperBuilding> buildings=new List<DeveloperBuilding>();
        public List<DeveloperRune> runes=new List<DeveloperRune>();public List<DeveloperClip> clips=new List<DeveloperClip>();
    }
    public partial class ReplayViewer
    {
        string consoleLine="",coordinateText="8,7.5; 12,7.5",clipName="动作1",editSpeed="2",editSpin="180",presetName="training";
        bool consoleFocus,mapMark=true;Vector2 consoleScroll,editorScroll;int consoleHistoryIndex=-1,mapDraggingPoint=-1;
        readonly List<string> consoleHistory=new List<string>();float mapZoom=1;Vector2 mapPan,mapDrag;
        static readonly string[] GameCommands={"physics","help","get","set","start","pause","end","reset","time","seed","spawn","remove","control","path","armor","preview","base","rune","supply","buy","heal","revive","record","clip","save","load","rule-defaults","model","outpost","unstuck"};
        void DrawApplicationGui()
        {
            var matrix=GUI.matrix;bool enabled=GUI.enabled;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            if(appMode=="menu")DrawMainMenu();else if(game!=null)DrawDeveloperHud();
            DrawLidarGui();GUI.matrix=matrix;GUI.enabled=enabled;
        }
        void DrawMainMenu()
        {
            Panel(new Rect(0,0,1600,900));Text(110,145,1360,60,"2026 TCR · 雷达赛场",title);
            Text(110,205,1360,44,"历史战局回放 / 场地规则与自瞄训练",label);
            Text(110,255,1370,36,"半成品 / Alpha · 尚未完全做好 · 导航、日志融合、校准及跨平台验证仍有待办",small);
            if(Button(110,305,650,100,resourceProfile.SyntheticDemo?"回放模式\n合成示例 · 非真实比赛":"回放模式\n官方区域赛数据 · 战局搜索与筛选"))EnterReplay();
            bool developerEnabled=GUI.enabled;GUI.enabled=developerEnabled&&DeveloperResourcesAvailable;
            if(Button(110,425,650,100,DeveloperResourcesAvailable?"游戏模式 · 开发者\n驾驶、靶车运动、自瞄、机关与规则编辑":"游戏模式 · 开发者\n需要本地准备正式模型资源，见开源说明"))EnterDeveloper();GUI.enabled=developerEnabled;
            bool e=GUI.enabled;GUI.enabled=false;Button(110,545,650,90,"游戏模式 · 标准（暂未开放）");GUI.enabled=e;
            if(Button(110,690,300,48,"F8 显示与独显设置"))displayChooser=!displayChooser;
            if(Button(430,690,330,48,"退出程序"))Application.Quit();
            Text(850,310,640,240,"开发者模式\n\n扫地步兵车 · VT03 第一人称\n鼠标云台 / WASD 八向驾驶 / Shift 小陀螺\n靶车路径、动作录制、EKF 与重力补偿\n官方场地、红蓝基地护甲、前哨与能量机关",label);
            Text(110,820,1350,32,SystemInfo.graphicsDeviceName+" · "+SystemInfo.graphicsDeviceType+" · F11 全屏",small);
            if(error.Length>0)Text(110,760,1380,50,"初始化错误："+error);
        }
        void QueueGame(string command)
        {
            try{var actions=ParseGameCommand(command);game.commands.Enqueue(actions);}
            catch(Exception ex){game.Log("命令未执行："+ex.Message);}
        }
        void DrawDeveloperHud()
        {
            bool e=GUI.enabled;GUI.enabled=e&&!mouseCaptured;Panel(new Rect(0,0,1600,76));
            Text(20,10,800,30,"2026 TCR · 开发者模式"+(game.customRules?" · 自定义规则":" · V2.0.1 默认规则"),title);
            Text(20,44,680,27,$"{Clock(Mathf.Max(0,420-game.time))} · {(game.ended?"已结束 "+game.winner:game.running?"比赛中":"暂停 / 准备")} · {measuredFps:0} FPS · 固定物理120Hz",small);
            if(Button(755,15,105,43,game.running?"暂停 P":"开始 P"))QueueGame(game.running?"pause":"start");
            if(Button(868,15,96,43,"结束"))QueueGame("end");if(Button(972,15,96,43,"重置"))QueueGame("reset");
            if(Button(1076,15,156,43,"编辑面板 F6")){gameEdit=!gameEdit;ReleaseMouse();}
            if(Button(1240,15,150,43,"返回菜单")){EnterMenu();GUI.enabled=e;return;}
            if(Button(1398,15,182,43,"显示设置 F8")){displayChooser=true;ReleaseMouse();}
            if(!game.actors.TryGetValue(game.controlled,out var player)){GUI.enabled=e;return;}
            Panel(new Rect(18,704,1030,180));
            Text(30,714,1000,28,$"操控 {player.id} ({player.team}) · Lv{player.level} · HP {player.hp:0}/{player.maxHp:0} · 盾{player.shield:0} · 金币 {game.teams[player.team].coins}",label);
            Text(30,746,1000,24,$"17mm {player.ammo}/{player.allowedAmmo} · 热量 {player.heat:0.0}/{player.heatLimit:0} · 电机/执行器估算 {player.estimatedPower:0.0} W · 基础 {game.PowerBudget(player):0} W · 电容 {player.capPower:0.0} W · {(player.slow?"慢速":"正常")}",small);
            Text(30,774,1000,24,GameFolded(gameVehicles[player.id])?"正在收缩 / 过洞：禁止发弹":player.heatLocked&&!game.rules.unlimitedHeat?"过热锁定，冷却至0后恢复":player.permanentFireLock?"永久发射锁定：控制台可复位":!game.rules.infiniteAmmo&&(player.ammo<=0||player.allowedAmmo<=0)?"暂无弹量，Esc释放鼠标后使用补给按钮":aimStatus,small);
            DrawGameEnergyBar(player);
            Text(30,855,1000,24,"点击场地捕获鼠标 · WASD移动 · Shift小陀螺 · Ctrl爆发 · Caps快慢 · 左键发弹 · 右键自瞄 · Esc释放 · V第三人称",small);
            Panel(new Rect(1060,748,522,136));Text(1072,758,498,24,$"真实发弹 {gameShotCount} · 有效伤害 {gameHits} · 机关有效击打 {gameRuneHits}",small);
            var messages=game.messages.ToArray();for(int i=0;i<Mathf.Min(3,messages.Length);i++)Text(1072,788+i*26,495,25,messages[messages.Length-Mathf.Min(3,messages.Length)+i],small);
            GUI.enabled=e&&!mouseCaptured;
            if(Button(18,92,170,34,gamePreview?"F7 弹道预览：开":"F7 弹道预览：关")){gamePreview=!gamePreview;}
            if(Button(200,92,170,34,"Tab 二维地图")){gameMap=true;ReleaseMouse();}
            if(Button(382,92,170,34,allLabels?"F9 隐藏标签":"F9 显示标签"))allLabels=!allLabels;
            if(Button(564,92,174,34,"训练补给 100 发"))QueueGame("supply "+game.controlled+" 100");
            if(Button(750,92,174,34,"~ 开发控制台")){gameConsole=true;ReleaseMouse();consoleFocus=true;}
            bool rescueEnabled=GUI.enabled;GUI.enabled=rescueEnabled&&player.Alive&&!gameVehicles[player.id].recovering;
            if(Button(1280,92,300,34,gameVehicles[player.id].recovering?"正在寻找安全脱困点":"脱离卡死 · 当前车"))QueueGame("unstuck "+game.controlled);GUI.enabled=rescueEnabled;
            if(gameVehicles[player.id].stuckSeconds>1)Text(1280,174,300,32,"移动受阻 · Esc后可点脱困",small);
            else if(!string.IsNullOrEmpty(recoveryStatus))Text(1010,174,570,32,recoveryStatus,small);
            DrawPhysicsDebug(player);DrawGameReticle();if(allLabels)DrawGameLabels();GUI.enabled=e;
        }
        void DrawGameReticle()
        {
            var old=GUI.color;GUI.color=gameAutoAim?(gameAimValid?Color.green:Color.yellow):Color.white;
            GUI.DrawTexture(new Rect(791,449,6,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(803,449,6,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(799,441,2,6),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(799,453,2,6),Texture2D.whiteTexture);GUI.color=old;
            DrawGameAimOverlay();
            Text(690,480,460,24,gameAutoAim?"自瞄："+(gameLock.Length>0?gameLock:"未锁定"):"手动 · 右键启用自瞄",small);
        }
        void DrawGameLabels()
        {
            foreach(var v in gameVehicles.Values){if(v.state.id==game.controlled&&!gameThirdPerson)continue;var p=viewCamera.WorldToViewportPoint(v.state.position+Vector3.up*.58f);if(p.z<=0)continue;GUI.color=v.state.team=="red"?red:blue;GUI.Label(new Rect(p.x*1600-92,(1-p.y)*900-12,184,28),v.state.id+" "+v.state.hp.ToString("0"),worldLabel);}
            foreach(var b in game.buildings.Values){if(!buildingNodes.TryGetValue(b.id,out var node))continue;var p=viewCamera.WorldToViewportPoint(node.position+Vector3.up*1.35f);if(p.z>0){GUI.color=b.team=="red"?red:blue;GUI.Label(new Rect(p.x*1600-100,(1-p.y)*900-12,200,28),(b.team=="red"?"红方":"蓝方")+(b.role=="base"?"基地":"前哨站")+" "+b.hp.ToString("0"),worldLabel);}}
            GUI.color=Color.white;
        }
        void DrawGameEditor()
        {
            Panel(new Rect(930,142,650,592));Text(950,157,595,32,"开发者编辑 · 面板打开时暂停仿真",title);
            if(Button(1500,154,60,32,"关闭"))gameEdit=false;
            editorScroll=GUI.BeginScrollView(new Rect(948,205,610,510),editorScroll,new Rect(0,0,588,1740+game.actors.Count*42));
            Text(0,0,580,28,"控制台可编辑全部公开状态；此处提供常用动作",small);
            float y=38;foreach(var a in game.actors.Values)
            {
                GUI.color=a.team=="red"?red:blue;if(Button(0,y,240,32,(gameSelection==a.id?"▶ ":"")+a.id+" Lv"+a.level+" HP"+a.hp.ToString("0"))){gameSelection=a.id;editSpeed=a.requestedSpeed.ToString(CultureInfo.InvariantCulture);editSpin=a.spinRate.ToString(CultureInfo.InvariantCulture);}GUI.color=Color.white;
                if(Button(252,y,145,32,game.controlled==a.id?"当前接管":"人工接管"))QueueGame("control "+a.id);
                if(Button(410,y,160,32,a.spin?"关闭自动小陀螺":"开启自动小陀螺"))QueueGame("set "+a.id+" spin "+(!a.spin));y+=42;
            }
            if(Button(0,y,570,32,"增加蓝方练习靶车")){int number=1;while(game.actors.ContainsKey("target"+number))number++;QueueGame("spawn target"+number+" blue "+(7+game.actors.Count*.8f).ToString("0.0",CultureInfo.InvariantCulture)+",0,7.5");}y+=48;
            if(!game.actors.ContainsKey(gameSelection))gameSelection="player";var selectedActor=game.actors[gameSelection];
            Text(0,y,560,26,"选中 "+gameSelection+" · "+selectedActor.model+" · 训练累计伤害 "+selectedActor.trainingDamage.ToString("0"),small);y+=32;
            if(Button(0,y,180,32,"车型："+selectedActor.model))QueueGame("model "+gameSelection+" "+(selectedActor.model=="sweeper"?"jiageng":"sweeper"));
            if(Button(190,y,380,32,"训练不死："+(selectedActor.trainingImmortal?"开（最低1HP）":"关")))QueueGame("set "+gameSelection+" trainingImmortal "+(!selectedActor.trainingImmortal));y+=42;
            Text(0,y,560,26,"速度 m/s / 自旋 °/s",small);y+=32;
            editSpeed=GUI.TextField(new Rect(0,y,135,30),editSpeed);editSpin=GUI.TextField(new Rect(145,y,135,30),editSpin);
            if(Button(295,y,275,30,"应用速度 / 自旋"))QueueGame("set "+gameSelection+" requestedSpeed "+editSpeed+"; set "+gameSelection+" spinRate "+editSpin);y+=42;
            string[] dirs={"↖","↑","↗","←","停止","→","↙","↓","↘"};for(int i=0;i<9;i++)if(Button((i%3)*68,y+(i/3)*38,62,32,dirs[i])){int x=i%3-1,z=1-i/3;QueueGame("set "+gameSelection+" direction "+x+",0,"+z+"; set "+gameSelection+" motion "+(i==4?"still":"direction"));}
            if(Button(245,y,155,32,"随机八向"))QueueGame("set "+gameSelection+" motion random");if(Button(410,y,160,32,"两点往返"))QueueGame("set "+gameSelection+" motion return");
            if(Button(245,y+45,325,32,"地图标点 / 编辑路径")){gameMap=true;gameEdit=false;}y+=125;
            Text(0,y,575,26,"路径坐标 x,z（米），分号分隔；Ctrl地图点选可拖动",small);y+=32;coordinateText=GUI.TextField(new Rect(0,y,570,32),coordinateText);y+=42;
            if(Button(0,y,280,32,"应用路径并巡航"))QueueGame("path "+gameSelection+" "+coordinateText.Replace(" ",""));if(Button(290,y,280,32,"停止 / 清空路径"))QueueGame("set "+gameSelection+" motion still");y+=48;
            clipName=GUI.TextField(new Rect(0,y,250,32),clipName);if(Button(260,y,310,32,game.recording!=null?"结束并保存录制":"录制当前接管车"))QueueGame(game.recording!=null?"record stop":"record start "+clipName);y+=45;
            Text(0,y,580,50,"片段："+string.Join(", ",game.clips.Keys)+"\n控制台 clip play <车辆> <片段1,片段2> loop",small);y+=64;
            foreach(string team in new[]{"red","blue"})
            {
                var b=game.buildings[team+"_base"];var r=game.runes[team];string side=team=="red"?"红":"蓝";
                if(Button(0,y,280,32,side+"基地 "+(b.armorTarget>0?"收拢护甲":"展开护甲")))QueueGame("base "+team+" "+(b.armorTarget>0?"close":"open"));
                if(Button(290,y,280,32,side+"方复活前哨 1500HP"))QueueGame("outpost "+team+" revive");y+=42;
                foreach(string kind in new[]{"small","large"})
                {
                    bool on=r.Selected(kind);string state=on?r.startPending?$"关闭 · 等待{Mathf.Max(0,r.startsAt-game.time):0.0}s":"关闭":"开启";
                    if(Button(0,y,328,32,side+"方"+(kind=="small"?"小":"大")+"能量 · "+state))QueueGame("rune "+team+" "+kind+" "+(on?"off":"on"));
                    bool loop=kind=="small"?r.smallLoop:r.largeLoop;int selected=GUI.SelectionGrid(new Rect(340,y,230,32),loop?1:0,new[]{"单次","循环"},2,button);
                    if(selected!=(loop?1:0))QueueGame("set rune_"+team+" "+(kind=="small"?"smallLoop":"largeLoop")+" "+(selected==1));y+=42;
                }
                int minimum=r.MinimumRing(game.time);string last=r.hitSerial>0?r.lastHitRing+"环":"尚无命中";
                Text(0,y,570,26,$"{side}方 · {(r.Large(game.time)?"大":"小")}机关 · 进度{r.groups}/5 · 有效{minimum}–10环 · 最近{last}",small);y+=32;
                Text(0,y,570,26,"关闭面板后比赛时间继续；对准本方R标，右键自瞄",small);y+=40;
            }
            if(Button(0,y,570,32,"当前车等级 +1（同步属性、经验，覆盖等级上限）"))QueueGame("set "+gameSelection+" level "+Mathf.Min(10,selectedActor.level+1));y+=44;
            if(Button(0,y,280,32,"无限弹量："+game.rules.infiniteAmmo))QueueGame("set rules infiniteAmmo "+(!game.rules.infiniteAmmo));if(Button(290,y,280,32,"关闭热量限制："+game.rules.unlimitedHeat))QueueGame("set rules unlimitedHeat "+(!game.rules.unlimitedHeat));
            y+=46;
            if(Button(0,y,570,32,"轮组物理："+(PhysicsDriving?"开 · 独立关节 / 惯性":"旧版驾驶")))QueueGame("physics "+(!PhysicsDriving));y+=42;
            if(Button(0,y,570,32,"轮组调试："+(physicsDebug?"开":"关")))physicsDebug=!physicsDebug;y+=42;
            if(Button(0,y,570,32,"坡道辅助："+(game.rules.wheelRampAssist?"开 · 自动使用电容":"关 · 手动 Ctrl")))QueueGame("set rules wheelRampAssist "+(!game.rules.wheelRampAssist));y+=42;
            if(Button(0,y,280,32,"电容启用："+selectedActor.capEnabled))QueueGame("set "+gameSelection+" capEnabled "+(!selectedActor.capEnabled));
            if(Button(290,y,280,32,"开发充满电容"))QueueGame("set "+gameSelection+" capVoltage "+game.rules.capMaxVoltage.ToString(CultureInfo.InvariantCulture));y+=42;
            if(Button(0,y,570,32,"Avia 点云显示设置")){lidarChooser=true;gameEdit=false;ReleaseMouse();}
            GUI.EndScrollView();
        }
        void DrawGameConsole()
        {
            Panel(new Rect(30,146,1538,580));Text(48,160,1400,32,"开发控制台 · 输入期间暂停仿真 / Tab补全 / ↑↓历史",title);
            if(Button(1460,158,88,32,"关闭"))gameConsole=false;
            var e=Event.current;if(GUI.enabled&&UiLayerInteractive&&e.type==EventType.KeyDown&&GUI.GetNameOfFocusedControl()=="developer-command")
            {
                if(e.keyCode==KeyCode.Return){if(consoleLine.Trim().Length>0){consoleHistory.Add(consoleLine);game.Log("> "+consoleLine);QueueGame(consoleLine);consoleLine="";consoleHistoryIndex=consoleHistory.Count;}e.Use();consoleFocus=true;}
                else if(e.keyCode==KeyCode.UpArrow||e.keyCode==KeyCode.DownArrow){if(consoleHistory.Count>0){consoleHistoryIndex=Mathf.Clamp(consoleHistoryIndex+(e.keyCode==KeyCode.UpArrow?-1:1),0,consoleHistory.Count);consoleLine=consoleHistoryIndex<consoleHistory.Count?consoleHistory[consoleHistoryIndex]:"";}e.Use();}
                else if(e.keyCode==KeyCode.Tab){var c=consoleLine.Split(' ');var candidates=c.Length==1?new List<string>(GameCommands):c.Length==2?ObjectNames():MemberNames(c[1]);string prefix=c[c.Length-1];var found=candidates.FindAll(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));if(found.Count==1){c[c.Length-1]=found[0];consoleLine=string.Join(" ",c)+" ";}else game.Log(string.Join(" ",found));e.Use();}
            }
            var messages=game.messages.ToArray();consoleScroll=GUI.BeginScrollView(new Rect(48,206,1500,418),consoleScroll,new Rect(0,0,1460,Mathf.Max(414,messages.Length*28)));
            for(int i=0;i<messages.Length;i++)Text(0,i*28,1450,28,messages[i],small);GUI.EndScrollView();
            GUI.SetNextControlName("developer-command");consoleLine=GUI.TextField(new Rect(48,640,1290,38),consoleLine,2048);
            if(Button(1350,640,198,38,"执行")){QueueGame(consoleLine);consoleHistory.Add(consoleLine);consoleLine="";consoleFocus=true;}
            Text(48,690,1490,26,"help · get target1 · set target1 spin on · set rules gravity 9.81 · armor target1 0 0.18 · path target1 8,7.5;12,7.5",small);
            if(consoleFocus&&GUI.enabled&&UiLayerInteractive&&Event.current.type==EventType.Repaint){GUI.FocusControl("developer-command");consoleFocus=false;consoleScroll.y=100000;}
        }
        List<string> ObjectNames(){var names=new List<string>{"rules","game","red","blue","rune_red","rune_blue"};names.AddRange(game.actors.Keys);names.AddRange(game.buildings.Keys);return names;}
        List<string> MemberNames(string name){var names=new List<string>();try{foreach(var field in game.Find(name).GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))if(!field.IsInitOnly)names.Add(field.Name);}catch{}return names;}
        Action ParseGameCommand(string raw)
        {
            if(string.IsNullOrWhiteSpace(raw))return ()=>{};
            // A batch is restricted to validated set commands so an invalid field
            // cannot leave half an economy/level/physics edit committed.
            if(raw.Contains(";")&&!raw.TrimStart().StartsWith("path ",StringComparison.Ordinal))
            {var list=new List<Action>();foreach(string part in raw.Split(';')){if(part.Trim().Length==0)continue;if(!part.TrimStart().StartsWith("set "))throw new ArgumentException("批量命令仅支持 set；每条先校验再原子执行");list.Add(ParseGameCommand(part.Trim()));}return ()=>{foreach(var action in list)action();};}
            var c=raw.Trim().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);string cmd=c[0].ToLowerInvariant();Func<int,string> arg=i=>i<c.Length?c[i]:throw new ArgumentException("参数不足；输入 help");
            if(cmd=="help")return ()=>game.Log("命令："+string.Join(" / ",GameCommands)+"；set 对象 字段 值；get 对象 [字段]；path 车 x,z;x,z；clip play 车 名1,名2 [loop]；save/load 名；time 秒；规则恢复 rule-defaults");
            if(cmd=="get"){string name=c.Length>1?c[1]:"game";object obj=game.Find(name);string key=c.Length>2?c[2]:null;return ()=>{foreach(var f in obj.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))if(key==null||f.Name==key){object value=f.GetValue(obj);if(value is System.Collections.IList list){var items=new List<string>();foreach(var item in list)items.Add(item is Vector3 point?$"({point.x:0.000},{point.y:0.000},{point.z:0.000})":item.ToString());game.Log(name+"."+f.Name+" = ["+string.Join(", ",items)+"]");}else if(value is System.Collections.ICollection collection)game.Log(name+"."+f.Name+" 集合数="+collection.Count);else game.Log(name+"."+f.Name+" = "+value);}if(obj is DeveloperActor actor&&key==null){int i=actor.level-1;game.Log("等级基值 / 当前覆盖：HP "+(actor.chassis=="health"?DeveloperTables.Health[i]:DeveloperTables.PowerHealth[i])+" / "+actor.maxHp+"；功率 "+(actor.chassis=="health"?DeveloperTables.HealthPower[i]:DeveloperTables.Power[i])+" / "+actor.powerLimit+" W；实际预算 "+game.PowerBudget(actor)+" W；冷却生效 "+Mathf.Max(actor.cooling*game.rules.coolingMultiplier*Mathf.Max(actor.coolingBuff,game.TeamCooling(actor.team)),actor.cooling+actor.zoneCooling));}};}
            if(cmd=="set")
            {
                string name=arg(1),key=arg(2),value=string.Join(" ",c,3,c.Length-3);object obj=game.Find(name);var f=obj.GetType().GetField(key);if(f==null||f.IsInitOnly||obj==game||key=="id")throw new ArgumentException("字段只读或不存在；get "+name);
                var parsed=DeveloperWorld.Parse(f.FieldType,value);DeveloperWorld.Validate(obj,key,parsed);return ()=>{game.Set(name,key,value);if(key=="model"&&obj is DeveloperActor modelActor)ChangeGameModel(modelActor,value);if(obj is DeveloperActor edited&&key=="trainingImmortal"&&edited.trainingImmortal&&edited.hp<1){edited.hp=1;edited.deadAt=-1;}lidarDirty=true;if(key=="position"||key=="armorHeights"||key=="team"){gameTrackers.Clear();observations.Clear();if(gameVehicles.TryGetValue(name,out var v)){if(key=="position")SnapGameGround(v);v.lastPosition=v.state.position;v.state.velocity=Vector3.zero;}}};
            }
            if(cmd=="start")return ()=>{if(game.ended)throw new ArgumentException("比赛已结束，请 reset");game.running=true;game.Log("比赛开始 / 继续");};
            if(cmd=="pause")return ()=>{game.running=false;game.Log("比赛暂停");};if(cmd=="end")return ()=>game.End();if(cmd=="reset")return ()=>ResetDeveloper();
            if(cmd=="time"){float t=F(arg(1));return ()=>{bool recording=game.recording!=null;game.SetTime(t);if(recording)SaveGameClips();InvalidateLidar();gameTrackers.Clear();observations.Clear();runeTrackers.Clear();runeObservations.Clear();nextObservation=nextPreview=nextRuneObservation=game.time;foreach(var bullet in gameBullets){bullet.root.SetActive(false);gameBulletPool.Push(bullet);}gameBullets.Clear();game.Log("在途弹丸清空；观测时钟重新建立");};}
            if(cmd=="seed"){int seed=int.Parse(arg(1));return ()=>{game.seed=seed;ResetDeveloper();game.Log("种子已更新并重置比赛；动作片段保留");};}
            if(cmd=="spawn"){string id=arg(1),team=arg(2);Vector3 p=(Vector3)DeveloperWorld.Parse(typeof(Vector3),arg(3));if(game.actors.Count>=17)throw new ArgumentException("首版最多16靶车 +1主车");return ()=>game.Add(id,team,p);}
            if(cmd=="remove"){string id=arg(1);return ()=>game.Remove(id);}
            if(cmd=="unstuck"){string id=c.Length>1?c[1]:game.controlled;if(!game.actors.ContainsKey(id))throw new ArgumentException("车辆不存在");return ()=>RequestRecovery(id);}
            if(cmd=="control"){string id=arg(1);if(!game.actors.ContainsKey(id))throw new ArgumentException("车辆不存在");return ()=>{game.controlled=id;foreach(var a in game.actors.Values)a.manualControl=a.id==id;gameAutoAim=false;gameLock="";gameDesiredYaw=game.actors[id].gimbalYaw;gameDesiredPitch=game.actors[id].pitch;foreach(var v in gameVehicles.Values)PoseGameVehicle(v);};}
            if(cmd=="path")
            {
                var actor=game.actors[arg(1)];string value=string.Join("",c,2,c.Length-2);var path=new List<Vector3>();foreach(string point in value.Split(';')){var v=point.Split(',');if(v.Length!=2)throw new ArgumentException("路径点格式 x,z;x,z");float x=F(v[0]),z=F(v[1]);if(x<.3f||x>field.width-.3f||z<.3f||z>field.depth-.3f)throw new ArgumentException("路径点超出内场");path.Add(new Vector3(x,0,z));}if(path.Count<2)throw new ArgumentException("至少两个路径点");return ()=>{actor.path=path;actor.pathIndex=0;actor.motion="path";game.Log(actor.id+" 路径 "+path.Count+" 点；遇不可通行碰撞停车");};
            }
            if(cmd=="armor"){var actor=game.actors[arg(1)];int i=int.Parse(arg(2));float h=F(arg(3));if(i<0||i>=actor.armorHeights.Count||h<.07f||h>.8f)throw new ArgumentException("装甲索引 / 高度无效");return ()=>{actor.armorHeights[i]=h;PoseGameVehicle(gameVehicles[actor.id]);gameVehicles[actor.id].compressedHeight=CompressedHeight(gameVehicles[actor.id]);lidarDirty=true;gameTrackers.Clear();observations.Clear();game.Log(actor.id+" 装甲"+i+" 高度="+h);};}
            if(cmd=="preview"){bool on=B(arg(1));return ()=>gamePreview=on;}
            if(cmd=="aim")throw new ArgumentException("自瞄统一使用场景内右键；锁定ROI中离准星最近的有效目标");
            if(cmd=="physics"){bool on=B(arg(1));return ()=>{game.rules.vehiclePhysics=on;game.customRules=true;ConfigureGamePhysics();};}
            if(cmd=="model"){var a=game.actors[arg(1)];string m=arg(2);if(m!="sweeper"&&m!="jiageng")throw new ArgumentException("model 车辆 sweeper/jiageng");return ()=>ChangeGameModel(a,m);}
            if(cmd=="outpost"){string team=arg(1),op=arg(2);if(!game.teams.ContainsKey(team)||(op!="revive"&&op!="rebuild"))throw new ArgumentException("outpost red/blue revive/rebuild");return ()=>{game.RestoreOutpost(team,op=="revive");postRebuildProgress.Clear();gameTrackers.Clear();observations.Clear();runeObservations.Clear();lidarDirty=true;UpdateGameMechanisms();CollectGamePlates();};}
            if(cmd=="base"){var b=game.buildings[arg(1)+"_base"];string state=arg(2);if(state!="open"&&state!="close")throw new ArgumentException("base red/blue open/close");return ()=>{b.armorTarget=state=="open"?1:0;lidarDirty=true;};}
            if(cmd=="rune")
            {
                var r=game.runes[arg(1)];string kind=arg(2);
                if(kind=="trigger")return ()=>{lidarDirty=true;if(!r.Trigger(game))game.Log("机关正在激活、等待启动、增益未结束或没有机会");};
                if(kind!="small"&&kind!="large")throw new ArgumentException("rune red/blue small/large on/off；set rune_red smallLoop/largeLoop true/false");
                bool on=B(arg(3));return ()=>{r.Training(game,kind,on);lidarDirty=true;};
            }
            if(cmd=="supply"){var actor=game.actors[arg(1)];int amount=int.Parse(arg(2));if(amount<=0||amount>10000)throw new ArgumentException("弹量1..10000");return ()=>{actor.ammo+=amount;actor.allowedAmmo+=amount;game.Log(actor.id+" 开发者补给 +"+amount+"（绕过购买位置）");};}
            if(cmd=="buy"){var actor=game.actors[arg(1)];int amount=int.Parse(arg(2));bool remote=c.Length>3&&c[3]=="remote";if(amount<=0)throw new ArgumentException("数量必须为正");return ()=>BuyGameAmmo(actor,amount,remote);}
            if(cmd=="heal"){var actor=game.actors[arg(1)];return ()=>BuyGameHeal(actor);}
            if(cmd=="revive"){var actor=game.actors[arg(1)];return ()=>{if(actor.Alive)throw new ArgumentException("车辆未战亡");var team=game.teams[actor.team];int cost=Mathf.CeilToInt(game.time/60)*80+actor.level*20;if(team.coins<cost)throw new ArgumentException("金币不足");team.coins-=cost;actor.instantRevives++;actor.hp=actor.maxHp;actor.deadAt=-1;actor.revivedAt=game.time;actor.revivePowerUntil=game.time+4;actor.weakUntil=actor.invulnerableUntil=game.time+3;actor.buffer=60;actor.heat=0;game.Log(actor.id+" 即时复活，费用="+cost);};}
            if(cmd=="record"){string op=arg(1);string name=c.Length>2?c[2]:"动作1";return ()=>{if(op=="start")game.BeginRecording(name);else if(op=="stop"){game.FinishRecording();SaveGameClips();}else throw new ArgumentException("record start 名 / stop");};}
            if(cmd=="clip")
            {
                string op=arg(1);if(op=="list")return ()=>game.Log(string.Join(", ",game.clips.Keys));if(op=="delete"){string name=arg(2);return ()=>{game.clips.Remove(name);SaveGameClips();};}
                var actor=game.actors[arg(2)];if(op=="stop")return ()=>actor.motion="still";
                if(op=="restore")
                {
                    var clip=game.clips[arg(3)];if(string.IsNullOrEmpty(clip.initialState))throw new ArgumentException("旧片段缺少初始状态");Vector3 origin=c.Length>4?(Vector3)DeveloperWorld.Parse(typeof(Vector3),c[4]):clip.origin;float yaw=c.Length>5?F(c[5]):clip.initialYaw;
                    return ()=>{string id=actor.id,team=actor.team;var restored=DeveloperWorld.ReadActor(clip.initialState);ChangeGameModel(actor,string.IsNullOrEmpty(restored.model)?"sweeper":restored.model);CopyGameFields(restored,actor);actor.id=id;actor.team=team;actor.position=origin;actor.yaw=yaw;actor.gimbalYaw+=Mathf.DeltaAngle(clip.initialYaw,yaw);actor.clipYawOffset=Mathf.DeltaAngle(clip.initialYaw,yaw);actor.velocity=Quaternion.Euler(0,actor.clipYawOffset,0)*actor.velocity;RotatePhysicsState(actor.physics,actor.clipYawOffset);if(clip.schema<3)actor.physics=null;actor.clip=clip.name;actor.clipTime=actor.clipWaitUntil=0;actor.replayIndex=0;actor.motion="still";CopyGameFields(DeveloperWorld.ReadRules(clip.parameters),game.rules);if(clip.schema<3)game.rules.vehiclePhysics=false;ConfigureGamePhysics();if(gameVehicles[id].physics!=null)gameVehicles[id].physics.Reset(actor.position,Quaternion.Euler(0,actor.yaw,0),actor.velocity,actor.physics);game.customRules=JsonUtility.ToJson(game.rules)!=JsonUtility.ToJson(new DeveloperRules());if(gameVehicles[id].physics==null)SnapGameGround(gameVehicles[id]);gameVehicles[id].compressedHeight=CompressedHeight(gameVehicles[id]);InvalidateLidar();gameVehicles[id].lastPosition=actor.position;gameTrackers.Clear();observations.Clear();game.Log(id+" 恢复录制性能与起点；需 clip play 开始播放");};
                }
                if(op!="play")throw new ArgumentException("clip list/delete/play/stop/restore");string names=arg(3);foreach(var name in names.Split(','))if(!game.clips.ContainsKey(name)||game.clips[name].frames.Count==0)throw new ArgumentException("未知或空片段 "+name);bool loop=c.Length>4&&c[4]=="loop";
                return ()=>{if(actor.clip!=names)actor.clipYawOffset=0;actor.clip=names;actor.clipTime=actor.clipWaitUntil=0;actor.replayIndex=0;actor.motion="clip";actor.manualControl=false;actor.pathLoop=loop;actor.loops=loop?-1:0;var first=game.clips[names.Split(',')[0]];if(first.parameters!=JsonUtility.ToJson(game.rules))game.Log("录制与当前规则参数不同，轨迹可能变化");game.Log(actor.id+" 播放动作 "+names+"；当前位置连续接续；control命令恢复人工接管");};
            }
            if(cmd=="save"||cmd=="load"){string name=SafeGameName(arg(1));return ()=>{if(cmd=="save")SaveGameScene(name);else LoadGameScene(name);};}
            if(cmd=="rule-defaults")return ()=>{CopyGameFields(new DeveloperRules(),game.rules);foreach(var a in game.actors.Values)game.ApplyLevel(a,a.level);foreach(var r in game.runes.Values){r.mode="auto";r.smallLoop=r.largeLoop=false;r.ResetTimeline(game.time);}game.customRules=false;game.Log("恢复手册默认规则、各车等级派生属性、机关时间阶段与初始功率估算参数");};
            throw new ArgumentException("未知命令 "+cmd+"；输入 help");
        }
        static float F(string value){float n=float.Parse(value,CultureInfo.InvariantCulture);if(!float.IsFinite(n))throw new ArgumentException("数值必须有限");return n;}
        static bool B(string value)=>value=="on"||value=="1"?true:value=="off"||value=="0"?false:bool.Parse(value);
        static string SafeGameName(string name){if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Contains("..")||name.Contains("/"))throw new ArgumentException("保存名称无效");return name;}
        static void CopyGameFields(object source,object destination){foreach(var f in source.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))if(!f.IsInitOnly)f.SetValue(destination,f.GetValue(source));}
        void SaveGameClips(){Directory.CreateDirectory(GameStorage);var store=new DeveloperClipStore();store.clips.AddRange(game.clips.Values);File.WriteAllText(Path.Combine(GameStorage,"clips.json"),JsonUtility.ToJson(store,true));}
        void LoadGameClips(){var path=Path.Combine(GameStorage,"clips.json");if(!File.Exists(path))return;try{var store=JsonUtility.FromJson<DeveloperClipStore>(File.ReadAllText(path));foreach(var clip in store.clips)game.clips[clip.name]=clip;}catch(Exception ex){game.Log("片段读取失败："+ex.Message);}}
        void SaveGameScene(string name)
        {
            foreach(var v in gameVehicles.Values)v.physics?.CaptureSnapshot();Directory.CreateDirectory(GameStorage);var scene=new DeveloperScene {time=game.time,seed=game.seed,customRules=game.customRules,controlled=game.controlled,rules=game.rules,runeAngle=game.runeAngle,direction=game.direction,observationState=observationRandom.state,motionState=motionRandom.state,zones=CaptureGameZones(),lidar=lidarSettings,ended=game.ended,winner=game.winner,shots=gameShotCount,hits=gameHits,runeHits=gameRuneHits,recording=game.recording,recordActor=game.recordingActor,recordElapsed=game.RecordingElapsed};scene.actors.AddRange(game.actors.Values);scene.teams.AddRange(game.teams.Values);scene.buildings.AddRange(game.buildings.Values);scene.runes.AddRange(game.runes.Values);scene.clips.AddRange(game.clips.Values);
            File.WriteAllText(Path.Combine(GameStorage,name+".json"),JsonUtility.ToJson(scene,true));SaveGameClips();game.Log("已保存场景 "+name);
        }
        void LoadGameScene(string name)
        {
            var scene=JsonUtility.FromJson<DeveloperScene>(File.ReadAllText(Path.Combine(GameStorage,name+".json")));if(scene==null||(scene.schema<1||scene.schema>5)||scene.actors==null||scene.rules==null||scene.actors.Count>17||!scene.actors.Exists(a=>a.id=="player"))throw new InvalidDataException("场景无效");
            if(scene.schema<5)
            {
                var defaults=new DeveloperRules();foreach(var f in typeof(DeveloperRules).GetFields())if(IsVehiclePhysicsParameter(f.Name))f.SetValue(scene.rules,f.GetValue(defaults));foreach(var a in scene.actors)a.physics=null;
            }
            if(scene.schema<4)
            {
                scene.rules.aimRoiWidth=.6f;scene.rules.aimRoiHeight=.5f;
                if(scene.runes==null)scene.runes=new List<DeveloperRune>();
                foreach(var rune in scene.runes){rune.mode="auto";rune.enabled=rune.activating;rune.startPending=rune.smallLoop=rune.largeLoop=false;}
            }
            if(scene.schema<3){var defaults=new DeveloperRules();foreach(var f in typeof(DeveloperRules).GetFields())if(f.Name.StartsWith("cap",StringComparison.Ordinal)||f.Name.StartsWith("burst",StringComparison.Ordinal)||f.Name=="wirelessPower"||f.Name=="ordinaryPowerFraction")f.SetValue(scene.rules,f.GetValue(defaults));foreach(var a in scene.actors){a.capEnabled=true;a.capEnergy=.5f*defaults.capacitance*defaults.capMaxVoltage*defaults.capMaxVoltage;a.capVoltage=defaults.capMaxVoltage;a.chassisEnergy=20000;a.capUnderSince=a.capRecoverSince=-1;}}
            var ids=new HashSet<string>();foreach(var a in scene.actors){if(string.IsNullOrEmpty(a.model))a.model="sweeper";if(scene.schema<3)a.trainingImmortal=a.id!="player";DeveloperWorld.Validate(a,"model",a.model);if(string.IsNullOrWhiteSpace(a.id)||!ids.Add(a.id))throw new InvalidDataException("重复或空车辆名称");DeveloperWorld.Validate(a,"team",a.team);DeveloperWorld.Validate(a,"level",a.level);DeveloperWorld.Validate(a,"position",a.position);if(a.armorHeights==null||a.armorHeights.Count!=4)throw new InvalidDataException("装甲布局无效");}foreach(var f in typeof(DeveloperRules).GetFields())DeveloperWorld.Validate(scene.rules,f.Name,f.GetValue(scene.rules));
            ResetDeveloper();foreach(var v in gameVehicles.Values){gamePhysics?.Remove(v.state.id);v.unit.root.SetActive(false);Destroy(v.unit.root);}gameVehicles.Clear();game.actors.Clear();CopyGameFields(scene.rules,game.rules);ConfigureGamePhysics();game.customRules=scene.customRules;game.seed=scene.seed;game.SetTime(scene.time);
            foreach(var saved in scene.actors){var actor=game.Add(saved.id,saved.team,saved.position,saved.model);var heights=new List<float>(saved.armorHeights);CopyGameFields(saved,actor);actor.armorHeights=heights;gameVehicles[actor.id].lastPosition=actor.position;if(gameVehicles[actor.id].physics!=null)gameVehicles[actor.id].physics.Reset(actor.position,Quaternion.Euler(0,actor.yaw,0),actor.velocity,actor.physics);}
            foreach(var team in scene.teams)game.teams[team.id]=team;foreach(var building in scene.buildings)game.buildings[building.id]=building;if(scene.schema>=2){game.runeAngle=scene.runeAngle;game.direction=scene.direction;observationRandom.state=scene.observationState;motionRandom.state=scene.motionState;foreach(var r in scene.runes)game.runes[r.team]=r;RestoreGameZones(scene.zones);foreach(var clip in scene.clips)game.clips[clip.name]=clip;}game.controlled=game.actors.ContainsKey(scene.controlled)?scene.controlled:"player";gameSelection=game.controlled;gameDesiredYaw=game.actors[game.controlled].gimbalYaw;gameDesiredPitch=game.actors[game.controlled].pitch;nextObservation=nextPreview=game.time;gameInput=new DeveloperInput();
            if(scene.schema>=3&&scene.lidar!=null){lidarSettings=scene.lidar;ValidateLidarSettings();InvalidateLidar();}
            if(scene.schema>=2){game.ended=scene.ended;game.winner=scene.winner;gameShotCount=scene.shots;gameHits=scene.hits;gameRuneHits=scene.runeHits;game.RestoreRecording(scene.recording,scene.recordActor,scene.recordElapsed);}foreach(var v in gameVehicles.Values){PoseGameVehicle(v);v.compressedHeight=CompressedHeight(v);}UpdateGameMechanisms();CollectGamePlates();game.Log("已载入场景 "+name+"（暂停；弹丸/预测观测重新建立）");
        }
        void DrawGameMap()
        {
            Panel(new Rect(80,146,1435,575));Text(102,160,1150,32,"二维场地编辑 · 滚轮缩放 / 中键拖动 / 左键标点 / 右键删点",title);
            if(Button(1410,158,85,32,"关闭"))gameMap=false;
            Rect viewport=new Rect(102,205,1120,495);GUI.BeginGroup(viewport);
            float scale=Mathf.Min(viewport.width/field.width,viewport.height/field.depth)*mapZoom;
            Vector2 origin=new Vector2(viewport.width*.5f,viewport.height*.5f)+mapPan;
            Rect map=new Rect(origin.x-field.width*.5f*scale,origin.y-field.depth*.5f*scale,field.width*scale,field.depth*scale);
            var uv=field.textureScaleOffset;GUI.DrawTextureWithTexCoords(map,mapTexture,new Rect(uv[2],uv[3],uv[0],uv[1]));
            Func<Vector3,Vector2> toMap=p=>new Vector2(map.x+p.x*scale,map.y+(field.depth-p.z)*scale);
            var actor=game.actors[gameSelection];
            if(navigation.TryGetValue(actor.id,out var route)){for(int i=Mathf.Max(1,route.index);i<route.route.Count;i++)MapLine(toMap(route.route[i-1]),toMap(route.route[i]),Color.green);if(route.temporary){Vector2 point=toMap(route.effective);GUI.color=Color.cyan;GUI.DrawTexture(new Rect(point.x-6,point.y-6,12,12),Texture2D.whiteTexture);GUI.color=Color.white;Text(point.x+10,point.y-16,280,24,"临时同层停靠点",small);}}
            for(int i=0;i<actor.path.Count;i++){var point=toMap(actor.path[i]);Color color=actor.blocked&&i==actor.pathIndex?Color.red:Color.yellow;if(i>0)MapLine(toMap(actor.path[i-1]),point,color);GUI.color=color;GUI.DrawTexture(new Rect(point.x-5,point.y-5,10,10),Texture2D.whiteTexture);GUI.color=Color.white;Text(point.x+8,point.y-10,100,24,(i+1)+" "+actor.path[i].x.ToString("0.0")+","+actor.path[i].z.ToString("0.0"),small);}
            foreach(var a in game.actors.Values){var point=toMap(a.position);GUI.color=a.team=="red"?red:blue;GUI.DrawTexture(new Rect(point.x-6,point.y-6,12,12),Texture2D.whiteTexture);GUI.color=Color.white;Text(point.x+8,point.y+4,130,24,a.id,small);}
            var evt=Event.current;Vector2 mouse=evt.mousePosition;bool inside=GUI.enabled&&UiLayerInteractive&&new Rect(0,0,viewport.width,viewport.height).Contains(mouse);
            if(inside&&evt.type==EventType.ScrollWheel){mapZoom=Mathf.Clamp(mapZoom*(evt.delta.y<0?1.15f:.87f),.7f,8);evt.Use();}
            if(inside&&evt.type==EventType.MouseDown){if(evt.button==2){mapDrag=mouse;evt.Use();}else if(evt.button==0){Vector3 p=new Vector3(Mathf.Clamp((mouse.x-map.x)/scale,.3f,field.width-.3f),0,Mathf.Clamp(field.depth-(mouse.y-map.y)/scale,.3f,field.depth-.3f));
                if(evt.control){float best=18;for(int i=0;i<actor.path.Count;i++){float d=Vector2.Distance(toMap(actor.path[i]),mouse);if(d<best){best=d;mapDraggingPoint=i;}}}
                else if(mapMark){actor.path.Add(p);actor.pathIndex=0;}
                else {float best=18;foreach(var a in game.actors.Values){float d=Vector2.Distance(toMap(a.position),mouse);if(d<best){best=d;gameSelection=a.id;}}}evt.Use();}
                else if(evt.button==1){for(int i=actor.path.Count-1;i>=0;i--)if(Vector2.Distance(toMap(actor.path[i]),mouse)<18){actor.path.RemoveAt(i);break;}evt.Use();}}
            if(evt.type==EventType.MouseDrag&&inside){if(evt.button==2){mapPan+=mouse-mapDrag;mapDrag=mouse;evt.Use();}else if(mapDraggingPoint>=0&&mapDraggingPoint<actor.path.Count){actor.path[mapDraggingPoint]=new Vector3(Mathf.Clamp((mouse.x-map.x)/scale,.3f,field.width-.3f),0,Mathf.Clamp(field.depth-(mouse.y-map.y)/scale,.3f,field.depth-.3f));evt.Use();}}
            if(evt.type==EventType.MouseUp)mapDraggingPoint=-1;GUI.EndGroup();
            Text(1240,208,250,58,"选中 "+gameSelection+"\n路径点 "+actor.path.Count,small);mapMark=GUI.Toggle(new Rect(1240,280,250,30),mapMark,"左键添加路径点");
            if(Button(1240,325,248,35,"顺序巡航 / 循环")){if(actor.path.Count>=2)QueueGame("set "+actor.id+" motion path");else game.Log("请先添加至少两个点");}
            if(Button(1240,371,248,35,"两点往返")){if(actor.path.Count>=2)QueueGame("set "+actor.id+" motion return");else game.Log("请先添加两个点");}
            if(Button(1240,417,248,35,"清空路径"))actor.path.Clear();if(Button(1240,463,248,35,"恢复地图缩放")){mapZoom=1;mapPan=Vector2.zero;}
            Text(1240,518,248,130,"路径通过车辆碰撞控制器执行。红色阻挡提示表示当前位置无法通行；不会直接移动穿墙。点序与停留可用控制台调整。",small);
        }
        void MapLine(Vector2 a,Vector2 b,Color color)
        {
            var matrix=GUI.matrix;var old=GUI.color;GUI.color=color;
            GUI.matrix=matrix*Matrix4x4.TRS(a,Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg),Vector3.one);
            GUI.DrawTexture(new Rect(0,0,(b-a).magnitude,2),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=old;
        }
    }
}
