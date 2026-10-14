using System;
using UnityEngine;

namespace TcrReplay
{
    public static class ReplayLidarChecks
    {
        struct Fixture
        {
            public int sample,frame,samples;public Vector3 direction;
            public Fixture(int sample,int frame,int samples,float x,float y,float z)
            {this.sample=sample;this.frame=frame;this.samples=samples;direction=new Vector3(x,y,z);}
        }
        public static void Run()
        {
            // Independent Python/libm reference using the original phase
            // formula and an explicit yaw/pitch rotation, not this polynomial.
            var fixtures=new[]{
                new Fixture(0,0,24000,0f,0f,1f),
                new Fixture(6897,0,24000,-.04160264609f,.01076460064f,.9990762449f),
                new Fixture(9317,0,24000,.04534812615f,-.01981070767f,.9987747911f),
                new Fixture(6897,238,24000,-.320696201f,.3254983446f,.8894969221f),
                new Fixture(23999,1200,24000,-.4674557358f,.2817218204f,.8379247884f),
                new Fixture(0,0,12000,0f,0f,1f),
                new Fixture(11013,239,12000,-.05113099394f,.5408245628f,.8395799031f),
                new Fixture(11999,6000,12000,-.4939231897f,-.213250733f,.8429495878f),
                new Fixture(4142,0,6000,-.3066953171f,.4152659072f,.8564415968f),
                new Fixture(2999,207,3000,-.07009978182f,-.324169759f,.9433981068f),
                new Fixture(7,100000,24000,.4117105937f,.106722886f,.9050439838f),
                new Fixture(19200,-17,24000,-.2466897557f,.05200660835f,.9676980299f)
            };
            foreach(var f in fixtures)
            {
                var actual=ReplayViewer.AviaDirection(f.sample,f.frame,f.samples);
                DeveloperChecks.Require((actual-f.direction).magnitude<.000002f,"Avia方向偏离独立参考："+f.frame+"/"+f.sample);
                DeveloperChecks.Require(actual==ReplayViewer.AviaDirection(f.sample,f.frame,f.samples),"Avia方向不确定");
            }
            foreach(double turn in new[]{0d,1d,-1d,1000000d,-1000000d})
            {
                DeveloperChecks.Require(Mathf.Abs(ReplayViewer.AviaSineTurns(turn))<.0000001f,"Avia周期归约零点错误");
                DeveloperChecks.Require(Mathf.Abs(ReplayViewer.AviaSineTurns(turn+.25)-1)<.0000001f,"Avia正峰值错误");
                DeveloperChecks.Require(Mathf.Abs(ReplayViewer.AviaSineTurns(turn-.25)+1)<.0000001f,"Avia负峰值错误");
            }
            foreach(int samples in new[]{24000,12000,6000,3000,2400,1200})
            foreach(int frame in new[]{0,1,238,1200,6000,100000,int.MaxValue,int.MinValue})
            for(int sample=0;sample<samples;sample+=127)
            {
                var p=ReplayViewer.AviaDirection(sample,frame,samples);
                DeveloperChecks.Require(!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&Mathf.Abs(p.sqrMagnitude-1)<.000002f,"Avia异常扫描方向");
                DeveloperChecks.Require(Mathf.Abs(p.x)<.578f&&Mathf.Abs(p.y)<.625f&&p.z>.63f,"Avia扫描超出光学视场");
            }
            bool invalid=false;try{ReplayViewer.AviaDirection(0,0,0);}catch(ArgumentOutOfRangeException){invalid=true;}
            DeveloperChecks.Require(invalid,"Avia零点数未拒绝");
        }
    }
}
