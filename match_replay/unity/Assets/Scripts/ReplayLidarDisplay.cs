using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        ComputeBuffer lidarBuffer;Material lidarMaterial;Camera lidarCamera;RenderTexture lidarTexture;LineRenderer[] lidarCones;
        Vector3 lidarFocus=new Vector3(14,0,7.5f);float lidarViewYaw=25,lidarViewPitch=40,lidarViewDistance=32;
        static readonly Rect LidarWindow=new Rect(22,280,700,420),LidarImage=new Rect(38,329,668,348),LidarPanel=new Rect(280,100,1040,775);
        void UploadLidarPoints()
        {
            if(lidarPointCount==0)return;
            if(lidarBuffer==null||lidarBuffer.count<lidarPointCount){lidarBuffer?.Release();lidarBuffer=new ComputeBuffer(Mathf.NextPowerOfTwo(lidarPointCount),32);}
            lidarBuffer.SetData(lidarPointArray,0,0,lidarPointCount);
            if(!lidarMaterial)lidarMaterial=new Material(Resources.Load<Material>("ReplayLidar"));
            lidarMaterial.SetBuffer("_Points",lidarBuffer);
        }
        void ReleaseLidarDisplay()
        {
            lidarBuffer?.Release();lidarBuffer=null;
            if(lidarMaterial)Destroy(lidarMaterial);lidarMaterial=null;
            if(lidarCamera)Destroy(lidarCamera.gameObject);lidarCamera=null;
            if(lidarTexture){lidarTexture.Release();Destroy(lidarTexture);}lidarTexture=null;
            if(lidarCones!=null)foreach(var line in lidarCones)if(line)Destroy(line.gameObject);lidarCones=null;
        }
        void DrawLidarCloud()
        {
            if(!lidarEnabled||appMode=="menu")return;
            UpdateLidarCones();if(lidarPointCount==0||lidarBuffer==null||!lidarMaterial)return;
            lidarMaterial.SetFloat("_PointSize",lidarSettings.size);lidarMaterial.SetFloat("_Alpha",lidarSettings.alpha);lidarMaterial.SetFloat("_ColorMode",lidarSettings.colorMode);
            var bounds=new Bounds(new Vector3(field.width*.5f,2,field.depth*.5f),new Vector3(160,160,160));
            if(lidarSettings.viewMode==0)Graphics.DrawProcedural(lidarMaterial,bounds,MeshTopology.Triangles,lidarPointCount*6,1,viewCamera,null,ShadowCastingMode.Off,false,LidarDrawLayer);
            else
            {
                if(!lidarCamera)
                {
                    lidarCamera=new GameObject("Independent point cloud inspection camera").AddComponent<Camera>();lidarCamera.enabled=false;lidarCamera.cullingMask=1<<LidarDrawLayer;
                    lidarCamera.clearFlags=CameraClearFlags.SolidColor;lidarCamera.backgroundColor=new Color(.015f,.022f,.033f);lidarCamera.nearClipPlane=.03f;lidarCamera.farClipPlane=150;
                    lidarTexture=new RenderTexture(1002,522,24);lidarTexture.Create();lidarCamera.targetTexture=lidarTexture;
                }
                lidarCamera.transform.position=lidarFocus+Quaternion.Euler(lidarViewPitch,lidarViewYaw,0)*new Vector3(0,0,-lidarViewDistance);lidarCamera.transform.LookAt(lidarFocus);
                Graphics.DrawProcedural(lidarMaterial,bounds,MeshTopology.Triangles,lidarPointCount*6,1,lidarCamera,null,ShadowCastingMode.Off,false,LidarDrawLayer);lidarCamera.Render();
            }
        }
        void UpdateLidarCones()
        {
            if(!lidarSettings.cone){if(lidarCones!=null)foreach(var line in lidarCones)if(line)line.enabled=false;return;}
            if(lidarCones==null)
            {
                lidarCones=new LineRenderer[2];for(int i=0;i<2;i++){var o=new GameObject("Avia optical envelope "+i);o.layer=17;var line=o.AddComponent<LineRenderer>();line.sharedMaterial=SharedPrimitiveMaterial(i==0?red:blue,true);line.widthMultiplier=.025f;lidarCones[i]=line;}
            }
            for(int source=0;source<2;source++)
            {
                var line=lidarCones[source];line.enabled=lidarSettings.source==2||lidarSettings.source==source;if(!line.enabled)continue;
                Vector3 origin=LidarOrigin(source);Quaternion rot=LidarRotation(source);Vector3[] corners=new Vector3[4];for(int i=0;i<4;i++)corners[i]=origin+rot*(Quaternion.Euler((i&1)==0?-38.6f:38.6f,(i&2)==0?-35.2f:35.2f,0)*Vector3.forward)*18;
                line.positionCount=13;line.SetPositions(new[]{origin,corners[0],corners[1],origin,corners[2],corners[3],origin,corners[0],corners[2],corners[3],corners[1],corners[0],origin});
            }
        }
        void DrawLidarGui()
        {
            if(appMode=="menu")return;
            float x=appMode=="game"?938:cleanView?1280:842,y=appMode=="game"?92:cleanView?138:228;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!mouseCaptured;
            float w=appMode=="replay"&&cleanView?145:160;
            if(Button(x,y,w,32,lidarEnabled?"点云：开":"点云：关")){SetLidar(!lidarEnabled);}
            if(Button(x+w+8,y,w,32,"点云设置")){lidarChooser=true;ReleaseMouse();}
            if(lidarEnabled)
            {
                string source=lidarSettings.source==0?"红方":lidarSettings.source==1?"蓝方":"双机位";
                Text(24,appMode=="game"?135:cleanView?100:259,Mathf.Min(1160,appMode=="game"?1300:1180),26,$"Avia模拟点云 · {source} · {lidarValidPoints:N0}点 / 当前显示{lidarPointCount:N0} · 更新{lidarActualHz:0.0}Hz · 帧耗时{lidarScanMs:0.0}ms · {(lidarSettings.quality==100?"全点频":"诊断降采样"+lidarSettings.quality+"%")} · 跳帧{lidarSkipped}",small);
            }
            GUI.enabled=enabled;
        }
        void DrawLidarWindow()
        {
            Panel(LidarWindow);Text(38,291,325,30,"Avia 点云观察 · Esc 返回场景叠加",small);
            if(Button(375,290,105,29,"复位观察")){lidarFocus=new Vector3(field.width*.5f,0,field.depth*.5f);lidarViewYaw=25;lidarViewPitch=40;lidarViewDistance=32;}
            if(Button(490,290,105,29,"点云设置"))lidarChooser=true;
            if(Button(605,290,97,29,"返回场景")){lidarSettings.viewMode=0;UiFocusChanged();}
            if(lidarTexture)GUI.DrawTexture(LidarImage,lidarTexture,ScaleMode.StretchToFill,false);
            Text(38,680,650,22,"数据来自固定雷达机位；观察窗口移动不改变扫描位置",small);
            var e=Event.current;if(!GUI.enabled||!UiLayerInteractive||!LidarImage.Contains(e.mousePosition))return;
            if(e.type==EventType.ScrollWheel){lidarViewDistance=Mathf.Clamp(lidarViewDistance+e.delta.y,1,80);e.Use();}
            if(e.type==EventType.MouseDrag)
            {
                if(e.button==2){Vector3 right=lidarCamera?lidarCamera.transform.right:Vector3.right,up=lidarCamera?lidarCamera.transform.up:Vector3.up;lidarFocus+=(-right*e.delta.x+up*e.delta.y)*lidarViewDistance*.0018f;}
                else {lidarViewYaw+=e.delta.x*.4f;lidarViewPitch=Mathf.Clamp(lidarViewPitch-e.delta.y*.4f,-85,85);}e.Use();
            }
            if(e.type==EventType.MouseDown||e.type==EventType.MouseUp)e.Use();
        }
        float LidarSlider(float x,float y,string name,float value,float min,float max,string format="0.0")
        {Text(x,y,480,24,name+"："+value.ToString(format),small);return GUI.HorizontalSlider(new Rect(x,y+28,470,18),value,min,max);}
        void DrawLidarSettings()
        {
            Panel(LidarPanel);Text(304,160,830,32,"Livox Avia · 本地点云显示设置",title);if(Button(1210,160,86,30,"关闭"))lidarChooser=false;
            Text(304,202,980,25,"非重复扫描近似 · 第一回波 · 70.4° × 77.2° · 帧内姿态快照",small);
            if(Button(304,238,230,34,lidarEnabled?"停止点云扫描":"启用点云扫描"))SetLidar(!lidarEnabled);
            if(Button(548,238,230,34,"清空 / 刷新点云"))InvalidateLidar(LidarResetReason.Cleared);
            for(int i=0;i<3;i++)if(Button(790+i*166,238,156,34,(lidarSettings.source==i?"✓ ":"")+new[]{"红方机位","蓝方机位","双机位"}[i])){lidarSettings.source=i;InvalidateLidar(LidarResetReason.SettingsChanged);}
            if(Button(304,286,230,34,lidarSettings.viewMode==0?"显示：场景叠加":"显示：独立观察窗")){lidarSettings.viewMode=1-lidarSettings.viewMode;ReleaseMouse();}
            if(Button(548,286,230,34,"配色："+new[]{"高度","距离","机位来源"}[lidarSettings.colorMode]))lidarSettings.colorMode=(lidarSettings.colorMode+1)%3;
            if(Button(790,286,230,34,"更新："+lidarSettings.hz+"Hz")){lidarSettings.hz=lidarSettings.hz==10?20:10;InvalidateLidar(LidarResetReason.SettingsChanged);}
            if(Button(1032,286,258,34,"点频："+lidarSettings.quality+"%")){lidarSettings.quality=lidarSettings.quality==100?25:lidarSettings.quality==25?50:100;InvalidateLidar(LidarResetReason.SettingsChanged);}
            float oldRedPitch=lidarSettings.redPitch,oldBluePitch=lidarSettings.bluePitch,oldRedHeight=lidarSettings.redHeight,oldBlueHeight=lidarSettings.blueHeight;
            lidarSettings.redHeight=LidarSlider(304,335,"红方安装高度 m",lidarSettings.redHeight,1,6);lidarSettings.blueHeight=LidarSlider(804,335,"蓝方安装高度 m",lidarSettings.blueHeight,1,6);
            lidarSettings.redPitch=LidarSlider(304,389,"红方下倾 °",lidarSettings.redPitch,-20,65);lidarSettings.bluePitch=LidarSlider(804,389,"蓝方下倾 °",lidarSettings.bluePitch,-20,65);
            float oldRedYaw=lidarSettings.redYaw,oldBlueYaw=lidarSettings.blueYaw;
            lidarSettings.redYaw=LidarSlider(304,443,"红方朝向 °",lidarSettings.redYaw,0,360);lidarSettings.blueYaw=LidarSlider(804,443,"蓝方朝向 °",lidarSettings.blueYaw,0,360);
            lidarSettings.size=LidarSlider(304,497,"点大小 px",lidarSettings.size,1,8);lidarSettings.alpha=LidarSlider(804,497,"透明度",lidarSettings.alpha,.1f,1,"0.00");
            Vector2 oldRedOffset=lidarSettings.redOffset,oldBlueOffset=lidarSettings.blueOffset;
            lidarSettings.redOffset.x=LidarSlider(304,551,"红方平台横向偏移 m",lidarSettings.redOffset.x,-1,1,"0.00");lidarSettings.blueOffset.x=LidarSlider(804,551,"蓝方平台横向偏移 m",lidarSettings.blueOffset.x,-1,1,"0.00");
            lidarSettings.redOffset.y=LidarSlider(304,605,"红方平台纵向偏移 m",lidarSettings.redOffset.y,-1,1,"0.00");lidarSettings.blueOffset.y=LidarSlider(804,605,"蓝方平台纵向偏移 m",lidarSettings.blueOffset.y,-1,1,"0.00");
            float oldAccum=lidarSettings.accumulation;lidarSettings.accumulation=LidarSlider(304,659,"累积 s（0为单帧）",lidarSettings.accumulation,0,1,"0.00");
            if(Button(804,662,470,34,lidarSettings.cone?"视场示意：开":"视场示意：关"))lidarSettings.cone=!lidarSettings.cone;
            if(oldRedOffset!=lidarSettings.redOffset||oldBlueOffset!=lidarSettings.blueOffset||oldRedPitch!=lidarSettings.redPitch||oldBluePitch!=lidarSettings.bluePitch||oldRedHeight!=lidarSettings.redHeight||oldBlueHeight!=lidarSettings.blueHeight||oldRedYaw!=lidarSettings.redYaw||oldBlueYaw!=lidarSettings.blueYaw||oldAccum!=lidarSettings.accumulation)InvalidateLidar(LidarResetReason.SettingsChanged);
            Text(304,717,985,48,"短时累积会显示动态拖影；采用有界点数。安装位置为可编辑示意，已预留进程内单帧接口；录包、导出与外部算法接入暂未提供。",small);
            if(Button(304,805,470,32,"恢复显示默认值")){lidarSettings=new LidarSettings();InvalidateLidar(LidarResetReason.SettingsChanged);}
            if(Button(804,805,470,32,"保存显示偏好")){PlayerPrefs.SetString("replay.lidar",JsonUtility.ToJson(lidarSettings));PlayerPrefs.Save();}
        }
    }
}
