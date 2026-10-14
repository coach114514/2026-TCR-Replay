using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class MechanismRuntimeReport {public int outpostPanelChecks,runePanelChecks,sharedRotorChecks,renderedArmChecks,movingOpenShells,horizontalOrbitChecks,centreCoverChecks,frontGuideChecks,movingOutpostShells;public bool deterministic,staticMovingDuplicatesAbsent,oppositeFaceDirections,stageBoundaryContinuous,openShellOwnershipComplete,matchAndFieldSwitchNoDuplicates,centreMarksFixedUpright;public string source="真实对局状态；角度、靶序及共用变速策略为规则示意；中心R固定正向按用户要求";}
        IEnumerator MechanismSmoke(string[] args)
        {
            int arg=Array.IndexOf(args,"--mechanism-smoke");string output=arg+1<args.Length?args[arg+1]:Application.persistentDataPath;
            Directory.CreateDirectory(output);yield return null;showProjectiles=false;allLabels=false;
            var report=new MechanismRuntimeReport();bool failed=false;
            try
            {
                ReplayMechanismChecks.Rules();
                ReplayMechanismChecks.Geometry(mechanismConfig);
                report.movingOpenShells=mechanismConfig.rune.shellPartition.movingFreeShellCount;report.openShellOwnershipComplete=true;
                report.movingOutpostShells=mechanismConfig.outpost.shellPartition.movingFreeShellCount;
                int index=Array.FindIndex(catalog.matches,m=>m.tags.Any(t=>t.id=="T03" && t.status==1));LoadMatch(index);
                LibraryRequire(runeArms.Count==10 && runeArms.Count(a=>a.team=="red")==5 && runeArms.Count(a=>a.team=="blue")==5,"机关没有按同体红蓝各五臂构建");
                var redArm=runeArms.First(a=>a.team=="red");var blueArm=runeArms.First(a=>a.team=="blue");
                Vector3 worldAxis=runeRotor.parent.TransformDirection(PassagePose.Vector(mechanismConfig.rune.axis)).normalized;
                var redStart=RunePanelAt(runeArms.IndexOf(redArm),0);var blueStart=RunePanelAt(runeArms.IndexOf(blueArm),0);
                var redEnd=RunePanelAt(runeArms.IndexOf(redArm),.05f);var blueEnd=RunePanelAt(runeArms.IndexOf(blueArm),.05f);
                float redTurn=Vector3.SignedAngle(Vector3.ProjectOnPlane(redStart.center-runeRotor.position,worldAxis),Vector3.ProjectOnPlane(redEnd.center-runeRotor.position,worldAxis),redStart.rotation*Vector3.forward);
                float blueTurn=Vector3.SignedAngle(Vector3.ProjectOnPlane(blueStart.center-runeRotor.position,worldAxis),Vector3.ProjectOnPlane(blueEnd.center-runeRotor.position,worldAxis),blueStart.rotation*Vector3.forward);
                LibraryRequire(Mathf.Abs(redTurn+blueTurn)<.01f && Mathf.Abs(Mathf.Abs(redTurn)-3)<.01f,"从各自正面看到的红蓝旋向没有相反，或π/3转速不符");report.oppositeFaceDirections=true;
                float before=mechanismTimeline.RuneAngle(179.99f),after=mechanismTimeline.RuneAngle(180.01f);
                LibraryRequire(Mathf.Abs(Mathf.Abs(after-before)-1.2f)<.01f,"大小阶段边界发生跳角");report.stageBoundaryContinuous=true;
                foreach(float t in new[]{0f,1f,5f,30f,179.99f,180f,183f,240f})
                {
                    Seek(t);
                    LibraryRequire(Quaternion.Angle(runeRotor.localRotation,Quaternion.AngleAxis(mechanismTimeline.RuneAngle(t),PassagePose.Vector(mechanismConfig.rune.axis)))<.01f,"公共转子姿态与历史转轴不同步");
                    var hub=runeRotor.parent.Find("Stationary upright R marks and centre covers");
                    LibraryRequire(hub && hub.GetComponent<ReplayMovingCad>()==null && hub.GetComponent<MeshFilter>().sharedMesh==ReadMesh(mechanismConfig.rune.hub) && hub.localPosition.sqrMagnitude<.000001f && Quaternion.Angle(hub.localRotation,Quaternion.identity)<.001f,"R标或中心盖板没有保持固定正向");report.centreCoverChecks++;
                    foreach(var arm in runeArms)
                    {
                        LibraryRequire(arm.targetPattern.transform.parent==runeRotor && arm.successDot.transform.parent==runeRotor,"红蓝靶面被拆成独立旋转轴");report.sharedRotorChecks++;
                        LibraryRequire(arm.body.transform.parent==runeRotor && arm.body.GetComponent<ReplayMovingCad>()!=null && arm.body.GetComponent<MeshFilter>().sharedMesh==ReadMesh(arm.geometry.mesh),"灯臂实体/灰色外壳没有挂入共同旋转渲染网格");report.renderedArmChecks++;
                    }
                    foreach(var unit in units.Where(u=>u.data.role=="outpost"))
                        for(int p=0;p<3;p++)
                        {
                            var box=PanelAt(unit,t,p);var live=unit.armorColliders[p];
                            var initial=PanelAt(unit,0,p);Vector3 pivot=outpostRotors[unit.data.team].position;
                            LibraryRequire(Mathf.Abs(box.center.y-initial.center.y)<.0001f && Mathf.Abs(Vector3.ProjectOnPlane(box.center-pivot,Vector3.up).magnitude-Vector3.ProjectOnPlane(initial.center-pivot,Vector3.up).magnitude)<.0001f,"前哨装甲高度/水平轨道半径随转角变化");report.horizontalOrbitChecks++;
                            LibraryRequire(Vector3.Distance(box.center,live.transform.position)<.001f && Quaternion.Angle(box.rotation,live.transform.rotation)<.01f,"历史前哨装甲与当前碰撞姿态不同步");
                            Vector3 normal=box.rotation*Vector3.forward;
                            LibraryRequire(BallisticMath.SweepBox(box.center+normal*.2f,box.center-normal*.2f,box.center,box.center,box.rotation,box.half,.0085f,out _,out _),"转动后的前哨装甲不能检测高速穿越");
                            report.outpostPanelChecks++;
                        }
                    float expected=mechanismTimeline.RuneAngle(t);Seek(0);Seek(t);
                    LibraryRequire(Mathf.Abs(expected-mechanismTimeline.RuneAngle(t))<.001f,"机关跳转角度不一致");
                }
                var root=fieldRoot.transform.Find("Independent projectile field collision");
                var movingMeshes=terrain.GetComponentsInChildren<ReplayMovingCad>().Select(m=>m.GetComponent<MeshFilter>()?.sharedMesh).Where(m=>m!=null).ToArray();
                LibraryRequire(root!=null && !root.GetComponentsInChildren<MeshCollider>().Any(c=>movingMeshes.Contains(c.sharedMesh)),"活动件进入静态碰撞副本");
                LibraryRequire(root.GetComponentsInChildren<MeshCollider>().Any(c=>c.sharedMesh==ReadMesh(mechanismConfig.rune.hub)),"固定中心R组件没有进入固定场地碰撞");report.centreMarksFixedUpright=true;
                report.staticMovingDuplicatesAbsent=true;report.deterministic=true;
            }
            catch(Exception ex){Debug.LogException(ex);failed=true;}
            if(failed){Application.Quit(61);yield break;}
            foreach(string team in new[]{"red","blue"})
            {
                var post=units.Find(u=>u.data.id==team+"_outpost");Vector3 center=outpostRotors[team].position;
                Vector3 side=team=="red"?Vector3.right:Vector3.left;
                cameraRig.Free(center+side*1.45f+Vector3.up*.10f,Quaternion.LookRotation(-side,Vector3.up));UpdateCamera();
                foreach(float t in new[]{0f,6f,6.5f})
                {
                    Seek(t);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-horizontal-outpost-"+t.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"));yield return null;
                }
            }
            foreach(string team in new[]{"red","blue"})
            {
                int index=Array.FindIndex(catalog.matches,m=>m.tags.Any(t=>t.id=="T12" && t.team==team && t.status==1));LoadMatch(index);
                var entity=match.entities.First(e=>e.id==team+"_outpost");float death=entity.samples.First(s=>s.hasHp && s.hp<=0).t;
                var lens=guideLenses[team+"_base"];Vector3 enemy=team=="red"?Vector3.right:Vector3.left;
                LibraryRequire(Vector3.Dot(lens.forward,enemy)>.95f && lens.forward.y>0,"基地引导灯没有朝向敌方前向斜面");
                Vector3 eye=lens.position+enemy*.8f+Vector3.up*.06f;
                LibraryRequire(!Physics.Linecast(eye,lens.position+lens.forward*.008f,1<<10,QueryTriggerInteraction.Ignore),"基地前向引导灯仍埋在源CAD表面内");
                cameraRig.Free(eye,Quaternion.LookRotation(lens.position-eye,Vector3.up));UpdateCamera();
                foreach(float t in new[]{death-1,death,Mathf.Min(match.duration,death+10)})
                {
                    Seek(t);bool on=mechanismTimeline.GuideLamp(team,true,t,out bool known);
                    LibraryRequire(known && on==(t>=death) && guideMaterials[team+"_base"].GetFloat("_GuideLight")== (on?1:0),"实际前哨死亡没有切换基地灯");report.frontGuideChecks++;
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-front-guide-"+(t<death?"off":t==death?"on":"held")+".png"));yield return null;
                }
                // A wider frontal view verifies placement in the whole base.
                cameraRig.Free(lens.position+enemy*2.2f+Vector3.up*.25f,Quaternion.LookRotation(-enemy,Vector3.up));UpdateCamera();
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,team+"-front-base.png"));yield return null;
            }
            foreach(string code in new[]{"T19","T20"})
            {
                int index=Array.FindIndex(catalog.matches,m=>m.tags.Any(t=>t.id==code && t.status==1));LoadMatch(index);
                string kind=code=="T19"?"small":"large";
                var success=match.mechanisms.runeSuccess.First(s=>s.kind==kind);
                var progress=match.mechanisms.runeProgress.FirstOrDefault(p=>p.kind==kind && p.arms>0 && p.t<=success.t);
                foreach(float t in new[]{progress?.t??success.t,success.t})
                {
                    string team=progress!=null && t<success.t?progress.team:success.team;
                    try
                    {
                        Seek(t);
                        foreach(var arm in runeArms)
                        {
                            var box=RunePanelAt(runeArms.IndexOf(arm),t);
                            LibraryRequire(Vector3.Distance(arm.targetPattern.transform.position,box.center+box.rotation*Vector3.forward*.004f)<.001f,"机关靶面碰撞没有随实际转轴转动");report.runePanelChecks++;
                        }
                        var marker=Array.Find(field.landmarks,m=>m.role=="energy_mechanism");AttachMarker(marker,false);
                        var sample=runeArms.Find(a=>a.team==team);var panel=RunePanelAt(runeArms.IndexOf(sample),t);
                        Vector3 normal=panel.rotation*Vector3.forward;var center=runeRotor.position;
                        cameraRig.Free(center+normal*2.8f,Quaternion.LookRotation(-normal,Vector3.up));UpdateCamera();
                    }
                    catch(Exception ex){Debug.LogException(ex);failed=true;}
                    if(failed){Application.Quit(62);yield break;}
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,kind+"-"+team+"-"+t.ToString("0")+".png"));yield return null;
                }
            }
            // Reproduce the reported second round at 31s from both sides and
            // after an off-axis rotation; check the actual rendered assembly.
            int reproduction=Array.FindIndex(catalog.matches,m=>m.redSchool=="广州航海学院" && m.blueSchool=="上海交通大学" && m.round==2);
            LibraryRequire(reproduction>=0,"截图对局未找到");LoadMatch(reproduction);
            foreach(string team in new[]{"red","blue"})
                foreach(float t in new[]{31f,31.5f})
                {
                    Seek(t);
                    LibraryRequire(fieldRoot.GetComponentsInChildren<Transform>().Count(n=>n.name=="Shared red blue rune rotor")==1,"切换对局后多出机关转子");
                    var hub=runeRotor.parent.Find("Stationary upright R marks and centre covers");
                    LibraryRequire(hub && Quaternion.Angle(hub.localRotation,Quaternion.identity)<.001f && hub.GetComponent<ReplayMovingCad>()==null,"换局或旋转后R标没有保持正向");report.centreCoverChecks++;
                    foreach(var arm in runeArms)
                    {
                        LibraryRequire(runeRotor.GetComponentsInChildren<MeshRenderer>().Count(r=>r.name==arm.geometry.name)==1,"实际渲染层重复创建灯臂");report.renderedArmChecks++;
                        var panel=RunePanelAt(runeArms.IndexOf(arm),t);
                        LibraryRequire(Vector3.Distance(arm.targetPattern.transform.position,panel.center+panel.rotation*Vector3.forward*.004f)<.001f,"修复外壳后历史靶面错位");report.runePanelChecks++;
                    }
                    var sample=runeArms.First(a=>a.team==team);var view=RunePanelAt(runeArms.IndexOf(sample),t);var normal=view.rotation*Vector3.forward;
                    cameraRig.Free(runeRotor.position+normal*3.2f,Quaternion.LookRotation(-normal,Vector3.up));UpdateCamera();
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"round2-"+team+"-"+t.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"));yield return null;
                }
            int originalField=fieldIndex;SwitchField((fieldIndex+1)%fieldCatalog.fields.Length);yield return null;SwitchField(originalField);yield return null;
            LibraryRequire(fieldRoot.GetComponentsInChildren<Transform>().Count(n=>n.name=="Shared red blue rune rotor")==1 && runeArms.Count==10 && fieldRoot.GetComponentsInChildren<Transform>().Count(n=>n.name=="Stationary upright R marks and centre covers")==1,"重新加载场地后机关或固定R标重复");report.matchAndFieldSwitchNoDuplicates=true;
            File.WriteAllText(Path.Combine(output,"mechanism-runtime-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log($"REPLAY_MECHANISM_SMOKE_OK outpostPanels={report.outpostPanelChecks} runePanels={report.runePanelChecks} sharedRotor={report.sharedRotorChecks} oppositeFaces={report.oppositeFaceDirections} stageContinuous={report.stageBoundaryContinuous} movingOpenShells={report.movingOpenShells} renderedArms={report.renderedArmChecks} switchNoDuplicates={report.matchAndFieldSwitchNoDuplicates}");Application.Quit(0);
        }
    }
}
