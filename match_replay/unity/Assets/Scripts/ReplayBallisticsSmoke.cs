using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class BallisticsReport
        {
            public int matches,shots,spatialShots,inferredTargets,damageSupportedTargets,collisions,armorContacts,validArmorContacts,outpostRingTargets,outpostDamageTargets,outpostRegionsChecked;
            public int replayChecks,poseChecks,poolPeak,cachePeak,maxWindowShots;public float fps,coldSeekMaxMs;
            public string peakGame;public float peakTime,peakFps;
            public bool rawHealthPreserved,labelsRestored,gravityAndBouncePassed,deterministic;
            public List<ShotProof> reviewedShots=new List<ShotProof>();
            public string source="报告matches数对应的真实对局源事件；目标/俯仰/弹速/碰撞为推定；独立演示不代表真实命中";
        }
        [Serializable] class ShotProof
        {
            public string game,eventId,shooter,calibre,target,reason,hitEvidence,alternative,region;
            public float sourceTime,displayTime,pitch,confidence,hpDrop,hpTime,pitchLow,pitchHigh;public int candidates,armorContacts,panel;
        }
        IEnumerator BallisticsSmoke(string[] args)
        {
            int index=Array.IndexOf(args,"--ballistics-smoke");string output=index+1<args.Length?args[index+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;
            var report=new BallisticsReport();bool failed=false;int peakMatch=0;
            try
            {
                ReplayRobotChecks.Contracts();
                ReplayRobotChecks.Require(BallisticMath.LowArc(new Vector3(0,0,10),25,9.81f,out var angle,out var travel),"平地低抛解失败");
                var impact=BallisticMath.Direction(0,angle)*25*travel-Vector3.up*4.905f*travel*travel;
                ReplayRobotChecks.Require(Vector3.Distance(impact,new Vector3(0,0,10))<.001f,"重力解算未落在目标点");
                ReplayRobotChecks.Require(!BallisticMath.LowArc(new Vector3(0,0,100),12,9.81f,out _,out _),"不可达目标被强行命中");
                ReplayRobotChecks.Require(BallisticMath.SweepBox(new Vector3(0,0,1),new Vector3(0,0,-1),Vector3.zero,Vector3.zero,Quaternion.identity,new Vector3(.0675f,.0625f,.006f),.0084f,out var fraction,out var normal),"高速弹丸穿过装甲");
                ReplayRobotChecks.Require(fraction<.5f && normal.z>.99f,"装甲碰撞面法线错误");
                var reflected=BallisticMath.Bounce(Vector3.back*25,Vector3.forward,Vector3.zero,.28f,.68f);
                ReplayRobotChecks.Require(reflected.z>0 && reflected.magnitude<25,"反弹未反向或能量增加");
                ReplayRobotChecks.Require(BallisticMath.Opacity(6.99f)==1 && Mathf.Abs(BallisticMath.Opacity(7.5f)-.5f)<.0001f && BallisticMath.Opacity(8)==0,"寿命渐隐错误");
                var wrapped=new EntityData {samples=new [] {new Sample {t=1,gun=359,hasGun=true},new Sample {t=2,gun=1,hasGun=true}}};
                BallisticMath.Heading(wrapped,1.5f,new BallisticConfig {headingOffset=0,headingSign=1},out float midpoint);
                ReplayRobotChecks.Require(Mathf.Abs(Mathf.DeltaAngle(midpoint,0))<.001f,"跨界方向转了一整圈");
                ReplayRobotChecks.Require(BallisticMath.HorizontalRing(Vector3.zero,Vector3.forward,new Vector3(0,1.2f,10),.275f,.10f,out var ringPoint) && Mathf.Abs(ringPoint.z-9.725f)<.001f && ringPoint.y==1.2f,"前哨环形区域没有选取靠近射手的一侧");
                ReplayRobotChecks.Require(!BallisticMath.HorizontalRing(Vector3.zero,Vector3.forward,new Vector3(1,1.2f,10),.275f,.10f,out _) && !BallisticMath.HorizontalRing(Vector3.zero,Vector3.forward,new Vector3(0,1.2f,-10),.275f,.10f,out _),"环形区域无视枪口朝向");
                var victim=new Unit {data=new EntityData {id="aim-evidence-fixture",samples=new[]{new Sample {t=8,hp=1500,hasHp=true},new Sample {t=9,hp=1400,hasHp=true},new Sample {t=10,hp=1400,hasHp=true},new Sample {t=11,hp=1350,hasHp=true},new Sample {t=12,hp=1350,hasHp=true}}}};
                var fixtureShot=new Shot {source=new ReplayEvent {t=10,category="17mm"},time=10.5f};
                ReplayRobotChecks.Require(HealthDropAfterShot(victim,fixtureShot,.5f,out var hpTime)==50 && hpTime==11,"发弹前掉血被错误匹配，或发弹后掉血被漏掉");
                hitEvents[victim.data.id]=new List<ReplayEvent> {new ReplayEvent {id="old-17",t=9,category="17mm"},new ReplayEvent {id="future-42",t=11,category="42mm"}};
                ReplayRobotChecks.Require(!DamageEvidence(victim,fixtureShot,.5f,out _,out _),"发弹前受击或错误口径被当作伤害证据");
                hitEvents[victim.data.id].Add(new ReplayEvent {id="future-17",t=11,category="17mm"});
                ReplayRobotChecks.Require(DamageEvidence(victim,fixtureShot,.5f,out _,out var hitId) && hitId=="future-17","发弹后同口径受击没有匹配");hitEvents.Remove(victim.data.id);
                fixtureShot.source.t=12;fixtureShot.time=12.5f;
                ReplayRobotChecks.Require(HealthDropAfterShot(victim,fixtureShot,.5f,out _)==0,"已经发生的历史掉血被当作本发弹证据");
                report.gravityAndBouncePassed=true;
            }
            catch(Exception e){Debug.LogException(e);failed=true;}
            if(failed){Application.Quit(31);yield break;}
            foreach(int m in SmokeGameIndices(args))
            {
                try
                {
                    LoadMatch(m);ReplayRobotChecks.Require(string.IsNullOrEmpty(error),"比赛加载失败");report.matches++;report.shots+=shots.Count;
                    int raw=0;foreach(var e in match.events)if(e.kind=="发弹" && (e.category=="17mm" || e.category=="42mm"))raw++;
                    ReplayRobotChecks.Require(raw==shots.Count,"同秒发弹丢失或重复");
                    var ids=new HashSet<string>();foreach(var shot in shots)ReplayRobotChecks.Require(ids.Add(shot.source.id),"弹丸源事件重复");
                    foreach(var u in units)if(u.robot!=null)
                    {
                        int sample=Mathf.Min(100,u.data.samples.Length-1);
                        var original=u.data.samples[sample].hp;Seek(u.data.samples[sample].t);ReplayRobotChecks.Require(u.sample.hp==original,"仿真覆盖源血量");
                        var p=new Vector3(field.width/2,0,field.depth/2);
                        if(u.robot.model.id=="jiageng")
                        {
                            float saved=u.robot.preview;u.robot.preview=.5f;UpdateRobotPose(u,p);
                            var half=VehicleGroundHeight(u,p)+u.robot.bounds.max.y;
                            u.robot.preview=1;UpdateRobotPose(u,p);var full=VehicleGroundHeight(u,p)+u.robot.bounds.max.y;
                            ReplayRobotChecks.Require(full<half-.02f && full-GroundHeight(p.x,p.z)<.25f,"麦轮全摊未降低高度或仍超过 250mm");
                            u.robot.preview=saved;report.poseChecks++;
                        }
                    }
                    InvalidateBallistics();
                    foreach(var post in units.FindAll(u=>u.data.role=="outpost"))
                    {
                        var panel=PanelAt(post,0,0);Vector3 center=post.cadNode?post.cadNode.TransformPoint(PassagePose.Vector(mechanismConfig.outpost.pivot)):panel.center;
                        Vector3 muzzle=center+Vector3.right*5-Vector3.up*.7f;
                        ReplayRobotChecks.Require(OutpostAimRegion(post,0,muzzle,Vector3.left,out var before,out var low,out var high) && OutpostAimRegion(post,31.3f,muzzle,Vector3.left,out var after,out _,out _) && Vector3.Distance(before.center,after.center)<.001f && high-low>.20f,"前哨目标仍依赖三个装甲的示意相位，或丢失高度范围");report.outpostRegionsChecked++;
                    }
                    // Sample across early/mid/late real firing and both calibres.
                    var reviewShots=new HashSet<Shot>();
                    for(int i=0;i<shots.Count;i+=Mathf.Max(1,shots.Count/12))reviewShots.Add(shots[i]);
                    foreach(var shot in shots)if(shot.source.category=="42mm"){reviewShots.Add(shot);break;}
                    // Include real outpost damage cases, which sparse evenly
                    // spaced sampling may miss. Keep the expensive path review
                    // bounded while checking every shot's geometric candidate.
                    int outpostReviews=0;
                    foreach(var shot in shots)
                    {
                        var aim=SolveAim(shot);
                        if(aim.region!="outpost_armor_ring")continue;report.outpostRingTargets++;
                        if(aim.evidence || aim.hpDrop>0)
                        {
                            report.outpostDamageTargets++;
                            if(outpostReviews++<3)reviewShots.Add(shot);
                        }
                        ReplayRobotChecks.Require(aim.panel==-1 && aim.pitch>=aim.pitchLow-.05f && aim.pitch<=aim.pitchHigh+.05f,"环区推定误报具体装甲板或俯仰越出环区");
                    }
                    foreach(var shot in reviewShots)
                    {
                        var aim=SolveAim(shot);if(aim.spatial)report.spatialShots++;
                        if(aim.target!="")report.inferredTargets++;if(aim.evidence)report.damageSupportedTargets++;
                        if(!aim.spatial)continue;
                        var path=CachedFlight(shot);report.collisions+=path.collisions;report.armorContacts+=path.armorContacts;report.validArmorContacts+=path.validArmorContacts;
                        report.reviewedShots.Add(new ShotProof {game=match.gameId,eventId=shot.source.id,sourceTime=shot.source.t,displayTime=shot.time,shooter=shot.shooter.data.id,calibre=shot.source.category,target=aim.target,reason=aim.reason,hitEvidence=aim.hitEvent,alternative=aim.alternative,region=aim.region,panel=aim.panel,pitchLow=aim.pitchLow,pitchHigh=aim.pitchHigh,hpTime=aim.hpTime,pitch=aim.pitch,confidence=aim.confidence,hpDrop=aim.hpDrop,candidates=aim.candidateCount,armorContacts=path.armorContacts});
                        var first=path.At(.3f);var second=ComputeFlight(shot).At(.3f);
                        ReplayRobotChecks.Require(Vector3.Distance(first,second)<.00001f,"同事件轨迹不确定");report.replayChecks++;
                    }
                    // Exercise the densest lifetime window of each actual match,
                    // including a cold seek rather than only cached steady playback.
                    int left=0,count=0;float denseTime=0;
                    for(int i=0;i<shots.Count;i++)
                    {
                        while(shots[left].time<shots[i].time-8)left++;
                        if(i-left+1>count){count=i-left+1;denseTime=shots[i].time+.001f;}
                    }
                    InvalidateBallistics();var watch=System.Diagnostics.Stopwatch.StartNew();Seek(denseTime);watch.Stop();
                    report.coldSeekMaxMs=Mathf.Max(report.coldSeekMaxMs,(float)watch.Elapsed.TotalMilliseconds);
                    ReplayRobotChecks.Require(activeProjectiles.Count<=ProjectileBudget && projectileVisualCount<=ProjectileBudget,"对象池超过显示预算");
                    if(count>report.maxWindowShots){report.maxWindowShots=count;report.peakGame=match.gameId;report.peakTime=denseTime;peakMatch=m;}
                    float t=shots.Count>0?shots[shots.Count/2].time+.3f:120.5f;Seek(t);
                    var positions=new Dictionary<string,Vector3>();foreach(var pair in activeProjectiles)positions[pair.Key.source.id]=pair.Value.root.transform.position;
                    Seek(0);Seek(t);
                    foreach(var pair in activeProjectiles)if(positions.TryGetValue(pair.Key.source.id,out var before))ReplayRobotChecks.Require(Vector3.Distance(before,pair.Value.root.transform.position)<.00001f,"跳转改变弹丸位置");
                    report.poolPeak=Mathf.Max(report.poolPeak,projectileVisualCount);report.cachePeak=Mathf.Max(report.cachePeak,collisionFrames.Count);
                }
                catch(Exception e){Debug.LogException(e);failed=true;}
                if(failed){Application.Quit(32);yield break;}
                Debug.Log($"REPLAY_BALLISTICS_PROGRESS matches={report.matches} shots={report.shots} pool={report.poolPeak} frames={report.cachePeak}");yield return null;
            }
            report.rawHealthPreserved=true;report.deterministic=true;
            LoadMatch(peakMatch);Seek(report.peakTime);playing=true;float denseStart=Time.unscaledTime;int denseFrame=Time.frameCount;
            for(int i=0;i<180;i++)yield return null;
            playing=false;report.peakFps=(Time.frameCount-denseFrame)/(Time.unscaledTime-denseStart);
            bool labels=allLabels;ToggleAllLabels();labelBounds.Add(new Rect());DrawLabels();
            report.labelsRestored=labelBounds.Count==0;ToggleAllLabels();report.labelsRestored&=allLabels==labels;
            LoadMatch(0);headings=true;showProjectiles=true;playing=false;
            // Find a live frame containing real shots for the overview.
            float chosen=120.5f;foreach(var shot in shots)if(shot.time>20 && shot.time<120 && SolveAim(shot).spatial){chosen=shot.time+.15f;break;}
            Seek(chosen);SetCameraMode(ReplayCameraMode.Observe,false);focus=new Vector3(14,0,7.5f);distance=32;pitch=58;yaw=0;
            UpdateCamera();yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"real-shooting-overview.png"));yield return null;
            var supported=shots.Find(s=>SolveAim(s).region=="outpost_armor_ring" && SolveAim(s).evidence && SolveAim(s).hpDrop>0)??shots.Find(s=>SolveAim(s).evidence && SolveAim(s).hpDrop>0);
            if(supported!=null)
            {
                Seek(supported.time+.1f);SelectUnit(units.IndexOf(supported.shooter));focus=supported.shooter.root.transform.position;distance=8;pitch=40;yaw=0;UpdateCamera();
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"inferred-aim-detail.png"));yield return null;
            }
            foreach(string role in new [] {"infantry3","infantry4"})
            {
                var unit=units.Find(u=>u.data.role==role && u.data.team=="red");
                var shot=shots.Find(s=>s.shooter==unit && s.time>20 && SolveAim(s).spatial);
                if(shot!=null)
                {
                    Seek(shot.time+.09f);SelectUnit(units.IndexOf(unit));cleanView=true;focus=unit.root.transform.position+Vector3.up*.22f;distance=1.6f;pitch=20;yaw=unit.robot.heading+35;UpdateCamera();
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,role+"-aim-projectile.png"));yield return null;
                }
            }
            // Review both postures with source geometry at an explicitly demo position.
            robotDemo=true;ClearProjectileVisuals();headings=false;
            foreach(var u in units){u.root.SetActive(false);u.visible=false;u.trail.enabled=false;}
            var wheel=units.Find(u=>u.data.role=="infantry4" && u.data.team=="red");wheel.root.SetActive(true);wheel.visible=true;
            foreach(float fold in new [] {.5f,1f})
            {
                wheel.robot.preview=fold;var position=new Vector3(6,0,9.4f);UpdateRobotPose(wheel,position);position.y=VehicleGroundHeight(wheel,position);wheel.root.transform.position=position;
                cleanView=true;focus=position+Vector3.up*.14f;distance=1.3f;pitch=17;yaw=30;UpdateCamera();
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,fold<1?"mecanum-half.png":"mecanum-full-lowered.png"));yield return null;
            }
            robotDemo=false;LoadMatch(0);headings=true;cleanView=false;allLabels=false;Seek(chosen);SetCameraMode(ReplayCameraMode.Observe,false);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"labels-hidden.png"));yield return null;
            allLabels=true;Seek(120.5f);SetCameraMode(ReplayCameraMode.Observe,false);float start=Time.unscaledTime;int frame=Time.frameCount;playing=true;
            for(int i=0;i<180;i++)yield return null;
            playing=false;report.fps=(Time.frameCount-frame)/(Time.unscaledTime-start);
            File.WriteAllText(Path.Combine(output,"ballistics-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log($"REPLAY_BALLISTICS_SMOKE_OK matches={report.matches} shots={report.shots} samples={report.replayChecks} targets={report.inferredTargets} contacts={report.armorContacts} fps={report.fps:0.0}");
            Application.Quit(report.labelsRestored?0:33);
        }
    }
}
