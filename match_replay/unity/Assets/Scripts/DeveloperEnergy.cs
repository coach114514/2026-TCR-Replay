using System;
using UnityEngine;
namespace TcrReplay
{
    public sealed partial class DeveloperWorld
    {
        public float CapOutputLimit(DeveloperActor a)
        {
            a.capEnergy=Mathf.Clamp(a.capEnergy,0,.5f*rules.capacitance*rules.capMaxVoltage*rules.capMaxVoltage);
            a.capVoltage=Mathf.Sqrt(2*a.capEnergy/Mathf.Max(.001f,rules.capacitance));
            if(!a.capEnabled||a.capProtected||a.capVoltage<=rules.capMinVoltage)return 0;
            float available=Mathf.Max(0,a.capEnergy-.5f*rules.capacitance*rules.capMinVoltage*rules.capMinVoltage);
            return Mathf.Min(rules.capMaxPower,a.capVoltage*rules.capInterfaceCurrent*rules.capEfficiency,available*rules.capEfficiency/Step);
        }
        public float DrivePowerBudget(DeveloperActor a,bool burst)
        {
            float basis=PowerBudget(a);
            return burst?basis+CapOutputLimit(a):basis*Mathf.Clamp(rules.ordinaryPowerFraction,.05f,1);
        }
        public void EnergyStep(DeveloperActor a,bool burst,float motorPower)
        {
            var r=rules;float basePower=PowerBudget(a),capLimit=CapOutputLimit(a);
            a.capPower=burst?Mathf.Min(Mathf.Max(0,motorPower-basePower),capLimit):0;
            float driveInput=Mathf.Max(0,motorPower-a.capPower);
            if(r.powerLimiter)driveInput=Mathf.Min(basePower,driveInput);
            a.capEnergy-=a.capPower*Step/Mathf.Max(.05f,r.capEfficiency);
            float wire=a.wirelessCharging?r.wirelessPower:0;
            float batteryCharge=burst?0:Mathf.Max(0,basePower-driveInput);
            float headroom=Mathf.Max(0,.5f*r.capacitance*r.capMaxVoltage*r.capMaxVoltage-a.capEnergy);
            float totalCharge=Mathf.Min(batteryCharge+wire,r.capMaxPower,Mathf.Max(1,a.capVoltage)*r.capInterfaceCurrent,headroom/(Step*Mathf.Max(.05f,r.capEfficiency)));
            // External charging has its own source. Chassis output cannot exceed
            // its referee budget merely because charging is happening.
            float wireUsed=Mathf.Min(wire,totalCharge),batteryUsed=Mathf.Min(batteryCharge,totalCharge-wireUsed);
            a.capChargePower=totalCharge*r.capEfficiency;
            a.capEnergy+=a.capChargePower*Step;a.chassisInputPower=driveInput+batteryUsed;
            a.chassisEnergy=Mathf.Clamp(a.chassisEnergy-a.chassisInputPower*Step+8*Mathf.Max(0,wireUsed*r.capEfficiency-a.chassisInputPower)*Step,0,40000);
            a.capVoltage=Mathf.Sqrt(2*Mathf.Max(0,a.capEnergy)/Mathf.Max(.001f,r.capacitance));
            if(a.capVoltage<=r.capMinVoltage+.001f)
            {
                if(a.capUnderSince<0)a.capUnderSince=time;
                if(time-a.capUnderSince>=r.capUnderDelay)a.capProtected=true;
                a.capRecoverSince=-1;
            }
            else a.capUnderSince=-1;
            if(a.capProtected&&a.capVoltage>=r.capRecoverVoltage)
            {if(a.capRecoverSince<0)a.capRecoverSince=time;if(time-a.capRecoverSince>=r.capRecoverDelay){a.capProtected=false;a.capRecoverSince=-1;}}
            else if(a.capProtected)a.capRecoverSince=-1;
        }
        public void RestoreOutpost(string team,bool full=true)
        {
            var b=buildings[team+"_outpost"];b.hp=full?b.maxHp:750;b.shield=0;b.deadAt=-1;b.rebuildProgress=0;
            if(b.destroyedBefore||time>=180){b.stopped=true;b.angle=0;if(b.rotationStopAt<0)b.rotationStopAt=time;}
            foreach(var key in new System.Collections.Generic.List<string>(lastArmorHit.Keys))if(key.StartsWith(b.id+":",StringComparison.Ordinal))lastArmorHit.Remove(key);
            Log("开发编辑："+team+"前哨恢复 "+b.hp+"HP；"+(b.stopped?"保持停转":"继续正常旋转"));
        }
    }
}
