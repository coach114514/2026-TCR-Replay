using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer : MonoBehaviour
    {
        class Unit
        {
            public EntityData data;
            public GameObject root;
            public Transform turret;
            public Renderer[] renderers;
            public LineRenderer trail;
            public Sample sample;
            public bool visible;
            public Transform cadNode;
            public RobotInstance robot;
            public BoxCollider[] armorColliders;
            public readonly List<Vector3> trailPoints=new List<Vector3>(34);
        }
        public Camera viewCamera;
        MatchCatalog catalog;
        MatchData match;
        FieldConfig field;
        FieldCatalog fieldCatalog;
        int fieldIndex;
        bool fieldMismatch;
        GameObject fieldRoot;
        readonly Dictionary<string,Transform> buildingNodes = new Dictionary<string,Transform>();
        readonly Dictionary<string,Mesh> meshCache = new Dictionary<string,Mesh>();
        readonly Dictionary<string,Texture2D> textureCache = new Dictionary<string,Texture2D>();
        readonly List<Material> cadMaterials = new List<Material>();
        readonly List<Unit> units = new List<Unit>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        GameObject terrain;
        Renderer ground;
        Texture2D mapTexture;
        Material groundMaterial;
        Material terrainMaterial;
        string error = "";
        string folder;
        float playhead, speed = 1, yaw = 0, pitch = 58, distance = 32;
        Vector3 focus = new Vector3(14, 0, 7.5f);
        bool playing, trails = true, headings=true, texture = true, meshVisible = true, topView, filterShots, follow, landmarkLabels = true;
        readonly List<Rect> labelBounds = new List<Rect>();
        int selected, matchIndex;
        Vector2 eventScroll, matchScroll;
        Font font;
        GUIStyle label, small, title, button, worldLabel;
        readonly Color red = new Color(.96f, .28f, .32f), blue = new Color(.22f, .65f, 1);

        void Start()
        {
            unitColorBlock=new MaterialPropertyBlock();
            InitializeDisplaySettings();
            folder = Path.Combine(Application.streamingAssetsPath, "replay");
            try
            {
                resourceProfile=ReplayResourceProfile.Load(folder);
                font = Resources.Load<Font>("ReplayChinese");
                string fieldCatalogPath=Path.Combine(folder,"fields_catalog.json");
                fieldCatalog=File.Exists(fieldCatalogPath)?JsonUtility.FromJson<FieldCatalog>(File.ReadAllText(fieldCatalogPath)):
                    new FieldCatalog {schema=1,defaultField="rmuc_2026_region_candidate",fields=new [] {new FieldEntry {id="rmuc_2026_region_candidate",name="区域赛模型",file="field_config.json"}}};
                if(fieldCatalog.schema!=1 || fieldCatalog.fields==null || fieldCatalog.fields.Length==0) throw new InvalidDataException("场地目录无效");
                fieldIndex=Array.FindIndex(fieldCatalog.fields,a=>a.id==fieldCatalog.defaultField);
                if(fieldIndex<0) fieldIndex=0;
                field = JsonUtility.FromJson<FieldConfig>(File.ReadAllText(Path.Combine(folder,fieldCatalog.fields[fieldIndex].file)));
                catalog = JsonUtility.FromJson<MatchCatalog>(File.ReadAllText(Path.Combine(folder, "catalog.json")));
                if (catalog.schema != 1 || catalog.matches == null || catalog.matches.Length == 0)
                    throw new InvalidDataException("比赛目录为空或版本无效");
                if (field.width <= 0 || field.depth <= 0) throw new InvalidDataException("场地尺寸无效");
                var warmup = Resources.Load<TextAsset>("ReplayGlyphs");
                if(font && warmup)
                {
                    foreach(int size in new [] {12,14,16,17,24}) font.RequestCharactersInTexture(warmup.text,size,FontStyle.Normal);
                    font.RequestCharactersInTexture(warmup.text,24,FontStyle.Bold);
                }
                LoadLidarPreferences();
                LoadBallisticConfig();
                LoadRobotCatalog();
                PrepareLibrary();
                LoadMechanismAssets();
                BuildField();
                LoadMatch(0);
            }
            catch (Exception ex) { error = ex.Message; Debug.LogException(ex); }
            string[] args = Environment.GetCommandLineArgs();
            InitializeUiProbe(args);
            if(Array.IndexOf(args,"--traction-safety-smoke")>=0){BeginTractionSafetyAudit(args[Array.IndexOf(args,"--traction-safety-smoke")+1]);return;}
            if(Array.IndexOf(args,"--traction-smoke")>=0){BeginTractionAudit(args[Array.IndexOf(args,"--traction-smoke")+1]);return;}
            if(Array.IndexOf(args,"--replay-lidar-smoke")>=0){EnterReplay();StartCoroutine(ReplayLidarAudit(args[Array.IndexOf(args,"--replay-lidar-smoke")+1]));return;}
            if(Array.IndexOf(args,"--lidar-api-smoke")>=0){EnterReplay();StartCoroutine(LidarApiAudit(args[Array.IndexOf(args,"--lidar-api-smoke")+1]));return;}
            if(Array.IndexOf(args,"--lidar-smoke")>=0){EnterDeveloper();StartCoroutine(LidarAudit(args[Array.IndexOf(args,"--lidar-smoke")+1]));return;}
            if(Array.IndexOf(args,"--physics-smoke")>=0){BeginPhysicsAudit(args[Array.IndexOf(args,"--physics-smoke")+1]);return;}
            if(Array.IndexOf(args,"--drive-smoke")>=0){EnterDeveloper();StartCoroutine(DriveRecoverySmoke(args));return;}
            if(Array.IndexOf(args,"--drive-probe")>=0){EnterDeveloper();StartCoroutine(DriveProbe(args));return;}
            if(Array.IndexOf(args,"--rune-precision-smoke")>=0){EnterDeveloper();StartCoroutine(RunePrecisionSmoke(args));return;}
            if(Array.IndexOf(args,"--camera-aim-smoke")>=0){EnterDeveloper();StartCoroutine(CameraAimSmoke(args));return;}
            if(Array.IndexOf(args,"--rune-auto-smoke")>=0){EnterDeveloper();StartCoroutine(RuneAutoSmoke(args));return;}
            if(Array.IndexOf(args,"--aim-rune-smoke")>=0){EnterDeveloper();StartCoroutine(AimRuneSmoke(args));return;}
            if(Array.IndexOf(args,"--base-visual-smoke")>=0){EnterDeveloper();StartCoroutine(BaseVisualSmoke(args));return;}
            if(Array.IndexOf(args,"--phase2-smoke")>=0){EnterDeveloper();StartCoroutine(Phase2Smoke(args));return;}
            if(Array.IndexOf(args,"--game-smoke")>=0){EnterDeveloper();StartCoroutine(DeveloperSmoke(args));return;}
            bool automated=Array.Exists(args,a=>a.EndsWith("-smoke")||a=="--camera-probe");
            if(!automated && string.IsNullOrEmpty(error))EnterMenu();else appMode="replay";
            int probe=Array.IndexOf(args,"--camera-probe");
            if(probe>=0 && probe+1<args.Length) cameraProbePath=args[probe+1];
            if (Array.IndexOf(args, "--library-smoke") >= 0) StartCoroutine(LibrarySmoke(args));
            else if(Array.IndexOf(args,"--mechanism-smoke")>=0)StartCoroutine(MechanismSmoke(args));
            else if(Array.IndexOf(args,"--display-smoke")>=0)StartCoroutine(DisplaySmoke(args));
            else if (Array.IndexOf(args, "--physics-performance-smoke") >= 0) {EnterDeveloper();StartCoroutine(PhysicsPerformanceSmoke(args));}
            else if (Array.IndexOf(args, "--replay-smoke") >= 0) StartCoroutine(Smoke(args));
            else if (Array.IndexOf(args, "--camera-smoke") >= 0) StartCoroutine(CameraSmoke(args));
            else if (Array.IndexOf(args, "--ballistics-smoke") >= 0) StartCoroutine(BallisticsSmoke(args));
            else if (Array.IndexOf(args, "--robot-smoke") >= 0) StartCoroutine(RobotSmoke(args));
            else if (Array.IndexOf(args, "--label-smoke") >= 0) StartCoroutine(LabelSmoke(args));
        }

        Material Material(Color color, bool unlit = false)
        {
            var template = Resources.Load<Material>(unlit ? "ReplayUnlit" : "ReplayStandard");
            var material = template ? new Material(template) : new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard"));
            material.color = color;
            if(!unlit) {material.SetFloat("_Glossiness",.08f);material.SetFloat("_Metallic",0);}
            owned.Add(material);
            return material;
        }
        GameObject Shape(PrimitiveType shape, Transform parent, Vector3 pos, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(shape);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = pos;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
        Texture2D LoadTexture(string path,bool repeat=false)
        {
            if(textureCache.TryGetValue(path,out var cached)) return cached;
            var result=new Texture2D(2,2,TextureFormat.RGB24,true);
            if(!result.LoadImage(File.ReadAllBytes(Path.Combine(folder,path)))) throw new InvalidDataException("贴图读取失败："+path);
            result.wrapMode=repeat?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
            result.filterMode=FilterMode.Trilinear;result.anisoLevel=8;
            textureCache[path]=result;owned.Add(result);return result;
        }
        void ApplyMapTransform(Material material)
        {
            var map=field.textureScaleOffset;
            material.mainTextureScale=map!=null && map.Length==4?new Vector2(map[0],map[1]):Vector2.one;
            material.mainTextureOffset=map!=null && map.Length==4?new Vector2(map[2],map[3]):Vector2.zero;
        }
        void BuildField()
        {
            InvalidateLidar(LidarResetReason.FieldChanged);
            var root = new GameObject(field.name); fieldRoot=root;buildingNodes.Clear();cadMaterials.Clear();
            ResetMechanismField();
            groundMaterial = Material(new Color(.18f, .23f, .28f));
            var plane = Shape(PrimitiveType.Cube, root.transform, new Vector3(field.width/2, -.10f, field.depth/2),
                new Vector3(field.width, .10f, field.depth), groundMaterial);
            // A quad has planar UVs, avoiding the wrapping of a cube texture.
            var quad = Shape(PrimitiveType.Quad, root.transform, new Vector3(field.width/2, -.04f, field.depth/2),
                new Vector3(field.width, field.depth, 1), groundMaterial);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Destroy(quad.GetComponent<Collider>());
            plane.layer=8;
            ground = quad.GetComponent<Renderer>();
            mapTexture = LoadTexture(field.texture);
            if(field.meshFormat=="assembly_binary_v1") BuildOfficialAssembly(root.transform);
            else BuildRegionalGeometry(root.transform);
            BuildGroundCollision(root.transform);
            BuildRadarPlatforms(root.transform);
            SetTexture();
            BuildVisibleBoundary(root.transform);
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) viewCamera = new GameObject("Replay Camera").AddComponent<Camera>();
            viewCamera.backgroundColor = new Color(.045f,.065f,.095f);
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.fieldOfView = 50;
            viewCamera.nearClipPlane = .1f; viewCamera.farClipPlane = 150;
            viewCamera.rect = CameraViewport;
            if(!GameObject.Find("Field Light"))
            {
                var light = new GameObject("Field Light").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.0f;
                light.transform.rotation = Quaternion.Euler(55,-30,0);
            }
            RenderSettings.ambientLight = new Color(.45f,.48f,.52f);
            focus = new Vector3(field.width/2,0,field.depth/2);
            BuildProjectileFieldCollision(root.transform);
            Physics.SyncTransforms();
        }
        void BuildRegionalGeometry(Transform parent)
        {
            FieldMesh data = JsonUtility.FromJson<FieldMesh>(File.ReadAllText(Path.Combine(folder, field.mesh)));
            var vertices = new Vector3[data.positions.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.positions[i*3],data.positions[i*3+1]-field.meshFloorHeight,data.positions[i*3+2]);
            var faces = new List<int>();
            for(int i=0;i<data.indices.Length;i+=3)
            {
                int a=data.indices[i],b=data.indices[i+1],c=data.indices[i+2];
                if(vertices[a].y>field.meshMaxDisplayHeight || vertices[b].y>field.meshMaxDisplayHeight || vertices[c].y>field.meshMaxDisplayHeight) continue;
                faces.Add(a);faces.Add(b);faces.Add(c);
            }
            var mesh = new Mesh { name = "Regional field (display floor aligned, overhead cropped)", vertices = vertices, triangles = faces.ToArray() };
            var uv = new Vector2[vertices.Length];
            for(int i=0;i<uv.Length;i++) uv[i]=new Vector2(vertices[i].x/field.width,vertices[i].z/field.depth);
            mesh.uv=uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            terrain = new GameObject("Regional geometry"); terrain.transform.SetParent(parent);
            terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
            terrainMaterial = Material(new Color(.48f,.53f,.59f));
            terrain.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        }
        Mesh ReadMesh(string path)
        {
            if(meshCache.TryGetValue(path,out var cached)) return cached;
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(folder,path))))
            {
                string magic=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
                bool colored=magic=="TCRMESH2";
                if(!colored && magic!="TCRMESH1") throw new InvalidDataException("模型格式无效");
                int count=reader.ReadInt32(),indexCount=reader.ReadInt32();
                if(count<1 || count>10_000_000 || indexCount<3 || indexCount%3!=0 || reader.BaseStream.Length!=16L+count*(colored?16L:12L)+indexCount*4L)
                    throw new InvalidDataException("模型大小无效");
                var vertices=new Vector3[count];var indices=new int[indexCount];
                for(int i=0;i<count;i++)
                {
                    vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    if(!float.IsFinite(vertices[i].x)||!float.IsFinite(vertices[i].y)||!float.IsFinite(vertices[i].z)) throw new InvalidDataException("模型坐标无效");
                }
                for(int i=0;i<indexCount;i++) {indices[i]=reader.ReadInt32();if(indices[i]<0 || indices[i]>=count) throw new InvalidDataException("模型索引无效");}
                var mesh=new Mesh {name=Path.GetFileName(path),indexFormat=count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};
                mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                var colors=new Color32[count];
                for(int i=0;i<count;i++) colors[i]=colored?new Color32(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte()):new Color32(100,100,100,255);
                mesh.colors32=colors;
                owned.Add(mesh);meshCache[path]=mesh;return mesh;
            }
        }
        void BuildOfficialAssembly(Transform parent)
        {
            terrain=new GameObject("Official CAD assembly");terrain.transform.SetParent(parent,false);
            var data=JsonUtility.FromJson<FieldAssembly>(File.ReadAllText(Path.Combine(folder,field.mesh)));
            if(data.schema!=1 || data.parts==null) throw new InvalidDataException("CAD装配配置无效");
            Texture2D surface=string.IsNullOrEmpty(field.surfaceTexture)?null:LoadTexture(field.surfaceTexture,true);
            foreach(var part in data.parts)
            {
                if(part.matrix==null || part.matrix.Length!=16) throw new InvalidDataException("CAD装配变换无效");
                var matrix=new Matrix4x4();for(int r=0;r<4;r++) for(int c=0;c<4;c++) matrix[r,c]=part.matrix[r*4+c];
                var obj=new GameObject(part.id+" "+part.name);obj.transform.SetParent(terrain.transform,false);
                obj.transform.localPosition=matrix.GetColumn(3);
                obj.transform.localRotation=Quaternion.LookRotation(matrix.GetColumn(2),matrix.GetColumn(1));
                var mesh=ReadMesh(MechanismFixedMesh(part));
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();
                var template=Resources.Load<Material>("ReplayCad");
                var material=template?new Material(template):new Material(Shader.Find("TCR/Replay CAD surface"));
                material.color=Color.white;owned.Add(material);cadMaterials.Add(material);
                material.SetFloat("_Structural",part.colorMode=="structural"?1:0);
                material.SetFloat("_Team",part.team=="red"?0:part.team=="blue"?1:2);
                material.SetTexture("_SurfaceTex",surface?surface:Texture2D.whiteTexture);
                material.SetTexture("_MainTex",mapTexture);ApplyMapTransform(material);
                renderer.sharedMaterial=material;
                BuildCadMechanisms(part,obj.transform,material);
                if(part.textured)
                {
                    terrainMaterial=material;
                    if(mesh.uv.Length==0)
                    {
                        var v=mesh.vertices;var indices=mesh.triangles;var uv=new Vector2[v.Length];var masks=new Vector2[v.Length];
                        for(int i=0;i<v.Length;i++) {var p=matrix.MultiplyPoint3x4(v[i]);uv[i]=new Vector2(p.x/field.width,p.z/field.depth);}
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            Vector3 normal=Vector3.Cross(v[indices[i+1]]-v[indices[i]],v[indices[i+2]]-v[indices[i]]).normalized;
                            if(matrix.MultiplyVector(normal).y>.65f)
                                for(int j=0;j<3;j++) masks[indices[i+j]]=Vector2.right;
                        }
                        mesh.uv=uv;mesh.uv2=masks;
                    }
                }
                if(!string.IsNullOrEmpty(part.binding))
                {
                    buildingNodes[part.binding]=obj.transform;
                    if(part.binding.EndsWith("_base") || part.binding.EndsWith("_outpost")) obj.AddComponent<MeshCollider>().sharedMesh=mesh;
                }
            }
            Debug.Log($"REPLAY_OFFICIAL_ASSEMBLY_OK parts={data.parts.Length} uniqueMeshes={meshCache.Count}");
        }
        void SetTexture()
        {
            groundMaterial.mainTexture=texture?mapTexture:null;groundMaterial.color=texture?Color.white:new Color(.18f,.23f,.28f);
            ApplyMapTransform(groundMaterial);
            if(field.meshFormat=="assembly_binary_v1")
            {
                foreach(var material in cadMaterials)
                {
                    material.SetFloat("_TextureEnabled",texture?1:0);
                    material.SetFloat("_SurfaceEnabled",texture && !string.IsNullOrEmpty(field.surfaceTexture)?1:0);
                }
            }
            else {terrainMaterial.mainTexture=texture?mapTexture:null;terrainMaterial.color=texture?Color.white:new Color(.28f,.33f,.39f);}
        }
        void SwitchField(int index)
        {
            playing=false;
            float time=playhead;int previousSelected=selected;
            try
            {
                var next=JsonUtility.FromJson<FieldConfig>(File.ReadAllText(Path.Combine(folder,fieldCatalog.fields[index].file)));
                if(fieldRoot) {fieldRoot.SetActive(false);Destroy(fieldRoot);}
                field=next;fieldIndex=index;meshVisible=true;BuildField();LoadMatch(matchIndex);
                selected=Mathf.Clamp(previousSelected,0,units.Count-1);Seek(time);
                if(attachedMarker!=null)
                {
                    var marker=Array.Find(field.landmarks,m=>m.team==attachedMarker.team && m.role==attachedMarker.role);
                    if(marker!=null) AttachMarker(marker,false);
                    else SetCameraMode(ReplayCameraMode.Observe,false);
                }
            }
            catch(Exception ex) {error=ex.Message;Debug.LogException(ex);}
        }
        void LoadMatch(int index)
        {
            ++loadGeneration;loadCancellation?.Cancel();loading=false;
            try {InstallMatch(index,CachedMatch(index));}
            catch(Exception ex) {playing=false;error=ex.Message;Debug.LogException(ex);}
        }
        void InstallMatch(int index,MatchData data)
        {
            try
            {
                InvalidateLidar(LidarResetReason.MatchChanged);fieldMismatch=data.fieldId!=field.id;
                RestoreVehicle(); attachedUnit=null;
                foreach (var unit in units) { unit.root.SetActive(false);unit.trail.gameObject.SetActive(false);Destroy(unit.root);Destroy(unit.trail.gameObject); }
                units.Clear();
                match = data; matchIndex = index; playhead = 0; playing = false; selected = 0; chooser = false; follow = false; error = "";
                focus = new Vector3(field.width/2,0,field.depth/2);
                var ordered = new List<EntityData>(match.entities);
                string[] roles = {"base","outpost","hero","engineer","infantry3","infantry4","sentry","air"};
                ordered.Sort((a,b) => {int role = Array.IndexOf(roles,a.role).CompareTo(Array.IndexOf(roles,b.role));return role!=0?role:(a.team==b.team?0:a.team=="red"?-1:1);});
                mechanismTimeline=new ReplayMechanismTimeline(match);
                foreach (var entity in ordered) BuildUnit(entity);
                PrepareShots();
                ApplyTime(); UpdateCamera();
            }
            catch (Exception ex) { playing = false; error = ex.Message; Debug.LogException(ex); }
        }
        void BuildUnit(EntityData entity)
        {
            var unit = new Unit { data = entity, root = new GameObject(entity.id) };
            var body = SharedPrimitiveMaterial(entity.team == "red" ? red : blue);
            var wheel = SharedPrimitiveMaterial(new Color(.08f,.09f,.11f));
            bool building = entity.role == "base" || entity.role == "outpost";
            if(!building && BuildRobot(unit)) { }
            else if(building && buildingNodes.TryGetValue(entity.id,out var node)) unit.cadNode=node;
            else if (building)
            {
                Shape(PrimitiveType.Cylinder, unit.root.transform, new Vector3(0,.45f,0),new Vector3(1,.45f,1),body);
                Shape(PrimitiveType.Cube, unit.root.transform, new Vector3(0,1,0),new Vector3(.55f,.4f,.55f),body);
            }
            else if (entity.role == "air")
            {
                Shape(PrimitiveType.Cube, unit.root.transform, Vector3.zero,new Vector3(.38f,.12f,.38f),body);
                Shape(PrimitiveType.Cube, unit.root.transform, Vector3.zero,new Vector3(.9f,.05f,.08f),wheel);
                Shape(PrimitiveType.Cube, unit.root.transform, Vector3.zero,new Vector3(.08f,.05f,.9f),wheel);
                foreach (float x in new [] {-.4f,.4f}) foreach (float z in new [] {-.4f,.4f})
                    Shape(PrimitiveType.Cylinder,unit.root.transform,new Vector3(x,0,z),new Vector3(.28f,.025f,.28f),body);
            }
            else
            {
                bool hero = entity.role == "hero", engineer = entity.role == "engineer";
                float size = hero || engineer ? .8f : .62f;
                Shape(PrimitiveType.Cube,unit.root.transform,new Vector3(0,.22f,0),new Vector3(size,.23f,size*.85f),body);
                foreach (float x in new [] {-.36f,.36f}) foreach (float z in new [] {-.25f,.25f})
                {
                    var w = Shape(PrimitiveType.Cylinder,unit.root.transform,new Vector3(x,.15f,z),new Vector3(.28f,.08f,.28f),wheel);
                    w.transform.localRotation = Quaternion.Euler(0,0,90);
                }
                unit.turret = new GameObject("Turret").transform; unit.turret.SetParent(unit.root.transform,false);
                if (engineer)
                    Shape(PrimitiveType.Cube,unit.turret,new Vector3(0,.6f,.18f),new Vector3(.14f,.7f,.14f),body);
                else
                {
                    Shape(PrimitiveType.Cylinder,unit.turret,new Vector3(0,.48f,0),new Vector3(.38f,.14f,.38f),body);
                    Shape(PrimitiveType.Cube,unit.turret,new Vector3(0,.53f,.32f),new Vector3(hero?.15f:.08f,.09f,.55f),wheel);
                    if (entity.role == "sentry") Shape(PrimitiveType.Cube,unit.turret,new Vector3(.18f,.53f,.32f),new Vector3(.08f,.09f,.55f),wheel);
                }
            }
            unit.renderers = unit.robot!=null?unit.robot.renderers:unit.cadNode?unit.cadNode.GetComponentsInChildren<Renderer>():unit.root.GetComponentsInChildren<Renderer>();
            unit.trail = new GameObject(entity.id+" trail").AddComponent<LineRenderer>();
            unit.trail.sharedMaterial = SharedPrimitiveMaterial(entity.team == "red" ? red : blue,true);
            unit.trail.widthMultiplier = .045f; unit.trail.positionCount = 0;
            units.Add(unit);BuildArmorColliders(unit);
        }
        void ApplyTime()
        {
            if (match == null) return;
            foreach (var unit in units)
            {
                int i = Timeline.StateIndex(unit.data.samples,playhead);
                unit.sample = i < 0 ? null : unit.data.samples[i];
                bool building = unit.data.role == "base" || unit.data.role == "outpost";
                Vector3 point = Vector3.zero;
                unit.visible = Timeline.Pose(unit.data,playhead,out point);
                if (building)
                {
                    var anchor = Array.Find(field.buildings,a => a.team == unit.data.team && a.role == unit.data.role);
                    unit.visible = anchor != null;
                    if (anchor != null) point = new Vector3(anchor.x,0,anchor.y);
                }
                // Aircraft use a symbolic display plane; recorded altitude is only shown in the inspector.
                UpdateRobotPose(unit,point);
                point.y = VehicleGroundHeight(unit,point)+(unit.data.role == "air"?1.8f:0);
                if(unit.cadNode) point=unit.cadNode.position;
                unit.root.SetActive(unit.visible); unit.root.transform.position = point;
                if(unit.turret) {bool known=BallisticMath.Heading(unit.data,playhead,ballisticConfig,out var angle);unit.turret.localRotation=Quaternion.Euler(headings?-CurrentElevation(unit,playhead):0,headings&&known?angle:0,0);}
                float dim = unit.sample != null && unit.sample.hasHp && unit.sample.hp <= 0 ? .3f : 1;
                foreach (var renderer in unit.renderers)
                {
                    renderer.GetPropertyBlock(unitColorBlock);
                    bool armor=renderer.sharedMaterial.HasProperty("_ArmorLight");
                    unitColorBlock.SetColor("_Color",renderer.sharedMaterial.color*(armor?1:dim));
                    if(armor) unitColorBlock.SetFloat("_ArmorLight",dim<1?0:1);
                    renderer.SetPropertyBlock(unitColorBlock);
                }
                var positions = unit.trailPoints;positions.Clear();
                // Only the most recent continuous valid segment is drawn: no line across missing states.
                if (trails && !building && unit.visible)
                {
                    int start = i;
                    while (start > 0 && unit.data.samples[start-1].valid && unit.data.samples[start].t-unit.data.samples[start-1].t <= 2 && playhead-unit.data.samples[start-1].t <= 30) start--;
                    for (int j = start; j <= i; j++)
                        if (unit.data.samples[j].valid) positions.Add(new Vector3(unit.data.samples[j].x,GroundHeight(unit.data.samples[j].x,unit.data.samples[j].y)+(unit.data.role=="air"?1.8f:0)+.05f,unit.data.samples[j].y));
                    positions.Add(point+Vector3.up*.05f);
                }
                unit.trail.positionCount = positions.Count;
                for(int j=0;j<positions.Count;j++)unit.trail.SetPosition(j,positions[j]);
            }
            UpdateMechanisms();
        }
        void Update()
        {
            UpdateUiShortcuts();
            if(appMode=="game"){UpdateDeveloper();return;}
            if(appMode=="menu"){UpdateDisplaySettings();return;}
            CameraShortcuts();
            UpdateDisplaySettings();
            if(BackgroundInputAllowed && Input.GetKeyDown(KeyCode.F9)) ToggleAllLabels();
            if (match == null) return;
            if (BackgroundInputAllowed && (Input.GetKeyDown(KeyCode.P) ||
                (!TravelCamera && Input.GetKeyDown(KeyCode.Space))))
            { if(playhead>=match.duration) Seek(0); playing = !playing; }
            if (BackgroundInputAllowed && Input.GetKeyDown(KeyCode.LeftArrow)) Seek(playhead-5);
            if (BackgroundInputAllowed && Input.GetKeyDown(KeyCode.RightArrow)) Seek(playhead+5);
            if (playing) { playhead = Mathf.Min(match.duration,playhead+Time.unscaledDeltaTime*speed); if (playhead >= match.duration) playing = false; }
            if(!robotDemo) ApplyTime();
            bool onField = BackgroundInputAllowed && viewCamera.pixelRect.Contains(Input.mousePosition) &&
                (cleanView ? Input.mousePosition.y < Screen.height * .87f :
                    Input.mousePosition.y < Screen.height * (1-260f/900) && Input.mousePosition.y > Screen.height * (1-730f/900));
            if (TravelCamera && BackgroundInputAllowed) UpdateTravelInput(onField);
            else if (onField)
            {
                distance = Mathf.Clamp(distance-Input.mouseScrollDelta.y*1.5f,9,50);
                if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X")*3; pitch = Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*3,20,85); }
                if (Input.GetMouseButton(2))
                {
                    follow=false;
                    Vector3 right=viewCamera.transform.right; right.y=0;right.Normalize();
                    Vector3 forward=Vector3.Cross(right,Vector3.up);
                    focus+=(-right*Input.GetAxis("Mouse X")-forward*Input.GetAxis("Mouse Y"))*.2f;
                    focus.x=Mathf.Clamp(focus.x,-5,field.width+5);focus.z=Mathf.Clamp(focus.z,-5,field.depth+5);
                }
                if (Input.GetMouseButtonDown(0) && Physics.Raycast(viewCamera.ScreenPointToRay(Input.mousePosition),out var hit))
                    for (int i = 0; i < units.Count; i++) if (hit.transform.IsChildOf(units[i].root.transform) || units[i].cadNode && hit.transform.IsChildOf(units[i].cadNode)) SelectUnit(i);
            }
            UpdateProjectiles();
            UpdateCamera();
            WriteCameraProbe();
        }
        void UpdateCamera()
        {
            if(!viewCamera) return;
            viewCamera.rect = CameraViewport;
            if(TravelCamera)
            {
                if(cameraRig.Mode == ReplayCameraMode.Attached) RefreshAttachment();
                ConstrainCameraGround();
                var hide = cameraRig.Mode == ReplayCameraMode.Attached && attachedUnit != null &&
                    attachedUnit.data.role != "base" && attachedUnit.data.role != "outpost" ? attachedUnit : null;
                if(hiddenVehicle != hide)
                {
                    RestoreVehicle(); hiddenVehicle=hide;
                    if(hiddenVehicle != null) foreach(var renderer in hiddenVehicle.renderers) renderer.forceRenderingOff=true;
                }
                viewCamera.orthographic=false; viewCamera.nearClipPlane=.03f;
                viewCamera.transform.SetPositionAndRotation(cameraRig.Position,cameraRig.Rotation);
                return;
            }
            RestoreVehicle(); viewCamera.nearClipPlane=.1f;
            if(follow && units.Count>selected && units[selected].visible) focus = units[selected].root.transform.position;
            viewCamera.orthographic = topView; viewCamera.orthographicSize = distance*.30f;
            viewCamera.transform.position = focus+Quaternion.Euler(topView ? 89.9f : pitch,yaw,0)*new Vector3(0,0,-distance);
            viewCamera.transform.LookAt(focus);
        }
        void Seek(float value) { InvalidateLidar(LidarResetReason.TimelineJump);playhead = Mathf.Clamp(value,0,match.duration); ApplyTime(); UpdateProjectiles(); UpdateCamera(); }
        float GroundHeight(float x,float z)
        {
            return Physics.Raycast(new Vector3(x,8,z),Vector3.down,out var hit,12,1<<8)?Mathf.Max(0,hit.point.y):0;
        }
        string Clock(float t) { return $"{(int)t/60:00}:{(int)t%60:00}"; }
        void Styles()
        {
            if (font) GUI.skin.font = font;
            GUI.skin.toggle.fontSize = 14;
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
            small = new GUIStyle(label) { fontSize = 14 };
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, wordWrap = true };
            worldLabel = new GUIStyle(label) {fontSize=12,wordWrap=false,alignment=TextAnchor.MiddleCenter};
        }
        void Text(float x,float y,float w,float h,string s,GUIStyle style = null) { GUI.Label(new Rect(x,y,w,h),s,style??label); }
        bool Button(float x,float y,float w,float h,string s)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&(!uiInGui||UiLayerInteractive);
            bool clicked=GUI.Button(new Rect(x,y,w,h),s,button);GUI.enabled=enabled;
            return clicked;
        }
        void Panel(Rect r) { Color old=GUI.color; GUI.color=new Color(.12f,.17f,.23f,.98f); GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=old; }
        void DrawReplayGui()
        {
            var oldMatrix = GUI.matrix;
            bool oldEnabled=GUI.enabled;
            GUI.enabled=oldEnabled && !mouseCaptured;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(Screen.width/1600f,Screen.height/900f,1));
            if(cleanView && string.IsNullOrEmpty(error))
            {
                Panel(new Rect(12,12,1220,84));
                Text(24,18,1190,28,robotDemo?"车型姿态演示 · 非比赛位置 / 非日志姿态":$"{CameraStatus} · {Clock(playhead)} · {(playing?"播放中":"暂停")}",small);
                Text(24,44,1190,24,CameraHelp,small);
                Text(24,68,1190,24,cameraRig.Mode==ReplayCameraMode.Attached && !cameraRig.TargetVisible?"目标位置缺失，镜头保持 · F10 显示面板 / 1、2、3 切换视角":
                    "1 观察 / 2 自由 / 3 第一人称 · F10 显示面板 · F11 全屏 · "+(resourceProfile.SyntheticDemo?"Alpha 合成示例，非实际比赛":fieldMismatch?"区域赛数据 + 全国赛模型对照，待校准":"区域赛数据，场地映射待校准"),small);
                if(Button(1280,18,300,32,"F10  显示控制面板")) ToggleCleanView();
                if(Button(1280,58,300,32,Screen.fullScreen?"F11  返回窗口":"F11  全屏")) ToggleFullscreen();
                if(Button(1280,98,300,32,allLabels?"F9 隐藏所有标签":"F9 显示所有标签")) ToggleAllLabels();
                if(mouseCaptured) DrawCrosshair();
                DrawLidarGui();
                GUI.matrix=oldMatrix; GUI.enabled=oldEnabled; return;
            }
            Panel(new Rect(0,0,1600,116)); Panel(new Rect(0,774,1600,126)); Panel(new Rect(1216,116,384,658));
            Text(20,12,780,38,"2026 TCR · 赛场回放",title);
            Text(20,56,1100,30,match == null ? "加载回放数据…" : $"红：{match.redSchool}   蓝：{match.blueSchool}   第 {match.round} 局");
            Text(20,88,1130,24,resourceProfile.SyntheticDemo?"Alpha 半成品 · 合成比赛与程序生成场地 · 不是实际赛事数据":robotDemo?"车型姿态演示 · 非比赛位置 / 非日志姿态": "区域赛记录 · 状态分辨率 1 秒 · 3 号扫地 / 4 号嘉庚 · 外观映射，洞口姿态为场景推导",small);
            if (Button(1010,20,194,38,Screen.fullScreen?"F11  返回窗口":"F11  全屏")) ToggleFullscreen();
            if (Button(1228,20,168,38,"选择比赛")) {chooser = !chooser; ReleaseMouse();}
            if (Button(1408,20,174,38,topView?"切换透视":"切换俯视")) {if(TravelCamera)SetCameraMode(ReplayCameraMode.Observe,false);topView=!topView;}
            if (Button(1228,66,168,34,trails?"轨迹：开":"轨迹：关")) trails=!trails;
            if (field != null && Button(1408,66,174,34,(resourceProfile.SyntheticDemo?"示例贴图：":field.meshFormat=="assembly_binary_v1"?"官方贴图：":"区域赛贴图：")+(texture?"开":"关")))
            {
                texture=!texture;
                SetTexture();
            }
            if (!string.IsNullOrEmpty(error)) { Text(40,140,1100,100,"加载错误："+error); GUI.matrix=oldMatrix; GUI.enabled=oldEnabled; return; }
            if (match != null)
            {
                DrawLabels(); DrawInspector(); DrawTimeline();
                Panel(new Rect(12,728,1190,40));
                Text(20,734,1180,36,CameraHelp,small);
                Text(20,118,1180,30,resourceProfile.SyntheticDemo?"程序生成场地与动作 · 仅用于验证回放和点云接口，非官方场地与实际赛况。":fieldMismatch?"区域赛数据 + 官方全国赛模型对照 · 场地版本不同，轨迹叠加待校准 · 建筑绑定来自 CAD 装配。":
                    "区域赛场地与数据 · 建筑依据规则图定位，几何与观测坐标仍待校准。",small);
                if(Button(20,152,150,32,follow?"观察跟随：开":"观察跟随：关")) {if(TravelCamera)SetCameraMode(ReplayCameraMode.Observe,false);follow=!follow;}
                if(Button(182,152,150,32,"重置视角")) ResetCamera();
                if(Button(344,152,170,32,allLabels?"F9 隐藏所有标签":"F9 显示所有标签")) ToggleAllLabels();
                if(fieldCatalog.fields.Length>1 && Button(526,152,310,32,"场地："+fieldCatalog.fields[fieldIndex].name)) SwitchField((fieldIndex+1)%fieldCatalog.fields.Length);
                if(Button(848,152,342,32,attachedMarker!=null?"地物视角："+attachedMarker.name:"选择固定地物视角")) {landmarkChooser=!landmarkChooser;ReleaseMouse();}
                DrawCameraControls();
                if(mouseCaptured) DrawCrosshair();
            }
            DrawLidarGui();
            GUI.matrix=oldMatrix; GUI.enabled=oldEnabled;
        }
        void DrawCrosshair()
        {
            float x=(CameraViewport.x+CameraViewport.width/2)*1600;
            float y=(1-CameraViewport.y-CameraViewport.height/2)*900;
            GUI.DrawTexture(new Rect(x-6,y,12,1),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x,y-6,1,12),Texture2D.whiteTexture);
        }
        void DrawLabels()
        {
            if(captureLabelGeometry && Event.current.type==EventType.Repaint)capturedLabels.Clear();
            labelBounds.Clear();if(!allLabels)return;
            // Buildings exist independently of telemetry. Their HP can still be unknown at t=0.
            foreach (var unit in units)
            {
                if (!unit.visible || (cameraRig.Mode==ReplayCameraMode.Attached && unit==attachedUnit)) continue;
                bool building=unit.data.role=="base" || unit.data.role=="outpost";
                if(!building) continue;
                var anchor=Array.Find(field.buildings,a=>a.team==unit.data.team && a.role==unit.data.role);
                string name=anchor!=null && !string.IsNullOrEmpty(anchor.name)?anchor.name:(unit.data.team=="red"?"红方":"蓝方")+unit.data.name;
                string hp=unit.sample!=null && unit.sample.hasHp?unit.sample.hp.ToString("0"):"未知";
                WorldLabel(unit.root.transform.position+Vector3.up*1.05f,name+" "+hp,unit.data.team=="red"?red:blue);
            }
            if(landmarkLabels && field.landmarks!=null)
                foreach(var marker in field.landmarks)
                    if(!(cameraRig.Mode==ReplayCameraMode.Attached && marker==attachedMarker))WorldLabel(MarkerLabelPosition(marker),marker.name+(marker.role=="energy_mechanism"?" · 灯序/角度示意":""),
                        marker.team=="red"?red:marker.team=="blue"?blue:new Color(.98f,.85f,.45f));
            foreach(var unit in units)
            {
                if(!unit.visible || unit.data.role=="base" || unit.data.role=="outpost" || (cameraRig.Mode==ReplayCameraMode.Attached && unit==attachedUnit)) continue;
                string hp=unit.sample!=null && unit.sample.hasHp?unit.sample.hp.ToString("0"):"?";
                WorldLabel(RobotLabel(unit),(unit==units[selected]?"▶ ":"")+unit.data.name+" "+hp,unit.data.team=="red"?red:blue);
            }
        }
        void WorldLabel(Vector3 point,string text,Color color)
        {
            Vector3 p=viewCamera.WorldToScreenPoint(point);
            if(p.z<=0) return;
            Vector2 origin=new Vector2(p.x*1600/Screen.width,(Screen.height-p.y)*900/Screen.height);
            if(origin.x<0 || origin.x>1214 || origin.y<264 || origin.y>718) return;
            float width=Mathf.Max(96,worldLabel.CalcSize(new GUIContent(text)).x+16);
            Rect rect=new Rect(origin.x-width/2,origin.y,width,22);
            // Keep label positions distinct while a leader line retains the real anchor.
            Vector2[] offsets={Vector2.zero,new Vector2(0,-25),new Vector2(0,25),new Vector2(0,-50),new Vector2(0,50),new Vector2(-width,0),new Vector2(width,0),new Vector2(0,-75),new Vector2(0,75)};
            foreach(var offset in offsets)
            {
                var candidate=new Rect(Mathf.Clamp(rect.x+offset.x,4,1212-width),Mathf.Clamp(rect.y+offset.y,264,712),width,22);
                if(!labelBounds.Exists(r=>r.Overlaps(candidate))) {rect=candidate;break;}
            }
            labelBounds.Add(rect);
            Vector2 end=rect.center;
            if(captureLabelGeometry && Event.current.type==EventType.Repaint)capturedLabels.Add(new LabelGeometry {anchor=origin,end=end,bounds=rect});
            var matrix=GUI.matrix;
            GUI.color=new Color(color.r,color.g,color.b,.65f);
            // Compose the line in logical UI coordinates BEFORE the existing
            // screen scale. RotateAroundPivot instead prepends a screen-space
            // rotation and displaces these lines in resized/fullscreen windows.
            float length=Vector2.Distance(origin,end);
            if(length>.001f)
            {
                GUI.matrix=matrix*Matrix4x4.TRS(origin,Quaternion.Euler(0,0,Mathf.Atan2(end.y-origin.y,end.x-origin.x)*Mathf.Rad2Deg),Vector3.one);
                GUI.DrawTexture(new Rect(0,-.5f,length,1),Texture2D.whiteTexture);
            }
            GUI.matrix=matrix;
            GUI.DrawTexture(new Rect(origin.x-2,origin.y-2,4,4),Texture2D.whiteTexture);
            GUI.color=Color.white;Panel(rect);
            GUI.color=color;GUI.Label(rect,text,worldLabel);GUI.color=Color.white;
        }
        void DrawInspector()
        {
            Text(1230,125,360,30,"车组状态 · 点击选择",title);
            for (int i=0;i<units.Count;i++)
            {
                var u=units[i]; int row=i/2,col=i%2;
                // Export order is unspecified; the label includes team identity.
                GUI.color=u.data.team=="red"?red:blue;
                string hp=u.sample != null && u.sample.hasHp?u.sample.hp.ToString("0"):"未知";
                if (Button(1230+col*179,164+row*31,173,29,(i==selected?"▶ ":"")+(u.data.team=="red"?"红":"蓝")+u.data.name+" "+hp)) SelectUnit(i);
                GUI.color=Color.white;
            }
            if (units.Count==0) return;
            if(attachedMarker!=null && attachedMarker.role=="energy_mechanism")
            {
                DrawRuneInspector();DrawRecentEvents();return;
            }
            var unit=units[selected]; var s=unit.sample;
            Text(1230,420,356,30,$"{unit.data.school} · {unit.data.name}");
            if(unit.robot!=null)
            {
                var robot=unit.robot;
                Text(1230,570,230,42,$"{robot.model.name} · 收缩 {robot.fold*100:0}%\n{(robot.model.id=="jiageng"?(robot.proximity>.99f?"底盘全摊 · ":"底盘半摊/过渡 · "):"")}{(robot.preview>=0?"预览":"洞口位置推导")}",small);
                if(Button(1465,575,116,28,robot.preview<0?"预览收缩":"恢复自动")) {robot.preview=robot.preview<0?1:-1;InvalidateBallistics();ApplyTime();UpdateProjectiles();UpdateCamera();}
            }
            if (s==null) Text(1230,453,356,85,"该时刻尚无记录（首条状态通常在 1 秒）。",small);
            else
            {
                bool building=unit.data.role=="base"||unit.data.role=="outpost";
                var anchor=building?Array.Find(field.buildings,a=>a.team==unit.data.team && a.role==unit.data.role):null;
                string pos=building && anchor!=null?$"{(unit.cadNode?"CAD建筑锚点":"规则图锚点")} ({anchor.x:0.000}, {anchor.y:0.000})":(unit.visible?"有效 XY · 显示位置插值":"坐标缺失或越界 · 隐藏车辆");
                Text(1230,453,356,112,$"状态 {s.t:0}s | 血量 {(s.hasHp?s.hp.ToString("0"):"未知")} / {(s.hasMaxHp?s.maxHp.ToString("0"):"未知")}\n原始 XYZ ({s.x:0.00}, {s.y:0.00}, {s.z:0.00}) · {pos}\n累计发弹 17mm {(s.hasAmmo17?s.ammo17.ToString("0"):"未知")} / 42mm {(s.hasAmmo42?s.ammo42.ToString("0"):"未知")}\n功率 {(s.hasPower?s.power.ToString("0.0"):"未知")} · 热量 {(s.hasHeat17?s.heat17.ToString("0"):"?")} / {(s.hasHeat42?s.heat42.ToString("0"):"?")}\n金币 {(s.hasCoins?s.coins.ToString("0"):"未知")} · 原始枪口角 {(s.hasGun?s.gun.ToString("0.0"):"未知")}",small);
            }
            Text(1230,614,356,58,SelectedAimSummary(unit),small);
            if(unit.data.role=="base")
            {
                var pose=mechanismTimeline.BaseArmor(unit.data.team,playhead).pose;
                Text(1230,570,356,35,"基地护甲："+(pose==BaseArmorPose.Unknown?"未知":pose==BaseArmorPose.Open?"展开":"闭合")+" · 仅预留接口",small);
            }
            if(unit.data.role=="outpost")Text(1230,570,356,35,$"装甲旋转：规则推演 · 停转触发≤{mechanismTimeline.OutpostStop(unit.data.team):0}s",small);
            DrawRecentEvents();
        }
        void DrawRecentEvents()
        {
            float eventY=698, eventHeight=758-eventY;
            Text(1230,eventY-33,190,28,"最近事件（10 秒）",label);
            filterShots=GUI.Toggle(new Rect(1420,eventY-30,164,26),filterShots,"隐藏发弹");
            Rect area=new Rect(1230,eventY,356,eventHeight);
            int end=Timeline.EventEnd(match.events,playhead),start=Timeline.EventEnd(match.events,playhead-10);
            var visible=new List<ReplayEvent>();
            for(int i=end-1;i>=start && visible.Count<60;i--)
                if(!filterShots || match.events[i].kind!="发弹") visible.Add(match.events[i]);
            eventScroll=GUI.BeginScrollView(area,eventScroll,new Rect(0,0,330,Mathf.Max(eventHeight,visible.Count*26)));
            for(int i=0;i<visible.Count;i++)
            {
                var e=visible[i];
                var entity=Array.Find(match.entities,a=>a.id==e.entity);
                string who=entity==null?"场地":(entity.team=="red"?"红 ":"蓝 ")+entity.name;
                GUI.Label(new Rect(0,i*26,330,26),$"{Clock(e.t)} {who} {e.kind} {(e.hasValue?e.value.ToString("0.##"):"")}",small);
            }
            GUI.EndScrollView();
        }
        void DrawTimeline()
        {
            if(Button(20,790,105,38,playing?"暂停":"播放")) { if(playhead>=match.duration) Seek(0); playing=!playing; }
            if(Button(136,790,90,38,"回到开头")) {playing=false;Seek(0);}
            if(Button(238,790,66,38,"-5s")) Seek(playhead-5);
            if(Button(312,790,66,38,"+5s")) Seek(playhead+5);
            Text(398,796,180,30,$"{Clock(playhead)} / {Clock(match.duration)}");
            float next=GUI.HorizontalSlider(new Rect(590,805,740,24),playhead,0,match.duration);
            if(Mathf.Abs(next-playhead)>.0001f) {playing=false;Seek(next);}
            if(Button(1350,790,110,38,$"速度 {speed:0.##}×")) speed=speed==.5f?1:speed==1?2:speed==2?4:.5f;
            if(Button(1470,790,110,38,"返回菜单")) EnterMenu();
            headings=GUI.Toggle(new Rect(20,847,285,28),headings,"瞄准回放（方向映射待校准）");
            bool nextMesh=GUI.Toggle(new Rect(320,847,210,28),meshVisible,"场地模型");
            if(nextMesh!=meshVisible) {meshVisible=nextMesh;terrain.SetActive(meshVisible);}
            showProjectiles=GUI.Toggle(new Rect(550,847,190,28),showProjectiles,"绿色弹丸 / 推定弹道");
            landmarkLabels=GUI.Toggle(new Rect(750,847,150,28),landmarkLabels,"地物标签");
            Text(915,847,675,35,$"目标/俯仰/碰撞为推定 · 初速17:{ballisticConfig.speed17:0} /42:{ballisticConfig.speed42:0}m/s · 活跃 {activeProjectiles.Count}",small);
        }
        void DrawChooser()
        {
            DrawLibraryChooser();
        }
        IEnumerator Smoke(string[] args)
        {
            yield return null;
            if(!string.IsNullOrEmpty(error) || match==null) {Application.Quit(2);yield break;}
            for(int i=0;i<catalog.matches.Length;i++)
            {
                LoadMatch(i);
                if(!string.IsNullOrEmpty(error)) {Application.Quit(2);yield break;}
                Seek(match.duration); Seek(0);
                foreach(var unit in units)
                {
                    if(unit.sample!=null && unit.sample.t>0) {Application.Quit(3);yield break;}
                    if((unit.data.role=="base" || unit.data.role=="outpost") && !unit.visible) {Application.Quit(4);yield break;}
                }
                Seek(120.5f); ApplyTime();
                Debug.Log($"REPLAY_MATCH_OK game={match.gameId} entities={units.Count}");
                yield return null;
            }
            LoadMatch(0);
            Seek(120.5f); UpdateCamera();
            if(field.meshFormat=="assembly_binary_v1")
            {
                int bound=0;
                foreach(var unit in units)
                {
                    if(unit.data.role!="base" && unit.data.role!="outpost") continue;
                    if(!unit.cadNode || unit.root.GetComponentsInChildren<Renderer>().Length!=0 ||
                       Vector3.Distance(unit.root.transform.position,unit.cadNode.position)>.001f)
                    {Debug.LogError("CAD建筑未正确绑定，或有重复示意建筑");Application.Quit(5);yield break;}
                    bound++;
                }
                if(bound!=4 || !fieldMismatch) {Application.Quit(6);yield break;}
                Debug.Log("REPLAY_CAD_BINDING_OK buildings=4 duplicateBuildings=0 sourceField="+match.fieldId);
                foreach(var material in cadMaterials)
                    if(material.shader.name!="TCR/Replay CAD surface" || !material.shader.isSupported || !material.GetTexture("_SurfaceTex"))
                    {Debug.LogError("CAD贴图材质在本机不可用");Application.Quit(8);yield break;}
                foreach(var unit in units)
                    if(unit.cadNode && unit.renderers[0].sharedMaterial.GetFloat("_Team")!=(unit.data.team=="red"?0:1))
                    {Debug.LogError("建筑材质红蓝阵营混淆");Application.Quit(9);yield break;}
                Debug.Log($"REPLAY_MATERIAL_RUNTIME_OK materials={cadMaterials.Count} floorTexture={mapTexture.width}x{mapTexture.height} teamBindings=4");
            }
            yield return new WaitForEndOfFrame();
            int index=Array.IndexOf(args,"--screenshot");
            if(index>=0 && index+1<args.Length) ScreenCapture.CaptureScreenshot(args[index+1]);
            Debug.Log($"REPLAY_SMOKE_OK game={match.gameId} units={units.Count} events={Timeline.EventEnd(match.events,playhead)}");
            for(int i=0;i<15;i++) yield return null;
            topView=true;UpdateCamera();
            yield return new WaitForEndOfFrame();
            if(index>=0 && index+1<args.Length) ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(args[index+1]),"replay-top.png"));
            for(int i=0;i<15;i++) yield return null;
            Seek(0);UpdateCamera();
            yield return new WaitForEndOfFrame();
            if(index>=0 && index+1<args.Length) ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(args[index+1]),"replay-start.png"));
            for(int i=0;i<15;i++) yield return null;
            Debug.Log($"REPLAY_FIELD_RUNTIME_OK buildings=4 landmarks={field.landmarks.Length} time=0");
            int initialField=fieldIndex;
            string originalGame=match.gameId,originalSourceField=match.fieldId;
            foreach(var entry in fieldCatalog.fields)
            {
                int f=Array.FindIndex(fieldCatalog.fields,a=>a.id==entry.id);
                Seek(120.5f);SwitchField(f);yield return null;
                if(!string.IsNullOrEmpty(error) || match.gameId!=originalGame || match.fieldId!=originalSourceField ||
                   Mathf.Abs(playhead-120.5f)>.001f || fieldMismatch!=(match.fieldId!=field.id))
                {Debug.LogError("场地切换改变了比赛身份、来源或时间");Application.Quit(7);yield break;}
                ApplyTime();UpdateCamera();
                Debug.Log($"REPLAY_FIELD_SWITCH_OK field={field.id} sourceField={match.fieldId} time={playhead} comparison={fieldMismatch}");
                yield return new WaitForEndOfFrame();
                if(index>=0 && index+1<args.Length)
                    ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(args[index+1]),"field-"+field.id+".png"));
                for(int i=0;i<15;i++) yield return null;
            }
            SwitchField(initialField);yield return null;
            if(field.meshFormat=="assembly_binary_v1" && index>=0 && index+1<args.Length)
            {
                topView=false;landmarkLabels=false;distance=4;pitch=30;Seek(120.5f);
                foreach(string id in new [] {"red_base","blue_base","red_outpost","blue_outpost"})
                {
                    selected=units.FindIndex(u=>u.data.id==id);
                    yaw=id.StartsWith("red")?-115:65;
                    focus=units[selected].root.transform.position+Vector3.up*.55f;UpdateCamera();
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(args[index+1]),id+".png"));
                    for(int i=0;i<15;i++) yield return null;
                }
                texture=false;SetTexture();UpdateCamera();
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(args[index+1]),"materials-without-texture.png"));
                for(int i=0;i<15;i++) yield return null;
                texture=true;SetTexture();
                if(terrainMaterial.GetFloat("_TextureEnabled")!=1) {Application.Quit(10);yield break;}
                Debug.Log("REPLAY_TEXTURE_TOGGLE_OK enabled_after_toggle=1");
            }
            Application.Quit(0);
        }
        void OnDestroy() {SetLidar(false);lidarApi?.Close();ReleaseMouse();RestoreVehicle();StopDeveloper();foreach(var obj in owned) if(obj) Destroy(obj);}
    }
}
