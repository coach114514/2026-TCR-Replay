using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TcrReplay
{
    public sealed class ReplaySearchIndex
    {
        readonly MatchCatalog catalog;
        readonly string[] text;
        readonly string[][] words;
        readonly Dictionary<string,string[]> aliases=new Dictionary<string,string[]>();
        public ReplaySearchIndex(MatchCatalog data)
        {
            catalog=data;text=new string[data.matches.Length];words=new string[data.matches.Length][];
            if(data.schools!=null)foreach(var school in data.schools)aliases[school.name]=school.aliases??new[]{school.name};
            for(int i=0;i<data.matches.Length;i++)
            {
                var m=data.matches[i];
                text[i]=Normalize(m.region+" "+m.startTime+" "+m.matchNumber+" "+m.round+" "+m.gameId+" "+m.webGameId+" "+m.ruleVersion+" "+
                    string.Join(" ",Names(m.redSchool))+" "+string.Join(" ",Names(m.blueSchool)));
                words[i]=text[i].Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            }
        }
        string[] Names(string school)=>aliases.TryGetValue(school,out var names)?names:new[]{school};
        public static string Normalize(string value)=>(value??"").Trim().ToLowerInvariant();
        public string ResolveSchool(string query)
        {
            query=Normalize(query);if(query.Length==0)return "";
            foreach(var school in aliases)if(Normalize(school.Key)==query)return school.Key;
            var exact=aliases.Where(s=>s.Value.Any(a=>Normalize(a)==query)).Select(s=>s.Key).ToArray();
            if(exact.Length==1)return exact[0];
            var partial=aliases.Where(s=>s.Value.Any(a=>Normalize(a).Contains(query))).Select(s=>s.Key).ToArray();
            return partial.Length==1?partial[0]:"";
        }
        static bool EditOne(string a,string b)
        {
            if(Math.Abs(a.Length-b.Length)>1 || a.Length<4)return false;
            int i=0,j=0,errors=0;
            while(i<a.Length && j<b.Length)
            {
                if(a[i]==b[j]){i++;j++;continue;}
                if(++errors>1)return false;
                if(a.Length>=b.Length)i++;if(b.Length>=a.Length)j++;
            }
            return errors+(a.Length-i)+(b.Length-j)<=1;
        }
        bool Token(int i,string token)
        {
            // Short initial aliases are whole words: "jg" must not match "yjgldx".
            if(token.Length<=3 && token.All(c=>c>='a' && c<='z'))return words[i].Contains(token);
            return text[i].Contains(token) || words[i].Any(word=>EditOne(token,word));
        }
        public List<int> Query(string query,Func<MatchEntry,bool> filter,int sort)
        {
            var tokens=Normalize(query).Split(new[]{' ','\t','，',','},StringSplitOptions.RemoveEmptyEntries);
            var result=new List<int>();
            for(int i=0;i<text.Length;i++)if(tokens.All(t=>Token(i,t)) && filter(catalog.matches[i]))result.Add(i);
            result.Sort((a,b)=>
            {
                var x=catalog.matches[a];var y=catalog.matches[b];int order=0;
                if(sort==0)
                {
                    int sx=Score(x,tokens),sy=Score(y,tokens);order=sy.CompareTo(sx);
                }
                else if(sort==1)order=string.CompareOrdinal(y.startTime,x.startTime);
                else if(sort==2)order=y.duration.CompareTo(x.duration);
                else if(sort==3)order=Metric(x,"minBase").CompareTo(Metric(y,"minBase"));
                else if(sort==4)order=Metric(y,"shotPeak").CompareTo(Metric(x,"shotPeak"));
                return order!=0?order:a.CompareTo(b);
            });return result;
        }
        int Score(MatchEntry m,string[] tokens)
        {
            int score=0;
            foreach(var t in tokens)
                foreach(var school in new[]{m.redSchool,m.blueSchool})
                    score+=Normalize(school)==t?100:Names(school).Any(a=>Normalize(a)==t)?40:Normalize(school).Contains(t)?20:0;
            return score;
        }
        static float Metric(MatchEntry m,string key)=>m.metrics==null?0:key=="minBase"?m.metrics.Where(x=>x.minBase>=0).Select(x=>x.minBase).DefaultIfEmpty(float.MaxValue).Min():m.metrics.Select(x=>x.shotPeak).DefaultIfEmpty(0).Max();
    }

    public partial class ReplayViewer
    {
        ReplaySearchIndex searchIndex;
        readonly Dictionary<int,MatchData> libraryCache=new Dictionary<int,MatchData>();
        readonly LinkedList<int> libraryLru=new LinkedList<int>();
        readonly Dictionary<string,int> tagFilters=new Dictionary<string,int>();
        CancellationTokenSource loadCancellation;
        int loadGeneration;bool loading;
        string loadingName="",searchQuery="",schoolQuery="",dateFrom="",dateTo="",durationFrom="",durationTo="",metricFrom="",metricTo="";
        int regionFilter,winnerFilter,sideFilter,searchSort,metricFilter,qualityFilter;
        bool tagsAny,advancedTags,bySeries;
        Vector2 tagScroll,bookmarkScroll;
        List<int> searchResults=new List<int>(),displayResults=new List<int>();
        string searchKey="",resolvedFocusSchool="";double lastSearchMs;int previewMatch=-1;
        static readonly string[] RegionNames={"全部赛区","南部赛区","东部赛区","北部赛区"};
        static readonly string[] SortNames={"相关度","日期：新到旧","时长：长到短","基地最低血量","发弹峰值"};
        static readonly string[] MetricNames={"无数值条件","基地最低血量","破前哨时刻","17mm发弹量","42mm发弹量","10秒发弹峰值","小机关次数","大机关次数","飞镖命中次数","战亡次数","复活次数","累计金币","末段剩余金币","撞击扣血/分"};
        string FocusSchool=>resolvedFocusSchool;
        string FocusTeam(MatchEntry m)=>FocusSchool==m.redSchool?"red":FocusSchool==m.blueSchool?"blue":"";
        string FilterTeam(MatchEntry m)=>sideFilter==1?"red":sideFilter==2?"blue":FocusTeam(m);
        void PrepareLibrary() {searchIndex=new ReplaySearchIndex(catalog);RefreshSearch(true);}
        MatchData CachedMatch(int index)
        {
            if(libraryCache.TryGetValue(index,out var data)) {libraryLru.Remove(index);libraryLru.AddLast(index);return data;}
            data=Timeline.ReadMatch(Path.Combine(folder,catalog.matches[index].file));CacheMatch(index,data);return data;
        }
        void CacheMatch(int index,MatchData data)
        {
            libraryCache[index]=data;libraryLru.Remove(index);libraryLru.AddLast(index);
            while(libraryLru.Count>3){int evicted=libraryLru.First.Value;libraryLru.RemoveFirst();libraryCache.Remove(evicted);}
        }
        void RequestMatch(int index,float time=0)
        {
            loadCancellation?.Cancel();loadCancellation?.Dispose();loadCancellation=new CancellationTokenSource();
            int generation=++loadGeneration;playing=false;loading=true;ReleaseMouse();
            loadingName=catalog.matches[index].redSchool+" / "+catalog.matches[index].blueSchool;
            StartCoroutine(LoadMatchAsync(index,time,generation,loadCancellation.Token));
        }
        IEnumerator LoadMatchAsync(int index,float time,int generation,CancellationToken cancellation)
        {
            MatchData data=null;Task<MatchData> task=null;
            if(libraryCache.TryGetValue(index,out var cached))data=cached;
            else
            {
                string path=Path.Combine(folder,catalog.matches[index].file);
                task=Task.Run(()=>Timeline.ReadMatch(path,cancellation),cancellation);
                while(!task.IsCompleted){if(generation!=loadGeneration)yield break;yield return null;}
                if(generation!=loadGeneration)yield break;
                if(task.IsCanceled){loading=false;yield break;}
                if(task.IsFaulted){error=task.Exception.GetBaseException().Message;Debug.LogException(task.Exception);loading=false;yield break;}
                data=task.Result;
            }
            if(generation!=loadGeneration)yield break;
            CacheMatch(index,data);InstallMatch(index,data);loading=false;
            if(string.IsNullOrEmpty(error))Seek(time);
        }
        static bool InRange(float value,string minimum,string maximum)
        {
            bool hasMin=float.TryParse(minimum,NumberStyles.Float,CultureInfo.InvariantCulture,out float low);
            bool hasMax=float.TryParse(maximum,NumberStyles.Float,CultureInfo.InvariantCulture,out float high);
            if(!hasMin && !hasMax)return true;
            return value>=0 && (!hasMin || value>=low) && (!hasMax || value<=high);
        }
        float MetricValue(TeamMetrics m)
        {
            switch(metricFilter)
            {
                case 1:return m.minBase;case 2:return m.outpostAt;case 3:return m.shots17;case 4:return m.shots42;case 5:return m.shotPeak;
                case 6:return m.runeSmall;case 7:return m.runeLarge;case 8:return m.darts;case 9:return m.deaths;case 10:return m.respawns;
                case 11:return m.totalCoins;case 12:return m.coins;case 13:return m.collisionPerMinute;default:return 0;
            }
        }
        bool SearchFilter(MatchEntry m)
        {
            if(regionFilter>0 && m.region!=RegionNames[regionFilter])return false;
            string school=ReplaySearchIndex.Normalize(schoolQuery);
            if(school.Length>0)
            {
                bool matches=false;
                if(FocusSchool.Length>0)matches=m.redSchool==FocusSchool || m.blueSchool==FocusSchool;
                else if(catalog.schools!=null)
                {
                    foreach(var s in catalog.schools)
                        if((s.name==m.redSchool || s.name==m.blueSchool) && s.aliases.Any(a=>ReplaySearchIndex.Normalize(a).Contains(school))){matches=true;break;}
                }
                else matches=m.redSchool.Contains(schoolQuery) || m.blueSchool.Contains(schoolQuery);
                if(!matches)return false;
            }
            string team=FilterTeam(m),winner=m.winner=="红"?"red":m.winner=="蓝"?"blue":"";
            if(winnerFilter==1 && winner!="red" || winnerFilter==2 && winner!="blue")return false;
            if(winnerFilter>=3 && (FocusTeam(m)=="" || (winner==FocusTeam(m))!=(winnerFilter==3)))return false;
            string date=(m.startTime??"").Length>=10?m.startTime.Substring(0,10):"";
            if(dateFrom.Length>0 && string.CompareOrdinal(date,dateFrom)<0 || dateTo.Length>0 && string.CompareOrdinal(date,dateTo)>0)return false;
            if(!InRange(m.duration,durationFrom,durationTo))return false;
            var quality=m.quality??Array.Empty<MatchQuality>();
            if(qualityFilter==1 && quality.Any(q=>q.flag=="outside_candidate_field" && q.count>0))return false;
            if(qualityFilter==2 && quality.Any(q=>(q.flag=="missing_xy" || q.flag=="zero_xy" || q.flag=="outside_candidate_field") && q.count>0))return false;
            if(qualityFilter==3 && !quality.Any(q=>q.flag=="outside_candidate_field" && q.count>0))return false;
            if(metricFilter>0 && !(m.metrics??Array.Empty<TeamMetrics>()).Any(v=>(team=="" || v.team==team) && InRange(MetricValue(v),metricFrom,metricTo)))return false;
            bool included=false;int includeCount=0;
            foreach(var selection in tagFilters)
            {
                if(selection.Value==0)continue;
                var relevant=(m.tags??Array.Empty<MatchTag>()).Where(t=>t.id==selection.Key && (team=="" || t.team=="" || t.team==team));
                bool yes=relevant.Any(t=>t.status==1),unknown=!relevant.Any() || relevant.Any(t=>t.status==-1);
                if(selection.Value==-1 && yes)return false;
                if(selection.Value==2 && !unknown)return false;
                if(selection.Value==1)
                {
                    includeCount++;included|=yes;if(!tagsAny && !yes)return false;
                }
            }
            return !tagsAny || includeCount==0 || included;
        }
        void RefreshSearch(bool force=false)
        {
            string key=searchQuery+"|"+schoolQuery+"|"+regionFilter+"|"+winnerFilter+"|"+sideFilter+"|"+searchSort+"|"+dateFrom+"|"+dateTo+"|"+durationFrom+"|"+durationTo+"|"+metricFilter+"|"+metricFrom+"|"+metricTo+"|"+tagsAny+"|"+bySeries+"|"+string.Join(",",tagFilters.Select(p=>p.Key+":"+p.Value));
            key+="|"+qualityFilter;
            if(!force && key==searchKey)return;searchKey=key;
            var watch=System.Diagnostics.Stopwatch.StartNew();resolvedFocusSchool=searchIndex.ResolveSchool(schoolQuery);searchResults=searchIndex.Query(searchQuery,SearchFilter,searchSort);
            displayResults=bySeries?searchResults.GroupBy(i=>catalog.matches[i].seriesKey).Select(g=>g.First()).ToList():new List<int>(searchResults);
            watch.Stop();lastSearchMs=watch.Elapsed.TotalMilliseconds;matchScroll=Vector2.zero;
        }
        string TagName(string id)=>Array.Find(catalog.tagDefinitions??Array.Empty<TagDefinition>(),d=>d.id==id)?.name??id;
        string SummaryTags(MatchEntry m)
        {
            var definitions=catalog.tagDefinitions??Array.Empty<TagDefinition>();
            return string.Join(" · ",(m.tags??Array.Empty<MatchTag>()).Where(t=>t.status==1 && definitions.Any(d=>d.id==t.id && d.@default)).Select(t=>TagName(t.id)).Distinct().Take(5));
        }
        void DrawLibraryChooser()
        {
            RefreshSearch();Panel(new Rect(20,122,1560,650));
            Text(38,132,890,34,$"区域赛对局库 · {searchResults.Count}/{catalog.matches.Length}局 · {catalog.series}场",title);
            if(Button(1360,132,100,32,bySeries?"按场显示":"按局显示"))bySeries=!bySeries;
            if(Button(1470,132,90,32,"关闭"))chooser=false;
            Text(40,176,80,26,"关键词",small);searchQuery=GUI.TextField(new Rect(120,176,980,28),searchQuery);
            if(Button(1110,176,240,28,"排序："+SortNames[searchSort]))searchSort=(searchSort+1)%SortNames.Length;
            if(Button(1360,176,200,28,"清空筛选"))
            {searchQuery=schoolQuery=dateFrom=dateTo=durationFrom=durationTo=metricFrom=metricTo="";regionFilter=winnerFilter=sideFilter=metricFilter=qualityFilter=0;tagFilters.Clear();}
            Text(40,216,350,24,"关注学校（全名、简称或拼音）",small);schoolQuery=GUI.TextField(new Rect(40,242,350,28),schoolQuery);
            Text(40,273,350,24,schoolQuery.Length==0?"标签/数值：任一方":FocusSchool.Length>0?"已匹配："+FocusSchool:"名称有歧义或未匹配，请输入完整名称",small);
            if(Button(40,301,170,28,RegionNames[regionFilter]))regionFilter=(regionFilter+1)%4;
            string[] winNames={"全部胜负","红方胜","蓝方胜","关注学校胜","关注学校负"};
            if(Button(220,301,170,28,winNames[winnerFilter]))winnerFilter=(winnerFilter+1)%5;
            if(Button(40,335,350,28,"标签阵营："+(sideFilter==1?"红方":sideFilter==2?"蓝方":FocusSchool.Length>0?"关注学校":"任一方")))sideFilter=(sideFilter+1)%3;
            Text(40,371,350,22,"日期范围 YYYY-MM-DD",small);
            dateFrom=GUI.TextField(new Rect(40,396,170,26),dateFrom);dateTo=GUI.TextField(new Rect(220,396,170,26),dateTo);
            Text(40,428,350,22,"时长范围（秒，留空不限）",small);
            durationFrom=GUI.TextField(new Rect(40,453,170,26),durationFrom);durationTo=GUI.TextField(new Rect(220,453,170,26),durationTo);
            if(Button(40,486,350,28,MetricNames[metricFilter]))metricFilter=(metricFilter+1)%MetricNames.Length;
            metricFrom=GUI.TextField(new Rect(40,520,170,26),metricFrom);metricTo=GUI.TextField(new Rect(220,520,170,26),metricTo);
            if(Button(40,553,170,28,tagsAny?"标签：任一满足":"标签：全部满足"))tagsAny=!tagsAny;
            if(Button(220,553,170,28,advancedTags?"显示推荐标签":"显示全部标签"))advancedTags=!advancedTags;
            string[] qualityNames={"坐标质量：全部","坐标质量：无越界样本","坐标质量：无缺失/零点/越界","坐标质量：含越界样本"};
            if(Button(40,586,350,22,qualityNames[qualityFilter]))qualityFilter=(qualityFilter+1)%4;
            var tagDefs=(catalog.tagDefinitions??Array.Empty<TagDefinition>()).Where(d=>advancedTags || d.@default).ToArray();
            tagScroll=GUI.BeginScrollView(new Rect(40,611,350,142),tagScroll,new Rect(0,0,325,tagDefs.Length*27));
            int tagStart=Mathf.Max(0,Mathf.FloorToInt(tagScroll.y/27)),tagEnd=Mathf.Min(tagDefs.Length,tagStart+8);
            for(int i=tagStart;i<tagEnd;i++)
            {
                var d=tagDefs[i];int value=tagFilters.TryGetValue(d.id,out var s)?s:0;
                string prefix=value==1?"[有] ":value==-1?"[排除] ":value==2?"[未知] ":"[ ] ";
                if(GUI.Button(new Rect(0,i*27,322,25),prefix+d.id+" "+d.name,small))tagFilters[d.id]=value==0?1:value==1?-1:value==-1?2:0;
            }
            GUI.EndScrollView();
            int preview=previewMatch>=0?previewMatch:matchIndex;
            float rowHeight=74;var area=new Rect(414,216,1144,334);
            matchScroll=GUI.BeginScrollView(area,matchScroll,new Rect(0,0,1115,Mathf.Max(area.height,displayResults.Count*rowHeight)));
            int start=Mathf.Max(0,Mathf.FloorToInt(matchScroll.y/rowHeight)),end=Mathf.Min(displayResults.Count,start+7);
            for(int k=start;k<end;k++)
            {
                int index=displayResults[k];var m=catalog.matches[index];
                int rounds=bySeries?searchResults.Count(i=>catalog.matches[i].seriesKey==m.seriesKey):1;
                string result=$"{(index==matchIndex?"▶ ":"")}{m.region} · 场次{m.matchNumber} · 第{m.round}局{(bySeries?$"（匹配{rounds}局）":"")} · {Clock(m.duration)} · {m.startTime}\n红 {m.redSchool} / 蓝 {m.blueSchool} · {m.winner}方胜\n{SummaryTags(m)}";
                if(GUI.Button(new Rect(0,k*rowHeight,930,70),result,button))previewMatch=index;
                if(GUI.Button(new Rect(940,k*rowHeight+5,164,60),"载入回放",button))RequestMatch(index);
            }
            GUI.EndScrollView();
            var chosen=catalog.matches[preview];
            Text(414,560,1140,26,$"详情：{chosen.redSchool} / {chosen.blueSchool} · {chosen.region} 场次{chosen.matchNumber} 第{chosen.round}局",label);
            Text(414,590,bySeries?540:1140,25,"结束原因未知 · 基地护甲未知 · "+chosen.ruleVersion,small);
            if(bySeries)
            {
                var rounds=searchResults.Where(i=>catalog.matches[i].seriesKey==chosen.seriesKey).OrderBy(i=>catalog.matches[i].round).ToArray();
                for(int r=0;r<rounds.Length && r<4;r++)
                    if(Button(970+r*145,587,140,28,"第"+catalog.matches[rounds[r]].round+"局 · "+catalog.matches[rounds[r]].winner+"胜"))previewMatch=rounds[r];
            }
            var points=chosen.bookmarks??Array.Empty<MatchBookmark>();
            bookmarkScroll=GUI.BeginScrollView(new Rect(414,619,1144,98),bookmarkScroll,new Rect(0,0,1108,Mathf.Max(98,points.Length*27)));
            int firstPoint=Mathf.Max(0,Mathf.FloorToInt(bookmarkScroll.y/27));
            for(int j=firstPoint;j<Mathf.Min(points.Length,firstPoint+6);j++)
            {
                var p=points[j];
                if(GUI.Button(new Rect(0,j*27,1102,25),$"{Clock(p.t)} {(p.team=="red"?"红方":p.team=="blue"?"蓝方":"场地")} · {p.name}  → 事件前5秒",small))RequestMatch(preview,Mathf.Max(0,p.t-5));
            }
            GUI.EndScrollView();
            Text(414,725,1136,28,$"查询 {lastSearchMs:0.0}ms · 标签轮换：不限/有/排除/未知 · 单击比赛看事件，右侧载入回放",small);
            if(loading){Panel(new Rect(550,390,620,80));Text(570,410,580,55,"正在载入："+loadingName,label);}
        }
    }
}
