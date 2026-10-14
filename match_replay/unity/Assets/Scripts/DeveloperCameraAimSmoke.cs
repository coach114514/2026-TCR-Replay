using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        // Opt-in raster regression: only suppress the candidate line so its
        // actual rendered pixels can be compared with the intended endpoints.
        bool aimDashDiagnosticHidden;
        [Serializable] class CameraAimCase
        {
            public string name,target;public int width,height,changedPixels,strayPixels;
            public Vector2 from,to;public bool passed;
        }
        [Serializable] class CameraAimReport
        {
            public string gpu,error="";public bool passed,originalLipReproduced,adjustedViewClear,bodyRetained,muzzleUnchanged;
            public int poseChecks;public float forwardOffset;public List<CameraAimCase> lines=new List<CameraAimCase>();
        }
        static bool CameraAimRayMesh(Mesh mesh,Vector3 origin,Vector3 direction,float near,float far)
        {
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var e1=vertices[triangles[i+1]]-a;var e2=vertices[triangles[i+2]]-a;
                var p=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-10f)continue;
                var delta=origin-a;float u=Vector3.Dot(delta,p)/det;if(u<0||u>1)continue;
                var q=Vector3.Cross(delta,e1);float v=Vector3.Dot(direction,q)/det;if(v<0||u+v>1)continue;
                float distance=Vector3.Dot(e2,q)/det;if(distance>near&&distance<far)return true;
            }
            return false;
        }
        IEnumerator CameraAimSmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--camera-aim-smoke")+1];Directory.CreateDirectory(output);
            developerSmokeActive=true;allLabels=false;game.running=false;gameThirdPerson=false;
            var report=new CameraAimReport {gpu=SystemInfo.graphicsDeviceName,forwardOffset=SweeperViewForwardOffset};
            yield return null;
            try
            {
                var player=game.actors[game.controlled];var vehicle=gameVehicles[player.id];player.pitch=0;player.gimbalYaw=90;
                PoseGameVehicle(vehicle);UpdateGameCamera();GameMuzzle(vehicle,out var muzzle,out var forward);
                var source=vehicle.unit.robot.groups[3].TransformPoint(Vt03SourcePoint);
                DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position-source,forward*.040f)<.00001f,"C视点未沿原枪管光轴前移40mm");
                var mesh=ReadMesh(vehicle.unit.robot.model.groups[3].meshes[0]);
                var axis=PassagePose.Vector(vehicle.unit.robot.model.gunForward).normalized;
                var up=Vector3.Cross(axis,Vector3.Cross(Vector3.up,axis).normalized);
                foreach(float pitch in new[]{5f,10f})
                {
                    Vector3 ray=axis*Mathf.Cos(pitch*Mathf.Deg2Rad)+up*Mathf.Sin(pitch*Mathf.Deg2Rad);
                    DeveloperChecks.Require(CameraAimRayMesh(mesh,Vt03SourcePoint,ray,.012f,.080f),"原生枪管上沿遮挡未复现");
                    DeveloperChecks.Require(!CameraAimRayMesh(mesh,Vt03Point,ray,.012f,.080f),"前移后上沿仍遮挡视线");
                }
                report.originalLipReproduced=report.adjustedViewClear=true;
                foreach(string model in new[]{"sweeper","jiageng"})
                {
                    ChangeGameModel(player,model);vehicle=gameVehicles[player.id];
                    foreach(float yaw in new[]{35f,215f})foreach(float pitch in new[]{-20f,20f})foreach(float fold in new[]{0f,1f})
                    {
                        player.gimbalYaw=yaw;player.pitch=pitch;player.fold=fold;PoseGameVehicle(vehicle);UpdateGameCamera();
                        GameMuzzle(vehicle,out muzzle,out forward);
                        var gun=vehicle.unit.robot.groups[vehicle.unit.robot.model.gunGroup];
                        DeveloperChecks.Require(Vector3.Distance(muzzle,gun.TransformPoint(PassagePose.Vector(vehicle.unit.robot.model.muzzlePoint)))<.00001f,"调整视点改变A枪口");
                        DeveloperChecks.Require(Vector3.Angle(viewCamera.transform.forward,forward)<.01f,"镜头与枪管方向不一致");
                        DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position,vehicle.unit.robot.groups[vehicle.eyeGroup].TransformPoint(vehicle.eyePoint))<.00001f,"云台俯仰或收缩丢失镜头挂点");
                        DeveloperChecks.Require(vehicle.unit.root.activeInHierarchy&&Array.TrueForAll(vehicle.unit.renderers,r=>!r.forceRenderingOff),"第一人称隐藏控制车体");
                        report.poseChecks++;
                    }
                }
                report.muzzleUnchanged=report.bodyRetained=true;
            }
            catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
            ResetDeveloper();game.running=false;UpdateGameCamera();
            var driver=gameVehicles[game.controlled];var cameraRotation=viewCamera.transform.rotation;
            viewCamera.transform.position=driver.unit.robot.groups[3].TransformPoint(Vt03SourcePoint);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"sweeper-before.png"));yield return null;
            UpdateGameCamera();viewCamera.transform.rotation=cameraRotation;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"sweeper-after.png"));yield return null;
            foreach(var size in new[]{new Vector2Int(1600,900),new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1920,1200)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);float deadline=Time.realtimeSinceStartup+5;
                while((Screen.width!=size.x||Screen.height!=size.y)&&Time.realtimeSinceStartup<deadline)yield return null;
                for(int n=0;n<8;n++)yield return null;
                foreach(int quadrant in new[]{0,1,2,3})
                {
                    Vector2 to=Vector2.zero;string target="",name=$"{size.x}x{size.y}-quadrant{quadrant}";bool ready=false;
                    try
                    {
                        ResetDeveloper();game.running=false;gameAutoAim=false;Vector3 center,normal;
                        if(quadrant<2)
                        {
                            // Isolate the vehicle view from the red R mark just
                            // above it: blue's own R faces the opposite side.
                            game.actors[game.controlled].team="blue";game.actors["target1"].team="red";
                            foreach(var vehicle in gameVehicles.Values)PoseGameVehicle(vehicle);
                            CollectGamePlates();
                            UpdateGameCamera();
                            DeveloperChecks.Require(TryGameAimCandidate(out var initial)&&initial.target=="target1","默认驾驶视点无法选到靶车");
                            center=initial.center;normal=Vector3.zero;
                        }
                        else
                        {
                            string team=quadrant==2?"red":"blue";game.actors[game.controlled].team=team;
                            var anchor=Array.Find(mechanismConfig.rune.selectionAnchors,a=>a.team==team);
                            center=runeRotor.parent.TransformPoint(PassagePose.Vector(anchor.center));normal=runeRotor.parent.TransformDirection(PassagePose.Vector(anchor.normal));
                        }
                        if(quadrant>=2)viewCamera.transform.position=center+normal*3;
                        viewCamera.transform.LookAt(center);
                        Vector3 right=viewCamera.transform.right,up=viewCamera.transform.up;
                        viewCamera.transform.LookAt(center+right*(quadrant%2==0?.8f:-.8f)+up*(quadrant<2?.6f:-.6f));
                        DeveloperChecks.Require(TryGameAimCandidate(out var candidate),"实际ROI缺少候选 "+name);
                        // The nearest visible object may change after turning;
                        // verify the real selected candidate, never a forced ID.
                        target=candidate.target;to=candidate.screen;
                        DeveloperChecks.Require(Vector2.Distance(new Vector2(800,450),to)>25,"候选与准星重合，无法验证虚线");ready=true;
                    }
                    catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
                    if(!ready)continue;
                    aimDashDiagnosticHidden=false;for(int n=0;n<3;n++)yield return null;
                    yield return new WaitForEndOfFrame();var shown=ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(output,name+"-on.png"),shown.EncodeToPNG());
                    aimDashDiagnosticHidden=true;for(int n=0;n<3;n++)yield return null;
                    yield return new WaitForEndOfFrame();var hidden=ScreenCapture.CaptureScreenshotAsTexture();
                    var result=new CameraAimCase {name=name,target=target,width=shown.width,height=shown.height,from=new Vector2(800,450),to=to};
                    if(shown.width!=hidden.width||shown.height!=hidden.height)result.strayPixels=-1;
                    else
                    {
                        var a=shown.GetPixels32();var b=hidden.GetPixels32();Vector2 delta=to-result.from;
                        for(int y=0;y<shown.height;y++)
                        {
                            float gy=(shown.height-y-.5f)*900/shown.height;if(gy<170||gy>695)continue;
                            for(int x=0;x<shown.width;x++)
                            {
                                int at=y*shown.width+x;if(Mathf.Abs(a[at].r-b[at].r)+Mathf.Abs(a[at].g-b[at].g)+Mathf.Abs(a[at].b-b[at].b)<24)continue;
                                result.changedPixels++;Vector2 point=new Vector2((x+.5f)*1600/shown.width,gy);
                                float t=Mathf.Clamp01(Vector2.Dot(point-result.from,delta)/delta.sqrMagnitude);
                                if(Vector2.Distance(point,result.from+delta*t)>2)result.strayPixels++;
                            }
                        }
                    }
                    result.passed=result.changedPixels>8&&result.strayPixels==0;report.lines.Add(result);Destroy(shown);Destroy(hidden);
                    if(!result.passed)report.error+=$"虚线偏离实际候选连线 {name} changed={result.changedPixels} stray={result.strayPixels}\n";
                    Debug.Log("CAMERA_AIM_LINE "+JsonUtility.ToJson(result));yield return null;
                }
            }
            aimDashDiagnosticHidden=false;
            report.passed=report.error.Length==0&&report.lines.Count==16;
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("CAMERA_AIM_SMOKE_"+(report.passed?"OK":"FAILED"));Application.Quit(report.passed?0:74);
        }
    }
}
