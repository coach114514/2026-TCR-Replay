using System;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public sealed class VehicleRigNode
    {
        public string id;public int module;public float[] pivot;public string[] meshes,collisionMeshes;
    }
    [Serializable] public sealed class VehicleRigWheel
    {
        public string id,label,kind,travelProvenance;
        public float[] center,pivot,axis,axle,rollingDirection,tractionDirection,damperFixed,damperMoving;
        public float radius,width,foldAngle,minAngle,maxAngle,minTravel,maxTravel,moduleMass,rotorMass;
        public Vector3 Center=>PassagePose.Vector(center);
        public Vector3 Pivot=>PassagePose.Vector(pivot);
        public Vector3 Axis=>PassagePose.Vector(axis).normalized;
    }
    [Serializable] public sealed class VehicleRig
    {
        public int schema;public string id,units,sourceSha256,parameterProvenance;
        public float mass,bodyMass,defaultFold;public VehicleRigNode[] nodes;public VehicleRigWheel[] wheels;
        public int[] legacyTurretGroups;
        [NonSerialized] public string revisionHash;
        public static VehicleRig Load(string folder,string id)
        {
            string raw=File.ReadAllText(Path.Combine(folder,"assets/developer/robots",id,"rig.json"));var rig=JsonUtility.FromJson<VehicleRig>(raw);
            if(rig==null||rig.schema!=1||rig.id!=id||rig.units!="m kg s degrees"||rig.nodes==null||rig.wheels==null||rig.wheels.Length!=4||!float.IsFinite(rig.mass)||!float.IsFinite(rig.bodyMass))throw new InvalidDataException("车辆机械配置无效："+id);
            float sum=rig.bodyMass;
            foreach(var w in rig.wheels)
            {
                if(w==null||!VectorValid(w.center)||!VectorValid(w.pivot)||!VectorValid(w.axis)||!VectorValid(w.axle)||!VectorValid(w.rollingDirection)||!VectorValid(w.tractionDirection)||
                   !(w.radius>.02f&&w.radius<.3f&&w.width>.005f&&w.width<.2f&&w.moduleMass>.1f&&w.rotorMass>0&&w.rotorMass<=w.moduleMass)||w.Center.magnitude>2||w.Pivot.magnitude>2||w.Axis.sqrMagnitude<.9f||
                   !(float.IsFinite(w.foldAngle)&&float.IsFinite(w.minAngle)&&float.IsFinite(w.maxAngle)&&float.IsFinite(w.minTravel)&&float.IsFinite(w.maxTravel))||
                   w.kind!="slider"&&w.kind!="hinge")throw new InvalidDataException("轮组参数无效："+w?.id);
                sum+=w.moduleMass;
            }
            if(Mathf.Abs(sum-rig.mass)>.001f||rig.bodyMass<=0)throw new InvalidDataException("轮组质量不守恒："+id);
            using(var sha=System.Security.Cryptography.SHA256.Create())rig.revisionHash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw))).Replace("-","").ToLowerInvariant();return rig;
        }
        static bool VectorValid(float[] v)=>v?.Length==3&&float.IsFinite(v[0])&&float.IsFinite(v[1])&&float.IsFinite(v[2]);
    }
    [Serializable] public sealed class VehiclePhysicsState
    {
        public int schema=1;public string rigHash;public Quaternion rotation=Quaternion.identity;
        public Vector3 angularVelocity;public VehicleModuleState[] modules;
        public Vector3 driveReference,driftIntegral;public float yawReference,foldReference,torqueScale,holdTime,rideReference,climbCooldown,underbodyHold,rampAssistHold;
    }
    [Serializable] public sealed class VehicleModuleState
    {
        public Vector3 localPosition,velocity,angularVelocity;public Quaternion localRotation=Quaternion.identity;
        public float wheelOmega,wheelAngle,wheelIntegral,suspensionTrim;
    }
}
