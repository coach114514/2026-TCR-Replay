using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class LidarAuditCase {public string name,error="";public bool passed;public int points,surfaces;}
        [Serializable] class LidarAuditReport {public bool passed;public List<LidarAuditCase> cases=new List<LidarAuditCase>();}
        void CheckLidarAudit(LidarAuditReport report,string name,Action check)
        {
            var row=new LidarAuditCase {name=name,points=lidarPointCount,surfaces=lidarGeometry?.Count??0};report.cases.Add(row);
            try{check();row.passed=true;}catch(Exception ex){row.error=ex.ToString();}
        }
        IEnumerator WaitForLidarAudit()
        {float deadline=Time.realtimeSinceStartup+15;while(lidarPointCount==0&&Time.realtimeSinceStartup<deadline)yield return null;}
        void CheckLidarReturns()
        {
            // Compare the latest RAW scan to its frozen query scene. Display
            // accumulation contains old moving vehicles and cannot be checked
            // against current geometry. Complete a pending newer scene first.
            if(lidarPending)FinishLidarFrame();
            DeveloperChecks.Require(lidarGeometry!=null&&lidarGeometry.PhysicsScene.IsValid()&&lidarPointCount>1000,"点云缺失或扫描场景失效");
            LidarPoint[] points=lidarPointArray;int count=lidarValidPoints;
            if(lidarSettings.accumulation>.001f)foreach(var frame in lidarFrames){points=frame.points;count=frame.count;}
            for(int i=0;i<count;i+=Mathf.Max(1,count/100))
            {
                var p=points[i];Vector3 origin=LidarOrigin((int)p.meta.x),delta=p.position-origin;
                DeveloperChecks.Require(lidarGeometry.PhysicsScene.Raycast(origin,delta.normalized,out var hit,delta.magnitude+.02f,~0,QueryTriggerInteraction.Ignore)&&Mathf.Abs(hit.distance-delta.magnitude)<.025f,"扫描第一回波被穿透");
            }
        }
        IEnumerator LidarAudit(string output)
        {
            Directory.CreateDirectory(output);developerSmokeActive=true;game.running=false;var report=new LidarAuditReport();yield return null;
            CheckLidarAudit(report,"scan-math-reference",ReplayLidarChecks.Run);
            foreach(int source in new[]{0,1,2})
            {
                SetLidar(false);lidarSettings=new LidarSettings {source=source};SetLidar(true);yield return WaitForLidarAudit();
                CheckLidarAudit(report,"developer-source-"+source,CheckLidarReturns);
            }
            ResetDeveloper();yield return null;yield return WaitForLidarAudit();CheckLidarAudit(report,"reset-during-scan",CheckLidarReturns);
            var oldScene=lidarGeometry.PhysicsScene;int original=lidarGeometry.Count;
            game.Add("lidar-audit","blue",new Vector3(8,0,9),"jiageng");yield return null;
            CheckLidarAudit(report,"add-vehicle-rebuild",()=>DeveloperChecks.Require(lidarGeometry.Count>original&&lidarGeometry.PhysicsScene==oldScene,"加车未刷新／隔离场景未复用"));
            game.Remove("lidar-audit");yield return null;
            CheckLidarAudit(report,"remove-vehicle-rebuild",()=>DeveloperChecks.Require(lidarGeometry.Count==original,"删车留下扫描几何"));
            lidarSettings.accumulation=1;InvalidateLidar();game.running=true;gameInput=new DeveloperInput();double accumulator=0,last=Time.realtimeSinceStartupAsDouble;float start=Time.realtimeSinceStartup;int peak=0;
            while(Time.realtimeSinceStartup-start<3)
            {double now=Time.realtimeSinceStartupAsDouble;accumulator+=now-last;last=now;while(accumulator>=DeveloperWorld.Step){GameStep();accumulator-=DeveloperWorld.Step;}peak=Math.Max(peak,lidarPointCount);yield return null;}
            game.running=false;CheckLidarAudit(report,"bounded-dual-accumulation",()=>DeveloperChecks.Require(peak>1000&&peak<=LidarPointLimit,"点云累积上限失效"));
            SetLidar(false);yield return null;
            CheckLidarAudit(report,"off-releases-resources",()=>DeveloperChecks.Require(!lidarPending&&!lidarCommands.IsCreated&&lidarBuffer==null&&lidarGeometry==null&&lidarPointCount==0,"关闭扫描未释放资源"));
            EnterReplay();Seek(93);lidarSettings=new LidarSettings {source=2};SetLidar(true);yield return WaitForLidarAudit();CheckLidarAudit(report,"replay-dual-cloud",CheckLidarReturns);
            Seek(5);CheckLidarAudit(report,"seek-clears-cloud",()=>DeveloperChecks.Require(lidarPointCount==0&&!lidarPending,"跳转残留旧扫描"));yield return WaitForLidarAudit();CheckLidarAudit(report,"seek-new-cloud",CheckLidarReturns);
            LoadMatch((matchIndex+1)%catalog.matches.Length);yield return null;yield return WaitForLidarAudit();CheckLidarAudit(report,"match-switch-cloud",CheckLidarReturns);
            SwitchField((fieldIndex+1)%fieldCatalog.fields.Length);yield return null;yield return WaitForLidarAudit();CheckLidarAudit(report,"field-switch-cloud",CheckLidarReturns);
            EnterMenu();yield return null;CheckLidarAudit(report,"menu-releases-scanner",()=>DeveloperChecks.Require(!lidarEnabled&&lidarGeometry==null&&lidarBuffer==null,"返回菜单扫描残留"));
            report.passed=report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("LIDAR_AUDIT "+report.passed);Application.Quit(report.passed?0:80);
        }
        IEnumerator LidarApiAudit(string output)
        {
            Directory.CreateDirectory(output);developerSmokeActive=true;var report=new LidarAuditReport();
            CheckLidarAudit(report,"public-frame-contract",LidarFrameChecks.Run);
            var api=LidarFrames;SetLidar(false);lidarSettings=new LidarSettings {source=2,hz=20,accumulation=1};SetLidar(true);
            yield return WaitForLidarAudit();
            CheckLidarAudit(report,"default-zero-copy",()=>DeveloperChecks.Require(lidarApi.SnapshotCopies==0&&!api.TryGetLatestFrame(out _),"默认进行了点云快照复制"));
            LidarScanFrame seen=null;int received=0;var changes=new List<LidarStreamReset>();
            using(api.Subscribe(frame=>{seen=frame;received++;},change=>changes.Add(change)))
            {
                api.RetainLatestFrame=true;InvalidateLidar();yield return WaitForLidarAudit();
                CheckLidarAudit(report,"raw-frame-and-captured-poses",()=>{
                    DeveloperChecks.Require(seen!=null&&seen.Points.Count==lidarValidPoints&&seen.Sensors.Count==2&&seen.AttemptedRays==24000,"API未返回原始双机位帧");
                    foreach(var p in seen.Points){var sensor=seen.Sensors[p.SensorId];DeveloperChecks.Require(Mathf.Abs(Vector3.Distance(p.WorldPosition,sensor.WorldPosition)-p.RangeMetres)<.001f&&p.TimeOffsetSeconds>=0&&p.TimeOffsetSeconds<seen.DurationSeconds,"点距离/时间/机位不一致");}
                    DeveloperChecks.Require(api.TryGetLatestFrame(out var latest)&&ReferenceEquals(latest,seen),"公共取帧未保留最新帧");
                });
                var saved=seen;Vector3 first=saved.Points[0].WorldPosition;int before=received;playing=true;float start=Time.realtimeSinceStartup;
                while(received-before<25&&Time.realtimeSinceStartup-start<10)yield return null;playing=false;
                CheckLidarAudit(report,"retained-frame-and-raw-not-accumulated",()=>DeveloperChecks.Require(received-before>=25&&saved.Points[0].WorldPosition==first&&seen.Sequence>saved.Sequence&&seen.Points.Count<=seen.AttemptedRays&&lidarPointCount>seen.Points.Count,"旧快照被覆盖/单帧混入显示累积"));
                Seek(5);
                CheckLidarAudit(report,"seek-invalidates-pull-immediately",()=>DeveloperChecks.Require(!api.TryGetLatestFrame(out _)&&changes.Exists(c=>c.Reason==LidarResetReason.TimelineJump),"跳转未即时撤掉旧帧/通知"));
                yield return WaitForLidarAudit();
                CheckLidarAudit(report,"post-seek-new-epoch",()=>DeveloperChecks.Require(seen.Epoch>saved.Epoch&&seen.SimulationTimeSeconds==5,"跳转后帧时间/代次错误"));
                long epoch=api.Epoch;LoadMatch((matchIndex+1)%catalog.matches.Length);yield return WaitForLidarAudit();
                CheckLidarAudit(report,"match-identifies-new-stream",()=>DeveloperChecks.Require(api.Epoch>epoch&&seen.GameId==match.gameId,"换局帧身份/代次错误"));
                epoch=api.Epoch;SwitchField((fieldIndex+1)%fieldCatalog.fields.Length);yield return WaitForLidarAudit();
                CheckLidarAudit(report,"field-identifies-new-stream",()=>DeveloperChecks.Require(api.Epoch>epoch&&seen.FieldId==field.id,"换场地帧身份/代次错误"));
                SetLidar(false);CheckLidarAudit(report,"disabled-invalidates-pull",()=>DeveloperChecks.Require(!api.TryGetLatestFrame(out _)&&changes.Exists(c=>c.Reason==LidarResetReason.Disabled),"关闭未撤掉帧/通知"));
                SetLidar(true);yield return WaitForLidarAudit();api.RetainLatestFrame=false;
            }
            long copies=lidarApi.SnapshotCopies;InvalidateLidar();yield return WaitForLidarAudit();
            CheckLidarAudit(report,"unsubscribed-zero-copy",()=>DeveloperChecks.Require(lidarApi.SnapshotCopies==copies&&!api.TryGetLatestFrame(out _),"解除订阅后仍复制/保留点云"));
            EnterMenu();yield return null;CheckLidarAudit(report,"api-menu-releases-scanner",()=>DeveloperChecks.Require(lidarGeometry==null&&!lidarPending,"API影响菜单清理"));
            report.passed=report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("LIDAR_API_AUDIT "+report.passed);Application.Quit(report.passed?0:82);
        }
        IEnumerator ReplayLidarAudit(string output)
        {
            Directory.CreateDirectory(output);developerSmokeActive=true;var report=new LidarAuditReport();yield return null;
            CheckLidarAudit(report,"scan-math-reference",ReplayLidarChecks.Run);
            foreach(int source in new[]{0,1,2})foreach(int hz in new[]{10,20})
            {
                SetLidar(false);lidarSettings=new LidarSettings {source=source,hz=hz,quality=100};SetLidar(true);
                yield return WaitForLidarAudit();CheckLidarAudit(report,"fresh-replay-"+source+"-"+hz+"Hz",CheckLidarReturns);
            }
            foreach(int quality in new[]{25,50,100})
            {
                lidarSettings.quality=quality;InvalidateLidar();yield return WaitForLidarAudit();
                CheckLidarAudit(report,"replay-quality-"+quality,CheckLidarReturns);
            }
            lidarSettings.source=2;lidarSettings.hz=20;lidarSettings.quality=100;lidarSettings.accumulation=1;InvalidateLidar();
            playing=true;float started=Time.realtimeSinceStartup;int frames=lidarFrame,peak=0;
            while(Time.realtimeSinceStartup-started<35)
            {peak=Math.Max(peak,lidarPointCount);yield return null;}
            playing=false;
            CheckLidarAudit(report,"sustained-replay-35s",()=>{CheckLidarReturns();DeveloperChecks.Require(lidarFrame-frames>=100&&peak<=LidarPointLimit,"持续扫描没有更新／累积失去上限");});
            for(int i=0;i<8;i++)
            {
                SetLidar(false);SetLidar(true);Seek(i%2==0?200:5);yield return WaitForLidarAudit();
                CheckLidarAudit(report,"replay-toggle-seek-"+i,CheckLidarReturns);
            }
            LoadMatch((matchIndex+1)%catalog.matches.Length);yield return WaitForLidarAudit();CheckLidarAudit(report,"fresh-replay-match-switch",CheckLidarReturns);
            SwitchField((fieldIndex+1)%fieldCatalog.fields.Length);yield return WaitForLidarAudit();CheckLidarAudit(report,"fresh-replay-field-switch",CheckLidarReturns);
            lidarSettings.viewMode=1;yield return null;yield return null;
            CheckLidarAudit(report,"fresh-replay-inspection-window",()=>DeveloperChecks.Require(lidarCamera&&lidarTexture&&lidarTexture.IsCreated(),"点云观察窗没有渲染"));
            EnterMenu();yield return null;
            CheckLidarAudit(report,"fresh-replay-menu-releases",()=>DeveloperChecks.Require(!lidarPending&&!lidarCommands.IsCreated&&lidarBuffer==null&&lidarGeometry==null,"回放返回菜单资源未释放"));
            report.passed=report.cases.TrueForAll(c=>c.passed);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("REPLAY_LIDAR_AUDIT "+report.passed);Application.Quit(report.passed?0:81);
        }
    }
}
