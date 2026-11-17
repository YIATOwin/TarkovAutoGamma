internal static class Program
{
    [STAThread]static void Main(string[] args)
    {
        if(args.FirstOrDefault()=="--watchdog"){Environment.ExitCode=RecoveryGuard.Watch(args);return;}
        ApplicationConfiguration.Initialize();
        if(args.FirstOrDefault()=="--recovery-fixture"){Environment.ExitCode=NativeChecks.Fixture(args);return;}
        using var mutex=new Mutex(true,"Local\\YIATOwin.TarkovAutoGamma",out var first);
        if(!first){MessageBox.Show("工具已经运行，请从系统托盘打开。","TarkovAutoGamma");return;}
        try{GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());}
        catch(Exception e){MessageBox.Show("上次的显示颜色尚未恢复，请先关闭其他色彩工具，再重新打开本软件。\n"+e.Message,"恢复未完成");return;}
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if(args.FirstOrDefault()=="--native-test"){Environment.ExitCode=NativeChecks.Run(args[1]);return;}
        try
        {
            using var form=new MainForm();
            if(args.Contains("--ui-test"))
            {
                form.Show();Application.DoEvents();using var image=new System.Drawing.Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new System.Drawing.Rectangle(0,0,form.Width,form.Height));image.Save(Path.Combine(AppContext.BaseDirectory,"ui-test.png"));return;
            }
            Application.Run(form);
        }
        catch(Exception e){FilterEngine.Log(e);try{GammaSession.Recover(Settings.RecoveryPath,new GammaDevice());}catch{}MessageBox.Show(e.Message,"TarkovAutoGamma 已退出");}
    }
}
