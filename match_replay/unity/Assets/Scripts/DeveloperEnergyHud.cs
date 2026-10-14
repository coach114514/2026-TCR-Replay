using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        void DrawGameEnergyBar(DeveloperActor actor)
        {
            float min=.5f*game.rules.capacitance*game.rules.capMinVoltage*game.rules.capMinVoltage,max=.5f*game.rules.capacitance*game.rules.capMaxVoltage*game.rules.capMaxVoltage;
            float ratio=actor.capEnabled?Mathf.Clamp01((actor.capEnergy-min)/Mathf.Max(1,max-min)):0;
            string state=!actor.capEnabled?"停用":actor.capProtected?"欠压保护":actor.capPower>.1f?"放电":actor.capChargePower>.1f?"充电":"待机";
            var color=GUI.color;GUI.color=new Color(.07f,.1f,.13f);GUI.DrawTexture(new Rect(30,804,980,20),Texture2D.whiteTexture);
            GUI.color=actor.capProtected?new Color(.8f,.18f,.1f):actor.capPower>.1f?new Color(.85f,.5f,.1f):new Color(.08f,.55f,.34f);GUI.DrawTexture(new Rect(30,804,980*ratio,20),Texture2D.whiteTexture);GUI.color=color;
            Text(36,802,970,25,$"超级电容 {ratio*100:0}% · {actor.capVoltage:0.0}V / {actor.capEnergy:0}J · {state} · 充{actor.capChargePower:0}W / 放{actor.capPower:0}W · Chassis输入{actor.chassisInputPower:0}W",small);
            Text(30,829,980,23,$"底盘能量 {actor.chassisEnergy:0}/40000 · 裁判缓冲 {actor.buffer:0.0}J · 电容参考：7串60F / 150W（可控制台修改）",small);
        }
    }
}
