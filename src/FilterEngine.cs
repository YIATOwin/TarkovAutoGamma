using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed record FilterStatus(string Message,double Gamma=1,Scene Scene=default,double CaptureMs=0,double AnalysisMs=0,double WriteMs=0,bool Verified=false,long Frames=0,bool Stopped=false,double Brightness=0,double Contrast=0)
{
    internal Tone CurrentTone=>new(Gamma,Brightness,Contrast);
}
internal sealed class FilterEngine
{
    readonly Action<FilterStatus> notify;readonly AdaptiveMeter meter=new();CancellationTokenSource? cancellation;Task? task;Settings settings;RecoveryGuard? unresolvedGuard;
    internal bool Running=>task is {IsCompleted:false};
    internal Settings Configuration {set=>Volatile.Write(ref settings,value);}
    internal FilterEngine(Settings settings,Action<FilterStatus> notify){this.settings=settings;this.notify=notify;}
    internal void Start()
    {
        if(Running)return;cancellation?.Dispose();cancellation=new();task=Task.Run(()=>Run(cancellation.Token));
    }
    internal async Task Stop()
    {
        cancellation?.Cancel();if(task!=null)await task.ConfigureAwait(false);
    }
    async Task Run(CancellationToken token)
    {
        DesktopSampler? sampler=null;GammaSession? gamma=null;RecoveryGuard? guard=null;string result="已停止，原始颜色已恢复。";
        var clock=Stopwatch.StartNew();var nextCapture=0.0;var nextUi=0.0;var nextWrite=0.0;var lastGood=0.0;var transition=new ToneTransition();var target=Tone.Identity;long frames=0;int failures=0;Scene scene=default;
        var captureMs=0.0;var analysisMs=0.0;bool active=false;
        try
        {
            var config=Volatile.Read(ref settings);GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());unresolvedGuard?.Dispose();unresolvedGuard=null;
            gamma=new(new GammaDevice(),config.Display,Settings.RecoveryPath);guard=new(Settings.RecoveryPath);
            while(true)
            {
                token.ThrowIfCancellationRequested();config=Volatile.Read(ref settings);var now=clock.Elapsed.TotalSeconds;guard.Check();
                var eligible=MayAdjust(config,out var reason);
                if(!eligible)
                {
                    if(active){gamma.Restore();active=false;}
                    target=Tone.Identity;transition.Reset();sampler?.Dispose();sampler=null;nextCapture=now;
                    if(now>=nextUi){notify(new(reason,Frames:frames));nextUi=now+.5;}
                    await Task.Delay(100,token);continue;
                }
                if(sampler==null)
                {
                    sampler=new(config.Display);nextCapture=now;lastGood=now;
                }
                if(now>=nextCapture)
                {
                    try
                    {
                        if(sampler.Capture())
                        {
                            captureMs=sampler.LastCaptureMs;var watch=Stopwatch.StartNew();scene=meter.Analyze(sampler.Pixels,config.CenterWeight);target=AdaptiveMeter.TargetTone(scene,config);analysisMs=watch.Elapsed.TotalMilliseconds;
                            frames++;lastGood=now;failures=0;
                        }
                        // A static frame is valid. Do not repeatedly recreate the desktop duplication.
                    }
                    catch(Exception e)
                    {
                        gamma.Restore();active=false;target=Tone.Identity;transition.Reset();sampler.Dispose();sampler=null;
                        if(++failures>=3 || e is NotSupportedException)throw;
                        notify(new("采集中断，已恢复颜色，正在重试："+e.Message,Frames:frames));await Task.Delay(800,token);continue;
                    }
                    nextCapture+=1.0/config.Frequency;if(nextCapture<clock.Elapsed.TotalSeconds)nextCapture=clock.Elapsed.TotalSeconds+1.0/config.Frequency;
                }
                now=clock.Elapsed.TotalSeconds;transition.SetTarget(target,now,config.TransitionMs);var current=transition.At(now);
                if(frames>0 && now-lastGood<30)
                {
                    if(current.IsIdentity){if(active)gamma.Restore();active=false;}
                    else if(now>=nextWrite){gamma.Apply(current);active=true;nextWrite=clock.Elapsed.TotalSeconds+.05;}
                }
                else if(active){gamma.Restore();active=false;target=Tone.Identity;transition.Reset();current=Tone.Identity;}
                if(now>=nextUi)
                {
                    var message=now-lastGood>=30?"画面长时间未更新，已恢复颜色。":scene.BlackFraction>.995?"近黑画面保护":scene.NightVision?"疑似夜视画面，暂停提亮":$"自动调整中 · {sampler?.SourceWidth}×{sampler?.SourceHeight} · {config.Frequency}次/秒";
                    notify(new(message,current.Gamma,scene,captureMs,analysisMs,gamma.LastWriteMs,gamma.Verified,frames,Brightness:current.Brightness,Contrast:current.Contrast));nextUi=now+.5;
                }
                await Task.Delay(Math.Clamp((int)((nextCapture-clock.Elapsed.TotalSeconds)*1000),1,50),token);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception e){result="已停止："+e.Message;Log(e);}
        finally
        {
            sampler?.Dispose();
            try{gamma?.Dispose();}catch(Exception e){result="恢复颜色失败，请点击恢复重试："+e.Message;Log(e);}
            if(!File.Exists(Settings.RecoveryPath))guard?.Dispose();else unresolvedGuard=guard;
            notify(new(result,Stopped:true));
        }
    }
    internal static void Log(Exception e){try{Directory.CreateDirectory(Settings.Folder);File.AppendAllText(Path.Combine(Settings.Folder,"errors.log"),DateTimeOffset.Now+" "+e+Environment.NewLine);}catch{}}
    static bool MayAdjust(Settings config,out string reason)
    {
        reason="等待游戏前台";
        try
        {
            var window=GetForegroundWindow();GetWindowThreadProcessId(window,out var id);using var process=Process.GetProcessById((int)id);
            if(id==Environment.ProcessId){reason="预览中 · 显示颜色已恢复";return false;}
            return !config.GameOnly || process.ProcessName.Equals("EscapeFromTarkov",StringComparison.OrdinalIgnoreCase) && Screen.FromHandle(window).DeviceName==config.Display;
        }
        catch{return false;}
    }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
}
