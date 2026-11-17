using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal sealed class DesktopSampler : IDisposable
{
    internal const int Width=320,Height=180;
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly IDXGIOutputDuplication duplication;
    readonly ID3D11ComputeShader shader;
    readonly ID3D11SamplerState sampler;
    ID3D11Texture2D? source;
    ID3D11ShaderResourceView? sourceView;
    readonly ID3D11Texture2D small,staging;
    readonly ID3D11UnorderedAccessView smallView;
    internal readonly byte[] Pixels=new byte[Width*Height*4];
    internal readonly string Adapter,Display;
    internal int SourceWidth,SourceHeight;
    internal double LastCaptureMs;
    internal long KnownTextureBytes=>SourceWidth*(long)SourceHeight*4+Width*Height*8;
    internal DesktopSampler(string display)
    {
        using var factory=CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? chosen=null;IDXGIOutput? output=null;
        for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++)
        {
            for(uint o=0;adapter.EnumOutputs(o,out var candidate).Success;o++)
            {
                if(candidate.Description.AttachedToDesktop && candidate.Description.DeviceName==display){chosen=adapter;output=candidate;break;}
                candidate.Dispose();
            }
            if(chosen!=null)break;adapter.Dispose();
        }
        if(chosen==null || output==null)throw new InvalidOperationException("找不到所选显示器，请重新打开工具刷新显示器列表。");
        using(chosen)using(output)
        {
            Adapter=chosen.Description1.Description.TrimEnd('\0');Display=output.Description.DeviceName;
            using(var output6=output.QueryInterface<IDXGIOutput6>())
                if((int)output6.Description1.ColorSpace!=0)throw new NotSupportedException("所选显示器不是 SDR 色彩模式，请关闭 HDR / 自动 HDR 后重试。");
            if(output.Description.Rotation is not ModeRotation.Identity and not ModeRotation.Unspecified)throw new NotSupportedException("此测试版仅支持横向显示器。");
            D3D11CreateDevice(chosen,DriverType.Unknown,DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out device,out context).CheckError();
            try{using var output1=output.QueryInterface<IDXGIOutput1>();duplication=output1.DuplicateOutput(device);}
            catch{context.Dispose();device.Dispose();throw;}
        }
        try
        {
            var code=Compiler.Compile("""
                Texture2D<float4> Source : register(t0);
                SamplerState Linear : register(s0);
                RWTexture2D<float4> Target : register(u0);
                [numthreads(8,8,1)] void main(uint3 p:SV_DispatchThreadID) {
                    if(p.x>=320 || p.y>=180) return;
                    Target[p.xy]=Source.SampleLevel(Linear,(float2(p.xy)+0.5)/float2(320,180),0);
                }
                ""","main","capture-downsample","cs_5_0",ShaderFlags.OptimizationLevel3);
            shader=device.CreateComputeShader(code.Span);
            sampler=device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear,TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureAddressMode.Clamp));
            small=device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,Width,Height,1,1,BindFlags.UnorderedAccess));
            smallView=device.CreateUnorderedAccessView(small);
            staging=device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,Width,Height,1,1,BindFlags.None,ResourceUsage.Staging,CpuAccessFlags.Read));
        }
        catch{smallView?.Dispose();staging?.Dispose();small?.Dispose();sampler?.Dispose();shader?.Dispose();duplication.Dispose();context.Dispose();device.Dispose();throw;}
    }
    internal bool Capture()
    {
        var watch=Stopwatch.StartNew();
        var result=duplication.AcquireNextFrame(0,out _,out var resource);
        if(result.Code==unchecked((int)0x887A0027))return false; // Static desktop: no new frame, not a failure.
        result.CheckError();
        try
        {
            using(resource)using(var texture=resource.QueryInterface<ID3D11Texture2D>())
            {
                var desc=texture.Description;
                if(desc.Format!=Format.B8G8R8A8_UNorm)throw new NotSupportedException("当前捕获不是 SDR BGRA8；请确认系统 HDR 已关闭。");
                if(source==null || SourceWidth!=desc.Width || SourceHeight!=desc.Height)
                {
                    sourceView?.Dispose();source?.Dispose();SourceWidth=(int)desc.Width;SourceHeight=(int)desc.Height;
                    source=device.CreateTexture2D(new Texture2DDescription(desc.Format,desc.Width,desc.Height,1,1,BindFlags.ShaderResource));
                    sourceView=device.CreateShaderResourceView(source);
                }
                context.CopyResource(source,texture);
                ReadSmall();
            }
        }
        finally{duplication.ReleaseFrame();}
        LastCaptureMs=watch.Elapsed.TotalMilliseconds;return true;
    }
    void ReadSmall()
    {
        context.CSSetShader(shader);context.CSSetSampler(0,sampler);context.CSSetShaderResource(0,sourceView);context.CSSetUnorderedAccessView(0,smallView);
        context.Dispatch(40,23,1);
        context.CSSetShaderResource(0,null);context.CSSetUnorderedAccessView(0,null);
        context.CopyResource(staging,small);
        var mapped=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
        try{for(var y=0;y<Height;y++)Marshal.Copy(mapped.DataPointer+y*(int)mapped.RowPitch,Pixels,y*Width*4,Width*4);}
        finally{context.Unmap(staging,0);}
    }
    internal int ValidateGpuPipeline()
    {
        if(source==null)throw new InvalidOperationException("Capture first.");
        var data=new byte[SourceWidth*SourceHeight*4];
        for(var y=0;y<SourceHeight;y++)for(var x=0;x<SourceWidth;x++)
        {var i=(y*SourceWidth+x)*4;data[i]=33;data[i+1]=(byte)(y*255.0/(SourceHeight-1));data[i+2]=(byte)(x*255.0/(SourceWidth-1));data[i+3]=255;}
        context.UpdateSubresource<byte>(data,source,0,(uint)(SourceWidth*4));ReadSmall();
        foreach(var (x,y) in new[]{(20,20),(160,90),(300,160)})
        {var i=(y*Width+x)*4;if(Math.Abs(Pixels[i]-(x+.5)*255/Width)>2 || Math.Abs(Pixels[i+1]-(y+.5)*255/Height)>2 || Math.Abs(Pixels[i+2]-33)>1)throw new InvalidOperationException($"GPU resize/channel check failed at {x},{y}: {Pixels[i]},{Pixels[i+1]},{Pixels[i+2]}");}
        return 3;
    }
    public void Dispose()
    {
        context.ClearState();sourceView?.Dispose();source?.Dispose();smallView.Dispose();small.Dispose();staging.Dispose();sampler.Dispose();shader.Dispose();
        duplication.Dispose();context.Dispose();device.Dispose();
    }
}
