using UnityEngine;

namespace TcrReplay
{
    public enum ReplayCameraMode { Observe, Free, Attached }

    // Replay time controls the anchor; wall-clock time controls the viewer.
    // No recorded chassis/turret heading is used to rotate this camera.
    public sealed class ReplayCameraRig
    {
        public const float NormalSpeed = 5f, SlowSpeed = .75f, MouseSensitivity = 2.5f;
        public ReplayCameraMode Mode { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0);
        public bool Slow { get; private set; }
        public bool TargetVisible { get; private set; }
        public bool HasAnchor { get; private set; }
        public float MoveSpeed => Slow ? SlowSpeed : NormalSpeed;
        float yaw, pitch;
        Vector3 anchor, offset;

        void SetPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            yaw = rotation.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, rotation.eulerAngles.x);
            pitch = Mathf.Clamp(pitch, -89.5f, 89.5f);
        }
        public void Observe() { Mode = ReplayCameraMode.Observe; }
        public void Free(Vector3 position, Quaternion rotation)
        {
            Mode = ReplayCameraMode.Free;
            SetPose(position, rotation);
        }
        public void Attach(Vector3 currentPosition, Quaternion rotation, Vector3 eyeOffset,
                           bool visible, Vector3 target)
        {
            Mode = ReplayCameraMode.Attached;
            SetPose(currentPosition, rotation);
            offset = eyeOffset;
            HasAnchor = false;
            Track(visible, target);
        }
        public void Track(bool visible, Vector3 target)
        {
            TargetVisible = visible;
            // Preserve the last real anchor through missing telemetry. In particular,
            // do not treat an inactive unit's (0,0,0) as a new position.
            if (visible) { anchor = target; HasAnchor = true; }
            if (HasAnchor) Position = anchor + offset;
        }
        public void ToggleSlow() { Slow = !Slow; }
        public void ConstrainPosition(Vector3 position)
        {
            if (Mode == ReplayCameraMode.Attached) offset += position - Position;
            Position = position;
        }
        public void Look(Vector2 mouseDelta)
        {
            yaw = Mathf.Repeat(yaw + mouseDelta.x * MouseSensitivity, 360);
            pitch = Mathf.Clamp(pitch - mouseDelta.y * MouseSensitivity, -89.5f, 89.5f);
        }
        public void Move(Vector3 input, float elapsed)
        {
            // Space/Shift always move vertically in the world. WASD follows the
            // viewing direction. Normalize the result so diagonals are not faster.
            Vector3 direction = Rotation * new Vector3(input.x, 0, input.z) + Vector3.up * input.y;
            Vector3 delta = Vector3.ClampMagnitude(direction, 1) * MoveSpeed * elapsed;
            Position += delta;
            if (Mode == ReplayCameraMode.Attached) offset += delta;
        }
    }
}
