using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TcrReplay
{
    public sealed class DeveloperPhysicsWorld : IDisposable
    {
        sealed class Surface {public Collider source,copy;}
        readonly List<Surface> surfaces=new List<Surface>();
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        readonly Dictionary<string,DeveloperVehiclePhysics> vehicles=new Dictionary<string,DeveloperVehiclePhysics>();
        readonly Dictionary<string,Mesh> tires=new Dictionary<string,Mesh>();
        readonly Dictionary<Rigidbody,string> owners=new Dictionary<Rigidbody,string>();
        Collider walkableQuerySurface;
        public readonly Scene scene;public readonly GameObject root;
        public readonly PhysicsMaterial tireMaterial,bodyMaterial;
        public PhysicsScene Physics=>scene.GetPhysicsScene();
        public float Substep {get;private set;}=DeveloperWorld.Step/2;
        public double LastMilliseconds {get;private set;}
        public int BodyCount=>vehicles.Count*5;
        public int SurfaceCount=>surfaces.Count;
        public DeveloperPhysicsWorld(GameObject fieldRoot,Func<string,Mesh> readMesh=null)
        {
            scene=SceneManager.CreateScene("Developer vehicle physics "+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            root=new GameObject("Developer physical objects");SceneManager.MoveGameObjectToScene(root,scene);
            tireMaterial=new PhysicsMaterial("Omni tire normal contact") {staticFriction=0,dynamicFriction=0,bounciness=0,frictionCombine=PhysicsMaterialCombine.Multiply,bounceCombine=PhysicsMaterialCombine.Minimum};
            bodyMaterial=new PhysicsMaterial("Vehicle frame contact") {staticFriction=.5f,dynamicFriction=.35f,bounciness=0,frictionCombine=PhysicsMaterialCombine.Multiply,bounceCombine=PhysicsMaterialCombine.Minimum};
            owned.Add(tireMaterial);owned.Add(bodyMaterial);
            if(fieldRoot)foreach(var source in fieldRoot.GetComponentsInChildren<Collider>(true))
            {
                int layer=source.gameObject.layer;if(source.isTrigger||(layer!=8&&layer!=10&&layer!=14&&layer!=17)||layer==17&&!source.enabled)continue;
                var node=NewObject("Physical surface "+source.name,layer);Collider copy=null;
                if(source is MeshCollider m){var x=node.AddComponent<MeshCollider>();x.sharedMesh=readMesh!=null&&source.name.StartsWith("cad_343127_",StringComparison.Ordinal)?readMesh("assets/developer/field/final_road_contact.tcrmesh"):m.sharedMesh;x.convex=m.convex;copy=x;}
                else if(source is BoxCollider b){var x=node.AddComponent<BoxCollider>();x.center=b.center;x.size=b.size;copy=x;}
                else if(source is SphereCollider s){var x=node.AddComponent<SphereCollider>();x.center=s.center;x.radius=s.radius;copy=x;}
                else if(source is CapsuleCollider c){var x=node.AddComponent<CapsuleCollider>();x.center=c.center;x.radius=c.radius;x.height=c.height;x.direction=c.direction;copy=x;}
                if(copy){copy.sharedMaterial=bodyMaterial;copy.contactOffset=.002f;surfaces.Add(new Surface {source=source,copy=copy});if(source.name=="Independent walkable floor")walkableQuerySurface=copy;}else UnityEngine.Object.Destroy(node);
            }
            SyncSurfaces();Physics.Simulate(.000001f);
        }
        public GameObject NewObject(string name,int layer)
        {var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);obj.transform.SetParent(root.transform,false);obj.layer=layer;return obj;}
        public void SyncSurfaces()
        {
            foreach(var s in surfaces)
            {
                bool enabled=s.source&&s.source.enabled&&s.source.gameObject.activeInHierarchy;s.copy.enabled=enabled;if(!enabled)continue;
                var a=s.source.transform;var b=s.copy.transform;
                if(b.position!=a.position||b.rotation!=a.rotation||b.localScale!=a.lossyScale){b.SetPositionAndRotation(a.position,a.rotation);b.localScale=a.lossyScale;}
            }
            UnityEngine.Physics.SyncTransforms();
        }
        public bool TryOwner(Collider c,out string id){id=null;return c&&c.attachedRigidbody&&owners.TryGetValue(c.attachedRigidbody,out id);}
        public void Add(DeveloperVehiclePhysics vehicle)
        {
            vehicles.Add(vehicle.Actor.id,vehicle);owners.Add(vehicle.Body,vehicle.Actor.id);foreach(var w in vehicle.Wheels)owners.Add(w.Body,vehicle.Actor.id);
            // The walkable mesh repeats the road triangles already present in
            // the complete CAD collision mesh. Keep it for height/navigation
            // queries, but never resolve the same normal contact twice.
            foreach(var c in vehicle.Shapes)IgnoreDuplicateFloor(c);
        }
        public void IgnoreDuplicateFloor(Collider c)
        {if(walkableQuerySurface&&surfaces.Exists(s=>s.source&&s.source.gameObject.layer==10))UnityEngine.Physics.IgnoreCollision(c,walkableQuerySurface,true);}
        public void Remove(string id){if(vehicles.TryGetValue(id,out var v)){owners.Remove(v.Body);foreach(var w in v.Wheels)owners.Remove(w.Body);v.Dispose();}vehicles.Remove(id);}
        public void Step(DeveloperWorld game)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();SyncSurfaces();int n=Mathf.Clamp(game.rules.physicsSubsteps,1,4);Substep=DeveloperWorld.Step/n;
            foreach(var v in vehicles.Values)v.BeginTick();
            for(int i=0;i<n;i++)
            {
                foreach(var v in vehicles.Values)v.PreStep(game,Substep);
                Physics.Simulate(Substep);
                foreach(var v in vehicles.Values)v.PostStep(Substep);
            }
            foreach(var v in vehicles.Values)v.Publish(game);
            LastMilliseconds=timer.Elapsed.TotalMilliseconds;
        }
        public Mesh TireMesh(VehicleRigWheel wheel)
        {
            string key=wheel.radius.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+wheel.width.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(tires.TryGetValue(key,out var mesh))return mesh;
            // A bounded bevel represents the tire/roller rim. A sharp-ended
            // cylinder can hook a CAD triangle edge while crossing a slope.
            const int count=24,rings=4;var vertices=new Vector3[count*rings];var indices=new List<int>();
            for(int ring=0;ring<rings;ring++)for(int i=0;i<count;i++)
            {float a=i*Mathf.PI*2/count,x=(ring==0?-.5f:ring==1?-.35f:ring==2?.35f:.5f)*wheel.width,r=wheel.radius-((ring==0||ring==3)?Mathf.Min(.0015f,wheel.width*.08f):0);vertices[ring*count+i]=new Vector3(x,Mathf.Cos(a)*r,Mathf.Sin(a)*r);}
            for(int ring=0;ring<rings-1;ring++)for(int i=0;i<count;i++){int j=(i+1)%count,a=ring*count,b=(ring+1)*count;indices.AddRange(new[]{a+i,a+j,b+j,a+i,b+j,b+i});}
            for(int i=1;i<count-1;i++){int end=(rings-1)*count;indices.AddRange(new[]{0,i+1,i,end,end+i,end+i+1});}
            mesh=new Mesh {name="Wheel normal contact "+key};mesh.vertices=vertices;mesh.triangles=indices.ToArray();mesh.RecalculateBounds();owned.Add(mesh);tires.Add(key,mesh);return mesh;
        }
        public void Dispose()
        {
            foreach(var v in vehicles.Values)v.Dispose();vehicles.Clear();owners.Clear();surfaces.Clear();
            if(root){root.SetActive(false);UnityEngine.Object.Destroy(root);}foreach(var o in owned)if(o)UnityEngine.Object.Destroy(o);owned.Clear();
            if(scene.IsValid()&&scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
        }
    }
}
