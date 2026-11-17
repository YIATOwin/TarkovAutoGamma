using System.Text.Json;

int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var meter=new AdaptiveMeter();var pixels=new byte[320*180*4];var settings=new Settings();
Scene Gray(byte value){for(var i=0;i<pixels.Length;i+=4){pixels[i]=pixels[i+1]=pixels[i+2]=value;pixels[i+3]=255;}return meter.Analyze(pixels,40);}
Check(AdaptiveMeter.TargetGamma(Gray(0),settings)==1,"Black loading screen must remain neutral");
Check(AdaptiveMeter.TargetGamma(Gray(255),settings)==1,"Bright scene must remain neutral");
var dark=Gray(30);Check(AdaptiveMeter.TargetGamma(dark,settings)>1.2,"Dark scene boost");
Check(AdaptiveMeter.TargetGamma(dark,settings with{Strength=0})==1,"Strength zero is identity");
Check(AdaptiveMeter.TargetGamma(dark,settings with{MaxGamma=1})==1,"Max gamma one is identity");
Check(AdaptiveMeter.TargetGamma(dark,settings with{Strength=100})<=settings.MaxGamma,"Respect gamma ceiling");
for(var i=0;i<pixels.Length;i+=4){pixels[i]=10;pixels[i+1]=80;pixels[i+2]=10;}
Check(meter.Analyze(pixels,40).NightVision,"Green night vision guard");Check(AdaptiveMeter.TargetGamma(meter.Analyze(pixels,40),settings)==1,"Night vision neutral");
Gray(220);for(var y=60;y<120;y++)for(var x=107;x<214;x++){var i=(y*320+x)*4;pixels[i]=pixels[i+1]=pixels[i+2]=20;}
Check(meter.Analyze(pixels,60).Median<.1,"Center weighting prefers center at sixty percent");Check(meter.Analyze(pixels,20).Median>.8,"Balanced weighting retains surroundings");
var smoothed=1.0;for(var i=0;i<100;i++){var next=AdaptiveMeter.SmoothGamma(smoothed,1.4,.05,1200);Check(next>=smoothed && next<=1.4,"Bounded smooth ramp");smoothed=next;}
Check(smoothed>1.38,"Smoothing reaches destination");
var baseline=Enumerable.Range(0,768).Select(i=>(ushort)((i%256)*257)).ToArray();
Check(GammaSession.Compose(baseline,1).SequenceEqual(baseline),"Neutral composition preserves baseline");
foreach(var gamma in new[]{1.1,1.4,1.8,2.0})
{
    var curve=GammaSession.Compose(baseline,gamma);
    for(var c=0;c<3;c++)Check(curve[c*256]==0 && curve[c*256+255]==65535 && Enumerable.Range(1,255).All(i=>curve[c*256+i]>=curve[c*256+i-1]),"Curve endpoints and monotonicity");
}
var calibrated=baseline.Select(v=>(ushort)(Math.Pow(v/65535.0,1.1)*65535)).ToArray();Check(GammaSession.Compose(calibrated,1).SequenceEqual(calibrated),"Preserve existing calibration");
var dir=Path.Combine(Path.GetTempPath(),"TarkovAutoGamma-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var path=Path.Combine(dir,"recovery.json");var fake=new FakeGamma(baseline);
using(var session=new GammaSession(fake,"test",path))
{
    Check(File.Exists(path),"Journal precedes writes");Check(fake.Writes==0,"Constructing session does not alter screen");session.Apply(1.2);Check(session.Verified,"Driver readback verified");Check(!fake.Current.SequenceEqual(baseline),"Boost applied");session.Restore();Check(fake.Current.SequenceEqual(baseline),"Restore original calibration");
}
Check(!File.Exists(path),"Normal stop clears journal");
var failing=new GammaSession(fake,"test",path);failing.Apply(1.2);fake.Fail=true;try{failing.Dispose();}catch(IOException){}Check(File.Exists(path),"Failed restore retains recovery journal");fake.Fail=false;GammaSession.Recover(path,fake);Check(fake.Current.SequenceEqual(baseline) && !File.Exists(path),"Recover pending journal");
fake.Ignore=true;using(var ignored=new GammaSession(fake,"test",path)){var detected=false;try{ignored.Apply(1.3);}catch(IOException){detected=true;}Check(detected,"Detect silently ignored writes");}fake.Ignore=false;
var broken=Path.Combine(dir,"bad.json");File.WriteAllText(broken,JsonSerializer.Serialize(new Recovery("test",[0])));var rejected=false;try{GammaSession.Recover(broken,fake);}catch(IOException){rejected=true;}Check(rejected && File.Exists(broken),"Reject corrupt journal without dropping it");
Console.WriteLine($"PASS: {checks} checks; temp={dir}");

sealed class FakeGamma(ushort[] baseline):IGammaDevice
{
    internal ushort[] Current=(ushort[])baseline.Clone();internal int Writes;internal bool Fail,Ignore;
    public ushort[] Read(string display)=>(ushort[])Current.Clone();
    public void Write(string display,ushort[] curve){if(Fail)throw new IOException("Injected restore failure");Writes++;if(!Ignore)Current=(ushort[])curve.Clone();}
}
