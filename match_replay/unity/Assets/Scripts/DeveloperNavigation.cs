using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
namespace TcrReplay
{
    // Incremental A*: node identity includes height, and every edge (including
    // the final approach) uses the same terrain/vehicle envelope as driving.
    public sealed class DeveloperNavigation
    {
        public sealed class Search
        {
            struct Node {public Vector3 position;public int parent;public float cost,score;public bool closed;}
            const float Cell=.35f;
            readonly List<Node> nodes=new List<Node>();readonly Dictionary<Vector3Int,int> indices=new Dictionary<Vector3Int,int>();readonly List<int> open=new List<int>();
            readonly Func<Vector3,Vector3,Vector3?> step;readonly Vector3 goal;readonly int budget;readonly bool fallback;
            int nearest=-1;float nearestDistance=float.MaxValue;
            public bool Done {get;private set;}public bool Temporary {get;private set;}public int Expanded {get;private set;}
            public readonly List<Vector3> Route=new List<Vector3>();
            public Search(Vector3 start,Vector3 goal,Func<Vector3,Vector3,Vector3?> step,int budget=12000,bool fallback=false)
            {this.goal=goal;this.step=step;this.budget=budget;this.fallback=fallback;nodes.Add(new Node {position=start,parent=-1,cost=0,score=Distance(start,goal)});indices[Key(start)]=0;open.Add(0);}
            static float Distance(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
            static Vector3Int Key(Vector3 p)=>new Vector3Int(Mathf.RoundToInt(p.x/Cell),Mathf.RoundToInt(p.y/.04f),Mathf.RoundToInt(p.z/Cell));
            void Finish(int end,Vector3? final=null)
            {Done=true;if(end<0)return;for(int i=end;i>=0;i=nodes[i].parent)Route.Add(nodes[i].position);Route.Reverse();if(final.HasValue&&(Route[Route.Count-1]-final.Value).sqrMagnitude>1e-8f)Route.Add(final.Value);}
            public void Advance(int expansions=32,double milliseconds=3)
            {
                var timer=Stopwatch.StartNew();int count=0;
                while(!Done&&open.Count>0&&nodes.Count<budget&&count++<expansions)
                {
                    int selected=0;for(int i=1;i<open.Count;i++)if(nodes[open[i]].score<nodes[open[selected]].score)selected=i;
                    int index=open[selected];open.RemoveAt(selected);var node=nodes[index];if(node.closed)continue;node.closed=true;nodes[index]=node;Expanded++;
                    float distance=Distance(node.position,goal);
                    if(Mathf.Abs(node.position.y-goal.y)<.065f&&distance<nearestDistance){nearest=index;nearestDistance=distance;}
                    if(distance<Cell*1.5f)
                    {var last=step(node.position,goal);if(last.HasValue&&Mathf.Abs(last.Value.y-goal.y)<.065f){Finish(index,last);return;}}
                    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(x!=0||z!=0)
                    {
                        var desired=node.position+new Vector3(x*Cell,0,z*Cell);var result=step(node.position,desired);if(!result.HasValue)continue;var p=result.Value;var key=Key(p);
                        float cost=node.cost+Vector3.Distance(node.position,p)+Mathf.Abs(p.y-node.position.y)*.5f;
                        if(!indices.TryGetValue(key,out int next)){next=nodes.Count;indices[key]=next;nodes.Add(new Node {position=p,parent=index,cost=cost,score=cost+Distance(p,goal)});open.Add(next);}
                        else if(!nodes[next].closed&&cost<nodes[next].cost){var n=nodes[next];n.parent=index;n.cost=cost;n.score=cost+Distance(p,goal);nodes[next]=n;open.Add(next);}
                    }
                    if(timer.Elapsed.TotalMilliseconds>=milliseconds)break;
                }
                if(open.Count==0||nodes.Count>=budget)
                {Temporary=fallback&&nearest>=0;Finish(Temporary?nearest:-1);}
            }
        }
        public static List<Vector3> Plan(Vector3 start,Vector3 goal,Func<Vector3,Vector3,Vector3?> step,int budget=2600)
        {var search=new Search(start,goal,step,budget);while(!search.Done)search.Advance(256,1000);return search.Route;}
    }
    public partial class ReplayViewer
    {
        class NavigationState
        {public List<Vector3> route=new List<Vector3>();public int index,lastFrame=-1;public Vector3 destination,effective;public float retryAt;public bool unreachable,temporary,fallbackAttempted;public Vector3 originalGoal;public Func<Vector3,Vector3,Vector3?> query;public DeveloperNavigation.Search search;}
        readonly Dictionary<string,NavigationState> navigation=new Dictionary<string,NavigationState>();
        bool NavigationArrived(DeveloperActor a,Vector3 desired)
        {Vector3 goal=desired;if(navigation.TryGetValue(a.id,out var n)){if(n.search!=null||n.unreachable)return false;if(n.temporary)return false;}return new Vector2(a.position.x-goal.x,a.position.z-goal.z).magnitude<.15f;}
        bool NavigationOccupied(GameVehicle v,Vector3 at)
        {foreach(var other in gameVehicles.Values)if(VehicleOverlap(v,at,other,.07f))return true;return false;}
        bool NavigationTurningSpace(GameVehicle v,Vector3 at)
        {
            // A temporary parking goal must stay clear as the approaching
            // chassis turns, rather than only fitting its current heading.
            for(int heading=0;heading<8;heading++)foreach(var other in gameVehicles.Values)
                if(VehicleOverlap(v,at,other,.07f,heading*45,v.unit.robot.model.defaultFold))return false;
            return true;
        }
        Vector3 RouteDirection(DeveloperActor actor,Vector3 desired)
        {
            if(!gameVehicles.TryGetValue(actor.id,out var vehicle))return Vector3.zero;
            if(!navigation.TryGetValue(actor.id,out var state))navigation[actor.id]=state=new NavigationState();
            if((state.destination-desired).sqrMagnitude>.001f){state.route.Clear();state.index=0;state.destination=desired;state.retryAt=0;state.unreachable=false;state.search=null;state.temporary=false;}
            if(state.search!=null)
            {
                if(state.lastFrame!=Time.frameCount){state.lastFrame=Time.frameCount;state.search.Advance(32,2.5);}
                if(state.search.Done){if(state.search.Route.Count==0&&state.temporary&&!state.fallbackAttempted){state.fallbackAttempted=true;state.search=new DeveloperNavigation.Search(actor.position,state.originalGoal,state.query,12000,true);return Vector3.zero;}state.route=state.search.Route;state.temporary=state.temporary||state.search.Temporary;state.index=Mathf.Min(1,state.route.Count-1);state.unreachable=state.route.Count==0;if(state.route.Count>0)state.effective=state.route[state.route.Count-1];state.search=null;state.retryAt=game.time+1;
                    if(state.unreachable)game.Log(actor.id+" 无可通行路线，停车并保留原始目标");else if(state.temporary)game.Log(actor.id+" 目标被占用，暂泊最近同平面可达点 "+state.effective.ToString("F2"));}
                else return Vector3.zero;
            }
            Vector3 goal=desired;goal.y=GameGround(vehicle,new Vector3(goal.x,desired.y<=0?8:desired.y,goal.z))+BallisticMath.Support(vehicle.unit.robot.model,vehicle.unit.robot.model.defaultFold)+.004f;
            bool occupied=NavigationOccupied(vehicle,goal);
            if(state.route.Count>0)
            {
                while(state.index<state.route.Count-1&&new Vector2(actor.position.x-state.route[state.index].x,actor.position.z-state.route[state.index].z).magnitude<.16f)state.index++;
                Vector3 next=state.route[Mathf.Max(0,state.index)],delta=next-actor.position;delta.y=0;
                Vector3 look=actor.position+Vector3.ClampMagnitude(delta,.45f);
                // Validate the planned edge in its travel orientation. Using
                // the current heading here suppresses the steering input that
                // is needed to turn into an otherwise valid edge.
                bool clear=TraverseGame(vehicle,actor.position,look,out _,true,true);
                if(clear&&!(state.temporary&&!occupied)&&!(actor.blocked&&game.time>=state.retryAt))return delta;
                state.route.Clear();state.retryAt=0;
            }
            if(state.unreachable&&game.time<state.retryAt)return Vector3.zero;
            // A short unobstructed segment does not require a global search.
            if(!occupied&&TraverseGame(vehicle,actor.position,goal,out _)){state.unreachable=state.temporary=false;state.effective=goal;return goal-actor.position;}
            if(game.time<state.retryAt)return Vector3.zero;
            state.originalGoal=goal;state.fallbackAttempted=false;
            if(occupied){
                Vector3 nearest=goal;bool found=false;
                for(float radius=.1f;radius<=2.5f&&!found;radius+=.1f)for(int k=0;k<64;k++){float angle=k*Mathf.PI/32;Vector3 p=goal+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                    if(!NavigationTurningSpace(vehicle,p)||p.x<.4f||p.z<.4f||p.x>field.width-.4f||p.z>field.depth-.4f)continue;
                    float y=GameGround(vehicle,p)+BallisticMath.Support(vehicle.unit.robot.model,vehicle.unit.robot.model.defaultFold)+.004f;if(Mathf.Abs(y-goal.y)>.06f)continue;p.y=y;
                    if(!MovementClear(vehicle,p,Vector3.right*.01f,vehicle.unit.robot.model.defaultFold)||!MovementClear(vehicle,p,Vector3.forward*.01f,vehicle.unit.robot.model.defaultFold))continue;nearest=p;found=true;break;}
                if(!found){state.unreachable=true;state.retryAt=game.time+1;return Vector3.zero;}goal=nearest;state.temporary=true;
            }
            state.query=(from,to)=>TraverseGame(vehicle,from,to,out var result,true,true)?result:(Vector3?)null;
            state.search=new DeveloperNavigation.Search(actor.position,goal,state.query,12000,false);
            state.lastFrame=-1;state.unreachable=false;return Vector3.zero;
        }
    }
}
