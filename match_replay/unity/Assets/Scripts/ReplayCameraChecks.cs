using System;
using UnityEngine;

namespace TcrReplay
{
    public static class ReplayCameraChecks
    {
        public static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("相机验证失败：" + message); }
        public static void Motion()
        {
            var a = new ReplayCameraRig(); var b = new ReplayCameraRig();
            Quaternion rotation = Quaternion.Euler(-30, 65, 0);
            a.Free(Vector3.zero, rotation); b.Free(Vector3.zero, rotation);
            a.Move(Vector3.forward, 1);
            for (int i = 0; i < 60; i++) b.Move(Vector3.forward, 1f / 60);
            Require(Vector3.Distance(a.Position, b.Position) < .0001f && Mathf.Abs(a.Position.magnitude - 5) < .0001f,
                "帧率改变移动距离");
            a.Free(Vector3.zero, rotation); a.Move(new Vector3(1, 1, 1), 1);
            Require(Mathf.Abs(a.Position.magnitude - 5) < .0001f, "斜向移动更快");
            a.Free(Vector3.zero, rotation); a.Move(Vector3.up, 1);
            Require(Vector3.Distance(a.Position, Vector3.up * 5) < .0001f, "空格不是世界坐标上升");
            a.Move(Vector3.down, 1);
            Require(a.Position.magnitude < .0001f, "Shift 不能下降");
            a.ToggleSlow(); a.Move(Vector3.forward, 1);
            Require(Mathf.Abs(a.Position.magnitude - .75f) < .0001f, "Caps Lock 慢速无效");
            a.ToggleSlow(); Require(a.MoveSpeed == 5, "不能切回正常速度");
            a.Look(new Vector2(600, 1000));
            Require(Vector3.Dot(a.Rotation * Vector3.up, Vector3.up) > 0 &&
                Mathf.Abs((a.Rotation * Vector3.forward).y) > .99f, "俯仰极值翻转相机");
            Debug.Log("REPLAY_CAMERA_MOTION_OK rate=60/1 normal=5 slow=0.75 diagonal=normalized vertical=world");
        }
    }
}
