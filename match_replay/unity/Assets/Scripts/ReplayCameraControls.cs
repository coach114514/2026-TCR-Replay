using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        readonly ReplayCameraRig cameraRig = new ReplayCameraRig();
        Unit attachedUnit, hiddenVehicle;
        BuildingAnchor attachedMarker;
        bool mouseCaptured, cleanView;
        Vector2 landmarkScroll;
        int captureFrame, windowWidth = 1600, windowHeight = 900;
        float screenChangeTime = -10;
        bool TravelCamera => cameraRig.Mode != ReplayCameraMode.Observe;
        Rect CameraViewport => appMode!="replay" || cleanView ? new Rect(0, 0, 1, 1) : new Rect(0, .14f, .76f, .73f);
        string CameraName => cameraRig.Mode == ReplayCameraMode.Free ? "自由相机" :
            cameraRig.Mode == ReplayCameraMode.Attached ? "第一人称跟随" : "观察视角";
        string CameraStatus
        {
            get
            {
                string status = CameraName;
                if (TravelCamera) status += $" · {(cameraRig.Slow ? "慢速" : "正常")} {cameraRig.MoveSpeed:0.##} m/s";
                if (cameraRig.Mode == ReplayCameraMode.Attached && (attachedMarker != null || units.Count > selected))
                    status += " · " + (attachedMarker != null ? attachedMarker.name :
                        (units[selected].data.team == "red" ? "红" : "蓝") + units[selected].data.name) +
                        (cameraRig.TargetVisible ? "" : " · 位置缺失，镜头保持");
                return status;
            }
        }

        void CaptureMouse()
        {
            if (!TravelCamera || !BackgroundInputAllowed) return;
            mouseCaptured = true;
            captureFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            GUIUtility.keyboardControl = 0;
        }
        void ReleaseMouse()
        {
            mouseCaptured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        void OnApplicationFocus(bool focused) { if (!focused) {ReleaseMouse();uiDismissedKeys.Clear();} }
        void OnDisable() { ReleaseMouse(); RestoreVehicle(); }
        void RestoreVehicle()
        {
            if (hiddenVehicle != null)
                foreach (var renderer in hiddenVehicle.renderers) if (renderer) renderer.forceRenderingOff = false;
            hiddenVehicle = null;
        }
        Vector3 EyeOffset(Unit unit)
        {
            if(unit.robot!=null) return Vector3.zero;
            bool building = unit.data.role == "base" || unit.data.role == "outpost";
            float height = unit.data.role == "air" ? .25f : building ? 1.3f : .85f;
            foreach (var renderer in unit.renderers)
                height = Mathf.Max(height, renderer.bounds.max.y - unit.root.transform.position.y + .15f);
            return Vector3.up * height;
        }
        Vector3 AttachmentAnchor(Unit unit) => unit.robot!=null?unit.robot.Eye:unit.root.transform.position;
        void SelectUnit(int index)
        {
            attachedMarker = null;
            selected = index;
            if (cameraRig.Mode == ReplayCameraMode.Attached) RefreshAttachment();
        }
        void RefreshAttachment()
        {
            if (attachedMarker != null)
            {
                cameraRig.Track(true, MarkerPosition(attachedMarker));
                return;
            }
            if (units.Count <= selected) return;
            var unit = units[selected];
            if (attachedUnit != unit)
            {
                attachedUnit = unit;
                cameraRig.Attach(viewCamera.transform.position, cameraRig.Rotation, EyeOffset(unit),
                    unit.visible, AttachmentAnchor(unit));
            }
            else cameraRig.Track(unit.visible, AttachmentAnchor(unit));
        }
        void SetCameraMode(ReplayCameraMode mode, bool capture = true)
        {
            attachedMarker = null; landmarkChooser = false;
            RestoreVehicle();
            if (mode == ReplayCameraMode.Observe)
            {
                cameraRig.Observe(); ReleaseMouse();
                follow = false;
                focus = units.Count > selected && units[selected].visible ? units[selected].root.transform.position :
                    new Vector3(field.width / 2, 0, field.depth / 2);
                distance = 32; yaw = 0; pitch = 58;
            }
            else if (mode == ReplayCameraMode.Free)
                cameraRig.Free(viewCamera.transform.position, viewCamera.transform.rotation);
            else if (units.Count > selected)
            {
                var unit = units[selected];
                Quaternion rotation = viewCamera.transform.rotation;
                if (!TravelCamera)
                {
                    Vector3 center = new Vector3(field.width / 2, 0, field.depth / 2);
                    Vector3 direction = center - unit.root.transform.position; direction.y = 0;
                    rotation = direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : Quaternion.identity;
                }
                attachedUnit = unit;
                cameraRig.Attach(viewCamera.transform.position, rotation, EyeOffset(unit), unit.visible, AttachmentAnchor(unit));
            }
            UpdateCamera();
            if (capture) CaptureMouse();
        }
        Vector3 MarkerPosition(BuildingAnchor marker)
        {
            if(markerEyes.TryGetValue(marker.team+"_"+marker.role,out var eye))return eye.position;
            if (buildingNodes.TryGetValue(marker.team + "_" + marker.role, out var node)) return node.position;
            return new Vector3(marker.x, GroundHeight(marker.x, marker.y), marker.y);
        }
        void AttachMarker(BuildingAnchor marker, bool capture = true)
        {
            RestoreVehicle(); attachedUnit = null; attachedMarker = marker; landmarkChooser = false;
            Vector3 point = MarkerPosition(marker);
            float eyeHeight = 1.6f;
            bool lensView=markerEyes.ContainsKey(marker.team+"_"+marker.role);
            if(lensView)eyeHeight=0;
            else if (buildingNodes.TryGetValue(marker.team + "_" + marker.role, out var node))
                foreach (var renderer in node.GetComponentsInChildren<Renderer>())
                    eyeHeight = Mathf.Max(eyeHeight, renderer.bounds.max.y - point.y + .2f);
            Vector3 towardCenter = new Vector3(field.width / 2, point.y, field.depth / 2) - point;
            Quaternion rotation = TravelCamera && !lensView ? viewCamera.transform.rotation :
                towardCenter.sqrMagnitude > .01f ? Quaternion.LookRotation(towardCenter) : Quaternion.identity;
            cameraRig.Attach(viewCamera.transform.position, rotation, Vector3.up * eyeHeight, true, point);
            UpdateCamera();
            if (capture) CaptureMouse();
        }
        void ResetCamera()
        {
            if (cameraRig.Mode == ReplayCameraMode.Attached)
            {
                // Reset the eye offset while keeping the independently chosen look direction.
                if (attachedMarker != null) AttachMarker(attachedMarker, false);
                else {attachedUnit = null; RefreshAttachment();}
            }
            else
            {
                bool free = cameraRig.Mode == ReplayCameraMode.Free;
                SetCameraMode(ReplayCameraMode.Observe, false);
                focus = new Vector3(field.width / 2, 0, field.depth / 2);
                UpdateCamera();
                if (free) SetCameraMode(ReplayCameraMode.Free, false);
            }
            UpdateCamera();
        }
        void ToggleCleanView()
        {
            cleanView = !cleanView;
            viewCamera.rect = CameraViewport;
            if (cleanView) {chooser = false; landmarkChooser = false;}
        }
        void ToggleFullscreen()
        {
            // Native display resolution in borderless fullscreen, then restore the
            // user's last window size. Resolution changes finish at frame end.
            if (Time.unscaledTime - screenChangeTime < .4f) return;
            screenChangeTime = Time.unscaledTime;
            ReleaseMouse();
            if (Screen.fullScreen)
                Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
            else
            {
                windowWidth = Screen.width; windowHeight = Screen.height;
                var display = Screen.currentResolution;
                Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
            }
        }
        void CameraShortcuts()
        {
            if (mouseCaptured && Cursor.lockState != CursorLockMode.Locked) ReleaseMouse();
            if (!BackgroundInputAllowed || match == null || !viewCamera) return;
            if (Input.GetKeyDown(KeyCode.F10)) ToggleCleanView();
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetCameraMode(ReplayCameraMode.Observe);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetCameraMode(ReplayCameraMode.Free);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetCameraMode(ReplayCameraMode.Attached);
            if (TravelCamera && Input.GetKeyDown(KeyCode.CapsLock)) cameraRig.ToggleSlow();
        }
        void UpdateTravelInput(bool onField)
        {
            if(!BackgroundInputAllowed)return;
            if (!mouseCaptured)
            {
                if (onField && Input.GetMouseButtonDown(0)) CaptureMouse();
                return;
            }
            // Discard the first mouse delta following a cursor warp / UI button click.
            if (Time.frameCount <= captureFrame + 1) return;
            cameraRig.Look(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));
            Vector3 input = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            cameraRig.Move(input, Time.unscaledDeltaTime);
        }
        void DrawCameraControls()
        {
            if (Button(20, 192, 140, 32, "1  观察视角")) SetCameraMode(ReplayCameraMode.Observe);
            if (Button(172, 192, 140, 32, "2  自由相机")) SetCameraMode(ReplayCameraMode.Free);
            if (Button(324, 192, 170, 32, "3  第一人称跟随")) SetCameraMode(ReplayCameraMode.Attached);
            if (Button(506, 192, 160, 32, "F10  纯场地视图")) ToggleCleanView();
            if (TravelCamera && Button(678, 192, 150, 32, mouseCaptured ? "Esc  释放鼠标" : "控制相机"))
            { if (mouseCaptured) ReleaseMouse(); else CaptureMouse(); }
            Text(20, 228, 805, 28, CameraStatus, small);
            if(Button(842,192,348,32,"F8 显示设置 · "+measuredFps.ToString("0")+" FPS")){displayChooser=!displayChooser;ReleaseMouse();}
        }
        void DrawLandmarkChooser()
        {
            Panel(new Rect(20, 262, 820, 338));
            Text(36, 276, 680, 30, "选择固定地物视角（CAD 建筑 / 规则图区域点）", label);
            if (Button(738, 274, 86, 30, "关闭")) landmarkChooser = false;
            var markers = field.landmarks;
            if (markers == null) return;
            landmarkScroll = GUI.BeginScrollView(new Rect(36, 318, 786, 260), landmarkScroll,
                new Rect(0, 0, 760, Mathf.Max(260, ((markers.Length + 1) / 2) * 44)));
            for (int i = 0; i < markers.Length; i++)
                if (Button(i % 2 * 380, i / 2 * 44, 370, 38, markers[i].name)) AttachMarker(markers[i]);
            GUI.EndScrollView();
        }
        string CameraHelp => TravelCamera ?
            (mouseCaptured ? "鼠标转向 · " : "点击场地控制相机 · ") +
            "WASD 移动 · 空格上升 / Shift 下降 · Caps Lock 正常/慢速 · Esc 释放鼠标 · P 播放/暂停" :
            "右键旋转 · 中键平移 · 滚轮缩放 · 点击车辆查看 · 空格/P 播放暂停 · ←/→ 跳转 5 秒";
    }
}
