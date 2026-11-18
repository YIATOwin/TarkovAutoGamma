internal readonly record struct Scene(double P10,double Median,double P95,double BlackFraction,bool NightVision);

internal sealed class AdaptiveMeter
{
    readonly int[,] bins=new int[9,256];readonly int[] counts=new int[9];readonly double[] histogram=new double[256];
    public Scene Analyze(byte[] rgba,int centerWeight)
    {
        Array.Clear(bins);Array.Clear(counts);Array.Clear(histogram);double red=0,green=0,blue=0;int samples=0;
        for(var y=7;y<173;y++)for(var x=7;x<313;x++)
        {
            // Exclude the bottom HUD band; retain samples throughout the nine regions.
            if(y>156)continue;
            var at=(y*320+x)*4;var r=Math.Min(2,y*3/180)*3+Math.Min(2,x*3/320);
            var l=Math.Clamp((int)Math.Round(.2126*rgba[at]+.7152*rgba[at+1]+.0722*rgba[at+2]),0,255);
            bins[r,l]++;counts[r]++;red+=rgba[at];green+=rgba[at+1];blue+=rgba[at+2];samples++;
        }
        var center=centerWeight/100.0;var edge=(1-center)/6;var corner=edge/2;
        for(var r=0;r<9;r++)for(var i=0;i<256;i++)histogram[i]+=bins[r,i]/(double)Math.Max(1,counts[r])*(r==4?center:r%2==1?edge:corner);
        var black=histogram[0]+histogram[1]+histogram[2];
        return new(Quantile(.1),Quantile(.5),Quantile(.95),black,green>red*1.35 && green>blue*1.35 && green/samples>20);
    }
    double Quantile(double q){double sum=0;for(var i=0;i<256;i++){sum+=histogram[i];if(sum>=q-1e-9)return i/255.0;}return 1;}
    internal static double TargetGamma(Scene s,Settings settings)
        =>TargetTone(s,settings).Gamma;
    internal static Tone TargetTone(Scene s,Settings settings)
    {
        if(s.BlackFraction>.995 || s.P95<.015 || s.NightVision)return Tone.Identity;
        var darkness=1-Smooth(.09,.43,s.Median);
        var shadows=1-Smooth(.025,.16,s.P10);
        // Bright windows/sky reduce the boost without disabling dark interior recovery.
        var highlightGuard=1-.3*Smooth(.76,.98,s.P95);
        var amount=settings.Strength/100.0*darkness*(.7+.3*shadows)*highlightGuard;
        var flat=1-Smooth(.2,.65,s.P95-s.P10);
        return new(1+(settings.MaxGamma-1)*amount,settings.Brightness/100.0*amount,
            settings.Contrast/100.0*amount*(.65+.35*flat));
    }
    static double Smooth(double a,double b,double x){var t=Math.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);}
}
