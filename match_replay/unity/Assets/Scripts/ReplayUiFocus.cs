using System.Collections.Generic;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        // Every popup uses the same owner, including controls that read raw IMGUI
        // events. A boolean property cannot accidentally leave two panels open.
        enum UiPanel { None, Matches, Landmarks, Display, Editor, Map, Console, LidarSettings, LidarView }
        UiPanel uiPanel, uiDrawPanel;
        int uiTransitionFrame=-10;
        bool uiInGui, uiResetFocus, uiWaitMouseRelease;
        readonly HashSet<KeyCode> uiDismissedKeys=new HashSet<KeyCode>();
        static readonly KeyCode[] UiDismissKeys={KeyCode.Escape,KeyCode.F8,KeyCode.F6,KeyCode.Tab,KeyCode.BackQuote};
        bool chooser { get=>uiPanel==UiPanel.Matches; set=>SetUiPanel(UiPanel.Matches,value); }
        bool landmarkChooser { get=>uiPanel==UiPanel.Landmarks; set=>SetUiPanel(UiPanel.Landmarks,value); }
        bool displayChooser { get=>uiPanel==UiPanel.Display; set=>SetUiPanel(UiPanel.Display,value); }
        bool gameEdit { get=>uiPanel==UiPanel.Editor; set=>SetUiPanel(UiPanel.Editor,value); }
        bool gameMap { get=>uiPanel==UiPanel.Map; set=>SetUiPanel(UiPanel.Map,value); }
        bool gameConsole { get=>uiPanel==UiPanel.Console; set=>SetUiPanel(UiPanel.Console,value); }
        bool lidarChooser { get=>uiPanel==UiPanel.LidarSettings; set=>SetUiPanel(UiPanel.LidarSettings,value); }
        UiPanel ActiveUiPanel=>uiPanel!=UiPanel.None?uiPanel:
            appMode!="menu"&&lidarEnabled&&lidarSettings.viewMode==1?UiPanel.LidarView:UiPanel.None;
        bool UiHasModal=>ActiveUiPanel!=UiPanel.None;
        bool UiTransitionBlocked=>Time.frameCount<=uiTransitionFrame+1||uiWaitMouseRelease;
        bool BackgroundInputAllowed=>Application.isFocused&&!UiHasModal&&!UiTransitionBlocked;
        bool UiLayerInteractive=>uiDrawPanel==ActiveUiPanel&&!UiTransitionBlocked;
        static bool UiPanelCloseKey(UiPanel panel,KeyCode key)=>key==KeyCode.Escape||
            panel==UiPanel.Display&&key==KeyCode.F8||panel==UiPanel.Editor&&key==KeyCode.F6||
            panel==UiPanel.Map&&key==KeyCode.Tab||panel==UiPanel.Console&&key==KeyCode.BackQuote;

        void SetUiPanel(UiPanel panel,bool open)
        {
            if(!open&&uiPanel!=panel)return;
            UiPanel next=open?panel:UiPanel.None;
            if(next==uiPanel)return;
            uiPanel=next;
            UiFocusChanged();
            consoleFocus=next==UiPanel.Console;
        }
        void UiFocusChanged()
        {
            ReleaseMouse();uiResetFocus=true;uiTransitionFrame=Time.frameCount;
            uiWaitMouseRelease=Input.GetMouseButton(0)||Input.GetMouseButton(1)||Input.GetMouseButton(2);
            mapDraggingPoint=-1;mapDrag=Vector2.zero;
            gameInput.x=gameInput.z=0;gameInput.fire=gameInput.spin=gameInput.burst=false;
            // If a button opens/closes/replaces a panel mid-pass, no remaining
            // control in that pass may react to the same event.
            if(uiInGui){GUI.enabled=false;ConsumeUiEvent();}
        }
        void ClearUiPanels()
        {
            if(uiPanel!=UiPanel.None){uiPanel=UiPanel.None;UiFocusChanged();}
            consoleFocus=false;
        }
        void CloseActiveUiPanel()
        {
            if(uiPanel!=UiPanel.None){uiPanel=UiPanel.None;UiFocusChanged();consoleFocus=false;}
            else if(ActiveUiPanel==UiPanel.LidarView){lidarSettings.viewMode=0;UiFocusChanged();}
            else {ReleaseMouse();UiFocusChanged();}
        }
        void DismissUiKey(KeyCode key)
        {
            // Legacy Input and IMGUI may report the same physical press several
            // frames apart. Hold the dismissal token until its key-up event.
            if(uiDismissedKeys.Add(key))CloseActiveUiPanel();
            if(uiInGui)ConsumeUiEvent();
        }
        void UpdateUiShortcuts()
        {
            foreach(var key in UiDismissKeys)if(Input.GetKeyUp(key))uiDismissedKeys.Remove(key);
            if(uiWaitMouseRelease&&!Input.GetMouseButton(0)&&!Input.GetMouseButton(1)&&!Input.GetMouseButton(2))uiWaitMouseRelease=false;
            if(!Application.isFocused)return;
            if(Input.GetKeyDown(KeyCode.Escape)){DismissUiKey(KeyCode.Escape);return;}
            if(UiTransitionBlocked)return;
            var active=ActiveUiPanel;
            // Only the current panel's own toggle can close it. Console Tab and
            // arrows remain text input; other panels cannot steal their focus.
            if(active!=UiPanel.None)
            {
                foreach(var key in UiDismissKeys)if(UiPanelCloseKey(active,key)&&Input.GetKeyDown(key)){DismissUiKey(key);break;}
                return;
            }
            if(Input.GetKeyDown(KeyCode.F8)){uiDismissedKeys.Add(KeyCode.F8);displayChooser=true;return;}
            if(appMode=="game")
            {
                if(Input.GetKeyDown(KeyCode.F6)){uiDismissedKeys.Add(KeyCode.F6);gameEdit=true;return;}
                if(Input.GetKeyDown(KeyCode.Tab)){uiDismissedKeys.Add(KeyCode.Tab);gameMap=true;return;}
                if(Input.GetKeyDown(KeyCode.BackQuote)){uiDismissedKeys.Add(KeyCode.BackQuote);gameConsole=true;return;}
            }
            if(Input.GetKeyDown(KeyCode.F11)||(Input.GetKeyDown(KeyCode.Return)&&(Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt))))ToggleFullscreen();
        }
        static bool IsUiInputEvent(Event e)=>e!=null&&(e.isMouse||e.isKey||e.type==EventType.ScrollWheel);
        void ConsumeUiEvent(){if(IsUiInputEvent(Event.current))Event.current.Use();}
        Rect UiPanelBounds(UiPanel panel)
        {
            switch(panel)
            {
                case UiPanel.Matches:return new Rect(20,122,1560,650);
                case UiPanel.Landmarks:return new Rect(20,262,820,338);
                case UiPanel.Display:return new Rect(280,205,1040,520);
                case UiPanel.Editor:return new Rect(930,142,650,592);
                case UiPanel.Map:return new Rect(80,146,1435,575);
                case UiPanel.Console:return new Rect(30,146,1538,580);
                case UiPanel.LidarSettings:return LidarPanel;
                case UiPanel.LidarView:return LidarWindow;
                default:return new Rect();
            }
        }

        void OnGUI()
        {
            Styles();var matrix=GUI.matrix;bool enabled=GUI.enabled;
            uiInGui=true;uiDrawPanel=UiPanel.None;
            // IMGUI text editing can consume a key before legacy Input reports
            // GetKeyDown. Route popup dismissal here as well, before text fields.
            if(Event.current.type==EventType.KeyUp)uiDismissedKeys.Remove(Event.current.keyCode);
            if(Event.current.type==EventType.KeyDown&&uiDismissedKeys.Contains(Event.current.keyCode))ConsumeUiEvent();
            else if(UiHasModal&&!UiTransitionBlocked&&Event.current.type==EventType.KeyDown&&UiPanelCloseKey(ActiveUiPanel,Event.current.keyCode))DismissUiKey(Event.current.keyCode);
            var original=new Event(Event.current);var originalOwner=ActiveUiPanel;
            try
            {
                if(uiResetFocus){GUIUtility.keyboardControl=0;GUIUtility.hotControl=0;uiResetFocus=false;}
                GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
                GUI.enabled=enabled&&!mouseCaptured&&BackgroundInputAllowed;
                // GUI.enabled alone does not protect scroll views or code that
                // reads Event.current. Hide input events from the entire backdrop.
                bool mask=IsUiInputEvent(original)&&(originalOwner!=UiPanel.None||UiTransitionBlocked);
                if(mask)Event.current=new Event(original){type=EventType.Ignore};
                if(appMode=="replay")DrawReplayGui();else DrawApplicationGui();
                if(mask)Event.current=new Event(original);
                uiDrawPanel=ActiveUiPanel;
                if(uiDrawPanel!=UiPanel.None)
                {
                    GUI.enabled=false;
                    var color=GUI.color;GUI.color=new Color(0,0,0,.38f);GUI.DrawTexture(new Rect(0,0,1600,900),Texture2D.whiteTexture);GUI.color=color;
                    GUI.enabled=enabled&&UiLayerInteractive;
                    // An outside click is inert, including IMGUI's implicit
                    // text-field blur. An owned slider may still finish its drag.
                    var e=Event.current;
                    if(GUIUtility.hotControl==0&&!UiPanelBounds(uiDrawPanel).Contains(e.mousePosition)&&
                       (e.type==EventType.MouseDown||e.type==EventType.ScrollWheel||e.type==EventType.MouseDrag))ConsumeUiEvent();
                    // Exactly one popup is drawn, last, and owns the whole event.
                    switch(uiDrawPanel)
                    {
                        case UiPanel.Matches:DrawChooser();break;
                        case UiPanel.Landmarks:DrawLandmarkChooser();break;
                        case UiPanel.Display:DrawDisplaySettings();break;
                        case UiPanel.Editor:DrawGameEditor();break;
                        case UiPanel.Map:DrawGameMap();break;
                        case UiPanel.Console:DrawGameConsole();break;
                        case UiPanel.LidarSettings:DrawLidarSettings();break;
                        case UiPanel.LidarView:DrawLidarWindow();break;
                    }
                }
                if(originalOwner!=UiPanel.None||UiHasModal||UiTransitionBlocked)ConsumeUiEvent();
            }
            finally
            {
                if(!string.IsNullOrEmpty(uiProbePath)){uiProbeFocus=GUI.GetNameOfFocusedControl();uiProbeKeyboard=GUIUtility.keyboardControl;uiProbeHotControl=GUIUtility.hotControl;}
                GUI.matrix=matrix;GUI.enabled=enabled;uiInGui=false;uiDrawPanel=UiPanel.None;
            }
        }
    }
}
