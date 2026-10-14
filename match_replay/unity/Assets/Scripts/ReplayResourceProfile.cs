using System;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public sealed class ReplayResourceProfile
    {
        public int schema=1;
        public string kind="external-full",notice="";
        public bool SyntheticDemo=>kind=="synthetic-demo";
        public static ReplayResourceProfile Load(string folder)
        {
            var path=Path.Combine(folder,"resource_profile.json");
            if(!File.Exists(path))return new ReplayResourceProfile();
            var profile=JsonUtility.FromJson<ReplayResourceProfile>(File.ReadAllText(path));
            if(profile==null||profile.schema!=1||string.IsNullOrEmpty(profile.kind))throw new InvalidDataException("资源配置版本无效");
            return profile;
        }
    }
    public partial class ReplayViewer
    {
        ReplayResourceProfile resourceProfile=new ReplayResourceProfile();
        bool DeveloperResourcesAvailable=>!resourceProfile.SyntheticDemo&&robotModels.ContainsKey("infantry3")&&
            File.Exists(Path.Combine(folder,"assets/developer/robots/sweeper/rig.json"));
    }
}
