using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed class MainForm : Form
{
    readonly Settings settings=Settings.Load();readonly FilterEngine engine;
    readonly ComboBox display=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=205};
    readonly ComboBox frequency=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox weight=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly TrackBar strength=Slider(0,100),gamma=Slider(100,200),brightness=Slider(-20,20),contrast=Slider(-30,30);
    readonly NumericUpDown transition=new(){Minimum=100,Maximum=4000,Increment=50,Dock=DockStyle.Fill};
    readonly CheckBox gameOnly=new(){Text="仅游戏前台",AutoSize=true};
    readonly Button start=new(){Text="开启",AutoSize=true},restore=new(){Text="恢复",AutoSize=true},minimize=new(){Text="收起",AutoSize=true};
    readonly Label state=new(){Text="已停止",AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    readonly Label details=new(){Text="详情",AutoSize=true,Anchor=AnchorStyles.Right};
    readonly Label sourceName=new(){Text="内置样图",AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    readonly Label targetInfo=new(){AutoSize=true,Anchor=AnchorStyles.Right,TextAlign=ContentAlignment.MiddleRight};
    readonly ComparisonPreview preview=new(){Dock=DockStyle.Fill,MinimumSize=new(200,140)};
    readonly ToolTip tips=new(){InitialDelay=450,ReshowDelay=100,AutoPopDelay=15000,ShowAlways=true};
    readonly System.Windows.Forms.Timer previewDelay=new(){Interval=90};
    readonly NotifyIcon tray=new(){Icon=SystemIcons.Application,Text="TarkovAutoGamma · 已停止",Visible=true};
    readonly Dictionary<TrackBar,Label> values=[];
    bool initialized,closing,stopping;string? hotkeyWarning;string metricsTip="开启后可查看采集、分析、写入耗时和驱动曲线状态。";
    static TrackBar Slider(int min,int max)=>new(){Minimum=min,Maximum=max,TickStyle=TickStyle.None,AutoSize=false,Height=32,Dock=DockStyle.Fill,Margin=new(0)};
    internal MainForm()
    {
        Text="TarkovAutoGamma";ClientSize=new(1000,610);MinimumSize=new(870,580);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Microsoft YaHei UI",9.5f);BackColor=Color.FromArgb(18,23,29);ForeColor=Color.FromArgb(223,231,239);
        engine=new(settings with{},s=>{if(!IsDisposed && IsHandleCreated)try{BeginInvoke(()=>UpdateStatus(s));}catch(InvalidOperationException){}});
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(18),ColumnCount=1,RowCount=4};root.ColumnStyles.Add(new(SizeType.Percent,100));
        root.RowStyles.Add(new(SizeType.Absolute,46));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,54));root.RowStyles.Add(new(SizeType.Absolute,24));
        var toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new(0)};toolbar.ColumnStyles.Add(new(SizeType.Percent,100));toolbar.ColumnStyles.Add(new(SizeType.AutoSize));
        var monitor=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false};monitor.Controls.Add(new Label{Text="显示器",AutoSize=true,Margin=new(0,6,9,0)});monitor.Controls.Add(display);
        var load=new Button{Text="载入截图",AutoSize=true};var sample=new Button{Text="内置样图",AutoSize=true};var imageActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false};imageActions.Controls.AddRange([load,sample]);toolbar.Controls.Add(monitor,0,0);toolbar.Controls.Add(imageActions,1,0);root.Controls.Add(toolbar,0,0);
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=new(0)};content.ColumnStyles.Add(new(SizeType.Absolute,280));content.ColumnStyles.Add(new(SizeType.Absolute,14));content.ColumnStyles.Add(new(SizeType.Percent,100));
        var controls=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new(14),AutoScroll=true,BackColor=Color.FromArgb(27,34,42)};controls.ColumnStyles.Add(new(SizeType.Percent,100));
        AddSlider(controls,"Gamma",gamma,"暗处提亮的 Gamma 上限。\n默认 1.60；实际值会随画面亮度及总强度减弱。");
        AddSlider(controls,"亮度",brightness,"暗处的柔和亮度补偿，范围 -20～+20。\n0 不补偿；保留黑白端点，避免整体泛白。明亮画面会减弱。");
        AddSlider(controls,"对比度",contrast,"暗处的 S 曲线对比度，范围 -30～+30。\n正值加强层次，负值柔化明暗差；0 不额外调整。");
        AddSlider(controls,"总强度",strength,"同时控制 Gamma、亮度和对比度的自动幅度。\n0% 为原始曲线，100% 使用完整建议。");
        controls.Controls.Add(gameOnly);tips.SetToolTip(gameOnly,"只在 EscapeFromTarkov 位于所选显示器前台时调整。\n本窗口在前台时始终恢复显示颜色，防止预览被重复调色。");
        var more=new Button{Text="更多 ▾",AutoSize=true,Margin=new(0,0,8,0)};var reset=new Button{Text="重置",AutoSize=true,Margin=new(0)};
        var extraActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,WrapContents=false,Margin=new(0,8,0,0)};extraActions.Controls.AddRange([more,reset]);controls.Controls.Add(extraActions);
        var advanced=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Dock=DockStyle.Top,Visible=false,Margin=new(0,8,0,0)};advanced.ColumnStyles.Add(new(SizeType.Absolute,82));advanced.ColumnStyles.Add(new(SizeType.Percent,100));
        AddAdvanced(advanced,0,"检测频率",frequency,"每秒采集次数。默认 5 次；画面分析与曲线过渡独立运行。");
        AddAdvanced(advanced,1,"中心权重",weight,"中央区域的占比；剩余权重分给四边与四角。");
        AddAdvanced(advanced,2,"过渡 ms",transition,"默认约 300ms 到达新目标，含亮度与对比度。\n目标持续变化时会重新衔接；显卡写入延迟可能延长实际耗时。");controls.Controls.Add(advanced);
        tips.SetToolTip(reset,"恢复默认滤镜参数及 300ms 过渡，保留所选显示器。");
        more.Click+=(_,_)=>{advanced.Visible=!advanced.Visible;more.Text=advanced.Visible?"更多 ▴":"更多 ▾";};content.Controls.Add(controls,0,0);
        var imageCard=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=Color.FromArgb(27,34,42),Margin=new(0)};imageCard.ColumnStyles.Add(new(SizeType.Percent,100));imageCard.RowStyles.Add(new(SizeType.Percent,100));imageCard.RowStyles.Add(new(SizeType.Absolute,40));imageCard.Controls.Add(preview,0,0);
        var caption=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new(10,0,10,0),Margin=new(0)};caption.ColumnStyles.Add(new(SizeType.Percent,100));caption.ColumnStyles.Add(new(SizeType.AutoSize));caption.Controls.Add(sourceName,0,0);caption.Controls.Add(targetInfo,1,0);imageCard.Controls.Add(caption,0,1);content.Controls.Add(imageCard,2,0);root.Controls.Add(content,0,1);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new(0,10,0,0),WrapContents=false};var menuButton=new Button{Text="⋯",AutoSize=true};actions.Controls.AddRange([start,restore,minimize,menuButton]);root.Controls.Add(actions,0,2);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new(0)};footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.AutoSize));footer.Controls.Add(state,0,0);footer.Controls.Add(details,1,0);root.Controls.Add(footer,0,3);Controls.Add(root);
        foreach(var combo in new[]{display,frequency,weight})StyleCombo(combo);foreach(var input in new Control[]{transition}){input.BackColor=Color.FromArgb(39,49,61);input.ForeColor=ForeColor;}
        foreach(var button in new[]{start,restore,minimize,menuButton,load,sample,more,reset})StyleButton(button);start.BackColor=Color.FromArgb(32,108,139);
        tips.SetToolTip(display,"选择游戏使用的横向 SDR 显示器。开启前请关闭 HDR / 自动 HDR，以及其他 Gamma 工具。");tips.SetToolTip(load,"载入本机 PNG / JPEG / BMP 截图作为对比图，最大 40MB。\n只在本机预览，不上传，也不改变显示器颜色。");tips.SetToolTip(sample,"使用内置的暗处、亮窗及灰阶样图。");tips.SetToolTip(preview,"拖动分界线查看同一位置的原图与效果，也可用左右方向键。\n预览显示当前图片的最终目标曲线；游戏中按实时画面重新计算。\n图片模拟不包含显示器校准及驱动差异。");tips.SetToolTip(targetInfo,"基于当前预览图片计算的目标值。\n亮度和对比度会随暗部、总强度及高光保护减弱。");
        tips.SetToolTip(start,"开启 / 暂停自动调整：Ctrl+Alt+F9。\n本窗口处于前台时暂停硬件调整，回到游戏后继续。");tips.SetToolTip(restore,"停止并恢复开启前的颜色：Ctrl+Alt+F10。");tips.SetToolTip(minimize,"收起到系统托盘继续运行；双击托盘图标打开。\n关闭窗口会停止并退出。");
        tips.SetToolTip(details,metricsTip);details.MouseEnter+=(_,_)=>tips.SetToolTip(details,metricsTip);
        foreach(var screen in Screen.AllScreens)display.Items.Add(screen.DeviceName);display.SelectedItem=settings.Display;if(display.SelectedIndex<0)display.SelectedItem=Screen.PrimaryScreen?.DeviceName;if(display.SelectedIndex<0 && display.Items.Count>0)display.SelectedIndex=0;
        frequency.Items.AddRange(["2 次/秒","5 次/秒","10 次/秒"]);weight.Items.AddRange(["20%","40%","60%"]);LoadControls();
        display.SelectedIndexChanged+=(_,_)=>SaveSettings();frequency.SelectedIndexChanged+=(_,_)=>SaveSettings();weight.SelectedIndexChanged+=(_,_)=>SaveSettings();transition.ValueChanged+=(_,_)=>SaveSettings();gameOnly.CheckedChanged+=(_,_)=>SaveSettings();foreach(var slider in values.Keys)slider.ValueChanged+=(_,_)=>SaveSettings();
        previewDelay.Tick+=(_,_)=>{previewDelay.Stop();try{preview.RefreshEffect(settings);UpdatePreviewCaption();}catch(Exception e){state.Text="预览失败";tips.SetToolTip(state,e.Message);}};
        load.Click+=(_,_)=>ImportImage();sample.Click+=(_,_)=>{preview.UseSample(settings);UpdatePreviewCaption();};reset.Click+=(_,_)=>ResetSettings();
        start.Click+=async(_,_)=>await Toggle();restore.Click+=async(_,_)=>await StopAndRestore();minimize.Click+=(_,_)=>Hide();tray.DoubleClick+=(_,_)=>ShowWindow();
        var menu=new ContextMenuStrip();menu.Items.Add("打开",null,(_,_)=>ShowWindow());menu.Items.Add("开启 / 暂停",null,async(_,_)=>await Toggle());menu.Items.Add("恢复颜色",null,async(_,_)=>await StopAndRestore());menu.Items.Add("设置与日志",null,(_,_)=>OpenSettings());menu.Items.Add("退出",null,(_,_)=>Close());tray.ContextMenuStrip=menu;menuButton.Click+=(_,_)=>menu.Show(menuButton,new Point(0,menuButton.Height));
        SystemEvents.DisplaySettingsChanging+=DisplayChanged;SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        initialized=true;SaveSettings();preview.UseSample(settings);UpdatePreviewCaption();
    }
    void AddSlider(TableLayoutPanel parent,string name,TrackBar slider,string tip)
    {
        slider.AccessibleName=name;var row=new TableLayoutPanel{ColumnCount=2,RowCount=2,Dock=DockStyle.Top,Height=54,Margin=new(0,0,0,2)};row.ColumnStyles.Add(new(SizeType.Percent,100));row.ColumnStyles.Add(new(SizeType.Absolute,64));row.RowStyles.Add(new(SizeType.Absolute,23));row.RowStyles.Add(new(SizeType.Percent,100));
        var label=new Label{Text=name,AutoSize=true,Anchor=AnchorStyles.Left};var value=new Label{AutoSize=true,Anchor=AnchorStyles.Right};row.Controls.Add(label,0,0);row.Controls.Add(value,1,0);row.Controls.Add(slider,0,1);row.SetColumnSpan(slider,2);parent.Controls.Add(row);values.Add(slider,value);tips.SetToolTip(label,tip);tips.SetToolTip(slider,tip);tips.SetToolTip(value,tip);
    }
    void AddAdvanced(TableLayoutPanel parent,int row,string name,Control control,string tip)
    {
        parent.RowStyles.Add(new(SizeType.Absolute,34));var label=new Label{Text=name,AutoSize=true,Anchor=AnchorStyles.Left};parent.Controls.Add(label,0,row);parent.Controls.Add(control,1,row);tips.SetToolTip(label,tip);tips.SetToolTip(control,tip);
    }
    void StyleCombo(ComboBox combo)
    {
        combo.BackColor=Color.FromArgb(39,49,61);combo.ForeColor=ForeColor;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.FlatStyle=FlatStyle.Flat;combo.ItemHeight=23;
        combo.DrawItem+=(_,e)=>{if(e.Index<0)return;using var bg=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(44,90,115):combo.BackColor);e.Graphics.FillRectangle(bg,e.Bounds);TextRenderer.DrawText(e.Graphics,combo.Items[e.Index]?.ToString(),combo.Font,e.Bounds,combo.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};
    }
    void StyleButton(Button button){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(64,79,94);button.BackColor=Color.FromArgb(35,44,54);button.ForeColor=ForeColor;button.Padding=new(10,5,10,5);}
    void LoadControls()
    {
        frequency.SelectedIndex=Array.IndexOf(new[]{2,5,10},settings.Frequency);weight.SelectedIndex=Array.IndexOf(new[]{20,40,60},settings.CenterWeight);gamma.Value=(int)Math.Round(settings.MaxGamma*100);brightness.Value=settings.Brightness;contrast.Value=settings.Contrast;strength.Value=settings.Strength;transition.Value=settings.TransitionMs;gameOnly.Checked=settings.GameOnly;UpdateValues();
    }
    void UpdateValues(){values[gamma].Text=(gamma.Value/100.0).ToString("F2");values[brightness].Text=brightness.Value.ToString("+0;-0;0");values[contrast].Text=contrast.Value.ToString("+0;-0;0");values[strength].Text=strength.Value+"%";}
    void SaveSettings()
    {
        if(!initialized)return;settings.Display=display.SelectedItem?.ToString()??"";settings.Frequency=new[]{2,5,10}[frequency.SelectedIndex];settings.CenterWeight=new[]{20,40,60}[weight.SelectedIndex];settings.Strength=strength.Value;settings.MaxGamma=gamma.Value/100.0;settings.Brightness=brightness.Value;settings.Contrast=contrast.Value;settings.TransitionMs=(int)transition.Value;settings.GameOnly=gameOnly.Checked;UpdateValues();engine.Configuration=settings with{};
        try{settings.Save();}catch(Exception e){state.Text="设置保存失败";tips.SetToolTip(state,e.Message);}previewDelay.Stop();previewDelay.Start();
    }
    void ResetSettings()
    {
        initialized=false;var monitor=settings.Display;var defaults=new Settings();settings.Strength=defaults.Strength;settings.MaxGamma=defaults.MaxGamma;settings.Brightness=defaults.Brightness;settings.Contrast=defaults.Contrast;settings.Frequency=defaults.Frequency;settings.CenterWeight=defaults.CenterWeight;settings.TransitionMs=defaults.TransitionMs;settings.GameOnly=defaults.GameOnly;settings.Display=monitor;LoadControls();initialized=true;SaveSettings();
    }
    void UpdatePreviewCaption(){sourceName.Text=preview.SourceName;tips.SetToolTip(sourceName,preview.SourceName);var t=preview.Target;targetInfo.Text=$"Gamma {t.Gamma:F2} · 亮度 {t.Brightness*100:+0.0;-0.0;0} · 对比 {t.Contrast*100:+0.0;-0.0;0}";}
    void ImportImage()
    {
        using var dialog=new OpenFileDialog{Title="载入对比截图",Filter="截图|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",CheckFileExists=true};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{preview.LoadFile(dialog.FileName,settings);UpdatePreviewCaption();}catch(Exception e){MessageBox.Show(this,e.Message,"无法载入图片");}
    }
    void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    void OpenSettings(){try{Directory.CreateDirectory(Settings.Folder);Process.Start(new ProcessStartInfo(Settings.Folder){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message);}}
    async Task Toggle(){if(stopping || closing)return;if(engine.Running){await StopAndRestore();return;}if(string.IsNullOrWhiteSpace(settings.Display)){state.Text="请选择显示器";return;}engine.Start();display.Enabled=false;start.Text="暂停";}
    async Task StopAndRestore()
    {
        if(stopping)return;stopping=true;start.Enabled=restore.Enabled=false;try{await engine.Stop();GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());state.Text="已恢复";}catch(Exception e){state.Text="恢复失败";tips.SetToolTip(state,e.Message);FilterEngine.Log(e);}finally{stopping=false;start.Enabled=restore.Enabled=true;display.Enabled=true;start.Text="开启";}
    }
    void UpdateStatus(FilterStatus s)
    {
        state.Text=s.Message;tips.SetToolTip(state,s.Message);metricsTip=$"Gamma {s.Gamma:F3}  亮度 {s.Brightness*100:+0.0;-0.0;0}  对比度 {s.Contrast*100:+0.0;-0.0;0}\n采集 {s.CaptureMs:F2}ms · 分析 {s.AnalysisMs:F2}ms · 写入 {s.WriteMs:F2}ms\n累计 {s.Frames} 帧 · "+(s.Verified?"曲线回读已核对":"尚未核对曲线回读")+(hotkeyWarning==null?"":"\n"+hotkeyWarning);
        tray.Text="TarkovAutoGamma · "+(!s.CurrentTone.IsIdentity?$"Gamma {s.Gamma:F2}":"等待 / 已恢复");if(s.Stopped || !engine.Running){display.Enabled=true;start.Text="开启";}
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);var first=RegisterHotKey(Handle,1,0x4003,0x78);var second=RegisterHotKey(Handle,2,0x4003,0x79);if(!first || !second)hotkeyWarning="部分热键已被占用，可使用窗口或托盘按钮。";}
    protected override void OnShown(EventArgs e){base.OnShown(e);if(hotkeyWarning!=null){state.Text="部分快捷键已被占用";tips.SetToolTip(state,hotkeyWarning);}}
    protected override void WndProc(ref Message m){var command=m.Msg==0x0312?(int)m.WParam:0;base.WndProc(ref m);if(command==1)_=Toggle();if(command==2)_=StopAndRestore();}
    protected override void OnFormClosing(FormClosingEventArgs e){closing=true;try{engine.Stop().GetAwaiter().GetResult();GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());}catch(Exception ex){FilterEngine.Log(ex);MessageBox.Show(this,"颜色未能恢复，请重新打开本工具重试。\n"+ex.Message,"恢复未完成");}base.OnFormClosing(e);}
    void DisplayChanged(object? s,EventArgs e)=>RequestSystemStop("显示配置变化，请重新选择显示器");
    void SessionChanged(object s,SessionSwitchEventArgs e){if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff)RequestSystemStop("已锁定，自动调整已停止");}
    void PowerChanged(object s,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)RequestSystemStop("已休眠，自动调整已停止");}
    void RequestSystemStop(string reason){try{engine.Stop().GetAwaiter().GetResult();if(IsHandleCreated)BeginInvoke(()=>{state.Text=reason;display.Enabled=true;start.Text="开启";});}catch(Exception e){FilterEngine.Log(e);}}
    protected override void Dispose(bool disposing)
    {
        if(disposing){previewDelay.Stop();previewDelay.Dispose();tips.Dispose();SystemEvents.DisplaySettingsChanging-=DisplayChanged;SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;engine.Stop().GetAwaiter().GetResult();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();if(IsHandleCreated){UnregisterHotKey(Handle,1);UnregisterHotKey(Handle,2);}}
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
