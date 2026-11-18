internal readonly record struct Tone(double Gamma,double Brightness=0,double Contrast=0)
{
    internal static Tone Identity=>new(1);
    internal bool IsIdentity=>Math.Abs(Gamma-1)<1e-8 && Math.Abs(Brightness)<1e-8 && Math.Abs(Contrast)<1e-8;
    internal bool Near(Tone other)=>Math.Abs(Gamma-other.Gamma)<.006 && Math.Abs(Brightness-other.Brightness)<.0015 && Math.Abs(Contrast-other.Contrast)<.0015;
    internal static Tone Lerp(Tone a,Tone b,double t)=>new(a.Gamma+(b.Gamma-a.Gamma)*t,a.Brightness+(b.Brightness-a.Brightness)*t,a.Contrast+(b.Contrast-a.Contrast)*t);
}

internal static class ToneCurve
{
    internal static double Map(double input,Tone tone)
    {
        var value=Math.Pow(Math.Clamp(input,0,1),1/Math.Clamp(tone.Gamma,1,2));
        // Bounded, endpoint-preserving brightness and S contrast avoid hard clipping.
        value+=Math.Clamp(tone.Brightness,-.2,.2)*4*value*(1-value);
        value+=Math.Clamp(tone.Contrast,-.3,.3)*(value-.5)*4*value*(1-value);
        return Math.Clamp(value,0,1);
    }
}

internal sealed class ToneTransition
{
    Tone from=Tone.Identity,target=Tone.Identity;double started;int durationMs=300;
    internal Tone At(double seconds)
    {
        var t=Math.Clamp((seconds-started)*1000/durationMs,0,1);return Tone.Lerp(from,target,t*t*(3-2*t));
    }
    internal void SetTarget(Tone next,double seconds,int milliseconds)
    {
        if(next.Near(target) && milliseconds==durationMs && !(next.IsIdentity && !target.IsIdentity))return;
        from=At(seconds);target=next;started=seconds;durationMs=Math.Max(1,milliseconds);
    }
    internal void Reset(){from=target=Tone.Identity;started=0;}
}
