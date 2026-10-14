using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace TcrReplay
{
    public partial class ReplayViewer
    {
        [Serializable] class RunePrecisionCase
        {
            public string team,status;public int direction,shots,hits,minimumRing,resets;public bool noisy,passed;
            public float amplitude,omega,fittedAmplitude,fittedOmega,distance,seconds,averageRing;public int[] rings;
        }
        [Serializable] class RunePrecisionReport
        {
            public string gpu,error="";public bool passed;public int validHits,tenRingHits;public float averageRing;
            public List<RunePrecisionCase> cases=new List<RunePrecisionCase>();
        }
        IEnumerator RunePrecisionSmoke(string[] args)
        {
            string output=args[Array.IndexOf(args,"--rune-precision-smoke")+1];Directory.CreateDirectory(output);
            developerSmokeActive=true;allLabels=false;var report=new RunePrecisionReport {gpu=SystemInfo.graphicsDeviceName};
            float[] amplitudes={.780f,.780f,1.045f,1.045f,.9125f},omegas={1.884f,2f,1.884f,2f,1.942f};
            yield return null;
            foreach(string team in new[]{"red","blue"})foreach(int sign in new[]{-1,1})for(int parameter=0;parameter<amplitudes.Length;parameter++)
            {
                var result=new RunePrecisionCase {team=team,direction=sign,amplitude=amplitudes[parameter],omega=omegas[parameter],distance=sign<0?9:6,noisy=parameter==4};report.cases.Add(result);
                try
                {
                    PlaceRuneDriver(team,result.distance);game.direction=sign;game.running=true;game.runes[team].Training(game,"large",true);
                    if(result.noisy){game.rules.observationNoise=.003f;game.rules.observationDelay=.04f;game.rules.observationDrop=.08f;}
                    for(int i=0;i<360;i++)GameStep();var rune=game.runes[team];
                    // Benchmark sets the simulator's legal source parameters.
                    // The predictor still receives ONLY rendered phase/lights.
                    rune.a=result.amplitude;rune.omega=result.omega;runeTrackers.Clear();runeObservations.Clear();nextRuneObservation=game.time;
                    for(int i=0;i<2450&&rune.successes==0;i++)
                    {
                        gameDesiredYaw=90;gameDesiredPitch=0;GameStep();UpdateGameCamera();
                    }
                    result.rings=rune.rings.ToArray();result.minimumRing=10;float sum=0;
                    foreach(int ring in result.rings){sum+=ring;result.minimumRing=Mathf.Min(result.minimumRing,ring);report.validHits++;if(ring==10)report.tenRingHits++;}
                    result.averageRing=result.rings.Length>0?sum/result.rings.Length:0;result.hits=gameRuneHits;result.shots=gameShotCount;result.resets=rune.resets;result.seconds=game.time-rune.started;result.status=aimStatus;
                    if(runeTrackers.TryGetValue(gameLock,out var motion)){result.fittedAmplitude=motion.FittedAmplitude;result.fittedOmega=motion.FittedOmega;}
                    result.passed=rune.successes==1&&rune.groups==5&&result.hits>=5&&result.averageRing>=9.8f&&result.minimumRing>=9;
                    if(!result.passed)report.error+="规则边界精度／完成度不足 "+JsonUtility.ToJson(result)+"\n";
                }
                catch(Exception ex){report.error+=ex+"\n";Debug.LogException(ex);}
                Debug.Log("RUNE_PRECISION_CASE "+JsonUtility.ToJson(result));
                File.WriteAllText(Path.Combine(output,"progress.json"),JsonUtility.ToJson(report,true));game.running=false;yield return null;
            }
            float total=0;foreach(var result in report.cases)if(result.rings!=null)foreach(int ring in result.rings)total+=ring;
            report.averageRing=report.validHits>0?total/report.validHits:0;
            report.passed=report.error.Length==0&&report.cases.Count==20;
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("RUNE_PRECISION_SMOKE_"+(report.passed?"OK":"FAILED"));Application.Quit(report.passed?0:75);
        }
    }
}
