using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class RecoveryGuard : IDisposable
{
    readonly EventWaitHandle stop;readonly Process child;
    internal RecoveryGuard(string path)
    {
        var name="Local\\TarkovAutoGamma-"+Guid.NewGuid().ToString("N");stop=new(false,EventResetMode.ManualReset,name);
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        info.ArgumentList.Add("--watchdog");info.ArgumentList.Add(Environment.ProcessId.ToString());info.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());info.ArgumentList.Add(name);info.ArgumentList.Add(path);
        try{child=Process.Start(info)??throw new IOException("无法启动恢复保护进程。");}catch{stop.Dispose();throw;}
    }
    internal void Check(){if(child.HasExited)throw new IOException("恢复保护进程已退出，自动调整已停止。");}
    public void Dispose(){stop.Set();if(!child.WaitForExit(1500))child.Kill();child.Dispose();stop.Dispose();}
    internal static int Watch(string[] args)
    {
        var path=args[4];
        try
        {
            using var parent=Process.GetProcessById(int.Parse(args[1]));
            if(parent.StartTime.ToUniversalTime().Ticks!=long.Parse(args[2]))throw new InvalidOperationException("Parent changed.");
            using var stop=EventWaitHandle.OpenExisting(args[3]);
            var result=WaitForMultipleObjects(2,[parent.Handle,stop.SafeWaitHandle.DangerousGetHandle()],false,uint.MaxValue);
            if(result==1)return 0;
        }
        catch{}
        for(var i=0;i<5;i++)
        {
            try{GammaSession.Recover(path,new GammaDevice());return 0;}catch(Exception e){try{File.WriteAllText(path+".error.txt",e.Message);}catch{}Thread.Sleep(500);}
        }
        return 1;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,bool all,uint milliseconds);
}
