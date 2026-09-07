using System.IO;
using System.Windows;

namespace Profit.Desktop;
public partial class App : Application
{
    private Mutex? single;
    private bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").EndsWith("-Setup", StringComparison.OrdinalIgnoreCase))
        {
            new InstallWindow().Show(); return;
        }
        single = new Mutex(false, "Local\\MonthlyProfit-Desktop-v1");
        try { ownsMutex = single.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) { MessageBox.Show("برنامه از قبل باز است."); Shutdown(); return; }
        try { new MainWindow().Show(); }
        catch (Exception ex) { MessageBox.Show("برنامه باز نشد. اطلاعات قبلی پاک نشده است.\n" + ex.Message, "خطا"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { if (ownsMutex) single?.ReleaseMutex(); single?.Dispose(); base.OnExit(e); }
}
