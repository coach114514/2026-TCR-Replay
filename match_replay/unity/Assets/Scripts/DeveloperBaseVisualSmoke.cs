using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class BaseVisualReport
        {
            public string gpu,error;public int openedContacts,closedChecks,hiddenSensorChecks,visibleSensorChecks,guideStateChecks,aimChecks,exteriorShots,exteriorInnerHits;
            public bool passed,sourceBezelPosition,threeInnerFaces,bothTeamColors,cameraBodyRetained;
        }
        IEnumerator BaseVisualSmoke(string[] args)
        {
            int index=Array.IndexOf(args,"--base-visual-smoke");string output=args[index+1];Directory.CreateDirectory(output);
            developerSmokeActive=true;game.running=false;allLabels=false;var report=new BaseVisualReport {gpu=SystemInfo.graphicsDeviceName};
            yield return null;
            try
            {
                DeveloperChecks.Require(baseInnerPlates.Count==2,"两方缺少内侧装甲");
                foreach(string team in new[]{"red","blue"})
                {
                    string id=team+"_base";var building=game.buildings[id];var node=buildingNodes[id];
                    var lamp=guideLenses[id];Vector3 local=node.InverseTransformPoint(lamp.position);
                    // Independently measured original CAD circle: native
                    // (x=.0044,y=1042.3819,z=88.5184)mm, front offset1.5mm.
                    DeveloperChecks.Require(Mathf.Abs(local.z-1.0427575f)<.0002f&&Mathf.Abs(local.y-.08997061f)<.0002f,"灯没有位于原生上部圆孔");
                    DeveloperChecks.Require(Mathf.Abs(lamp.localScale.x-.055f)<.00001f,"55mm引导灯尺寸");
                    foreach(float hp in new[]{1500f,0f,750f})
                    {game.buildings[team+"_outpost"].hp=hp;UpdateGameMechanisms();DeveloperChecks.Require(guideMaterials[id].GetFloat("_GuideLight")== (hp<=0?1:0),"前哨生灭/重建没有切换基地灯");report.guideStateChecks++;}
                    var color=baseInnerPlates[team][0].GetComponent<Renderer>().sharedMaterial.GetColor("_TeamColor");
                    DeveloperChecks.Require(team=="red"?color.r>color.b:color.b>color.r,"内侧装甲红蓝混淆");
                    using(var sensor=new ReplayLidarGeometry())
                    {
                        foreach(var panel in baseInnerPlates[team])sensor.Add(panel.gameObject);
                        foreach(float amount in new[]{0f,.5f,1f,.98f,0f})
                        {
                            building.armorOpen=amount;UpdateGameMechanisms();CollectGamePlates();sensor.Freeze();bool open=amount>=.99f;
                            DeveloperChecks.Require(gamePlates.FindAll(p=>p.target==id).Count==(open?6:3),"装甲开合没有同步自瞄目标列表");report.aimChecks++;
                            for(int i=0;i<3;i++)
                            {
                                var panel=gameBase.panels[i+3];Vector3 center=node.TransformPoint(PassagePose.Vector(panel.center)),normal=node.TransformDirection(PassagePose.Vector(panel.normal));
                                DeveloperChecks.Require(baseInnerPlates[team][i].gameObject.activeInHierarchy==open,"闭合内侧装甲仍显露");
                                bool scanned=sensor.PhysicsScene.Raycast(center+normal*.2f,-normal,out _,.3f);
                                DeveloperChecks.Require(scanned==open,"隐藏装甲仍出现在激光扫描中");if(open)report.visibleSensorChecks++;else report.hiddenSensorChecks++;
                                bool contact=GameContactAt(center+normal*.08f,center-normal*.04f,"player",1,1,out var hit)&&hit.armor&&hit.plate.target==id&&hit.plate.index==i+3;
                                DeveloperChecks.Require(contact==open,"内侧装甲开合未同步命中/遮挡 "+id+":"+(i+3)+" amount="+amount+" hit="+hit.point);
                                if(!open){report.closedChecks++;game.buildings[team+"_outpost"].hp=0;float hp=building.hp,shield=building.shield;int damage=game.Hit(new DeveloperHit {target=id,shooter="player",panel=i+3,normalSpeed=25,local=new Vector2(.02f,.02f)});DeveloperChecks.Require(damage==0&&building.hp==hp&&building.shield==shield,"闭合内侧装甲造成伤害");}
                            }
                        }
                    }
                    building.armorOpen=building.armorTarget=1;UpdateGameMechanisms();CollectGamePlates();
                    foreach(var plate in gamePlates.FindAll(p=>p.target==id))
                    {
                        DeveloperChecks.Require(GameContactAt(plate.center+plate.Normal*.08f,plate.center-plate.Normal*.04f,"player",1,1,out var hit)&&hit.armor&&hit.plate.target==id&&hit.plate.index==plate.index,"展开装甲被源CAD遮挡 "+id+":"+plate.index);report.openedContacts++;
                        // Upper source armor leans under the dart wing; a
                        // normal-axis camera can itself sit behind that wing.
                        // Test new inner armor from a real exterior gap view.
                        if(plate.index>=3)DeveloperChecks.Require(FindInnerArmorView(plate,out _),"展开内侧装甲没有可见的外部自瞄视线 "+id+":"+plate.index);
                    }
                    game.buildings[team+"_outpost"].hp=0;building.shield=0;game.time+=.1f;
                    int dealt=game.Hit(new DeveloperHit {target=id,shooter="player",panel=3,normalSpeed=25,local=new Vector2(.02f,.02f)});
                    DeveloperChecks.Require(dealt==20,"展开内侧装甲未按20伤害结算");
                }
                // Exercise actual muzzle firing, gravity, EKF and damage from
                // outside the base, rather than only short contact probes.
                foreach(string team in new[]{"red","blue"})
                {
                    ResetDeveloper();string id=team+"_base";var building=game.buildings[id];var node=buildingNodes[id];
                    building.armorOpen=building.armorTarget=1;building.shield=0;game.buildings[team+"_outpost"].hp=0;
                    var driver=gameVehicles["player"];var actor=driver.state;actor.team=team=="red"?"blue":"red";actor.ammo=actor.allowedAmmo=100;
                    Vector3 outward=team=="red"?Vector3.right:Vector3.left;actor.position=node.position+outward*2.5f;actor.position.y=GroundFrom(new Vector3(actor.position.x,8,actor.position.z))+.004f;
                    actor.yaw=actor.gimbalYaw=team=="red"?270:90;gameDesiredYaw=actor.gimbalYaw;driver.lastPosition=actor.position;PoseGameVehicle(driver);UpdateGameCamera();
                    game.rules.unlimitedHeat=true;gameAutoAim=true;gameLock=id;aimPlate=3;gameInput=new DeveloperInput {yaw=actor.gimbalYaw};game.running=true;
                    for(int step=0;step<480;step++){gameInput.fire=step>=120;GameStep();UpdateGameCamera();}gameInput.fire=false;game.running=false;
                    int innerHits=0;foreach(string line in game.messages)if(line.Contains("→ "+id+" 装甲 3 -"))innerHits++;
                    DeveloperChecks.Require(gameShotCount>0&&innerHits>0&&building.hp<5000,"场外实际枪口未击中内侧装甲 "+id+" aim="+aimStatus+" gun="+actor.position);
                    report.exteriorShots+=gameShotCount;report.exteriorInnerHits+=innerHits;
                    building.armorOpen=building.armorTarget=0;UpdateGameMechanisms();CollectGamePlates();SolveGameAim();
                    DeveloperChecks.Require(!gameAimValid||aimPlate<3,"闭合后仍跟踪失效的内侧装甲");report.aimChecks++;
                }
                report.sourceBezelPosition=report.threeInnerFaces=report.bothTeamColors=true;
                var vehicle=gameVehicles[game.controlled];UpdateGameCamera();
                DeveloperChecks.Require(Vector3.Distance(viewCamera.transform.position,vehicle.unit.robot.groups[3].TransformPoint(Vt03Point))<.0001f,"C相机挂点被改变");
                DeveloperChecks.Require(vehicle.unit.root.activeInHierarchy&&vehicle.unit.renderers.Length>0,"控制车体被隐藏");report.cameraBodyRetained=true;
            }
            catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
            if(string.IsNullOrEmpty(report.error))
            {
                foreach(string team in new[]{"red","blue"})
                {
                    var node=buildingNodes[team+"_base"];var building=game.buildings[team+"_base"];game.buildings[team+"_outpost"].hp=0;
                    foreach(bool open in new[]{false,true})
                    {
                        building.armorOpen=building.armorTarget=open?1:0;UpdateGameMechanisms();CollectGamePlates();
                        for(int i=0;i<3;i++)
                        {
                            var panel=gameBase.panels[i+3];Vector3 face=node.TransformPoint(PassagePose.Vector(panel.center)),normal=node.TransformDirection(PassagePose.Vector(panel.normal));
                            var plate=gamePlates.Find(p=>p.target==team+"_base"&&p.index==i+3);
                            if(open&&FindInnerArmorView(plate,out var eye))viewCamera.transform.position=eye;else viewCamera.transform.position=face+Quaternion.AngleAxis(60,Vector3.up)*normal*1.35f;
                            viewCamera.transform.LookAt(node.position+Vector3.up*.52f);
                            yield return new WaitForEndOfFrame();CaptureBaseCamera(Path.Combine(output,team+"-"+(open?"open":"closed")+"-face"+i+".png"));yield return null;
                        }
                    }
                    var lens=guideLenses[team+"_base"];viewCamera.transform.position=lens.position+lens.forward*.55f;viewCamera.transform.LookAt(lens.position);
                    yield return new WaitForEndOfFrame();CaptureBaseCamera(Path.Combine(output,team+"-upper-guide.png"));yield return null;
                }
                report.passed=true;
            }
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("BASE_VISUAL_SMOKE_"+(report.passed?"OK":"FAILED"));Application.Quit(report.passed?0:72);
        }
        void CaptureBaseCamera(string path)
        {
            // Native scene render without the driver HUD over the armor under
            // review. Do not alter production UI visibility or camera controls.
            var previousTarget=viewCamera.targetTexture;var previousActive=RenderTexture.active;
            var target=RenderTexture.GetTemporary(Screen.width,Screen.height,24);Texture2D pixels=null;
            try
            {
                viewCamera.targetTexture=target;viewCamera.Render();RenderTexture.active=target;
                pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                viewCamera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                if(pixels)Destroy(pixels);RenderTexture.ReleaseTemporary(target);
            }
        }
        bool FindInnerArmorView(DeveloperPlate plate,out Vector3 eye)
        {
            foreach(float distance in new[]{1.35f,.9f,1.8f})foreach(int angle in new[]{0,30,-30,45,-45,60,-60,70,-70})
            {
                eye=plate.center+Quaternion.AngleAxis(angle,Vector3.up)*plate.Normal*distance;
                viewCamera.transform.position=eye;viewCamera.transform.LookAt(plate.center);
                if(GamePlateVisible(plate))return true;
            }
            eye=Vector3.zero;return false;
        }
    }
}
