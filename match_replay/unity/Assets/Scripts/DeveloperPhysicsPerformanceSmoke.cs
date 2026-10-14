using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class PhysicsPerformanceCase
        {
            public int cars,bodies,ticks,collections,scans,points;public bool lidar,passed;
            public float fps,physicsP50,physicsP95,physicsP99,frameP95,backlog,scanHz,seconds,tickRate;
            public long managedDelta,nativeDelta;public string error="";
        }
        [Serializable] class PhysicsPerformanceReport
        {public bool passed;public string gpu;public List<PhysicsPerformanceCase> cases=new List<PhysicsPerformanceCase>();}
        IEnumerator PhysicsPerformanceSmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--physics-performance-smoke")+1];Directory.CreateDirectory(output);
            developerSmokeActive=true;var report=new PhysicsPerformanceReport {gpu=SystemInfo.graphicsDeviceName};
            int previousCap=Application.targetFrameRate,previousSync=QualitySettings.vSyncCount;Application.targetFrameRate=240;QualitySettings.vSyncCount=0;
            string configuredCars=Environment.GetEnvironmentVariable("TCR_PERFORMANCE_CARS");
            int[] carCounts=string.IsNullOrEmpty(configuredCars)?new[]{1,4,8,17}:Array.ConvertAll(configuredCars.Split(','),s=>Mathf.Clamp(int.Parse(s),1,17));
            foreach(int cars in carCounts)foreach(bool scan in new[]{false,true})
            {
                lidarEnabled=false;InvalidateLidar();ResetDeveloper();if(gamePhysics==null)ConfigureGamePhysics();game.Remove("target1");
                for(int i=1;i<cars;i++)game.Add("perf"+i,i%2==0?"red":"blue",new Vector3(4+(i%6)*1.3f,0,4+(i/6)*1.4f),i%2==0?"jiageng":"sweeper");
                foreach(var a in game.actors.Values){a.spin=true;a.spinRate=90;a.motion="direction";a.direction=Vector3.right;a.requestedSpeed=.25f;}
                gameDesiredYaw=90;gameInput=new DeveloperInput {yaw=90,spin=true};game.running=true;gameThirdPerson=true;
                lidarEnabled=scan;InvalidateLidar();
                // Destroy is deferred until the frame ends. Match actual UI resets
                // before advancing new rigid bodies or measuring scan state.
                yield return null;double accumulator=0,last=Time.realtimeSinceStartupAsDouble;float start=Time.realtimeSinceStartup;
                // Warm geometry, LOD and first contacts before measuring.
                while(Time.realtimeSinceStartup-start<3)
                {double now=Time.realtimeSinceStartupAsDouble;accumulator+=now-last;last=now;while(accumulator>=DeveloperWorld.Step){GameStep();accumulator-=DeveloperWorld.Step;}UpdateGameCamera();yield return null;}
                var row=new PhysicsPerformanceCase {cars=cars,bodies=gamePhysics.BodyCount,lidar=scan};report.cases.Add(row);
                long managed=GC.GetTotalMemory(false),native=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();int gc=GC.CollectionCount(0),scans=lidarFrame;
                var costs=new List<float>();var frames=new List<float>();start=Time.realtimeSinceStartup;last=Time.realtimeSinceStartupAsDouble;accumulator=0;
                while(Time.realtimeSinceStartup-start<5)
                {
                    double now=Time.realtimeSinceStartupAsDouble,elapsed=now-last;last=now;accumulator+=elapsed;int count=0;
                    while(accumulator>=DeveloperWorld.Step&&count++<240){GameStep();costs.Add((float)gamePhysics.LastMilliseconds);row.ticks++;accumulator-=DeveloperWorld.Step;}
                    row.backlog=Mathf.Max(row.backlog,(float)accumulator);frames.Add((float)elapsed*1000);UpdateGameCamera();RenderGameBullets();yield return null;
                }
                row.seconds=Time.realtimeSinceStartup-start;row.tickRate=row.ticks/row.seconds;row.fps=frames.Count/row.seconds;row.collections=GC.CollectionCount(0)-gc;row.managedDelta=GC.GetTotalMemory(false)-managed;row.nativeDelta=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()-native;
                row.scans=lidarFrame-scans;row.points=lidarPointCount;row.scanHz=lidarActualHz;costs.Sort();frames.Sort();
                if(costs.Count>0){row.physicsP50=costs[costs.Count/2];row.physicsP95=costs[(int)(costs.Count*.95f)];row.physicsP99=costs[(int)(costs.Count*.99f)];}
                if(frames.Count>0)row.frameP95=frames[(int)(frames.Count*.95f)];
                try{DeveloperChecks.Require(row.bodies==cars*5&&row.ticks>=120&&(!scan||row.scans>0&&row.points>0),"性能场景未实际推进物理／点云");DeveloperChecks.Require(row.fps>=(cars<=4?60:30)&&row.tickRate>=100&&(!scan||row.scanHz>=8),"性能回归：帧率、物理推进或扫描频率不足");foreach(var a in game.actors.Values)DeveloperChecks.Require(float.IsFinite(a.position.y),"性能场景发散");row.passed=true;}catch(Exception ex){row.error=ex.Message;}
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));game.running=false;lidarEnabled=false;InvalidateLidar();yield return null;
            }
            Application.targetFrameRate=previousCap;QualitySettings.vSyncCount=previousSync;report.passed=report.cases.TrueForAll(c=>c.passed);
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:79);
        }
    }
}
