using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed class MainForm : Form
{
    readonly Settings settings=Settings.Load();readonly FilterEngine engine;
    readonly ComboBox display=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox frequency=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox weight=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly TrackBar strength=new(){Minimum=0,Maximum=100,TickFrequency=10,Dock=DockStyle.Fill,AutoSize=false,Height=40};
    readonly NumericUpDown gamma=new(){Minimum=1,Maximum=2,DecimalPlaces=2,Increment=.05m,Dock=DockStyle.Fill};
    readonly NumericUpDown transition=new(){Minimum=300,Maximum=4000,Increment=100,Dock=DockStyle.Fill};
    readonly CheckBox gameOnly=new(){Text="仅在塔科夫处于前台时启用（推荐）",AutoSize=true,Checked=true};
    readonly Button start=new(){Text="开启自动调整",AutoSize=true},restore=new(){Text="停止并恢复颜色",AutoSize=true},minimize=new(){Text="收起到托盘",AutoSize=true};
    readonly Label state=new(){Text="尚未开启 · 当前显示颜色保持原样",AutoSize=true,Dock=DockStyle.Fill};
    readonly Label metrics=new(){Text="等待采样",AutoSize=true,Dock=DockStyle.Fill};
    readonly Label strengthLabel=new(){AutoSize=true};
    readonly CurvePanel curve=new(){Dock=DockStyle.Fill,MinimumSize=new(120,110)};
    readonly NotifyIcon tray=new(){Icon=SystemIcons.Application,Text="TarkovAutoGamma · 已停止",Visible=true};
    bool initialized,closing,stopping;FilterStatus latest=new("未开启");
    internal MainForm()
    {
        Text="TarkovAutoGamma 0.1.0 · 自动亮度适应";ClientSize=new(790,780);MinimumSize=new(700,650);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Microsoft YaHei UI",10);BackColor=Color.FromArgb(18,23,29);ForeColor=Color.FromArgb(223,231,239);
        engine=new(settings with{},s=>{if(!IsDisposed && IsHandleCreated)try{BeginInvoke(()=>UpdateStatus(s));}catch(InvalidOperationException){}});
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(26),ColumnCount=1,RowCount=7,AutoScroll=true};
        root.ColumnStyles.Add(new(SizeType.Percent,100));
        var title=new Label{Text="随环境变化，轻柔提亮暗部",Font=new(Font.FontFamily,18,FontStyle.Bold),AutoSize=true,Dock=DockStyle.Fill,Margin=new(0,0,0,8)};
        root.Controls.Add(title,0,0);
        root.Controls.Add(new Label{Text="九区域加权采样  /  SDR 显示器  /  独立运行",AutoSize=true,ForeColor=Color.FromArgb(139,160,180),Margin=new(0,0,0,18)},0,1);
        var form=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,Padding=new(12),BackColor=Color.FromArgb(27,34,42)};
        form.ColumnStyles.Add(new(SizeType.Absolute,150));form.ColumnStyles.Add(new(SizeType.Percent,100));form.ColumnStyles.Add(new(SizeType.Absolute,100));
        void Row(int row,string label,Control input,Control? extra=null)
        {
            form.RowStyles.Add(new(SizeType.Absolute,row==1?52:40));form.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,row);
            form.Controls.Add(input,1,row);if(extra!=null)form.Controls.Add(extra,2,row);else form.SetColumnSpan(input,2);
        }
        Row(0,"游戏所在显示器",display);Row(1,"滤镜强度",strength,strengthLabel);Row(2,"最强提亮 Gamma",gamma);Row(3,"检测频率",frequency);Row(4,"中心区域权重",weight);Row(5,"平滑时间（毫秒）",transition);
        form.Controls.Add(gameOnly,0,6);form.SetColumnSpan(gameOnly,3);root.Controls.Add(form,0,2);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new(0,16,0,12)};actions.Controls.AddRange([start,restore,minimize]);root.Controls.Add(actions,0,3);
        var live=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,BackColor=Color.FromArgb(27,34,42),Padding=new(12)};
        live.ColumnStyles.Add(new(SizeType.Percent,65));live.ColumnStyles.Add(new(SizeType.Percent,35));
        live.Controls.Add(state,0,0);live.Controls.Add(metrics,0,1);live.Controls.Add(curve,1,0);live.SetRowSpan(curve,2);root.Controls.Add(live,0,4);
        root.Controls.Add(new Label{Text="Ctrl+Alt+F9 开启 / 停止    ·    Ctrl+Alt+F10 立即恢复\n关闭窗口会停止并退出；需要后台运行时，请收起到托盘。\n开启前请关闭 HDR 和其他 Gamma 工具。全黑画面和疑似绿色夜视会减弱提亮。",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(139,160,180),Margin=new(0,14,0,0)},0,5);
        var logs=new LinkLabel{Text="打开设置与错误日志目录",AutoSize=true,LinkColor=Color.FromArgb(101,185,224),Margin=new(0,12,0,0)};logs.LinkClicked+=(_,_)=>{try{Directory.CreateDirectory(Settings.Folder);Process.Start(new ProcessStartInfo(Settings.Folder){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message);}};root.Controls.Add(logs,0,6);
        Controls.Add(root);
        foreach(var c in new Control[]{display,frequency,weight,gamma,transition}){c.BackColor=Color.FromArgb(39,49,61);c.ForeColor=ForeColor;}
        foreach(var combo in new[]{display,frequency,weight})
        {
            combo.DrawMode=DrawMode.OwnerDrawFixed;combo.FlatStyle=FlatStyle.Flat;combo.ItemHeight=24;
            combo.DrawItem+=(_,e)=>{if(e.Index<0)return;using var bg=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(44,90,115):combo.BackColor);e.Graphics.FillRectangle(bg,e.Bounds);TextRenderer.DrawText(e.Graphics,combo.Items[e.Index]?.ToString(),combo.Font,e.Bounds,combo.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};
        }
        foreach(Button b in actions.Controls){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(72,92,111);b.BackColor=Color.FromArgb(39,49,61);b.ForeColor=ForeColor;b.Padding=new(9,6,9,6);}
        start.BackColor=Color.FromArgb(32,108,139);
        foreach(var screen in Screen.AllScreens)display.Items.Add(screen.DeviceName);
        display.SelectedItem=settings.Display;if(display.SelectedIndex<0)display.SelectedItem=Screen.PrimaryScreen?.DeviceName;if(display.SelectedIndex<0 && display.Items.Count>0)display.SelectedIndex=0;
        frequency.Items.AddRange(["2 次 / 秒（省电）","5 次 / 秒（推荐）","10 次 / 秒"]);frequency.SelectedIndex=Array.IndexOf(new[]{2,5,10},settings.Frequency);
        weight.Items.AddRange(["20%（更均衡）","40%（推荐）","60%（优先中心）"]);weight.SelectedIndex=Array.IndexOf(new[]{20,40,60},settings.CenterWeight);
        strength.Value=settings.Strength;gamma.Value=(decimal)settings.MaxGamma;transition.Value=settings.TransitionMs;gameOnly.Checked=settings.GameOnly;strengthLabel.Text=settings.Strength+"%";
        display.SelectedIndexChanged+=(_,_)=>SaveSettings();frequency.SelectedIndexChanged+=(_,_)=>SaveSettings();weight.SelectedIndexChanged+=(_,_)=>SaveSettings();strength.ValueChanged+=(_,_)=>SaveSettings();gamma.ValueChanged+=(_,_)=>SaveSettings();transition.ValueChanged+=(_,_)=>SaveSettings();gameOnly.CheckedChanged+=(_,_)=>SaveSettings();
        start.Click+=async(_,_)=>await Toggle();restore.Click+=async(_,_)=>await StopAndRestore();minimize.Click+=(_,_)=>{Hide();tray.ShowBalloonTip(2000,"TarkovAutoGamma","仍在后台运行；双击托盘图标打开。",ToolTipIcon.Info);};
        tray.DoubleClick+=(_,_)=>ShowWindow();var menu=new ContextMenuStrip();menu.Items.Add("打开",null,(_,_)=>ShowWindow());menu.Items.Add("开启 / 停止",null,async(_,_)=>await Toggle());menu.Items.Add("恢复颜色",null,async(_,_)=>await StopAndRestore());menu.Items.Add("退出",null,(_,_)=>Close());tray.ContextMenuStrip=menu;
        SystemEvents.DisplaySettingsChanging+=DisplayChanged;SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        initialized=true;SaveSettings();
    }
    void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    void SaveSettings()
    {
        if(!initialized)return;settings.Display=display.SelectedItem?.ToString()??"";settings.Frequency=new[]{2,5,10}[frequency.SelectedIndex];settings.CenterWeight=new[]{20,40,60}[weight.SelectedIndex];settings.Strength=strength.Value;settings.MaxGamma=(double)gamma.Value;settings.TransitionMs=(int)transition.Value;settings.GameOnly=gameOnly.Checked;
        strengthLabel.Text=settings.Strength+"%";engine.Configuration=settings with{};
        try{settings.Save();}catch(Exception e){state.Text="偏好保存失败："+e.Message;}
    }
    async Task Toggle()
    {
        if(stopping || closing)return;if(engine.Running){await StopAndRestore();return;}
        if(string.IsNullOrWhiteSpace(settings.Display)){state.Text="请先选择显示器。";return;}
        engine.Start();display.Enabled=false;start.Text="停止自动调整";
    }
    async Task StopAndRestore()
    {
        if(stopping)return;stopping=true;start.Enabled=restore.Enabled=false;
        try{await engine.Stop();GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());state.Text="已停止，原始颜色已恢复。";}
        catch(Exception e){state.Text="恢复失败，请重试："+e.Message;FilterEngine.Log(e);}
        finally{stopping=false;start.Enabled=restore.Enabled=true;display.Enabled=true;start.Text="开启自动调整";}
    }
    void UpdateStatus(FilterStatus s)
    {
        latest=s;state.Text=s.Message;curve.Gamma=s.Gamma;curve.Invalidate();
        metrics.Text=$"\n当前 Gamma  {s.Gamma:F3}   ·   加权亮度  {s.Scene.Median:P0}\n采集 {s.CaptureMs:F2} ms  /  分析 {s.AnalysisMs:F2} ms\n最近写入 {s.WriteMs:F2} ms  ·  累计 {s.Frames} 帧\n"+(s.Verified?"驱动曲线回读已核对":"提亮后将核对驱动曲线回读");
        tray.Text="TarkovAutoGamma · "+(s.Gamma>1.01?$"Gamma {s.Gamma:F2}":"等待 / 已恢复");
        if(s.Stopped || !engine.Running){display.Enabled=true;start.Text="开启自动调整";}
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);var a=RegisterHotKey(Handle,1,0x4003,0x78);var b=RegisterHotKey(Handle,2,0x4003,0x79);
        if(!a || !b)Shown+=(_,_)=>state.Text="部分热键被其他程序占用，可使用窗口或托盘按钮。";
    }
    protected override void WndProc(ref Message m)
    {
        var command=m.Msg==0x0312?(int)m.WParam:0;base.WndProc(ref m);
        if(command==1)_=Toggle();if(command==2)_=StopAndRestore();
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing=true;try{engine.Stop().GetAwaiter().GetResult();GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());}catch(Exception ex){FilterEngine.Log(ex);MessageBox.Show(this,"显示颜色未能恢复。请关闭其他色彩软件，再重新打开本工具恢复。\n"+ex.Message,"恢复未完成");}
        base.OnFormClosing(e);
    }
    void DisplayChanged(object? s,EventArgs e)=>RequestSystemStop("显示配置发生变化，已停止，请重新选择显示器后开启。");
    void SessionChanged(object s,SessionSwitchEventArgs e){if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff)RequestSystemStop("会话已锁定，自动调整已停止。");}
    void PowerChanged(object s,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)RequestSystemStop("系统休眠，自动调整已停止。");}
    void RequestSystemStop(string reason){try{engine.Stop().GetAwaiter().GetResult();if(IsHandleCreated)BeginInvoke(()=>{state.Text=reason;display.Enabled=true;start.Text="开启自动调整";});}catch(Exception e){FilterEngine.Log(e);}}
    protected override void Dispose(bool disposing)
    {
        if(disposing){SystemEvents.DisplaySettingsChanging-=DisplayChanged;SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;engine.Stop().GetAwaiter().GetResult();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();if(IsHandleCreated){UnregisterHotKey(Handle,1);UnregisterHotKey(Handle,2);}}
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
internal sealed class CurvePanel : Panel
{
    internal double Gamma=1;
    internal CurvePanel(){DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var r=new Rectangle(12,12,Math.Max(1,Width-24),Math.Max(1,Height-24));
        using var baseline=new Pen(Color.FromArgb(75,90,104));using var line=new Pen(Color.FromArgb(89,193,221),2);
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;e.Graphics.DrawLine(baseline,r.Left,r.Bottom,r.Right,r.Top);
        var points=Enumerable.Range(0,65).Select(i=>new PointF(r.Left+i/64f*r.Width,r.Bottom-(float)Math.Pow(i/64.0,1/Gamma)*r.Height)).ToArray();e.Graphics.DrawLines(line,points);
    }
}
