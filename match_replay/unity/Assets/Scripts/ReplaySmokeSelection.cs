using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        IEnumerable<int> SmokeGameIndices(string[] args)
        {
            if(Array.IndexOf(args,"--sample-games")<0)return Enumerable.Range(0,catalog.matches.Length);
            var indices=new HashSet<int>();
            var backup=JsonUtility.FromJson<MatchCatalog>(File.ReadAllText(Path.Combine(folder,"catalog-13-backup.json")));
            foreach(var m in backup.matches)
            {
                int i=Array.FindIndex(catalog.matches,e=>e.gameId==m.gameId);if(i>=0)indices.Add(i);
            }
            foreach(string id in new[]{"T07","T13","T19","T20","T31","T41"})
            {
                int i=Array.FindIndex(catalog.matches,m=>m.tags.Any(t=>t.id==id && t.status==1));if(i>=0)indices.Add(i);
            }
            return indices.OrderBy(i=>i).ToArray();
        }
    }
}
