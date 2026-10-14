using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class LibraryRuntimeReport
        {
            public int matches,shots,mechanismChecks,lampChecks,tagDefinitions,searchChecks,cachePeak;
            public double queryP95Ms,queryMaxMs,loadMaxMs,coldSeekMs;public float idleFps,denseFps;
            public string gpu;public bool baseArmorInterfaceOnly,platformsVerified,deterministic,asyncCancellationVerified;
            public List<string> sampleGames=new List<string>();
        }
        void LibraryRequire(bool condition,string message) {if(!condition)throw new InvalidOperationException("全量回放验证失败："+message);}
        IEnumerator LibrarySmoke(string[] args)
        {
            int arg=Array.IndexOf(args,"--library-smoke");string output=arg+1<args.Length?args[arg+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;
            var report=new LibraryRuntimeReport {gpu=SystemInfo.graphicsDeviceName,tagDefinitions=catalog.tagDefinitions.Length};
            bool failed=false;showProjectiles=false;
            int denseIndex=0;float denseTime=0;int peak=0;
            for(int i=0;i<catalog.matches.Length;i++)
            {
                try
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();LoadMatch(i);watch.Stop();
                    report.loadMaxMs=Math.Max(report.loadMaxMs,watch.Elapsed.TotalMilliseconds);
                    LibraryRequire(error.Length==0 && match.gameId==catalog.matches[i].gameId,"身份或加载异常");
                    report.matches++;report.shots+=shots.Count;report.cachePeak=Math.Max(report.cachePeak,libraryCache.Count);
                    var samples=new[]{0f,1f,5f,60.5f,179.99f,180f,Mathf.Min(240,match.duration),match.duration};
                    foreach(float t in samples)
                    {
                        Seek(t);
                        foreach(string team in new[]{"red","blue"})
                        {
                            LibraryRequire(mechanismTimeline.BaseArmor(team,t).pose==BaseArmorPose.Unknown,"无记录时猜测了开甲");
                            bool baseLamp=mechanismTimeline.GuideLamp(team,true,t,out bool baseKnown);
                            bool outpostLamp=mechanismTimeline.GuideLamp(team,false,t,out bool outpostKnown);
                            LibraryRequire(baseKnown==outpostKnown && (!baseKnown || baseLamp!=outpostLamp),"引导灯未互斥切换");
                            if(baseKnown)
                            {
                                LibraryRequire(guideMaterials[team+"_base"].GetFloat("_GuideLight")== (baseLamp?1:0),"基地发光材质不同步");
                                LibraryRequire(guideMaterials[team+"_outpost"].GetFloat("_GuideLight")== (outpostLamp?1:0),"前哨发光材质不同步");
                                report.lampChecks++;
                            }
                            float angle=mechanismTimeline.OutpostAngle(team,t);Seek(0);Seek(t);
                            LibraryRequire(Mathf.Abs(angle-mechanismTimeline.OutpostAngle(team,t))<.001f,"跳转改变了前哨角度");
                            report.mechanismChecks++;
                        }
                    }
                    int left=0;
                    for(int s=0;s<shots.Count;s++)
                    {
                        while(shots[left].time<shots[s].time-8)left++;
                        if(s-left+1>peak){peak=s-left+1;denseIndex=i;denseTime=shots[s].time+.01f;}
                    }
                }
                catch(Exception ex){Debug.LogException(ex);failed=true;}
                if(failed){Application.Quit(51);yield break;}
                if((i+1)%25==0)Debug.Log($"REPLAY_LIBRARY_PROGRESS matches={report.matches} shots={report.shots} cache={report.cachePeak}");
                yield return null;
            }
            try
            {
                LibraryRequire(report.matches==613 && report.shots==1200632,"未覆盖全量比赛或发弹");
                LibraryRequire(report.cachePeak<=3,"加载缓存没有上限");
                var watch=System.Diagnostics.Stopwatch.StartNew();var timings=new List<double>();
                foreach(string q in new[]{"嘉庚","jiageng","jg","国防科大","gfkd","nudt","北部","嘉庚 国防","东莞"})
                    for(int n=0;n<20;n++)
                    {
                        watch.Restart();var found=searchIndex.Query(q,m=>true,n%5);watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);
                        if(q=="嘉庚" || q=="jiageng" || q=="jg")LibraryRequire(found.Count==13,"全名/拼音/简称查询不一致");
                        report.searchChecks++;
                    }
                timings.Sort();report.queryP95Ms=timings[(int)(timings.Count*.95)];report.queryMaxMs=timings.Last();
                LibraryRequire(report.queryP95Ms<=50,"热查询超过50ms");
                LibraryRequire(searchIndex.ResolveSchool("厦门大学")!="厦门大学嘉庚学院" && searchIndex.ResolveSchool("嘉庚")=="厦门大学嘉庚学院","学校身份串线");
                VerifyLibraryFilters();report.searchChecks+=11;
                foreach(string team in new[]{"red","blue"})
                {
                    var marker=Array.Find(field.landmarks,m=>m.team==team && m.role=="radar");
                    LibraryRequire(marker!=null,"雷达视角未登记");AttachMarker(marker,false);
                    LibraryRequire(Vector3.Distance(cameraRig.Position,markerEyes[team+"_radar"].position)<.001f,"雷达视角没有绑定镜头");
                    LibraryRequire(Mathf.Abs(cameraRig.Position.y-3.925f)<.01f,"雷达高度错误");
                    var platform=buildingNodes[team+"_radar_platform"];
                    cameraRig.Free(platform.position+Vector3.up,Quaternion.identity);ConstrainCameraGround();
                    LibraryRequire(cameraRig.Position.y<1.1f,"平台下方相机被抬到平台上");
                    cameraRig.Free(platform.position+Vector3.up*2.55f,Quaternion.identity);ConstrainCameraGround();
                    LibraryRequire(cameraRig.Position.y>=2.57f && cameraRig.Position.y<2.60f,"平台地面碰撞失效");
                }
                report.platformsVerified=true;report.baseArmorInterfaceOnly=true;report.deterministic=true;
            }
            catch(Exception ex){Debug.LogException(ex);failed=true;}
            if(failed){Application.Quit(52);yield break;}
            // Async cancellation: only the most recently requested game may bind.
            RequestMatch(0);RequestMatch(1);RequestMatch(2);
            while(loading)yield return null;
            if(match.gameId!=catalog.matches[2].gameId){Debug.LogError("异步旧请求覆盖了新比赛");Application.Quit(53);yield break;}
            report.asyncCancellationVerified=true;
            LoadMatch(0);Seek(60);SetCameraMode(ReplayCameraMode.Observe,false);
            int originalFps=fpsPreference;bool originalSync=vsyncPreference;
            fpsPreference=240;vsyncPreference=false;QualitySettings.vSyncCount=0;Application.targetFrameRate=240;
            float before=Time.unscaledTime;int frame=Time.frameCount;
            while(Time.unscaledTime-before<3)yield return null;
            report.idleFps=(Time.frameCount-frame)/(Time.unscaledTime-before);
            showProjectiles=true;LoadMatch(denseIndex);
            var seekWatch=System.Diagnostics.Stopwatch.StartNew();Seek(denseTime);seekWatch.Stop();report.coldSeekMs=seekWatch.Elapsed.TotalMilliseconds;
            playing=true;before=Time.unscaledTime;frame=Time.frameCount;
            while(Time.unscaledTime-before<4)yield return null;
            report.denseFps=(Time.frameCount-frame)/(Time.unscaledTime-before);playing=false;
            report.sampleGames.Add(match.gameId);fpsPreference=originalFps;vsyncPreference=originalSync;ApplyRenderRate();
            // Capture real success, live progress, rebuild and guide lamps.
            showProjectiles=false;
            var selectedGames=new HashSet<int> {0,denseIndex};
            foreach(string code in new[]{"T07","T19","T20","T31","T13"})
            {
                int index=Array.FindIndex(catalog.matches,m=>m.tags.Any(t=>t.id==code && t.status==1));
                if(index>=0)selectedGames.Add(index);
            }
            foreach(int index in selectedGames)
            {
                LoadMatch(index);var m=catalog.matches[index];
                var point=m.bookmarks.FirstOrDefault(p=>p.name.Contains("机关"));Seek(point==null?120:point.t);
                report.sampleGames.Add(match.gameId);
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"game-"+match.gameId+".png"));
                yield return null;
            }
            LoadMatch(Array.FindIndex(catalog.matches,m=>m.redSchool.Contains("嘉庚") || m.blueSchool.Contains("嘉庚")));Seek(120);
            searchQuery="嘉庚";chooser=true;RefreshSearch(true);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"library-search.png"));yield return null;
            searchQuery="";schoolQuery="嘉庚";tagFilters["T19"]=1;RefreshSearch(true);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"library-filter.png"));yield return null;
            chooser=false;displayChooser=true;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"display-settings.png"));yield return null;
            displayChooser=false;
            foreach(string team in new[]{"red","blue"})
            {
                AttachMarker(Array.Find(field.landmarks,m=>m.team==team && m.role=="radar"),false);
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-radar-view.png"));yield return null;
            }
            SetCameraMode(ReplayCameraMode.Observe,false);topView=false;landmarkLabels=false;allLabels=false;distance=3;pitch=20;
            foreach(string team in new[]{"red","blue"})
            {
                var entry=Array.Find(catalog.matches,m=>m.tags.Any(t=>t.id=="T12" && t.team==team && t.status==1));
                LoadMatch(Array.IndexOf(catalog.matches,entry));Seek(entry.duration-2);
                SelectUnit(units.FindIndex(u=>u.data.id==team+"_base"));
                LibraryRequire(guideLenses.TryGetValue(team+"_base",out var lens),"基地CAD没有独立灯面");
                Vector3 eye=lens.position+lens.forward*.42f+Vector3.right*.08f;
                SetCameraMode(ReplayCameraMode.Free,false);cameraRig.Free(eye,Quaternion.LookRotation(lens.position-eye,Vector3.up));UpdateCamera();
                float death=mechanismTimeline.OutpostStop(team);
                Seek(death-1);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-base-guide-off.png"));yield return null;
                Seek(entry.duration-2);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-base-guide.png"));yield return null;
            }
            File.WriteAllText(Path.Combine(output,"library-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log($"REPLAY_LIBRARY_SMOKE_OK matches={report.matches} shots={report.shots} queryP95={report.queryP95Ms:0.00}ms idleFPS={report.idleFps:0.0} denseFPS={report.denseFps:0.0} loadMax={report.loadMaxMs:0.0}ms coldSeek={report.coldSeekMs:0.0}ms");
            Application.Quit(0);
        }
        void VerifyLibraryFilters()
        {
            searchQuery="";schoolQuery="jg";RefreshSearch(true);
            LibraryRequire(searchResults.Count==13,"关注学校的短简称混入其他学校");
            schoolQuery="厦门大学";RefreshSearch(true);
            LibraryRequire(searchResults.Count>0 && searchResults.All(i=>catalog.matches[i].redSchool=="厦门大学" || catalog.matches[i].blueSchool=="厦门大学"),"全名学校筛选串到嘉庚");
            schoolQuery="";
            foreach(int region in new[]{1,2,3})
            {regionFilter=region;RefreshSearch(true);LibraryRequire(searchResults.Count==new[]{0,204,203,206}[region],"赛区筛选计数错误");}
            regionFilter=0;tagFilters["T07"]=1;RefreshSearch(true);LibraryRequire(searchResults.Count==11,"前哨重建筛选计数错误");
            tagFilters["T07"]=-1;RefreshSearch(true);LibraryRequire(searchResults.Count==602,"排除标签计数错误");
            tagFilters.Clear();tagFilters["C02"]=2;RefreshSearch(true);LibraryRequire(searchResults.Count==613,"缺少护甲状态不能筛选为未知");
            tagFilters.Clear();tagFilters["T19"]=1;RefreshSearch(true);LibraryRequire(searchResults.Count==228,"小机关成功筛选计数错误");
            tagFilters["T20"]=1;tagsAny=false;RefreshSearch(true);
            LibraryRequire(searchResults.Count==79,"大小机关AND条件计数错误");
            tagsAny=true;RefreshSearch(true);LibraryRequire(searchResults.Count==232,"大小机关OR条件计数错误");
            tagFilters.Clear();tagsAny=false;RefreshSearch(true);
        }
    }
}
