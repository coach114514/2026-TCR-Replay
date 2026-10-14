using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        const float CameraFloorRadius=.08f;
        void BuildGroundCollision(Transform parent)
        {
            if(string.IsNullOrEmpty(field.collisionMesh)) return;
            var obj=new GameObject("Independent walkable floor");obj.layer=8;obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshCollider>().sharedMesh=ReadMesh(field.collisionMesh);
        }
        void ConstrainCameraGround()
        {
            var position=cameraRig.Position;
            // The camera's offset is corrected in the rig too, so attachment and
            // the next frame cannot silently undo the floor constraint.
            // Query from the camera's current level, so a deck overhead cannot
            // teleport a camera walking beneath the platform onto its top.
            float floor=GroundHeight(position.x,position.z);
            if(Physics.Raycast(position+Vector3.up*.30f,Vector3.down,out var hit,12,1<<12))floor=Mathf.Max(floor,hit.point.y);
            floor+=CameraFloorRadius;
            if(position.y<floor) {position.y=floor;cameraRig.ConstrainPosition(position);}
        }
    }
}
