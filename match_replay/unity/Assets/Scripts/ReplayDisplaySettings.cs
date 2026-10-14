using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public class DisplayModeInfo {public int hz;public float actual;public string id;}
    [Serializable] public class DisplayBackendInfo {public bool ok;public string backend,connector,error;public int width,height;public float currentHz;public DisplayModeInfo[] modes;}
    public partial class ReplayViewer
    {
        bool vsyncPreference,displayBusy;int fpsPreference=60,hzPreference,displayResolution;
        float measuredFps,fpsSince,confirmationDeadline=-1;int fpsFrames;
        string displayMessage="",previousWindowsMode="";
        FullScreenMode previousWindowsFullscreen;int previousWindowWidth,previousWindowHeight;
        DisplayBackendInfo displayInfo;
        readonly FrameTiming[] frameTimings=new FrameTiming[1];double cpuMs,gpuMs;
        static readonly int[] FpsPresets={60,120,144,165,240,0};
        static readonly Vector2Int[] WindowSizes={new Vector2Int(1280,720),new Vector2Int(1600,900),new Vector2Int(1920,1080),new Vector2Int(2560,1600)};
        string DisplaySnapshot=>Path.Combine(Application.persistentDataPath,"display-previous.json");
        void InitializeDisplaySettings()
        {
            fpsPreference=PlayerPrefs.GetInt("replay.fps",60);vsyncPreference=PlayerPrefs.GetInt("replay.vsync",0)==1;
            hzPreference=PlayerPrefs.GetInt("replay.hz",0);displayResolution=PlayerPrefs.GetInt("replay.resolution",1);
            var size=WindowSizes[Mathf.Clamp(displayResolution,0,WindowSizes.Length-1)];windowWidth=size.x;windowHeight=size.y;
            if(!Screen.fullScreen && (Screen.width!=size.x || Screen.height!=size.y))Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
            ApplyRenderRate();fpsSince=Time.unscaledTime;
            StartCoroutine(QueryDisplay());
        }
        void ApplyRenderRate()
        {
            QualitySettings.vSyncCount=vsyncPreference?1:0;
            Application.targetFrameRate=vsyncPreference || fpsPreference==0?-1:fpsPreference;
            PlayerPrefs.SetInt("replay.fps",fpsPreference);PlayerPrefs.SetInt("replay.vsync",vsyncPreference?1:0);PlayerPrefs.Save();
        }
        static string QuoteArgument(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        Task<string> DisplayCommand(string action,int hz=0)
        {
            string script=Path.Combine(Application.streamingAssetsPath,"replay","display_backend.py");
            string arguments=QuoteArgument(script)+" "+action;
            if(hz>0)arguments+=" --hz "+hz;
            if(action!="query" && action!="verify")arguments+=" --snapshot "+QuoteArgument(DisplaySnapshot)+" --pid "+Process.GetCurrentProcess().Id;
            return Task.Run(()=>
            {
                using(var process=new Process {StartInfo=new ProcessStartInfo("/usr/bin/python3",arguments){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}})
                {
                    process.Start();var output=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(6000)){process.Kill();throw new TimeoutException("显示后端超时");}
                    if(output.Result.Length==0)throw new InvalidOperationException(stderr.Result);return output.Result;
                }
            });
        }
        IEnumerator QueryDisplay()
        {
            if(Application.platform==RuntimePlatform.LinuxPlayer || Application.platform==RuntimePlatform.LinuxEditor)
            {
                var task=DisplayCommand("query");while(!task.IsCompleted)yield return null;
                if(!task.IsFaulted)displayInfo=JsonUtility.FromJson<DisplayBackendInfo>(task.Result);
                else displayMessage="当前桌面后端不支持刷新率切换，仍可设置FPS";
            }
            else
            {
                var modes=Screen.resolutions.Where(r=>r.width==Screen.currentResolution.width && r.height==Screen.currentResolution.height)
                    .Select(r=>new DisplayModeInfo {hz=(int)Math.Round(r.refreshRateRatio.value),actual=(float)r.refreshRateRatio.value}).ToArray();
                displayInfo=new DisplayBackendInfo {ok=true,backend="Unity",currentHz=(float)Screen.currentResolution.refreshRateRatio.value,modes=modes};
            }
        }
        void UpdateDisplaySettings()
        {
            fpsFrames++;
            if(Time.unscaledTime-fpsSince>=.75f)
            {
                measuredFps=fpsFrames/(Time.unscaledTime-fpsSince);fpsFrames=0;fpsSince=Time.unscaledTime;
                FrameTimingManager.CaptureFrameTimings();
                if(FrameTimingManager.GetLatestTimings(1,frameTimings)>0){cpuMs=frameTimings[0].cpuFrameTime;gpuMs=frameTimings[0].gpuFrameTime;}
            }
            if(confirmationDeadline>0 && Time.unscaledTime>=confirmationDeadline && !displayBusy)StartCoroutine(RestoreDisplay());
        }
        IEnumerator ApplyPhysicalDisplay()
        {
            displayBusy=true;ReleaseMouse();
            if(Application.platform==RuntimePlatform.WindowsPlayer)
            {
                var current=Screen.currentResolution;previousWindowsMode=current.width+","+current.height+","+current.refreshRateRatio.numerator+","+current.refreshRateRatio.denominator;
                previousWindowsFullscreen=Screen.fullScreenMode;previousWindowWidth=Screen.width;previousWindowHeight=Screen.height;
                var mode=Screen.resolutions.Where(r=>r.width==current.width && r.height==current.height && Math.Abs(r.refreshRateRatio.value-hzPreference)<1).OrderBy(r=>Math.Abs(r.refreshRateRatio.value-hzPreference)).FirstOrDefault();
                if(mode.width==0)displayMessage="显示器不支持此刷新率";
                else {Screen.SetResolution(mode.width,mode.height,FullScreenMode.ExclusiveFullScreen,mode.refreshRateRatio);confirmationDeadline=Time.unscaledTime+14;displayMessage="已临时应用，请保留或等待恢复";yield return null;StartCoroutine(QueryDisplay());}
            }
            else if(Application.platform==RuntimePlatform.LinuxPlayer && displayInfo!=null && displayInfo.ok)
            {
                var task=DisplayCommand("apply",hzPreference);while(!task.IsCompleted)yield return null;
                if(task.IsFaulted)displayMessage=task.Exception.GetBaseException().Message;
                else
                {
                    var result=JsonUtility.FromJson<DisplayBackendInfo>(task.Result);
                    if(result.ok){displayInfo=result;confirmationDeadline=Time.unscaledTime+14;displayMessage="已临时应用，请保留或等待恢复";}
                    else displayMessage=result.error;
                }
            }
            else displayMessage="此平台当前只提供FPS设置，物理Hz请在桌面设置中调整";
            displayBusy=false;
        }
        IEnumerator RestoreDisplay()
        {
            displayBusy=true;confirmationDeadline=-1;
            if(Application.platform==RuntimePlatform.WindowsPlayer && previousWindowsMode.Length>0)
            {
                var parts=previousWindowsMode.Split(',');Screen.SetResolution(int.Parse(parts[0]),int.Parse(parts[1]),FullScreenMode.ExclusiveFullScreen,new RefreshRate {numerator=uint.Parse(parts[2]),denominator=uint.Parse(parts[3])});
                yield return null;Screen.SetResolution(previousWindowWidth,previousWindowHeight,previousWindowsFullscreen);
            }
            else if(File.Exists(DisplaySnapshot))
            {
                var task=DisplayCommand("restore");while(!task.IsCompleted)yield return null;
                if(!task.IsFaulted)
                {
                    var result=JsonUtility.FromJson<DisplayBackendInfo>(task.Result);
                    if(result.ok)displayInfo=result;
                    else {displayMessage="恢复失败："+result.error;displayBusy=false;yield break;}
                }
                else {displayMessage="恢复失败："+task.Exception.GetBaseException().Message;displayBusy=false;yield break;}
            }
            displayMessage="已恢复原显示模式";displayBusy=false;
        }
        IEnumerator ConfirmDisplay()
        {
            displayBusy=true;
            if(Application.platform==RuntimePlatform.LinuxPlayer)
            {
                var task=DisplayCommand("confirm");while(!task.IsCompleted)yield return null;
                if(task.IsFaulted || !JsonUtility.FromJson<DisplayBackendInfo>(task.Result).ok){displayMessage="保留失败，将自动恢复";displayBusy=false;yield break;}
            }
            confirmationDeadline=-1;PlayerPrefs.SetInt("replay.hz",hzPreference);PlayerPrefs.Save();displayBusy=false;displayMessage="已保留显示模式";
        }
        void DrawDisplaySettings()
        {
            Panel(new Rect(280,205,1040,520));Text(308,220,880,34,"显示设置 · 独显与帧率",title);
            if(Button(1210,220,84,30,"关闭"))displayChooser=false;
            Text(308,266,720,28,SystemInfo.graphicsDeviceName, label);
            if(appMode!="menu"&&Button(1040,264,246,32,"Avia 点云设置")){displayChooser=false;lidarChooser=true;ReleaseMouse();}
            Text(308,298,970,25,$"实际 {measuredFps:0.0} FPS · CPU {(cpuMs>0?cpuMs.ToString("0.00")+"ms":"未提供计时")} · GPU {(gpuMs>0?gpuMs.ToString("0.00")+"ms":"未提供计时")}",small);
            Text(308,335,970,28,"渲染FPS上限（回放时钟保持独立）",label);
            for(int i=0;i<FpsPresets.Length;i++)
            {
                int preset=FpsPresets[i];
                if(Button(308+i*163,373,153,34,(fpsPreference==preset?"✓ ":"")+(preset==0?"不限 FPS":preset+" FPS"))){fpsPreference=preset;vsyncPreference=false;ApplyRenderRate();}
            }
            bool sync=GUI.Toggle(new Rect(308,423,970,28),vsyncPreference,"垂直同步：开启后跟随实际显示刷新率，FPS限帧不生效");
            if(sync!=vsyncPreference){vsyncPreference=sync;ApplyRenderRate();}
            Text(308,463,970,26,$"物理刷新率：{(displayInfo!=null && displayInfo.ok?displayInfo.currentHz.ToString("0.00")+"Hz · "+displayInfo.backend:"桌面后端未提供")}（与FPS上限分别设置）",small);
            int[] rates={60,120,144,165,240};
            for(int i=0;i<rates.Length;i++)
            {
                bool supported=displayInfo!=null && displayInfo.ok && (displayInfo.modes??Array.Empty<DisplayModeInfo>()).Any(m=>Math.Abs(m.actual-rates[i])<1);
                bool enabled=GUI.enabled;GUI.enabled=enabled && supported && !displayBusy && confirmationDeadline<0;
                if(Button(308+i*145,505,135,32,(hzPreference==rates[i]?"✓ ":"")+rates[i]+" Hz"))hzPreference=rates[i];GUI.enabled=enabled;
            }
            if(Button(1048,505,238,32,"应用物理刷新率") && hzPreference>0 && !displayBusy && confirmationDeadline<0)StartCoroutine(ApplyPhysicalDisplay());
            if(Button(308,549,475,32,"窗口尺寸："+WindowSizes[Mathf.Clamp(displayResolution,0,3)]))
            {
                displayResolution=(displayResolution+1)%4;var size=WindowSizes[displayResolution];windowWidth=size.x;windowHeight=size.y;
                if(!Screen.fullScreen)Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                PlayerPrefs.SetInt("replay.resolution",displayResolution);PlayerPrefs.Save();
            }
            if(Button(798,549,488,32,Screen.fullScreen?"F11 返回窗口":"F11 全屏"))ToggleFullscreen();
            Text(308,591,970,50,displayBusy?"正在查询/应用显示设置…":displayMessage,small);
            if(confirmationDeadline>0)
            {
                if(Button(308,650,475,40,$"保留（{Mathf.CeilToInt(confirmationDeadline-Time.unscaledTime)}秒）") && !displayBusy)StartCoroutine(ConfirmDisplay());
                if(Button(798,650,488,40,"恢复原模式") && !displayBusy)StartCoroutine(RestoreDisplay());
            }
            else Text(308,650,970,42,"物理Hz仅列真实支持模式；应用后超时恢复。240档的实际FPS取决于场景负载。",small);
        }
    }
}
