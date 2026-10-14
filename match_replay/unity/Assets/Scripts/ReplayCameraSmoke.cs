using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class CameraProbe
        {
            public string mode, target;
            public float time, replayTime;
            public Vector3 position, euler;
            public bool focused, captured, slow, playing, fullscreen, clean, targetVisible, allLabels, landmarkLabels;
            public int width, height;
        }
        string cameraProbePath;
        float nextCameraProbe;
        void WriteCameraProbe()
        {
            // Explicit opt-in diagnostics for testing real OS keyboard/mouse input.
            if (string.IsNullOrEmpty(cameraProbePath) || Time.unscaledTime < nextCameraProbe) return;
            nextCameraProbe = Time.unscaledTime + .05f;
            try
            {
                File.WriteAllText(cameraProbePath, JsonUtility.ToJson(new CameraProbe {
                    mode=cameraRig.Mode.ToString(), time=Time.unscaledTime, replayTime=playhead,
                    position=viewCamera.transform.position, euler=viewCamera.transform.eulerAngles,
                    focused=Application.isFocused, captured=mouseCaptured, slow=cameraRig.Slow,
                    playing=playing, fullscreen=Screen.fullScreen, clean=cleanView,allLabels=allLabels,landmarkLabels=landmarkLabels,
                    targetVisible=cameraRig.TargetVisible, width=Screen.width, height=Screen.height,
                    target=attachedMarker!=null?attachedMarker.name:units.Count>selected?units[selected].data.id:""
                }));
            }
            catch (Exception ex) {cameraProbePath=null;Debug.LogWarning("相机输入诊断写入失败："+ex.Message);}
        }
        void VerifyCameraIntegration()
        {
            ReplayCameraChecks.Motion();
            Seek(120.5f); SetCameraMode(ReplayCameraMode.Free, false);
            cameraRig.Look(new Vector2(14, -5)); cameraRig.Move(Vector3.forward, .5f); UpdateCamera();
            Vector3 freePosition = viewCamera.transform.position;
            Quaternion look = viewCamera.transform.rotation;
            Seek(0); Seek(match.duration); Seek(120.5f);
            ReplayCameraChecks.Require(Vector3.Distance(viewCamera.transform.position, freePosition) < .0001f &&
                Quaternion.Angle(viewCamera.transform.rotation, look) < .01f, "时间跳转改变自由相机");
            Debug.Log("REPLAY_CAMERA_FREE_OK seek=0/end/120.5 position_and_look_preserved");
            // Use an actual moving trajectory, not a fabricated constant-position unit.
            int moving = -1; float first = 0, second = 0;
            for (int u = 0; u < units.Count && moving < 0; u++)
            {
                var unit = units[u];
                if (unit.data.role == "base" || unit.data.role == "outpost") continue;
                var states = unit.data.samples;
                for (int i = 1; i < states.Length; i++)
                    if (states[i-1].valid && states[i].valid && states[i].t - states[i-1].t <= 2 &&
                        Vector2.Distance(new Vector2(states[i-1].x, states[i-1].y), new Vector2(states[i].x, states[i].y)) > .2f)
                    { moving = u; first = states[i-1].t; second = states[i].t; break; }
            }
            ReplayCameraChecks.Require(moving >= 0, "没有找到真实移动轨迹");
            SelectUnit(moving); Seek(first); SetCameraMode(ReplayCameraMode.Attached, false);
            cameraRig.Look(new Vector2(25, 8)); cameraRig.Move(Vector3.right, .1f); UpdateCamera();
            look = viewCamera.transform.rotation;
            Vector3 previous = viewCamera.transform.position;
            Vector3 target = units[moving].root.transform.position;
            Seek(second);
            ReplayCameraChecks.Require(Vector3.Distance(viewCamera.transform.position - previous,
                units[moving].root.transform.position - target) < .0001f &&
                Quaternion.Angle(viewCamera.transform.rotation, look) < .01f, "跟随位移或自由朝向错误");
            foreach (var renderer in units[moving].renderers)
                ReplayCameraChecks.Require(renderer.forceRenderingOff, "第一人称被自身模型遮挡");
            previous = viewCamera.transform.position; Seek(0);
            ReplayCameraChecks.Require(!cameraRig.TargetVisible && !units[moving].visible &&
                Vector3.Distance(viewCamera.transform.position, previous) < .0001f, "缺失位置跳到原点");
            Seek(first);
            ReplayCameraChecks.Require(cameraRig.TargetVisible && Quaternion.Angle(viewCamera.transform.rotation, look) < .01f,
                "位置恢复时强制转向");
            SelectUnit(units.FindIndex(u => u.data.id == "blue_base")); UpdateCamera();
            ReplayCameraChecks.Require(cameraRig.TargetVisible && Quaternion.Angle(viewCamera.transform.rotation, look) < .01f,
                "切换建筑强制转向");
            foreach (var renderer in units[moving].renderers)
                ReplayCameraChecks.Require(!renderer.forceRenderingOff, "切换目标未恢复车辆显示");
            ReplayCameraChecks.Require(viewCamera.transform.position.y > units[selected].root.transform.position.y,
                "建筑视点未抬高");
            int originalField = fieldIndex;
            Quaternion savedLook = viewCamera.transform.rotation;
            SwitchField((fieldIndex + 1) % fieldCatalog.fields.Length);
            ReplayCameraChecks.Require(cameraRig.Mode == ReplayCameraMode.Attached && cameraRig.TargetVisible &&
                Quaternion.Angle(viewCamera.transform.rotation, savedLook) < .01f, "切换场地丢失跟随或朝向");
            SwitchField(originalField);
            AttachMarker(Array.Find(field.landmarks, m => m.team == "red" && m.role == "resource_station"), false);
            savedLook = viewCamera.transform.rotation; previous = viewCamera.transform.position;
            Seek(0); Seek(120.5f);
            ReplayCameraChecks.Require(cameraRig.TargetVisible && Vector3.Distance(previous, viewCamera.transform.position) < .0001f &&
                Quaternion.Angle(savedLook, viewCamera.transform.rotation) < .01f, "地物视角受回放时间影响");
            Debug.Log($"REPLAY_CAMERA_ATTACHED_OK entity={units[moving].data.id} follow_delta=actual_trajectory missing=held recovered=1 target_switch=independent_look field_switch=1 landmark=red_resource_station");
            SetCameraMode(ReplayCameraMode.Observe, false);
            ReleaseMouse();
        }
        IEnumerator CameraSmoke(string[] args)
        {
            yield return null;
            try
            {
                ReplayCameraChecks.Require(match != null && string.IsNullOrEmpty(error), "启动失败");
                VerifyCameraIntegration();
            }
            catch (Exception ex) { Debug.LogException(ex); Application.Quit(20); yield break; }
            int index = Array.IndexOf(args, "--screenshot");
            string output = index >= 0 && index + 1 < args.Length ? Path.GetDirectoryName(args[index+1]) : null;
            Seek(120.5f); SetCameraMode(ReplayCameraMode.Free, false);
            cameraRig.Free(new Vector3(14, 2.3f, 3.5f), Quaternion.Euler(10, -80, 0)); UpdateCamera();
            if (output != null)
            {
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,"camera-free.png"));
                for (int i=0;i<12;i++) yield return null;
            }
            SelectUnit(units.FindIndex(u => u.data.id == "red_base"));
            SetCameraMode(ReplayCameraMode.Observe, false); SetCameraMode(ReplayCameraMode.Attached, false);
            if (output != null)
            {
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,"camera-building.png"));
                for (int i=0;i<12;i++) yield return null;
            }
            int car = units.FindIndex(u => u.visible && u.data.role == "infantry3");
            if (car < 0) car = units.FindIndex(u => u.visible && u.data.role == "hero");
            SelectUnit(car); cameraRig.Look(new Vector2(12, -3)); UpdateCamera();
            if (output != null)
            {
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,"camera-vehicle.png"));
                for (int i=0;i<12;i++) yield return null;
            }
            ToggleCleanView(); ToggleFullscreen();
            float deadline = Time.realtimeSinceStartup + 5;
            while (!Screen.fullScreen && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i=0;i<30;i++) yield return null;
            if (!Screen.fullScreen || viewCamera.rect != new Rect(0,0,1,1))
            {Debug.LogError("全屏或纯场地视图未生效");Application.Quit(21);yield break;}
            Debug.Log($"REPLAY_FULLSCREEN_OK mode={Screen.fullScreenMode} resolution={Screen.width}x{Screen.height} viewport={viewCamera.rect}");
            if (output != null)
            {
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,"camera-fullscreen.png"));
                for (int i=0;i<12;i++) yield return null;
            }
            ToggleFullscreen(); deadline = Time.realtimeSinceStartup + 5;
            while (Screen.fullScreen && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i=0;i<30;i++) yield return null;
            if (Screen.fullScreen || Screen.width != windowWidth || Screen.height != windowHeight)
            {Debug.LogError("窗口尺寸没有恢复");Application.Quit(22);yield break;}
            ToggleCleanView(); SetCameraMode(ReplayCameraMode.Observe, false);
            Debug.Log($"REPLAY_WINDOW_RESTORE_OK resolution={Screen.width}x{Screen.height} viewport={viewCamera.rect}");
            Debug.Log("REPLAY_CAMERA_SMOKE_OK"); Application.Quit(0);
        }
    }
}
