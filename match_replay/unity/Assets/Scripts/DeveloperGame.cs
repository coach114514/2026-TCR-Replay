using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    [Serializable] public class DeveloperBaseGeometry {public int schema;public string @fixed,innerArmorMesh,source,calibration;public float innerActiveAt=.99f;public DeveloperBaseLeaf[] leaves;public MechanismPanel[] panels;public int sourceTriangles,movingSourceTriangles,unassignedTriangles;}
    [Serializable] public class DeveloperBaseLeaf {public string mesh;public float[] pivot,axis,translation;public float angle;public MechanismPanel armor;}
    public partial class ReplayViewer
    {
        string appMode="menu";DeveloperWorld game;GameObject gameRoot;DeveloperBaseGeometry gameBase;
        class GameVehicle {public DeveloperActor state;public Unit unit;public DeveloperVehiclePhysics physics;public LOD[] legacyLods;public float previousAimYaw,previousAimPitch,previousFold;public Vector3 physicsWish;public Quaternion tilt=Quaternion.identity;public Vector3 lastPosition;public Transform[] plateNodes;public string team,lastBlockReason;public float compressedHeight,stuckSeconds;public bool recovering,automaticRecovery,hasSafePosition;public Vector3 lastSafePosition;public float nextSafeSample;public Vector3 eyePoint;public int eyeGroup;}
        readonly Dictionary<string,GameVehicle> gameVehicles=new Dictionary<string,GameVehicle>();
        readonly Dictionary<string,Transform[]> baseLeaves=new Dictionary<string,Transform[]>();
        readonly Dictionary<string,Transform[]> baseInnerPlates=new Dictionary<string,Transform[]>();
        readonly Dictionary<string,DeveloperTracker> gameTrackers=new Dictionary<string,DeveloperTracker>();
        readonly List<DeveloperPlate> gamePlates=new List<DeveloperPlate>(80);readonly List<DeveloperPlate> previousPlates=new List<DeveloperPlate>(80);
        readonly Queue<DeveloperObservation> observations=new Queue<DeveloperObservation>();
        DeveloperRandom observationRandom=new DeveloperRandom(1945),motionRandom=new DeveloperRandom(2026);
        double gameAccumulator;DeveloperInput gameInput=new DeveloperInput();
        string gameSelection="target1",gameLock="",aimStatus="手动瞄准";int aimPlate=-1,gameShotCount,gameHits,gameRuneHits;
        bool gameAutoAim,gameAimValid,gamePreview,gameThirdPerson;float nextObservation,nextPreview;
        Vector3 gameAimDirection,gameAimPoint;float gameAimFlight,gameDesiredYaw,gameDesiredPitch;
        LineRenderer gamePreviewLine;GameObject gameAimMarker;DeveloperPlate gameSelectedPlate;
        static readonly Vector3 Vt03SourcePoint=new Vector3(.1248139657f,.2895201472f,.0920671801f);
        const float SweeperViewForwardOffset=.040f;
        // Keep the chosen VT03 side and gun-group attachment. The saved CAD's
        // barrel lip sits 33mm in front of C; move the optical viewpoint past
        // that lip without moving the physical gun, hiding the body or widening
        // the near clip plane. Source forward is calibrated in model coordinates.
        static readonly Vector3 Vt03Point=Vt03SourcePoint+new Vector3(.9997431452f,0,-.0226637051f)*SweeperViewForwardOffset;
        string GameStorage=>Path.Combine(Application.persistentDataPath,"developer");
        void LoadDeveloperBase()
        {
            string path=Path.Combine(folder,"developer_base.json");if(File.Exists(path))gameBase=JsonUtility.FromJson<DeveloperBaseGeometry>(File.ReadAllText(path));
        }
        void EnterMenu()
        {
            SetLidar(false);ClearUiPanels();UiFocusChanged();RestoreVehicle();playing=false;StopDeveloper();
            foreach(var u in units)if(u.root)u.root.SetActive(false);ClearProjectileVisuals();if(fieldRoot)fieldRoot.SetActive(false);appMode="menu";viewCamera.rect=new Rect(0,0,1,1);
        }
        void EnterReplay()
        {
            StopDeveloper();ClearUiPanels();UiFocusChanged();appMode="replay";viewCamera.fieldOfView=50;if(fieldRoot){fieldRoot.SetActive(false);Destroy(fieldRoot);}BuildField();LoadMatch(matchIndex);playing=false;cleanView=false;UpdateCamera();
        }
        void EnterDeveloper()
        {
            if(!DeveloperResourcesAvailable){error="开发者模式需要另行准备场地与车辆资源；公开示例提供回放和点云验证。";return;}
            ClearUiPanels();UiFocusChanged();RestoreVehicle();StopDeveloper();appMode="game";playing=false;ClearProjectileVisuals();foreach(var u in units){if(u.root){u.root.SetActive(false);Destroy(u.root);}if(u.trail){u.trail.gameObject.SetActive(false);Destroy(u.trail.gameObject);}}units.Clear();match=null;
            if(fieldRoot){fieldRoot.SetActive(false);Destroy(fieldRoot);}fieldIndex=Array.FindIndex(fieldCatalog.fields,f=>f.id=="rmuc_2026_final_official");if(fieldIndex<0)fieldIndex=0;
            field=JsonUtility.FromJson<FieldConfig>(File.ReadAllText(Path.Combine(folder,fieldCatalog.fields[fieldIndex].file)));LoadDeveloperBase();baseLeaves.Clear();baseInnerPlates.Clear();BuildField();
            game=new DeveloperWorld();game.Added+=BuildGameVehicle;game.Removed+=RemoveGameVehicle;game.ResetRequested+=ResetDeveloper;
            gameRoot=new GameObject("Developer simulation");LoadGameClips();ResetDeveloper();ConfigureGamePhysics();viewCamera.nearClipPlane=.012f;viewCamera.fieldOfView=75;gameThirdPerson=false;
            game.Log("开发者比赛已就绪；点击开始；Esc释放鼠标，F6编辑，F7弹道，Tab地图，~控制台");UpdateGameCamera();
        }
        void StopDeveloper()
        {
            recoveryStatus="";gamePhysics?.Dispose();gamePhysics=null;driveProbeBox=null;driveShapeProbes.Clear();
            if(gameRoot){gameRoot.SetActive(false);Destroy(gameRoot);}gameRoot=null;game=null;gameVehicles.Clear();gameTrackers.Clear();observations.Clear();runeTrackers.Clear();runeObservations.Clear();gamePlates.Clear();previousPlates.Clear();baseLeaves.Clear();baseInnerPlates.Clear();
            gameBullets.Clear();gameBulletPool.Clear();gamePreviewLine=null;gameAimMarker=null;gameAccumulator=0;gameAutoAim=false;gameLock="";ClearRuneAim();
        }
        void ResetDeveloper()
        {
            recoveryStatus="";
            if(game==null)return;InvalidateLidar();foreach(var v in gameVehicles.Values)if(v.unit.root){v.unit.root.SetActive(false);Destroy(v.unit.root);}bool rebuildPhysics=gamePhysics!=null;gamePhysics?.Dispose();gamePhysics=null;gameVehicles.Clear();
            // Reset the solver scene with the match, rather than recycling native
            // contact state across many destroyed/recreated compound vehicles.
            if(rebuildPhysics&&game.rules.vehiclePhysics)gamePhysics=new DeveloperPhysicsWorld(fieldRoot,ReadMesh);
            foreach(var bullet in gameBullets)if(bullet.root)bullet.root.SetActive(false);foreach(var bullet in gameBullets)gameBulletPool.Push(bullet);gameBullets.Clear();
            game.ResetState();observationRandom=new DeveloperRandom(game.seed+81);motionRandom=new DeveloperRandom(game.seed);gameTrackers.Clear();observations.Clear();gamePlates.Clear();gameLock="";gameAutoAim=false;gameAimValid=false;gameShotCount=gameHits=gameRuneHits=0;nextObservation=nextPreview=nextRuneObservation=0;runeTrackers.Clear();runeObservations.Clear();gameAccumulator=0;ClearRuneAim();
            navigation.Clear();LoadGameZones();var p=new Vector3(6,0,9.4f);var player=game.Add("player","red",p);player.manualControl=true;player.gimbalYaw=90;PoseGameVehicle(gameVehicles[player.id]);var target=game.Add("target1","blue",new Vector3(9.5f,0,9.4f));target.spinRate=180;
            gameInput=new DeveloperInput();gameDesiredYaw=90;gameDesiredPitch=0;UpdateGameMechanisms();CollectGamePlates();UpdateGameCamera();
        }
        Vector3 GameSpawn(string team)
        {
            var a=Array.Find(field.landmarks??Array.Empty<BuildingAnchor>(),x=>x.team==team&&x.role=="supply_zone");
            if(a!=null)return new Vector3(a.x,0,a.y);return new Vector3(team=="red"?5:field.width-5,0,field.depth*.5f);
        }
        void BuildGameVehicle(DeveloperActor actor)
        {
            var unit=new Unit {data=new EntityData {id=actor.id,name=actor.id,role=actor.model=="jiageng"?"infantry4":"infantry3",team=actor.team,samples=Array.Empty<Sample>()},root=new GameObject(actor.id)};
            unit.root.transform.SetParent(gameRoot.transform,false);if(!BuildRobot(unit))throw new InvalidDataException("扫地车模型未加载");unit.renderers=unit.root.GetComponentsInChildren<Renderer>();unit.robot.selection.enabled=false;
            for(int g=0;g<unit.robot.groups.Length;g++)if(!unit.robot.model.groups[g].turret)
            foreach(var filter in unit.robot.groups[g].GetComponentsInChildren<MeshFilter>())
            {int lod=int.Parse(filter.gameObject.name.Substring(4));string path=actor.model=="jiageng"?"assets/robots/jiageng-game/group-"+g+"-"+lod+".tcrmesh":"assets/robots/sweeper-game/chassis-"+lod+".tcrmesh";filter.sharedMesh=ReadMesh(path);}
            // Separate source face meshes let edited armor height match rendering and hit planes.
            foreach(var r in unit.robot.armorNode.GetComponents<Renderer>())r.enabled=false;
            var plates=new Transform[unit.robot.model.armorPanels.Length];var surface=ReadMesh("assets/robots/armor/am02-surface.tcrmesh");var digit=ReadMesh("assets/robots/armor/number-"+(actor.model=="jiageng"?"4":"3")+".tcrmesh");
            for(int i=0;i<plates.Length;i++)
            {
                var armor=unit.robot.model.armorPanels[i];var node=new GameObject("Editable armor "+i).transform;node.SetParent(unit.root.transform,false);node.localPosition=PassagePose.Vector(armor.position);node.localRotation=PassagePose.Rotation(armor.rotation);plates[i]=node;
                foreach(var mesh in new[]{surface,digit}){var child=new GameObject(mesh.name);child.transform.SetParent(node,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=robotMaterials[actor.team=="red"?"armor-red":"armor-blue"];}
                // Source casing moves with the face; it is removed from the static chassis mesh.
                var body=new GameObject("Source armor casing");body.transform.SetParent(node,false);body.AddComponent<MeshFilter>().sharedMesh=ReadMesh("assets/robots/"+(actor.model=="jiageng"?"jiageng-game":"sweeper-game")+"/armor-"+i+"-1.tcrmesh");body.AddComponent<MeshRenderer>().sharedMaterial=RobotMaterial("cad",Color.white);
                actor.armorHeights.Add(node.localPosition.y);
            }
            unit.renderers=unit.root.GetComponentsInChildren<Renderer>();var vehicle=new GameVehicle {state=actor,unit=unit,lastPosition=actor.position,plateNodes=plates,team=actor.team,eyePoint=actor.model=="jiageng"?new Vector3(.08169203f,.25871804f,-.18377416f):Vt03Point,eyeGroup=actor.model=="jiageng"?7:3};vehicle.compressedHeight=CompressedHeight(vehicle);gameVehicles[actor.id]=vehicle;PoseGameVehicle(vehicle);
            var groundQuery=actor.position;if(groundQuery.y<=0)groundQuery.y=8;actor.fold=unit.robot.model.defaultFold;actor.position.y=GameGround(vehicle,groundQuery)+BallisticMath.Support(unit.robot.model,actor.fold)+.004f;vehicle.lastPosition=actor.position;PoseGameVehicle(vehicle);game.ApplyLevel(actor,actor.level);BuildVehiclePhysics(vehicle);
        }
        void RemoveGameVehicle(DeveloperActor actor){gamePhysics?.Remove(actor.id);if(gameVehicles.TryGetValue(actor.id,out var v))Destroy(v.unit.root);gameVehicles.Remove(actor.id);gameTrackers.Remove(actor.id);if(gameSelection==actor.id)gameSelection="player";if(gameLock==actor.id)gameLock="";}
        void BuildGameBase(FieldPart part,Transform parent,Material material)
        {
            if(appMode!="game"||gameBase==null||!part.binding.EndsWith("_base"))return;var nodes=new Transform[gameBase.leaves.Length];
            for(int i=0;i<nodes.Length;i++)
            {
                var leaf=gameBase.leaves[i];var node=new GameObject("Folding base armor "+i).transform;node.SetParent(parent,false);node.localPosition=PassagePose.Vector(leaf.pivot);nodes[i]=node;
                var obj=CadChild("Source folding leaf and armor casing",node,leaf.mesh,material,-node.localPosition,true);obj.layer=14;obj.AddComponent<MeshCollider>().sharedMesh=obj.GetComponent<MeshFilter>().sharedMesh;
            }
            baseLeaves[part.team]=nodes;
            if(!string.IsNullOrEmpty(gameBase.innerArmorMesh))
            {
                string key="base-inner-"+part.team;
                if(!robotMaterials.TryGetValue(key,out var paint)){paint=new Material(Resources.Load<Material>("ReplayArmor"));paint.SetColor("_TeamColor",part.team=="red"?new Color(1,.025f,.055f):new Color(.025f,.30f,1));owned.Add(paint);robotMaterials[key]=paint;}
                var armor=new Transform[3];
                for(int i=0;i<armor.Length;i++)
                {
                    var panel=gameBase.panels[i+3];var obj=CadChild("Inner base armor "+(i+3),parent,gameBase.innerArmorMesh,paint,PassagePose.Vector(panel.center),true);obj.layer=13;
                    obj.transform.localRotation=Quaternion.LookRotation(PassagePose.Vector(panel.normal),Vector3.forward);
                    var collider=obj.AddComponent<BoxCollider>();collider.size=PassagePose.Vector(panel.half)*2;collider.isTrigger=true;
                    var housing=new GameObject("Inner armor mounting frame collision");housing.transform.SetParent(obj.transform,false);housing.layer=14;
                    housing.AddComponent<MeshCollider>().sharedMesh=obj.GetComponent<MeshFilter>().sharedMesh;
                    // Keep renderers enabled under inactive roots: the sensor can
                    // register the mesh once and sample actual open/closed state.
                    obj.SetActive(false);armor[i]=obj.transform;
                }
                baseInnerPlates[part.team]=armor;
            }
        }
        void UpdateDeveloper()
        {
            if(developerSmokeActive){UpdateDisplaySettings();return;}
            UpdateDisplaySettings();
            bool typing=UiHasModal;
            bool blockedInput=!BackgroundInputAllowed;
            if(!blockedInput)
            {
                if(Input.GetKeyDown(KeyCode.F7)){gamePreview=!gamePreview;if(gamePreviewLine)gamePreviewLine.enabled=gamePreview;}
                if(Input.GetKeyDown(KeyCode.V))gameThirdPerson=!gameThirdPerson;
                if(Input.GetKeyDown(KeyCode.F9))allLabels=!allLabels;
                if(Input.GetKeyDown(KeyCode.P))game.running=!game.running&&!game.ended;
                if(Input.GetKeyDown(KeyCode.CapsLock)&&game.actors.TryGetValue(game.controlled,out var slowActor))slowActor.slow=!slowActor.slow;
                if(!mouseCaptured&&Input.GetMouseButtonDown(0)&&Input.mousePosition.y<Screen.height*.86f&&Input.mousePosition.y>Screen.height*.09f)
                {mouseCaptured=true;captureFrame=Time.frameCount;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;GUIUtility.keyboardControl=0;}
                if(mouseCaptured&&Time.frameCount>captureFrame)
                {
                    gameDesiredYaw+=Input.GetAxis("Mouse X")*2;gameDesiredPitch=Mathf.Clamp(gameDesiredPitch+Input.GetAxis("Mouse Y")*2,-30,30);
                    if(Input.GetMouseButtonDown(1))ToggleGameAim();
                }
            }
            gameInput.x=!blockedInput&&mouseCaptured?(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0):0;
            gameInput.z=!blockedInput&&mouseCaptured?(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0):0;
            gameInput.burst=!blockedInput&&mouseCaptured&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl));
            gameInput.spin=!blockedInput&&mouseCaptured&&Input.GetKey(KeyCode.LeftShift);gameInput.fire=!blockedInput&&mouseCaptured&&Time.frameCount>captureFrame&&Input.GetMouseButton(0);
            gameInput.yaw=gameDesiredYaw;gameInput.pitch=gameDesiredPitch;gameInput.cameraRelative=true;
            bool simulation=Application.isFocused&&!typing;
            if(!simulation){gameAccumulator=0;game.DrainCommands();ConfigureGamePhysics();if(gamePhysics!=null){gamePhysics.SyncSurfaces();foreach(var vehicle in gameVehicles.Values)vehicle.physics?.SyncEdits();}}
            else
            {
                gameAccumulator+=Time.unscaledDeltaTime;int count=0;while(gameAccumulator>=DeveloperWorld.Step&&count++<240){GameStep();gameAccumulator-=DeveloperWorld.Step;}
            }
            if(!game.running||typing){foreach(var v in gameVehicles.Values)PoseGameVehicle(v);UpdateGameMechanisms();CollectGamePlates();}
            if(gamePhysics!=null&&simulation&&game.running)RenderPhysicsFrame(Mathf.Clamp01((float)(gameAccumulator/DeveloperWorld.Step)));UpdateGameCamera();UpdateGamePreview();RenderGameBullets();
        }
        void GameStep()
        {
            game.DrainCommands();ConfigureGamePhysics();game.Tick();if(!game.running||game.ended)return;
            previousPlates.Clear();previousPlates.AddRange(gamePlates);
            foreach(var vehicle in gameVehicles.Values)
            {
                var a=vehicle.state;vehicle.previousAimYaw=a.gimbalYaw;vehicle.previousAimPitch=a.pitch;vehicle.previousFold=a.fold;bool manual=a.id==game.controlled&&a.manualControl;DeveloperInput input=manual?gameInput.Copy():AutomaticGameInput(a);if(manual)input.slow=a.slow;
                if(gamePhysics!=null)PreparePhysicsVehicle(vehicle,input);else MoveGameVehicle(vehicle,input);if(a.motion=="path"&&navigation.TryGetValue(a.id,out var route)&&route.unreachable)a.blocked=true;if(!manual){a.gimbalYaw=input.yaw;a.pitch=input.pitch;}else if(!gameAutoAim||!gameAimValid&&!RuneLocked){a.gimbalYaw=gameDesiredYaw;a.pitch=gameDesiredPitch;}
                PoseGameVehicle(vehicle);if(gamePhysics==null)game.Record(a,input);
            }
            if(gamePhysics!=null){gamePhysics.Step(game);foreach(var vehicle in gameVehicles.Values){vehicle.state.blocked=vehicle.physicsWish.sqrMagnitude>.04f&&new Vector2(vehicle.state.velocity.x,vehicle.state.velocity.z).magnitude<.03f;vehicle.stuckSeconds=vehicle.state.blocked?vehicle.stuckSeconds+DeveloperWorld.Step:0;PhysicsBoundaryGuard(vehicle);PoseGameVehicle(vehicle);game.Record(vehicle.state,vehicle.physics.Command);}}
            UpdateGameMechanisms();CollectGamePlates();UpdateGameCamera();ObserveGameTargets();ObserveGameRunes();SolveGameAim();
            if(game.actors.TryGetValue(game.controlled,out var player)&&player.manualControl&&!gameVehicles[player.id].recovering)
            {
                if(gameAutoAim&&gameAimValid&&!GameFolded(gameVehicles[player.id]))
                {
                    float targetYaw=Mathf.Atan2(gameAimDirection.x,gameAimDirection.z)*Mathf.Rad2Deg,targetPitch=Mathf.Clamp(Mathf.Asin(Mathf.Clamp(gameAimDirection.y,-1,1))*Mathf.Rad2Deg,-30,30);
                    player.gimbalYaw=Mathf.MoveTowardsAngle(player.gimbalYaw,targetYaw,game.rules.gimbalSpeed*DeveloperWorld.Step);player.pitch=Mathf.MoveTowards(player.pitch,targetPitch,game.rules.gimbalSpeed*DeveloperWorld.Step);
                    gameDesiredYaw=player.gimbalYaw;gameDesiredPitch=player.pitch;PoseGameVehicle(gameVehicles[player.id]);
                }
                if(gameInput.fire&&(RuneLocked||!game.rules.aimFireGate||!gameAutoAim||GameAimReady(player)))FireGameBullet(player);
                else if(RuneAutomaticTrigger(player))FireGameBullet(player,true);
            }
            foreach(var a in game.actors.Values)if(!a.manualControl&&a.motion=="clip"&&a.clipFire&&gameClipFire.TryGetValue(a.id,out bool fire)&&fire)FireGameBullet(a);
            StepGameBullets();if(game.tick%12==0)UpdateGameZones();
        }
        readonly Dictionary<string,bool> gameClipFire=new Dictionary<string,bool>();
        DeveloperInput AutomaticGameInput(DeveloperActor a)
        {
            var input=new DeveloperInput {yaw=a.gimbalYaw,pitch=a.pitch,spin=a.spin,slow=a.slow};
            if(a.motion=="clip"){input=game.ClipInput(a);gameClipFire[a.id]=input.fire;return input;}
            Vector3 move=a.direction;
            if(a.motion=="path"&&a.path.Count>0)
            {
                if(game.time<a.dwellUntil)return input;var waypoint=a.path[Mathf.Clamp(a.pathIndex,0,a.path.Count-1)];move=waypoint-a.position;move.y=0;
                if(NavigationArrived(a,waypoint)){a.pathIndex++;a.dwellUntil=game.time+a.pathDwell;if(a.pathIndex>=a.path.Count){if(a.pathLoop&&(a.loops<0||a.loops>0)){a.pathIndex=0;if(a.loops>0)a.loops--;}else{a.motion="still";return input;}}move=a.path[a.pathIndex]-a.position;move.y=0;}
                move=RouteDirection(a,a.path[a.pathIndex]);move.y=0;
            }
            else if(a.motion=="random")
            {
                if(game.time>=a.dwellUntil||a.blocked){int direction=motionRandom.Next(8);a.direction=Quaternion.Euler(0,direction*45,0)*Vector3.forward;a.dwellUntil=game.time+1.5f+(float)motionRandom.NextDouble()*2;}move=a.direction;
            }
            else if(a.motion=="return")
            {
                if(a.path.Count<2)return input;move=a.path[a.pathIndex%2]-a.position;move.y=0;if(NavigationArrived(a,a.path[a.pathIndex%2]))a.pathIndex=(a.pathIndex+1)%2;move=RouteDirection(a,a.path[a.pathIndex%2]);move.y=0;
            }
            else if(a.motion!="direction")move=Vector3.zero;
            move=Vector3.ClampMagnitude(move,1);input.x=move.x;input.z=move.z;return input;
        }
        void PoseGameVehicle(GameVehicle vehicle)
        {
            var a=vehicle.state;var root=vehicle.unit.root.transform;root.position=a.position;
            if(vehicle.physics!=null){vehicle.physics.SyncEdits();vehicle.physics.ApplyVisuals(1);vehicle.tilt=Quaternion.Inverse(Quaternion.Euler(0,a.yaw,0))*vehicle.physics.Body.rotation;}
            else {GroundSupport(vehicle,a.position,a.yaw,out _,out Quaternion slope);root.rotation=slope*Quaternion.Euler(0,a.yaw,0);vehicle.tilt=Quaternion.Inverse(Quaternion.Euler(0,a.yaw,0))*slope*Quaternion.Euler(0,a.yaw,0);}
            var robot=vehicle.unit.robot;robot.fold=a.fold;robot.elevation=a.pitch;robot.heading=a.gimbalYaw-a.yaw;
            Vector3 localDirection=root.InverseTransformDirection(BallisticMath.Direction(a.gimbalYaw,a.pitch));float localHeading=Mathf.Atan2(localDirection.x,localDirection.z)*Mathf.Rad2Deg,localPitch=Mathf.Asin(Mathf.Clamp(localDirection.y,-1,1))*Mathf.Rad2Deg;
            for(int g=0;g<robot.groups.Length;g++)
            {
                var m=BallisticMath.Group(robot.model,g,a.fold,localHeading,GameFolded(vehicle)?0:Mathf.Clamp(localPitch,-30,30),true);robot.groups[g].localPosition=m.GetColumn(3);robot.groups[g].localRotation=m.rotation;
            }
            for(int i=0;i<vehicle.plateNodes.Length;i++){var position=PassagePose.Vector(robot.model.armorPanels[i].position);if(i<a.armorHeights.Count)position.y=a.armorHeights[i];vehicle.plateNodes[i].localPosition=position;}
            if(vehicle.team!=a.team){vehicle.team=a.team;foreach(var node in vehicle.plateNodes)foreach(var renderer in node.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=robotMaterials[a.team=="red"?"armor-red":"armor-blue"];}
            vehicle.unit.root.SetActive(true);foreach(var renderer in vehicle.unit.renderers)if(renderer)renderer.forceRenderingOff=!a.Alive;
        }
        void GameMuzzle(GameVehicle v,out Vector3 point,out Vector3 forward)
        {
            var m=v.unit.robot.model;var transform=v.unit.robot.groups[m.gunGroup];point=transform.TransformPoint(PassagePose.Vector(m.muzzlePoint));forward=transform.TransformDirection(PassagePose.Vector(m.gunForward)).normalized;
        }
        void UpdateGameCamera()
        {
            if(game==null||!gameVehicles.TryGetValue(game.controlled,out var v))return;GameMuzzle(v,out _,out var forward);Vector3 eye=v.unit.robot.groups[v.eyeGroup].TransformPoint(v.eyePoint);
            viewCamera.rect=new Rect(0,0,1,1);viewCamera.orthographic=false;viewCamera.nearClipPlane=.012f;viewCamera.fieldOfView=75;
            if(gameThirdPerson){Vector3 wanted=eye-forward*2+Vector3.up*.8f;Vector3 delta=wanted-eye;if(DevRaycast(eye,delta.normalized,out var hit,delta.magnitude,1<<10))wanted=hit.point+hit.normal*.08f;viewCamera.transform.position=wanted;viewCamera.transform.LookAt(eye+forward*3);}
            else viewCamera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(forward,v.unit.robot.groups[v.unit.robot.model.gunGroup].up));
        }
        void UpdateGameMechanisms()
        {
            foreach(var entry in guideMaterials){string team=entry.Key.StartsWith("red")?"red":"blue";bool dead=game.buildings[team+"_outpost"].hp<=0;entry.Value.SetFloat("_GuideLight",entry.Key.EndsWith("_base")?(dead?1:0):(dead?0:1));}
            foreach(var item in outpostRotors){var b=game.buildings[item.Key+"_outpost"];item.Value.localRotation=Quaternion.AngleAxis(b.angle,item.Value.parent.InverseTransformDirection(Vector3.up));}
            if(runeRotor)runeRotor.localRotation=Quaternion.AngleAxis(game.runeAngle,PassagePose.Vector(mechanismConfig.rune.axis));
            foreach(var arm in runeArms)
            {
                var rune=game.runes[arm.team];bool lit=rune.activating&&(arm.index==rune.lit&&!(rune.Large(game.time)&&rune.secondPending) || rune.Large(game.time)&&arm.index==rune.optional),completed=rune.activating&&!rune.Large(game.time)&&!rune.available.Contains(arm.index),buff=rune.enabled&&!rune.startPending&&!rune.activating&&(game.time<rune.buffUntil||game.time<rune.smallBuffUntil);
                arm.material.SetFloat("_RuneLight",lit||completed||buff?1:.08f);arm.material.SetFloat("_RunePhase",game.time);arm.material.SetFloat("_RuneFlow",lit?1:0);arm.material.SetFloat("_RuneRadial",1);arm.material.SetFloat("_RuneProgress",rune.activating&&rune.Large(game.time)?rune.groups/5f:0);
                arm.material.SetVector("_RuneOrigin",runeRotor.position);arm.material.SetVector("_RuneTarget",runeRotor.TransformPoint(PassagePose.Vector(arm.geometry.targetCenter)-PassagePose.Vector(mechanismConfig.rune.pivot)));
                if(arm.targetPattern)arm.targetPattern.enabled=lit;if(arm.successDot)arm.successDot.enabled=buff||completed||rune.activating&&rune.secondPending&&arm.index==rune.lit;
            }
            if(gameBase!=null)foreach(var item in baseLeaves){float amount=game.buildings[item.Key+"_base"].armorOpen;for(int i=0;i<item.Value.Length;i++){var leaf=gameBase.leaves[i];item.Value[i].localPosition=PassagePose.Vector(leaf.pivot)+PassagePose.Vector(leaf.translation)*amount;item.Value[i].localRotation=Quaternion.AngleAxis(leaf.angle*amount,PassagePose.Vector(leaf.axis));}}
            foreach(var item in baseInnerPlates){bool active=InnerBaseArmorActive(game.buildings[item.Key+"_base"]);foreach(var node in item.Value)node.gameObject.SetActive(active);}
            Physics.SyncTransforms();
            gamePhysics?.SyncSurfaces();
        }
        bool InnerBaseArmorActive(DeveloperBuilding building)=>building.armorOpen>=gameBase.innerActiveAt;
        void CollectGamePlates()
        {
            gamePlates.Clear();foreach(var v in gameVehicles.Values){var a=v.state;if(!a.Alive)continue;for(int i=0;i<v.plateNodes.Length;i++){var node=v.plateNodes[i];gamePlates.Add(new DeveloperPlate {target=a.id,team=a.team,index=i,center=node.position,rotation=node.rotation,local=node.localPosition,localRotation=node.localRotation,tilt=v.tilt,half=new Vector3(.0675f,.0625f,.004f),time=game.time});}}
            foreach(string team in new[]{"red","blue"})
            {
                if(buildingNodes.TryGetValue(team+"_outpost",out var post)&&game.buildings[team+"_outpost"].hp>0)
                {
                    var geometry=mechanismConfig.outpost;Vector3 pivot=post.TransformPoint(PassagePose.Vector(geometry.pivot));Quaternion rot=Quaternion.AngleAxis(game.buildings[team+"_outpost"].angle,Vector3.up);
                    for(int i=0;i<geometry.panels.Length;i++){var panel=geometry.panels[i];Vector3 local=post.TransformPoint(PassagePose.Vector(panel.center))-pivot;Quaternion localRot=Quaternion.LookRotation(post.TransformDirection(PassagePose.Vector(panel.normal)),Vector3.up);gamePlates.Add(new DeveloperPlate {target=team+"_outpost",team=team,index=i,center=pivot+rot*local,rotation=rot*localRot,local=local,localRotation=localRot,tilt=Quaternion.identity,half=new Vector3(.0555f,.049f,.004f),critical=true,time=game.time});}
                }
                if(gameBase!=null&&buildingNodes.TryGetValue(team+"_base",out var baseNode)&&game.buildings[team+"_base"].hp>0)
                {
                    for(int i=0;i<gameBase.panels.Length;i++)if(i<3||InnerBaseArmorActive(game.buildings[team+"_base"]))AddGameBasePlate(team,baseNode,gameBase.panels[i],i);
                }
            }
            if(runeRotor)foreach(var arm in runeArms){var g=mechanismConfig.rune;var node=runeRotor;Vector3 center=node.TransformPoint(PassagePose.Vector(arm.geometry.targetCenter)-PassagePose.Vector(g.pivot));Vector3 normal=node.TransformDirection(PassagePose.Vector(arm.geometry.targetNormal));gamePlates.Add(new DeveloperPlate {target="rune_"+arm.team,team=arm.team,index=arm.index,center=center,rotation=Quaternion.LookRotation(normal,node.TransformDirection(Vector3.up)),half=new Vector3(.15f,.15f,.004f),rune=true,time=game.time});}
        }
        void AddGameBasePlate(string team,Transform node,MechanismPanel panel,int i)
        {
            Vector3 center=node.TransformPoint(PassagePose.Vector(panel.center));Quaternion rot=Quaternion.LookRotation(node.TransformDirection(PassagePose.Vector(panel.normal)),node.TransformDirection(Vector3.forward));
            gamePlates.Add(new DeveloperPlate {target=team+"_base",team=team,index=i,center=center,rotation=rot,local=center-node.position,localRotation=rot,tilt=Quaternion.identity,half=PassagePose.Vector(panel.half),critical=true,time=game.time});
        }
    }
}
