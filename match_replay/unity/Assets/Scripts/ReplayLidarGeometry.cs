using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TcrReplay
{
    // An isolated physics scene prevents sensor surfaces from affecting vehicles
    // or projectiles. Copies are frozen for the entire batched query.
    sealed class ReplayLidarGeometry : IDisposable
    {
        sealed class Entry {public MeshFilter source;public MeshCollider copy;public Func<bool> present;}
        readonly List<Entry> entries=new List<Entry>();Scene scene;GameObject root,fieldRoot;int fieldEntries;
        public PhysicsScene PhysicsScene=>scene.GetPhysicsScene();
        public int Count=>entries.Count;
        public ReplayLidarGeometry()
        {
            scene=SceneManager.CreateScene("Avia isolated scan "+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            root=new GameObject("Physical surfaces for Avia");SceneManager.MoveGameObjectToScene(root,scene);
        }
        public void Reset(GameObject field)
        {
            // Reuse one local scene and the static field. Repeated asynchronous
            // scene disposal must not race the global transform flush on reset.
            int keep=fieldRoot==field?fieldEntries:0;
            for(int i=entries.Count-1;i>=keep;i--)
            {
                var copy=entries[i].copy;copy.enabled=false;
                UnityEngine.Object.Destroy(copy.gameObject);entries.RemoveAt(i);
            }
            if(keep==0){fieldRoot=field;Add(field);fieldEntries=entries.Count;}
        }
        public void Add(GameObject sourceRoot,Func<bool> present=null)
        {
            if(!sourceRoot)return;
            foreach(var filter in sourceRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh||filter.gameObject.layer==17)continue;
                if((filter.name.StartsWith("LOD ",StringComparison.Ordinal)||filter.name.StartsWith("Mechanical LOD ",StringComparison.Ordinal))&&!filter.name.EndsWith("LOD 1",StringComparison.Ordinal))continue;
                var renderer=filter.GetComponent<Renderer>();if(renderer&&!renderer.enabled)continue;
                var copy=new GameObject("Scan "+filter.name);SceneManager.MoveGameObjectToScene(copy,scene);copy.transform.SetParent(root.transform,false);
                var collider=copy.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
                entries.Add(new Entry {source=filter,copy=collider,present=present});
            }
        }
        public void Freeze()
        {
            foreach(var e in entries)
            {
                bool enabled=e.source&&e.source.gameObject.activeInHierarchy&&(e.present==null||e.present());
                if(enabled){var renderer=e.source.GetComponent<Renderer>();enabled=!renderer||renderer.enabled;}
                e.copy.enabled=enabled;if(!enabled)continue;
                var t=e.source.transform;e.copy.transform.SetPositionAndRotation(t.position,t.rotation);e.copy.transform.localScale=t.lossyScale;
            }
            Physics.SyncTransforms();
            // Static copies have no integration dynamics. Simulation flushes
            // newly inserted geometry in this local scene before queries.
            PhysicsScene.Simulate(.000001f);
        }
        public void Dispose()
        {
            if(root){root.SetActive(false);UnityEngine.Object.Destroy(root);}entries.Clear();
            if(scene.IsValid()&&scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
        }
    }
}
