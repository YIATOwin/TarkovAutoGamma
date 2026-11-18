using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class PreviewRenderer
{
    internal static Bitmap BoundedCopy(Image image)
    {
        if(image.Width>32768 || image.Height>32768 || (long)image.Width*image.Height>100_000_000)throw new IOException("图片尺寸过大，请使用普通游戏截图。");
        var scale=Math.Min(1,Math.Min(960.0/image.Width,540.0/image.Height));
        var result=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(result);g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height));return result;
    }
    internal static Scene Analyze(Bitmap source,int centerWeight)
    {
        using var small=new Bitmap(320,180,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.Bilinear;g.DrawImage(source,new Rectangle(0,0,320,180));}
        var rgba=new byte[320*180*4];var row=new byte[320*4];var data=small.LockBits(new Rectangle(0,0,320,180),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(var y=0;y<180;y++){Marshal.Copy(data.Scan0+y*data.Stride,row,0,row.Length);for(var x=0;x<320;x++){var i=x*4;var p=(y*320+x)*4;rgba[p]=row[i+2];rgba[p+1]=row[i+1];rgba[p+2]=row[i];rgba[p+3]=255;}}}
        finally{small.UnlockBits(data);}
        return new AdaptiveMeter().Analyze(rgba,centerWeight);
    }
    internal static Bitmap Render(Bitmap source,Tone tone)
    {
        var output=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);var map=new byte[256];
        for(var i=0;i<256;i++)map[i]=(byte)Math.Round(ToneCurve.Map(i/255.0,tone)*255);
        var bounds=new Rectangle(0,0,source.Width,source.Height);var src=source.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            var dst=output.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try
            {
                var row=new byte[source.Width*4];for(var y=0;y<source.Height;y++)
                {Marshal.Copy(src.Scan0+y*src.Stride,row,0,row.Length);for(var i=0;i<row.Length;i+=4){row[i]=map[row[i]];row[i+1]=map[row[i+1]];row[i+2]=map[row[i+2]];row[i+3]=255;}Marshal.Copy(row,0,dst.Scan0+y*dst.Stride,row.Length);}
            }
            finally{output.UnlockBits(dst);}
        }
        catch{output.Dispose();throw;}
        finally{source.UnlockBits(src);}
        return output;
    }
    internal static Bitmap CreateSample()
    {
        // A procedural test scene: no private screenshots are distributed with the app.
        var image=new Bitmap(960,540,PixelFormat.Format32bppArgb);using var g=Graphics.FromImage(image);
        using(var room=new LinearGradientBrush(new Rectangle(0,0,960,540),Color.FromArgb(12,17,23),Color.FromArgb(55,57,50),90f))g.FillRectangle(room,0,0,960,540);
        using(var wall=new SolidBrush(Color.FromArgb(24,29,33)))g.FillPolygon(wall,[new(0,0),new(610,90),new(610,360),new(0,540)]);
        using(var floor=new SolidBrush(Color.FromArgb(29,31,30)))g.FillPolygon(floor,[new(0,540),new(610,360),new(960,380),new(960,540)]);
        using(var window=new LinearGradientBrush(new Rectangle(690,78,180,210),Color.FromArgb(224,227,216),Color.FromArgb(116,143,151),90f))g.FillRectangle(window,690,78,180,210);
        using(var frame=new Pen(Color.FromArgb(52,57,59),10)){g.DrawRectangle(frame,685,73,190,220);g.DrawLine(frame,780,73,780,293);g.DrawLine(frame,685,178,875,178);}
        using(var edge=new Pen(Color.FromArgb(38,43,45),3))
        {
            for(var x=80;x<600;x+=100)g.DrawLine(edge,x,20+x/7,x,500-x/4);
            for(var y=130;y<360;y+=75)g.DrawLine(edge,0,y,610,100+(int)(y*.7));
        }
        void Crate(Rectangle rect,Color color)
        {
            using var fill=new SolidBrush(color);using var line=new Pen(Color.FromArgb(color.R+10,color.G+10,color.B+10),4);g.FillRectangle(fill,rect);g.DrawRectangle(line,rect);g.DrawLine(line,rect.Left+8,rect.Top+8,rect.Right-8,rect.Bottom-8);g.DrawLine(line,rect.Right-8,rect.Top+8,rect.Left+8,rect.Bottom-8);
        }
        Crate(new(355,285,175,135),Color.FromArgb(30,31,26));Crate(new(470,355,210,140),Color.FromArgb(44,42,32));Crate(new(210,375,140,110),Color.FromArgb(22,25,24));
        using(var mark=new Pen(Color.FromArgb(47,50,43),3)){g.DrawEllipse(mark,400,300,34,34);g.DrawRectangle(mark,550,380,55,35);}
        var grays=new[]{8,16,32,64,128,192,240};for(var i=0;i<grays.Length;i++){using var brush=new SolidBrush(Color.FromArgb(grays[i],grays[i],grays[i]));g.FillRectangle(brush,28+i*43,503,37,19);}
        var colors=new[]{Color.FromArgb(120,42,35),Color.FromArgb(36,90,55),Color.FromArgb(35,64,110)};for(var i=0;i<3;i++){using var brush=new SolidBrush(colors[i]);g.FillRectangle(brush,780+i*48,503,40,19);}
        return image;
    }
}

internal sealed class ComparisonPreview : Control
{
    Bitmap? original,effect;double split=.5;bool dragging;
    internal Scene Scene {get;private set;}
    internal Tone Target {get;private set;}=Tone.Identity;
    internal string SourceName {get;private set;}="内置样图";
    internal ComparisonPreview(){DoubleBuffered=true;BackColor=Color.FromArgb(13,17,22);Cursor=Cursors.SizeWE;TabStop=true;AccessibleName="原图与调整效果，左右方向键移动分界线";}
    internal void UseSample(Settings settings){SetSource(PreviewRenderer.CreateSample(),"内置样图",settings);}
    internal void LoadFile(string file,Settings settings)
    {
        if(new FileInfo(file).Length>40*1024*1024)throw new IOException("图片超过 40MB，请先缩小截图。");
        using var stream=File.OpenRead(file);using var image=Image.FromStream(stream,true,true);SetSource(PreviewRenderer.BoundedCopy(image),Path.GetFileName(file),settings);
    }
    void SetSource(Bitmap bitmap,string name,Settings settings)
    {
        try
        {
            var scene=PreviewRenderer.Analyze(bitmap,settings.CenterWeight);var target=AdaptiveMeter.TargetTone(scene,settings);var rendered=PreviewRenderer.Render(bitmap,target);
            original?.Dispose();effect?.Dispose();original=bitmap;effect=rendered;Scene=scene;Target=target;SourceName=name;Invalidate();
        }
        catch{bitmap.Dispose();throw;}
    }
    internal void RefreshEffect(Settings settings)
    {
        if(original==null)return;var scene=PreviewRenderer.Analyze(original,settings.CenterWeight);var target=AdaptiveMeter.TargetTone(scene,settings);var next=PreviewRenderer.Render(original,target);
        effect?.Dispose();effect=next;Scene=scene;Target=target;Invalidate();
    }
    Rectangle ImageBounds()
    {
        if(original==null)return Rectangle.Empty;var scale=Math.Min(Width/(double)original.Width,Height/(double)original.Height);var w=(int)(original.Width*scale);var h=(int)(original.Height*scale);return new((Width-w)/2,(Height-h)/2,w,h);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(original==null || effect==null)return;var rect=ImageBounds();if(rect.Width<1 || rect.Height<1)return;
        e.Graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;e.Graphics.DrawImage(original,rect);
        var x=rect.Left+(int)(rect.Width*split);var saved=e.Graphics.Save();e.Graphics.SetClip(new Rectangle(x,rect.Top,rect.Right-x,rect.Height));e.Graphics.DrawImage(effect,rect);e.Graphics.Restore(saved);
        using var line=new Pen(Color.FromArgb(225,233,239),2);e.Graphics.DrawLine(line,x,rect.Top,x,rect.Bottom);using var handle=new SolidBrush(Color.FromArgb(225,233,239));e.Graphics.FillEllipse(handle,x-12,rect.Top+rect.Height/2-12,24,24);
        using var arrows=new Pen(Color.FromArgb(28,39,49),2);var cy=rect.Top+rect.Height/2;e.Graphics.DrawLines(arrows,[new Point(x-4,cy-4),new Point(x-7,cy),new Point(x-4,cy+4)]);e.Graphics.DrawLines(arrows,[new Point(x+4,cy-4),new Point(x+7,cy),new Point(x+4,cy+4)]);
        void Badge(string text,int bx){using var bg=new SolidBrush(Color.FromArgb(185,15,20,26));e.Graphics.FillRectangle(bg,bx,rect.Top+12,52,26);TextRenderer.DrawText(e.Graphics,text,Font,new Rectangle(bx,rect.Top+12,52,26),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        if(split>.15)Badge("原图",rect.Left+12);if(split<.85)Badge("效果",rect.Right-64);
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left && ImageBounds().Contains(e.Location)){Focus();dragging=true;Capture=true;MoveSplit(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)MoveSplit(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;}
    void MoveSplit(int x){var rect=ImageBounds();if(rect.Width>0){split=Math.Clamp((x-rect.Left)/(double)rect.Width,0,1);Invalidate();}}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);split=e.KeyCode switch{Keys.Left=>Math.Max(0,split-.05),Keys.Right=>Math.Min(1,split+.05),Keys.Home=>0,Keys.End=>1,_=>split};Invalidate();}
    protected override void Dispose(bool disposing){if(disposing){original?.Dispose();effect?.Dispose();}base.Dispose(disposing);}
}
