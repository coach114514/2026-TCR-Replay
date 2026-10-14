using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        bool captureLabelGeometry;
        struct LabelGeometry {public Vector2 anchor,end;public Rect bounds;}
        readonly List<LabelGeometry> capturedLabels=new List<LabelGeometry>();
        [Serializable] class LabelCase
        {
            public string name;public int width,height,labels,changedPixels,unexpectedPixels;
        }
        [Serializable] class LabelReport
        {
            public bool baseline,passed;public List<LabelCase> cases=new List<LabelCase>();
            public string check="Pause real match at 93.5s; compare labels on/off pixels against actual label rectangles, anchors and intended leader segments. Trails and projectiles stay unchanged.";
        }
        static bool LabelPixelAllowed(Vector2 point,List<LabelGeometry> geometry)
        {
            foreach(var g in geometry)
            {
                var r=g.bounds;r.xMin-=3;r.xMax+=3;r.yMin-=3;r.yMax+=3;
                if(r.Contains(point) || (point-g.anchor).sqrMagnitude<=16)return true;
                var delta=g.end-g.anchor;
                float t=delta.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(point-g.anchor,delta)/delta.sqrMagnitude):0;
                if((point-(g.anchor+delta*t)).sqrMagnitude<=9)return true;
            }
            return false;
        }
        IEnumerator LabelSmoke(string[] args)
        {
            int index=Array.IndexOf(args,"--label-smoke");string output=index+1<args.Length?args[index+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;
            var report=new LabelReport {baseline=Array.IndexOf(args,"--label-baseline")>=0};
            LoadMatch(0);Seek(93.5f);playing=false;cleanView=false;landmarkLabels=true;captureLabelGeometry=true;
            int badCases=0;
            foreach(var size in new [] {new Vector2Int(1600,900),new Vector2Int(2490,1544),new Vector2Int(1920,1080),new Vector2Int(2560,1600)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                float deadline=Time.realtimeSinceStartup+5;
                while((Screen.width!=size.x || Screen.height!=size.y) && Time.realtimeSinceStartup<deadline)yield return null;
                for(int i=0;i<8;i++)yield return null;
                foreach(bool exterior in new [] {false,true})
                {
                    if(exterior)
                    {
                        SetCameraMode(ReplayCameraMode.Free,false);
                        cameraRig.Free(new Vector3(-4,3,-2),Quaternion.Euler(7,5,0));UpdateCamera();
                    }
                    else
                    {
                        SetCameraMode(ReplayCameraMode.Observe,false);
                        focus=new Vector3(14,0,7.5f);yaw=0;pitch=58;distance=32;UpdateCamera();
                    }
                    allLabels=true;for(int i=0;i<3;i++)yield return null;
                    yield return new WaitForEndOfFrame();
                    var geometry=new List<LabelGeometry>(capturedLabels);
                    var shown=ScreenCapture.CaptureScreenshotAsTexture();
                    var name=$"{size.x}x{size.y}-"+(exterior?"exterior":"overview");
                    File.WriteAllBytes(Path.Combine(output,name+"-labels-on.png"),shown.EncodeToPNG());
                    allLabels=false;for(int i=0;i<3;i++)yield return null;
                    yield return new WaitForEndOfFrame();
                    var hidden=ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(output,name+"-labels-off.png"),hidden.EncodeToPNG());
                    var result=new LabelCase {name=name,width=shown.width,height=shown.height,labels=geometry.Count};
                    var a=shown.GetPixels32();var b=hidden.GetPixels32();
                    // Only the scene viewport: toggling F9 changes its toolbar
                    // text too, which is expected and excluded from this test.
                    if(shown.width==hidden.width && shown.height==hidden.height)
                    for(int y=0;y<shown.height;y++)
                    {
                        float gy=(shown.height-y-.5f)*900/shown.height;if(gy<264 || gy>718)continue;
                        for(int x=0;x<shown.width;x++)
                        {
                            float gx=(x+.5f)*1600/shown.width;if(gx>1214)break;
                            int p=y*shown.width+x;
                            if(Mathf.Abs(a[p].r-b[p].r)+Mathf.Abs(a[p].g-b[p].g)+Mathf.Abs(a[p].b-b[p].b)<24)continue;
                            result.changedPixels++;
                            if(!LabelPixelAllowed(new Vector2(gx,gy),geometry))result.unexpectedPixels++;
                        }
                    }
                    else result.unexpectedPixels=-1;
                    Destroy(shown);Destroy(hidden);report.cases.Add(result);
                    if(result.labels==0 || result.unexpectedPixels!=0)badCases++;
                    Debug.Log($"REPLAY_LABEL_CASE name={name} actual={result.width}x{result.height} labels={result.labels} changed={result.changedPixels} stray={result.unexpectedPixels}");
                    yield return null;
                }
            }
            report.passed=report.baseline?badCases>0:badCases==0;
            File.WriteAllText(Path.Combine(output,"label-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log(report.baseline?$"REPLAY_LABEL_BASELINE_REPRODUCED cases={report.cases.Count} badCases={badCases}":$"REPLAY_LABEL_SMOKE_{(report.passed?"OK":"FAILED")} cases={report.cases.Count} badCases={badCases}");
            Application.Quit(report.passed?0:38);
        }
    }
}
