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
var destination=new Tone(1.4,.04,.1);var transition=new ToneTransition();transition.SetTarget(destination,0,300);
Check(transition.At(0).IsIdentity,"Transition starts from baseline");
Check(transition.At(.15).Near(Tone.Lerp(Tone.Identity,destination,.5)),"Midpoint of 300ms transition");
Check(transition.At(.3)==destination,"Transition reaches exact destination at 300ms");
transition.SetTarget(new Tone(1.2,-.02,-.1),.35,300);var beforeRetarget=transition.At(.45);transition.SetTarget(destination,.45,300);
Check(transition.At(.45)==beforeRetarget,"Retarget has no jump");Check(transition.At(.75)==destination,"Retarget completes in 300ms");
transition.SetTarget(Tone.Identity,.8,300);Check(transition.At(1.1).IsIdentity,"Neutral transition restores exact baseline");
Check(new Settings().TransitionMs==300,"Default transition is 300ms");
Check(Settings.Parse("{\"TransitionMs\":1200}").TransitionMs==300,"Migrate legacy default transition");
Check(Settings.Parse("{\"TransitionMs\":650}").TransitionMs==650,"Preserve customized legacy transition");
Check(Settings.Parse("{\"SchemaVersion\":2,\"TransitionMs\":1200}").TransitionMs==1200,"Preserve explicit new-version setting");
Check(AdaptiveMeter.TargetTone(dark,settings with{Strength=0}).IsIdentity,"Zero strength disables all three adjustments");
Check(AdaptiveMeter.TargetTone(Gray(255),settings).IsIdentity,"Bright scene preserves identity for all channels");
foreach(var b in new[]{-.2,0,.2})foreach(var c in new[]{-.3,0,.3})foreach(var gammaValue in new[]{1.0,1.6,2.0})
{
    var tone=new Tone(gammaValue,b,c);var points=Enumerable.Range(0,1001).Select(i=>ToneCurve.Map(i/1000.0,tone)).ToArray();
    Check(points[0]==0 && points[^1]==1,"Combined curve preserves endpoints");Check(points.Zip(points.Skip(1),(a,z)=>z>=a).All(v=>v),"Combined curve remains monotone at setting limits");
}
Check(ToneCurve.Map(.35,new Tone(1,.1))>.35,"Positive brightness increases midtone");
Check(ToneCurve.Map(.35,new Tone(1,-.1))<.35,"Negative brightness decreases midtone");
Check(ToneCurve.Map(.25,new Tone(1,0,.2))<.25 && ToneCurve.Map(.75,new Tone(1,0,.2))>.75,"Positive contrast separates midtones");
using(var sample=PreviewRenderer.CreateSample())
{
    using var neutral=PreviewRenderer.Render(sample,Tone.Identity);Check(sample.GetPixel(420,320)==neutral.GetPixel(420,320),"Neutral preview preserves pixels");
    using var adjusted=PreviewRenderer.Render(sample,destination);var pixel=sample.GetPixel(420,320);var result=adjusted.GetPixel(420,320);
    Check(result.R==(byte)Math.Round(ToneCurve.Map(pixel.R/255.0,destination)*255),"Preview uses same curve as display");
    Check(AdaptiveMeter.TargetTone(PreviewRenderer.Analyze(sample,40),settings).Gamma>1.05,"Built-in sample demonstrates dark-scene adjustment");
}
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
fake.Ignore=true;using(var ignored=new GammaSession(fake,"test",path)){var detected=false;try{ignored.Apply(1.3);}catch(IOException){detected=true;}Check(detected,"Detect silently ignored writes");}
using(var ignored=new GammaSession(fake,"test",path)){var detected=false;try{ignored.Apply(new Tone(1,.04));}catch(IOException){detected=true;}Check(detected,"Detect ignored brightness-only writes");}fake.Ignore=false;
var broken=Path.Combine(dir,"bad.json");File.WriteAllText(broken,JsonSerializer.Serialize(new Recovery("test",[0])));var rejected=false;try{GammaSession.Recover(broken,fake);}catch(IOException){rejected=true;}Check(rejected && File.Exists(broken),"Reject corrupt journal without dropping it");
foreach(var file in args)
{
    using var comparison=new ComparisonPreview();comparison.LoadFile(file,settings);Check(comparison.SourceName==Path.GetFileName(file),"Imported image becomes preview source");Check(double.IsFinite(comparison.Target.Gamma),"Imported image produces finite target");
    var invalid=Path.Combine(dir,"invalid-image.png");File.WriteAllText(invalid,"invalid image");var previous=comparison.SourceName;try{comparison.LoadFile(invalid,settings);}catch(ArgumentException){}Check(comparison.SourceName==previous,"Invalid import preserves prior image");
    Console.WriteLine($"Imported screenshot target: {comparison.Target}");
}
Console.WriteLine($"PASS: {checks} checks; temp={dir}");

sealed class FakeGamma(ushort[] baseline):IGammaDevice
{
    internal ushort[] Current=(ushort[])baseline.Clone();internal int Writes;internal bool Fail,Ignore;
    public ushort[] Read(string display)=>(ushort[])Current.Clone();
    public void Write(string display,ushort[] curve){if(Fail)throw new IOException("Injected restore failure");Writes++;if(!Ignore)Current=(ushort[])curve.Clone();}
}
