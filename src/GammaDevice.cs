using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal interface IGammaDevice { ushort[] Read(string display); void Write(string display,ushort[] curve); }
internal sealed class GammaDevice : IGammaDevice
{
    public ushort[] Read(string display)
    {
        var curve=new ushort[768];var dc=CreateDC("DISPLAY",display,null,IntPtr.Zero);
        if(dc==IntPtr.Zero)throw new IOException("无法访问显示器。");
        try{if(!GetDeviceGammaRamp(dc,curve))throw new IOException("显卡未提供 Gamma 曲线读取支持。");return curve;}finally{DeleteDC(dc);}
    }
    public void Write(string display,ushort[] curve)
    {
        if(curve.Length!=768)throw new ArgumentException("Invalid gamma ramp.");
        var dc=CreateDC("DISPLAY",display,null,IntPtr.Zero);if(dc==IntPtr.Zero)throw new IOException("无法访问显示器。");
        try{if(!SetDeviceGammaRamp(dc,curve))throw new IOException("Gamma 写入失败，请确认关闭 HDR 及其他色彩工具。");}finally{DeleteDC(dc);}
    }
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateDC(string driver,string device,string? output,IntPtr data);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool GetDeviceGammaRamp(IntPtr dc,[Out] ushort[] ramp);
    [DllImport("gdi32.dll")]static extern bool SetDeviceGammaRamp(IntPtr dc,[In] ushort[] ramp);
}
internal sealed record Recovery(string Display,ushort[] Baseline);
internal sealed class GammaSession : IDisposable
{
    readonly IGammaDevice device;readonly string path;readonly Recovery state;ushort[]? last;bool restored=true;
    internal GammaSession(IGammaDevice device,string display,string path)
    {
        this.device=device;this.path=path;state=new(display,device.Read(display));
        if(state.Baseline.Length!=768 || Enumerable.Range(0,3).Any(c=>state.Baseline[c*256+255]-state.Baseline[c*256]<10000))throw new IOException("读取到的原始曲线无效；没有修改显示设置。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state));File.Move(path+".tmp",path,true);
    }
    internal static ushort[] Compose(ushort[] baseline,double gamma)
    {
        var result=new ushort[768];
        for(var c=0;c<3;c++)for(var i=0;i<256;i++)
        {
            var at=Math.Pow(i/255.0,1/Math.Clamp(gamma,1,2))*255;var lo=Math.Min(255,(int)at);var hi=Math.Min(255,lo+1);
            result[c*256+i]=(ushort)Math.Round(baseline[c*256+lo]+(baseline[c*256+hi]-baseline[c*256+lo])*(at-lo));
        }
        return result;
    }
    internal double LastWriteMs {get;private set;}
    internal bool Verified {get;private set;}
    internal void Apply(double gamma)
    {
        var curve=Compose(state.Baseline,gamma);
        if(last!=null && curve.Zip(last,(a,b)=>Math.Abs((int)a-b)).Max()<80)return;
        restored=false;var watch=Stopwatch.StartNew();device.Write(state.Display,curve);LastWriteMs=watch.Elapsed.TotalMilliseconds;last=curve;
        if(!Verified && gamma>1.06)
        {
            var read=device.Read(state.Display);var difference=curve.Zip(read,(a,b)=>Math.Abs((int)a-b)).Max();
            if(difference>2048)throw new IOException("驱动的 Gamma 回读与目标不符，已停止自动调整。请确认 HDR 和其他色彩工具已关闭。");Verified=true;
        }
    }
    internal void Restore()
    {
        if(restored)return;device.Write(state.Display,state.Baseline);restored=true;last=null;
    }
    public void Dispose(){Restore();File.Delete(path);}
    internal static void Recover(string path,IGammaDevice device)
    {
        if(!File.Exists(path))return;
        var state=JsonSerializer.Deserialize<Recovery>(File.ReadAllText(path))??throw new IOException("恢复记录损坏。");
        if(state.Baseline.Length!=768 || string.IsNullOrWhiteSpace(state.Display))throw new IOException("恢复记录无效。");
        device.Write(state.Display,state.Baseline);File.Delete(path);
    }
}
