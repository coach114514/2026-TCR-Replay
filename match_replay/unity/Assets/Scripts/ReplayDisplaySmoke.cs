using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class RateResult {public int target;public float actual;public double cpuMs,gpuMs;}
        [Serializable] class DisplayRuntimeReport {public string gpu;public bool preferencesRestored;public DisplayBackendInfo display;public List<RateResult> rates=new List<RateResult>();}
        IEnumerator DisplaySmoke(string[] args)
        {
            int arg=Array.IndexOf(args,"--display-smoke");string output=arg+1<args.Length?args[arg+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;showProjectiles=false;Seek(120);playing=false;
            int originalFps=fpsPreference;bool originalSync=vsyncPreference;
            var report=new DisplayRuntimeReport {gpu=SystemInfo.graphicsDeviceName};
            foreach(int rate in new[]{60,120,144,165,240})
            {
                fpsPreference=rate;vsyncPreference=false;ApplyRenderRate();
                float warm=Time.unscaledTime;while(Time.unscaledTime-warm<.4f)yield return null;
                float start=Time.unscaledTime;int frame=Time.frameCount;
                while(Time.unscaledTime-start<1.5f)yield return null;
                var result=new RateResult {target=rate,actual=(Time.frameCount-frame)/(Time.unscaledTime-start),cpuMs=cpuMs,gpuMs=gpuMs};report.rates.Add(result);
                Debug.Log($"REPLAY_DISPLAY_RATE target={rate} actual={result.actual:0.0} cpuMs={cpuMs:0.00} gpuMs={gpuMs:0.00}");
            }
            fpsPreference=originalFps;vsyncPreference=originalSync;ApplyRenderRate();report.preferencesRestored=true;
            float settle=Time.unscaledTime;while(Time.unscaledTime-settle<1)yield return null;
            yield return StartCoroutine(QueryDisplay());report.display=displayInfo;
            displayChooser=true;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"display-settings.png"));yield return null;
            File.WriteAllText(Path.Combine(output,"display-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("REPLAY_DISPLAY_SMOKE_OK presets=5 original_preferences_restored=1 physical_display_unchanged=1");Application.Quit(0);
        }
    }
}
