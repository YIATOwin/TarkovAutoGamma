using System.Diagnostics;
using System.Text.Json;

internal static class NativeChecks
{
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder);var device=new GammaDevice();var display=Screen.PrimaryScreen!.DeviceName;var baseline=device.Read(display);var events=new List<string>();
        var journal=Path.Combine(folder,"native-recovery.json");var ready=Path.Combine(folder,"fixture-ready.json");Process? child=null;
        try
        {
            File.Delete(ready);
            using(var sampler=new DesktopSampler(display))
            {
                var found=false;for(var i=0;i<20;i++){if(sampler.Capture()){found=true;break;}Thread.Sleep(100);}
                if(!found)throw new Exception("No desktop frames.");events.Add($"Capture {sampler.Adapter}, {sampler.SourceWidth}x{sampler.SourceHeight}; GPU color/resize checks {sampler.ValidateGpuPipeline()}");
            }
            using(var session=new GammaSession(device,display,journal))
            using(var guard=new RecoveryGuard(journal))
            {
                session.Apply(1.08);events.Add($"Gamma write/readback verified={session.Verified}, write={session.LastWriteMs:F2}ms");session.Restore();
            }
            var difference=Difference(device.Read(display),baseline);if(difference>512)throw new Exception("Normal restoration differs: "+difference);events.Add("Normal restore verified, max difference "+difference);
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            foreach(var arg in new[]{"--recovery-fixture",display,journal,ready})info.ArgumentList.Add(arg);
            child=Process.Start(info)??throw new Exception("Fixture failed to start");
            var timer=Stopwatch.StartNew();while(!File.Exists(ready) && timer.Elapsed.TotalSeconds<15 && !child.HasExited)Thread.Sleep(100);
            if(!File.Exists(ready))throw new Exception("Fixture did not become ready");
            if(Difference(device.Read(display),baseline)<100)throw new Exception("Fixture did not change gamma before forced-exit test");
            events.Add("Recovery fixture applied gamma; terminating only parent process.");child.Kill();child.WaitForExit();timer.Restart();
            while(File.Exists(journal) && timer.Elapsed.TotalSeconds<15)Thread.Sleep(100);
            if(File.Exists(journal))throw new Exception("Watchdog did not clear recovery journal");
            difference=Difference(device.Read(display),baseline);if(difference>512)throw new Exception("Watchdog restoration differs: "+difference);
            events.Add("Forced-exit watchdog restore verified, max difference "+difference);
            var statuses=new List<FilterStatus>();var engine=new FilterEngine(new(){Display=display,GameOnly=false},s=>{lock(statuses)statuses.Add(s);});
            engine.Start();Thread.Sleep(6000);engine.Stop().GetAwaiter().GetResult();
            var last=statuses.LastOrDefault(s=>s.Frames>0);if(last==null || last.Frames<5)throw new Exception("Engine did not capture enough frames: "+statuses.LastOrDefault()?.Message);
            if(!statuses.Last().Message.StartsWith("已停止，") || File.Exists(Settings.RecoveryPath))throw new Exception("Engine did not stop cleanly: "+statuses.Last().Message);
            events.Add($"Engine manual-mode run: {last.Frames} frames; last write {last.WriteMs:F2}ms; final={statuses.Last().Message}");
            difference=Difference(device.Read(display),baseline);if(difference>512)throw new Exception("Engine restore differs: "+difference);
            File.WriteAllText(Path.Combine(folder,"native-checks.txt"),"PASS\n"+string.Join("\n",events));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(folder,"native-checks.txt"),"FAIL\n"+string.Join("\n",events)+"\n"+e);return 1;}
        finally
        {
            if(child is {HasExited:false}){child.Kill();child.WaitForExit();}child?.Dispose();
            try{GammaSession.Recover(journal,device);GammaSession.Recover(Settings.RecoveryPath,device);}finally{device.Write(display,baseline);}
        }
    }
    internal static int Fixture(string[] args)
    {
        try
        {
            using var session=new GammaSession(new GammaDevice(),args[1],args[2]);using var guard=new RecoveryGuard(args[2]);session.Apply(1.08);
            File.WriteAllText(args[3],JsonSerializer.Serialize(new{Parent=Environment.ProcessId}));Thread.Sleep(Timeout.Infinite);return 0;
        }
        catch(Exception e){File.WriteAllText(args[3]+".error",e.ToString());return 1;}
    }
    static int Difference(ushort[] a,ushort[] b)=>a.Zip(b,(x,y)=>Math.Abs((int)x-y)).Max();
}
