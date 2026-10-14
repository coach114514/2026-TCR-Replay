using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        bool robotDemo;
        [Serializable] class RobotSmokeReport
        {
            public int matches, vehicles, samples, seekChecks, groundChecks;
            public float framesPerSecond;
            public string source="运行时验证；姿态图为演示，不是比赛日志";
            public string[] checks;
        }
        IEnumerator RobotSmoke(string[] args)
        {
            int index=Array.IndexOf(args,"--robot-smoke");string output=index+1<args.Length?args[index+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;
            if(!string.IsNullOrEmpty(error)) {Application.Quit(12);yield break;}
            var report=new RobotSmokeReport();var checks=new List<string>();bool failed=false;
            try
            {
                ReplayRobotChecks.Contracts();
                foreach(int m in SmokeGameIndices(args))
                {
                    LoadMatch(m);ReplayRobotChecks.Require(string.IsNullOrEmpty(error),"比赛加载失败");
                    int count=0;
                    foreach(var u in units)
                    {
                        if(u.data.role!="infantry3" && u.data.role!="infantry4") continue;
                        count++;report.vehicles++;report.samples+=u.data.samples.Length;
                        ReplayRobotChecks.Require(u.robot!=null,"车型未实例化");
                        ReplayRobotChecks.Require(u.data.role=="infantry3"?u.robot.model.id=="sweeper":u.robot.model.id=="jiageng","红蓝车型映射错误");
                        foreach(float time in new [] {1f,60.5f,120.5f,260.5f,419f,120.5f,1f,0f})
                        {
                            Seek(time);bool valid=Timeline.Pose(u.data,playhead,out var original);
                            ReplayRobotChecks.Require(valid==u.visible,"缺失定位时显示了车型");
                            if(valid)
                            {
                                ReplayRobotChecks.Require(Mathf.Abs(original.x-u.root.transform.position.x)<.00001f && Mathf.Abs(original.z-u.root.transform.position.z)<.00001f,"模型更改历史 XY");
                                ReplayRobotChecks.Require(u.root.transform.position.y>=GroundHeight(original.x,original.z),"车辆落在地面下");
                                report.groundChecks++;
                            }
                            for(int g=0;g<u.robot.groups.Length;g++)
                                if(!u.robot.model.groups[g].turret && u.robot.model.id=="sweeper")
                                    ReplayRobotChecks.Require(u.robot.groups[g].localPosition.sqrMagnitude<.000001f && Quaternion.Angle(u.robot.groups[g].localRotation,Quaternion.identity)<.01f,"固定底盘随洞口改变姿态");
                            report.seekChecks++;
                        }
                        Seek(120.5f);Vector3[] positions=new Vector3[u.robot.groups.Length];Quaternion[] rotations=new Quaternion[positions.Length];
                        for(int g=0;g<positions.Length;g++) {positions[g]=u.robot.groups[g].localPosition;rotations[g]=u.robot.groups[g].localRotation;}
                        Seek(300.5f);Seek(1);Seek(120.5f);
                        for(int g=0;g<positions.Length;g++)
                            ReplayRobotChecks.Require(Vector3.Distance(positions[g],u.robot.groups[g].localPosition)<.000001f && Quaternion.Angle(rotations[g],u.robot.groups[g].localRotation)<.01f,"倒放姿态保留未来状态");
                    }
                    int recorded=Array.FindAll(match.entities,e=>e.role=="infantry3" || e.role=="infantry4").Length;
                    ReplayRobotChecks.Require(count==recorded,"记录中的 3/4 号实例数不一致");report.matches++;
                }
                checks.Add($"{report.matches} 局已有 3/4 号映射、原始 XY、缺失隐藏、双向 seek 姿态一致；不补造未登记实体");
                LoadMatch(0);Seek(120.5f);
                foreach(string role in new [] {"infantry3","infantry4"})
                {
                    var redUnit=units.Find(u=>u.data.role==role && u.data.team=="red");var blueUnit=units.Find(u=>u.data.role==role && u.data.team=="blue");
                    ReplayRobotChecks.Require(redUnit.robot.model==blueUnit.robot.model,"红蓝没有共享车型定义");
                    ReplayRobotChecks.Require(redUnit.robot.groups[0].GetComponentInChildren<MeshFilter>().sharedMesh==blueUnit.robot.groups[0].GetComponentInChildren<MeshFilter>().sharedMesh,"红蓝没有共享网格");
                }
                checks.Add("红蓝共享网格、独立阵营材质与编号；麦轮底盘按洞口全摊");
                foreach(var p in field.tunnels)
                {
                    var point=new Vector3(p.x,0,p.y);float h=GroundHeight(point.x,point.z);
                    ReplayRobotChecks.Require(h<p.floorMax,"洞顶被当成地面");
                    terrain.SetActive(false);Physics.SyncTransforms();float hidden=GroundHeight(point.x,point.z);
                    ReplayRobotChecks.Require(Mathf.Abs(hidden-h)<.0001f,"关闭场地显示撤销碰撞");terrain.SetActive(true);Physics.SyncTransforms();
                    cameraRig.Free(new Vector3(p.x,h-1,p.y),Quaternion.identity);UpdateCamera();
                    ReplayRobotChecks.Require(cameraRig.Position.y>=h+CameraFloorRadius-.0001f,"自由相机穿地");
                    report.groundChecks++;
                }
                checks.Add("四个洞口地面与屋顶分离；显示开关独立；自由相机地面限位");
                SetCameraMode(ReplayCameraMode.Observe,false);
                var vehicle=units.Find(u=>u.data.role=="infantry3" && u.data.team=="red");SelectUnit(units.IndexOf(vehicle));Seek(120.5f);SetCameraMode(ReplayCameraMode.Attached,false);
                Quaternion direction=Quaternion.Euler(-8,70,0);cameraRig.Free(viewCamera.transform.position,direction);UpdateCamera();SetCameraMode(ReplayCameraMode.Attached,false);
                vehicle.robot.preview=1;ApplyTime();UpdateCamera();
                ReplayRobotChecks.Require(Quaternion.Angle(direction,viewCamera.transform.rotation)<.01f,"折叠控制了第一人称朝向");
                foreach(var r in vehicle.renderers) ReplayRobotChecks.Require(r.forceRenderingOff,"第一人称未隐藏所有 LOD/装甲编号");
                vehicle.robot.preview=-1;SetCameraMode(ReplayCameraMode.Observe,false);ApplyTime();
                foreach(var r in vehicle.renderers) ReplayRobotChecks.Require(!r.forceRenderingOff,"退出第一人称未恢复车型");
                checks.Add("折叠视点随云台移动、朝向独立、全部 LOD 自身隐藏/恢复");
                Debug.Log($"REPLAY_ROBOT_RUNTIME_OK matches={report.matches} vehicles={report.vehicles} seeks={report.seekChecks} floorChecks={report.groundChecks}");
            }
            catch(Exception e) {failed=true;Debug.LogException(e);}
            if(failed) {Application.Quit(13);yield break;}
            // Only this explicit diagnostics invocation parks vehicles for asset
            // review. The source samples and normal runtime are never rewritten.
            Seek(1.5f);playing=false;headings=false;ClearProjectileVisuals();robotDemo=true;SetCameraMode(ReplayCameraMode.Observe,false);
            foreach(var u in units) {u.root.SetActive(false);u.visible=false;u.trail.enabled=false;}
            foreach(string role in new [] {"infantry3","infantry4"})
            foreach(bool folded in new [] {false,true})
            {
                var redUnit=units.Find(u=>u.data.role==role && u.data.team=="red");var blueUnit=units.Find(u=>u.data.role==role && u.data.team=="blue");
                foreach(var u in new [] {redUnit,blueUnit})
                {
                    u.root.SetActive(true);u.visible=true;u.robot.preview=folded?1:-1;
                    Vector3 point=new Vector3(u.data.team=="red"?4.8f:7.2f,0,9.4f);UpdateRobotPose(u,point);point.y=VehicleGroundHeight(u,point);u.root.transform.position=point;
                }
                cleanView=false;selected=units.IndexOf(redUnit);focus=new Vector3(6,GroundHeight(6,9.4f),9.4f);distance=4.8f;pitch=25;yaw=25;UpdateCamera();
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,role+(folded?"-folded":"-default")+".png"));yield return null;
                // Inspect the highest LOD at a close, unobstructed camera position.
                blueUnit.root.SetActive(false);blueUnit.visible=false;
                cleanView=true;viewCamera.rect=CameraViewport;focus=redUnit.root.transform.position+Vector3.up*.15f;distance=1.1f;pitch=20;yaw=25;UpdateCamera();
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,role+(folded?"-folded":"-default")+"-near.png"));yield return null;
                if(!folded)
                {
                    foreach(var review in new [] {redUnit,blueUnit})
                    {
                        redUnit.root.SetActive(review==redUnit);blueUnit.root.SetActive(review==blueUnit);
                        selected=units.IndexOf(review);
                        var armor=review.robot.model.armorPanels[0];
                        foreach(var other in review.robot.model.armorPanels)
                            if(other.position[2]>armor.position[2]) armor=other;
                        Vector3 face=review.root.transform.TransformPoint(PassagePose.Vector(armor.position));
                        Vector3 normal=review.root.transform.TransformDirection(PassagePose.Rotation(armor.rotation)*Vector3.forward);
                        Vector3 eye=face+normal*.26f+Vector3.up*.012f;
                        cameraRig.Free(eye,Quaternion.LookRotation(face-eye,Vector3.up));UpdateCamera();
                        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,role+"-"+review.data.team+"-armor.png"));yield return null;
                    }
                    SetCameraMode(ReplayCameraMode.Observe,false);
                }
                cleanView=false;viewCamera.rect=CameraViewport;
                redUnit.root.SetActive(false);blueUnit.root.SetActive(false);redUnit.visible=false;blueUnit.visible=false;
            }
            // Review the tunnel underpass at close range, independently of recorded motion.
            var tunnel=field.tunnels[0];var car=units.Find(u=>u.data.role=="infantry3" && u.data.team=="red");
            car.root.SetActive(true);car.visible=true;var inside=new Vector3(tunnel.x,0,tunnel.y);car.robot.preview=-1;UpdateRobotPose(car,inside);inside.y=VehicleGroundHeight(car,inside);car.root.transform.position=inside;
            cleanView=false;selected=units.IndexOf(car);focus=inside+Vector3.up*.2f;pitch=10;yaw=0;distance=1.9f;UpdateCamera();
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"tunnel-underpass-demo.png"));yield return null;
            robotDemo=false;headings=true;LoadMatch(0);foreach(var u in units) u.trail.enabled=true;Seek(120.5f);SetCameraMode(ReplayCameraMode.Observe,false);
            focus=new Vector3(14,0,7.5f);pitch=58;yaw=0;distance=32;UpdateCamera();
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"match-real-120.5.png"));yield return null;
            var timers=new List<float>();int frame=Time.frameCount;float start=Time.unscaledTime;playing=true;
            for(int i=0;i<180;i++) {yield return null;timers.Add(Time.unscaledDeltaTime);}
            playing=false;report.framesPerSecond=(Time.frameCount-frame)/(Time.unscaledTime-start);checks.Add("真实比赛场地与四车渲染采样，非纯空场景");report.checks=checks.ToArray();
            File.WriteAllText(Path.Combine(output,"robot-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log($"REPLAY_ROBOT_SMOKE_OK fps={report.framesPerSecond:0.0} screenshots={output}");Application.Quit(0);
        }
    }
}
