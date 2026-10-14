using UnityEngine;
using UnityEngine.Rendering;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        const int VehicleBoundaryLayer=17;
        GameObject safetyBoundary;
        float BoundaryHeight(Vector3 p)
        {return Physics.Raycast(p+Vector3.up*6,Vector3.down,out var h,8,1<<8,QueryTriggerInteraction.Ignore)?h.point.y:0;}
        void BuildVisibleBoundary(Transform parent)
        {
            Physics.SyncTransforms();safetyBoundary=new GameObject("Visible vehicle safety boundary");safetyBoundary.transform.SetParent(parent,false);
            var template=Resources.Load<Material>("ReplayBoundary");var glass=template?new Material(template):new Material(Shader.Find("TCR/Replay safety boundary"));
            glass.color=new Color(.18f,.8f,.85f,.08f);owned.Add(glass);
            var rail=Material(new Color(.3f,.85f,.85f),true);
            Vector3[] corners={Vector3.zero,new Vector3(field.width,0,0),new Vector3(field.width,0,field.depth),new Vector3(0,0,field.depth),Vector3.zero};
            for(int side=0;side<4;side++)
            {
                Vector3 from=corners[side],to=corners[side+1],direction=(to-from).normalized;float length=Vector3.Distance(from,to);int count=Mathf.CeilToInt(length);float span=length/count;
                for(int i=0;i<count;i++)
                {
                    Vector3 p=Vector3.Lerp(from,to,(i+.5f)/count);float height=Mathf.Max(BoundaryHeight(p-direction*span*.5f),Mathf.Max(BoundaryHeight(p),BoundaryHeight(p+direction*span*.5f)));
                    var wall=Shape(PrimitiveType.Cube,safetyBoundary.transform,new Vector3(p.x,(height+2-.6f)*.5f,p.z),new Vector3(span+.04f,height+2+.6f,.12f),glass);wall.name="Vehicle safety wall "+side+"-"+i;wall.layer=VehicleBoundaryLayer;wall.transform.rotation=Quaternion.LookRotation(Vector3.Cross(direction,Vector3.up));wall.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    var strip=Shape(PrimitiveType.Cube,safetyBoundary.transform,new Vector3(p.x,height+.32f,p.z),new Vector3(span+.04f,.035f,.14f),rail);strip.name="Boundary warning rail";strip.layer=VehicleBoundaryLayer;strip.transform.rotation=wall.transform.rotation;strip.GetComponent<Collider>().enabled=false;Destroy(strip.GetComponent<Collider>());
                }
            }
        }
        void PhysicsBoundaryGuard(GameVehicle v)
        {
            var a=v.state;if(v.physics==null||v.recovering||!a.Alive)return;
            bool outside=a.position.x<-.25f||a.position.x>field.width+.25f||a.position.z<-.25f||a.position.z>field.depth+.25f||a.position.y<-.8f;
            if(outside){game.Log(a.id+" 触发防坠落恢复");RequestRecovery(a.id,true);return;}
            if(game.time<v.nextSafeSample)return;v.nextSafeSample=game.time+.5f;
            if(v.physics.Grounded==4&&v.physics.SupportNormal.y>.99f&&a.velocity.magnitude<2&&RecoveryLanding(v,a.position,out var pad,out _))
            {v.lastSafePosition=pad;v.hasSafePosition=true;}
        }
    }
}
