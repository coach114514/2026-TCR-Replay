using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    // JsonUtility cannot deserialize nested arrays; flat polygon pairs are used at runtime.
    [Serializable] public class GameZone {public string id,team,kind,group;public int order;public float[] polygon;}
    [Serializable] public class GameZones {public int schema;public string source,precision;public GameZone[] zones;}
    [Serializable] public class DeveloperNumber {public string name;public float value;}
    [Serializable] public class DeveloperName {public string name,value;}
    [Serializable] public class DeveloperFortressTimer {public string key;public float progress,lastPresent;}
    [Serializable] public class DeveloperPurchase {public string actor;public int quantity;public float due;public bool heal;}
    [Serializable] public class DeveloperCrossing {public string actor,group,lastCard;public int step,first;public float began,lastRoad=-100;public List<string> rewarded=new List<string>();}
    [Serializable] public class DeveloperZoneSnapshot
    {
        public List<DeveloperNumber> lowest=new List<DeveloperNumber>(),rebuild=new List<DeveloperNumber>(),presence=new List<DeveloperNumber>();
        public List<DeveloperName> owners=new List<DeveloperName>();public List<DeveloperFortressTimer> fortress=new List<DeveloperFortressTimer>();
        public List<DeveloperPurchase> purchases=new List<DeveloperPurchase>();public List<DeveloperCrossing> crossings=new List<DeveloperCrossing>();
    }
    public partial class ReplayViewer
    {
        GameZones gameZones;
        readonly Dictionary<string,DeveloperCrossing> crossingState=new Dictionary<string,DeveloperCrossing>();
        readonly Dictionary<string,float> baseLowest=new Dictionary<string,float>();
        readonly Dictionary<string,string> fortressOwners=new Dictionary<string,string>();
        readonly Dictionary<string,DeveloperFortressTimer> fortressEntered=new Dictionary<string,DeveloperFortressTimer>();
        readonly Dictionary<string,float> postRebuildProgress=new Dictionary<string,float>(),zonePresence=new Dictionary<string,float>();
        readonly List<DeveloperPurchase> ammoOrders=new List<DeveloperPurchase>();
        void LoadGameZones()
        {
            var path=Path.Combine(folder,"developer_zones.json");if(File.Exists(path))gameZones=JsonUtility.FromJson<GameZones>(File.ReadAllText(path));
            crossingState.Clear();baseLowest.Clear();fortressOwners.Clear();fortressEntered.Clear();postRebuildProgress.Clear();zonePresence.Clear();ammoOrders.Clear();foreach(string team in new[]{"red","blue"})baseLowest[team]=5000;
        }
        static bool ZoneContains(GameZone zone,Vector3 p)
        {
            bool inside=false;int n=zone.polygon.Length/2;for(int i=0,j=n-1;i<n;j=i++){float xi=zone.polygon[2*i],zi=zone.polygon[2*i+1],xj=zone.polygon[2*j],zj=zone.polygon[2*j+1];if((zi>p.z)!=(zj>p.z)&&p.x<(xj-xi)*(p.z-zi)/(zj-zi)+xi)inside=!inside;}return inside;
        }
        List<GameZone> ZonesAt(DeveloperActor actor)
        {
            var result=new List<GameZone>();if(gameZones?.zones==null)return result;
            foreach(var zone in gameZones.zones)
            {
                string key=actor.id+"|"+zone.id;bool detected=ZoneContains(zone,actor.position)&&Mathf.Abs(actor.position.y-GroundFrom(actor.position)-BallisticMath.Support(gameVehicles[actor.id].unit.robot.model,actor.fold))<.15f;
                if(detected){result.Add(zone);zonePresence[key]=game.time+2;}
                else if(actor.Alive&&string.IsNullOrEmpty(zone.group)&&zonePresence.TryGetValue(key,out float until)&&game.time<until)result.Add(zone);
            }
            return result;
        }
        bool OwnSupply(DeveloperActor actor){foreach(var z in ZonesAt(actor))if(z.kind=="supply"&&z.team==actor.team)return true;return false;}
        bool CanBuyHere(DeveloperActor actor){foreach(var z in ZonesAt(actor))if(z.team==actor.team&&(z.kind=="supply"||z.kind=="base"||z.kind=="outpost"&&game.buildings[z.team+"_outpost"].hp>0))return true;return false;}
        bool OutOfCombat(DeveloperActor a)=>game.time-a.lastDamage>=6&&game.time-a.nextShot+game.rules.shotPeriod>=6;
        void BuyGameAmmo(DeveloperActor actor,int amount,bool remote)
        {
            var team=game.teams[actor.team];if(!actor.Alive)throw new ArgumentException("战亡车辆不能购买");if(remote&&!OutOfCombat(actor))throw new ArgumentException("远程购买需要脱战6秒");if(!remote&&!CanBuyHere(actor))throw new ArgumentException("需处于己方补给 / 基地 / 可占领前哨增益点");
            int quantity=Mathf.CeilToInt(amount/(remote?100f:10f))*(remote?100:10),cost=quantity/(remote?100:10)*(remote?150:10);
            if(team.remoteAmmo+quantity>1000)throw new ArgumentException("金币兑换发弹量上限1000 / 队");if(team.coins<cost)throw new ArgumentException("金币不足");team.coins-=cost;team.remoteAmmo+=quantity;
            if(remote){ammoOrders.Add(new DeveloperPurchase {actor=actor.id,quantity=quantity,due=game.time+6});game.Log("远程弹量兑换将在6秒后生效");}else{actor.allowedAmmo+=quantity;actor.ammo+=quantity;game.Log(actor.id+" 兑换允许发弹量 +"+quantity+" 金币 -"+cost+"；补给同步装弹");}
        }
        void BuyGameHeal(DeveloperActor actor)
        {
            if(!actor.Alive||!OutOfCombat(actor))throw new ArgumentException("远程回血需存活且脱战6秒");if(ammoOrders.Exists(x=>x.actor==actor.id&&x.heal))throw new ArgumentException("回血兑换正在进行");
            int cost=50+Mathf.CeilToInt(game.time/60*20);var team=game.teams[actor.team];if(team.coins<cost)throw new ArgumentException("金币不足");team.coins-=cost;ammoOrders.Add(new DeveloperPurchase {actor=actor.id,heal=true,due=game.time+6});game.Log(actor.id+" 远程回血：金币 -"+cost+"，6秒后回复上限血量60%");
        }
        void UpdateGameZones()
        {
            if(gameZones==null)return;foreach(string team in new[]{"red","blue"}){baseLowest[team]=Mathf.Min(baseLowest[team],game.buildings[team+"_base"].hp);game.buildings[team+"_outpost"].rebuildProgress=0;}
            for(int i=ammoOrders.Count-1;i>=0;i--){var order=ammoOrders[i];if(!game.actors.TryGetValue(order.actor,out var actor)||order.heal&&!actor.Alive){ammoOrders.RemoveAt(i);continue;}if(game.time>=order.due){if(order.heal)actor.hp=Mathf.Min(actor.maxHp,actor.hp+actor.maxHp*.6f);else actor.allowedAmmo+=order.quantity;ammoOrders.RemoveAt(i);}}
            var centralOwners=new Dictionary<string,string>();foreach(var actor in game.actors.Values)if(actor.Alive&&game.time>=actor.weakUntil)foreach(var z in ZonesAt(actor))if(z.kind=="central"&&!centralOwners.ContainsKey(z.id))centralOwners[z.id]=actor.team;
            foreach(var actor in game.actors.Values)
            {
                actor.wirelessCharging=false;foreach(var direct in gameZones.zones)if(direct.kind=="supply"&&direct.team==actor.team&&ZoneContains(direct,actor.position))actor.wirelessCharging=true;
                actor.zoneDefense=0;actor.zoneVulnerability=0;actor.zoneCooling=0;actor.coolingBuff=game.time<actor.tunnelCoolingUntil?2:1;var zones=ZonesAt(actor);bool supply=false,recovery=false,rebuilding=false;string crossingCard=null;var occupiedFortress=new HashSet<string>();
                foreach(var zone in zones)
                {
                    bool own=zone.team==actor.team;var post=game.buildings[zone.team+"_outpost"];
                    if(zone.kind=="supply"&&own){supply=true;recovery=true;if(actor.Alive)actor.hp=Mathf.Min(actor.maxHp,actor.hp+actor.maxHp*(game.time>=240&&OutOfCombat(actor)?.25f:.1f)*.1f);}
                    if(actor.Alive&&game.time>=actor.weakUntil)
                    {
                        if(zone.kind=="base"&&own){actor.zoneDefense=Mathf.Max(actor.zoneDefense,.5f);recovery=true;}
                        if(zone.kind=="trapezoid"&&own)actor.zoneDefense=Mathf.Max(actor.zoneDefense,.5f);
                        if(zone.kind=="central"&&centralOwners[zone.id]==actor.team)actor.zoneDefense=Mathf.Max(actor.zoneDefense,.25f);
                        if(zone.kind=="outpost"&&(own&&post.hp>0 || !own&&post.hp<=0&&game.buildings[actor.team+"_outpost"].hp>0&&game.time<300)){actor.zoneDefense=Mathf.Max(actor.zoneDefense,.25f);recovery=true;}
                        if(zone.kind=="fortress"&&post.destroyedBefore)
                        {
                            if(own)
                            {
                                if(!fortressOwners.TryGetValue(zone.id,out string owner)||!game.actors.TryGetValue(owner,out var occupant)||!occupant.Alive||!ZoneContains(zone,occupant.position))fortressOwners[zone.id]=actor.id;
                                if(fortressOwners[zone.id]==actor.id){actor.zoneDefense=Mathf.Max(actor.zoneDefense,.5f);actor.zoneCooling=Mathf.Min(75,Mathf.Floor((5000-baseLowest[actor.team])/40));actor.fortressReserveMax=Mathf.Min(500,100+2*Mathf.FloorToInt((5000-baseLowest[actor.team])/15));if(!actor.inFortress)actor.fortressReserve=actor.fortressReserveMax;actor.inFortress=true;}
                            }
                            else if(game.time>=180)
                            {
                                var targetBase=game.buildings[zone.team+"_base"];if(targetBase.armorTarget<=0){actor.zoneVulnerability=1;string key=actor.id+"|"+zone.id;occupiedFortress.Add(key);if(!fortressEntered.TryGetValue(key,out var timer))fortressEntered[key]=timer=new DeveloperFortressTimer {key=key};timer.progress+=.1f;timer.lastPresent=game.time;if(timer.progress>=20){targetBase.armorTarget=1;actor.zoneVulnerability=0;game.Log(zone.team+" 基地：敌方堡垒连续占领20秒，展开护甲");}}
                            }
                        }
                    }
                    if((zone.kind=="base"&&own)||(zone.kind=="outpost"&&own&&post.hp>0))recovery=true;
                    if(zone.kind=="outpost"&&own&&post.hp<=0&&actor.Alive&&game.time>=actor.weakUntil&&game.time<300&&game.buildings[actor.team+"_base"].rebuildChances>0)
                    {rebuilding=true;string key=actor.id;postRebuildProgress.TryGetValue(key,out float progress);progress+=.1f;postRebuildProgress[key]=progress;post.rebuildProgress=Mathf.Max(post.rebuildProgress,progress);if(progress>=10){post.hp=750;post.rebuildProgress=0;postRebuildProgress.Remove(key);game.buildings[actor.team+"_base"].rebuildChances--;game.Log(actor.team+" 前哨重建 750HP；保持停转");}}
                    if(zone.group.Length>0){crossingCard=zone.id;ProcessCrossing(actor,zone);}else if(crossingState.TryGetValue(actor.id,out var c)&&zone.kind!="central")c.step=0;
                }
                if(crossingCard==null&&crossingState.TryGetValue(actor.id,out var crossing))crossing.lastCard=null;
                if(!actor.Alive){actor.terrainBuffUntil=actor.tunnelCoolingUntil=0;actor.inFortress=false;if(actor.deadAt>=0&&(supply||game.buildings[actor.team+"_base"].hp<2000))actor.respawnPoints+=.3f;}
                if(recovery&&actor.Alive&&actor.weakUntil>game.time){actor.weakUntil=game.time;actor.invulnerableUntil=Mathf.Min(actor.invulnerableUntil,actor.revivedAt+10);}
                bool stillInFortress=false;foreach(var zone in zones)if(zone.kind=="fortress"&&zone.team==actor.team)stillInFortress=true;if(!stillInFortress){actor.inFortress=false;actor.fortressReserve=0;}
                if(!rebuilding)postRebuildProgress.Remove(actor.id);
                foreach(var key in new List<string>(fortressEntered.Keys))if(key.StartsWith(actor.id+"|",StringComparison.Ordinal)&&!occupiedFortress.Contains(key)&&game.time-fortressEntered[key].lastPresent>3)fortressEntered.Remove(key);
            }
        }
        void ProcessCrossing(DeveloperActor actor,GameZone zone)
        {
            if(!actor.Alive||game.time<actor.weakUntil)return;if(!crossingState.TryGetValue(actor.id,out var c))crossingState[actor.id]=c=new DeveloperCrossing {actor=actor.id};if(c.lastCard==zone.id)return;c.lastCard=zone.id;
            bool tunnel=zone.group.Contains("tunnel");float window=tunnel||zone.group.Contains("road")?3:zone.group.Contains("highland")?5:10;
            if(c.group!=zone.group||game.time-c.began>window||c.step==0){c.group=zone.group;c.step=0;c.began=game.time;c.first=zone.order;}
            if(tunnel){if(c.step==0&&(zone.order==0||zone.order==2)){c.first=zone.order;c.step=1;return;}if(c.step==1&&zone.order==1){c.step=2;return;}if(c.step!=2||zone.order==c.first||zone.order==1)return;}
            else{if(c.step==0&&zone.order==0){c.step=1;return;}if(c.step!=1||zone.order!=1)return;}
            string kind=tunnel?"tunnel":zone.group.Contains("road")?"road":zone.group.Contains("highland")?"highland":"fly";
            if(kind=="road"&&game.time-c.lastRoad<15){c.step=0;return;}
            float duration=tunnel?10:kind=="road"?5:30;actor.defenseBuff=tunnel||game.time<actor.terrainBuffUntil?.5f:.25f;actor.terrainBuffUntil=Mathf.Max(actor.terrainBuffUntil,game.time+duration);if(tunnel)actor.tunnelCoolingUntil=game.time+120;
            if(!c.rewarded.Contains(kind)){c.rewarded.Add(kind);game.Xp(actor,300);}if(kind=="road")c.lastRoad=game.time;c.step=0;game.Log(actor.id+" 地形跨越 "+kind+" · 防御增益");
        }
        DeveloperZoneSnapshot CaptureGameZones()
        {
            var snapshot=new DeveloperZoneSnapshot();foreach(var p in baseLowest)snapshot.lowest.Add(new DeveloperNumber {name=p.Key,value=p.Value});foreach(var p in postRebuildProgress)snapshot.rebuild.Add(new DeveloperNumber {name=p.Key,value=p.Value});foreach(var p in zonePresence)snapshot.presence.Add(new DeveloperNumber {name=p.Key,value=p.Value});foreach(var p in fortressOwners)snapshot.owners.Add(new DeveloperName {name=p.Key,value=p.Value});snapshot.fortress.AddRange(fortressEntered.Values);snapshot.purchases.AddRange(ammoOrders);snapshot.crossings.AddRange(crossingState.Values);return snapshot;
        }
        void RestoreGameZones(DeveloperZoneSnapshot snapshot)
        {
            if(snapshot==null)return;foreach(var p in snapshot.lowest)baseLowest[p.name]=p.value;foreach(var p in snapshot.rebuild)postRebuildProgress[p.name]=p.value;foreach(var p in snapshot.presence)zonePresence[p.name]=p.value;foreach(var p in snapshot.owners)fortressOwners[p.name]=p.value;foreach(var p in snapshot.fortress)fortressEntered[p.key]=p;ammoOrders.AddRange(snapshot.purchases);foreach(var p in snapshot.crossings)crossingState[p.actor]=p;
        }
    }
}
