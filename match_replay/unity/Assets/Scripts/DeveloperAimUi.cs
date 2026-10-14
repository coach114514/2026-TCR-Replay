using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        struct AimCandidate
        {
            public string target;public int plate;public bool rune;public Vector3 center;public Vector2 screen;public float score;
        }
        Rect GameAimRoi=>new Rect(800-game.rules.aimRoiWidth*800,450-game.rules.aimRoiHeight*450,
            game.rules.aimRoiWidth*1600,game.rules.aimRoiHeight*900);
        static readonly Color AimPredictionFill=new Color(.1f,1,.3f,.22f);
        static readonly Color AimPredictionEdge=new Color(.1f,1,.3f,.8f);

        bool AimCandidateAt(string target,int plate,bool rune,Vector3 center,out AimCandidate candidate)
        {
            candidate=default;var p=viewCamera.WorldToViewportPoint(center);
            if(p.z<=.03f)return false;
            Vector2 screen=new Vector2(p.x*1600,(1-p.y)*900);
            if(!GameAimRoi.Contains(screen))return false;
            candidate=new AimCandidate {target=target,plate=plate,rune=rune,center=center,screen=screen,
                score=new Vector2((p.x-.5f)*Screen.width,(p.y-.5f)*Screen.height).sqrMagnitude};return true;
        }
        bool TryGameAimCandidate(out AimCandidate candidate)
        {
            candidate=default;if(game==null||!game.actors.TryGetValue(game.controlled,out var shooter))return false;
            float closest=float.MaxValue;
            foreach(var plate in gamePlates)
            {
                // A rune is acquired at its stationary R mark, never at a
                // rotating target or an estimator's predicted rectangle.
                if(plate.rune||plate.target==shooter.id||plate.team==shooter.team||
                    !AimCandidateAt(plate.target,plate.index,false,plate.center,out var next)||!GamePlateVisible(plate))continue;
                if(next.score<closest){candidate=next;closest=next.score;}
            }
            if(runeRotor&&mechanismConfig.rune.selectionAnchors!=null)
            foreach(var anchor in mechanismConfig.rune.selectionAnchors)
            {
                if(anchor.team!=shooter.team)continue;
                // R marks remain fixed upright on the assembly, outside the rotor.
                Vector3 point=runeRotor.parent.TransformPoint(PassagePose.Vector(anchor.center));
                Vector3 normal=runeRotor.parent.TransformDirection(PassagePose.Vector(anchor.normal));
                var mark=new DeveloperPlate {center=point,rotation=Quaternion.LookRotation(normal,Vector3.up),
                    target="rune_"+anchor.team,team=anchor.team,rune=true,index=-1};
                if(!AimCandidateAt(mark.target,-1,true,point,out var next)||!GamePlateVisible(mark))continue;
                if(next.score<closest){candidate=next;closest=next.score;}
            }
            return closest<float.MaxValue;
        }
        void ToggleGameAim()
        {
            gameAutoAim=!gameAutoAim;gameAimValid=false;ClearRuneAim();
            if(gameAutoAim)AcquireGameTarget();
            else {gameLock="";aimPlate=-1;aimStatus="手动瞄准 · 右键启用自瞄";}
        }
        static void AimHudRect(Rect rect,Color fill,Color edge,float width)
        {
            var old=GUI.color;
            if(fill.a>0){GUI.color=fill;GUI.DrawTexture(rect,Texture2D.whiteTexture);}
            GUI.color=edge;
            GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width,width),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x,rect.yMax-width,rect.width,width),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x,rect.y,width,rect.height),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax-width,rect.y,width,rect.height),Texture2D.whiteTexture);GUI.color=old;
        }
        static void AimHudDashes(Vector2 from,Vector2 to)
        {
            Vector2 delta=to-from;float length=delta.magnitude;if(length<1)return;
            var matrix=GUI.matrix;var color=GUI.color;
            // Rotate in the logical 1600x900 HUD, then apply its screen scale.
            // RotateAroundPivot prepends a pixel-space transform and displaces
            // the endpoints under resized or non-16:9 windows.
            GUI.color=new Color(.7f,1,.8f,.45f);
            GUI.matrix=matrix*Matrix4x4.TRS(from,Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
            for(float at=8;at<length;at+=12)GUI.DrawTexture(new Rect(at,-.5f,Mathf.Min(6,length-at),1),Texture2D.whiteTexture);
            GUI.matrix=matrix;GUI.color=color;
        }
        void DrawGameAimOverlay()
        {
            var roi=GameAimRoi;AimHudRect(roi,Color.clear,new Color(.4f,.9f,.7f,.45f),1);
            Text(roi.x+7,roi.y+4,260,20,"自瞄 ROI · 右键切换",small);
            if(RuneLocked)Text(roi.x+7,roi.y+26,roi.width-14,24,RuneRingHud(),small);
            if(!gameAutoAim&&TryGameAimCandidate(out var candidate))
            {
                if(!aimDashDiagnosticHidden)AimHudDashes(new Vector2(800,450),candidate.screen);
                AimHudRect(new Rect(candidate.screen.x-3,candidate.screen.y-3,6,6),Color.clear,new Color(.7f,1,.8f,.6f),1);
            }
            if(RuneLocked)
            {
                foreach(var prediction in runePredictions)
                {
                    var p=viewCamera.WorldToViewportPoint(prediction.plate.center);if(p.z<=0||p.x<0||p.x>1||p.y<0||p.y>1)continue;
                    float x=p.x*1600,y=(1-p.y)*900;
                    AimHudRect(new Rect(x-10,y-10,20,20),AimPredictionFill,AimPredictionEdge,prediction.index==runeAim.current?2:1);
                    Text(x+13,y-14,165,24,(prediction.order==1?"①":"②")+(runeAim.confirmed.Contains(prediction.index)?" 已命中":prediction.index==runeAim.current?" 击打／补打":" 下一靶"),small);
                }
            }
            else if(gameAutoAim&&gameAimValid)
            {
                var p=viewCamera.WorldToViewportPoint(gameAimPoint);
                if(p.z>0&&p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1)
                    AimHudRect(new Rect(p.x*1600-10,(1-p.y)*900-10,20,20),AimPredictionFill,AimPredictionEdge,1);
            }
        }
    }
}
