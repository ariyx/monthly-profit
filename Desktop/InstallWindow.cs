using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Profit.Desktop;

// The self-contained published executable doubles as a per-user installer when named *-Setup.exe.
public sealed class InstallWindow : Window
{
    public InstallWindow()
    {
        Title = "نصب مدیریت سود ماهانه"; Width = 520; Height = 350; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "مدیریت سود ماهانه", FontSize = 23, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "نصب برای همین کاربر ویندوز انجام می‌شود.\nاطلاعات ماه‌های قبلی حفظ می‌شوند.\nExcel یا ابزار برنامه‌نویسی لازم نیست.", TextWrapping = TextWrapping.Wrap, LineHeight = 28 });
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 8) }; panel.Children.Add(result);
        var install = new Button { Content = "نصب و اجرای برنامه", Style = (Style)FindResource("Primary") }; panel.Children.Add(install);
        install.Click += (_, _) =>
        {
            try
            {
                using var probe = new Mutex(false, "Local\\MonthlyProfit-Desktop-v1");
                bool available; try { available = probe.WaitOne(0); } catch (AbandonedMutexException) { available = true; }
                if (!available) throw new IOException("ابتدا نسخه بازِ برنامه را ببندید و دوباره نصب را بزنید.");
                string installedPath = "";
                try
                {
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MonthlyProfit");
                    Directory.CreateDirectory(folder); var executable = Path.Combine(folder, "MonthlyProfit.exe");
                    var staging = executable + ".new";
                    try { File.Copy(Environment.ProcessPath ?? throw new IOException("مسیر نصب‌کننده یافت نشد."), staging, true); File.Move(staging, executable, true); }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                    // Some Windows code pages cannot marshal Persian shortcut filenames through WScript.Shell.
                    TryMakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Monthly Profit.lnk"), executable);
                    var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MonthlyProfit"); Directory.CreateDirectory(menu);
                    TryMakeShortcut(Path.Combine(menu, "Monthly Profit.lnk"), executable);
                    result.Text = "نصب انجام شد.";
                    installedPath = executable;
                }
                finally { probe.ReleaseMutex(); }
                Process.Start(new ProcessStartInfo { FileName = installedPath, WorkingDirectory = Path.GetDirectoryName(installedPath), UseShellExecute = true });
                Close();
            }
            catch (Exception ex) { result.Text = "نصب کامل نشد: " + ex.Message; }
        };
    }
    static void TryMakeShortcut(string path, string executable)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("ساخت میانبر ویندوز در دسترس نیست.");
            dynamic shell = Activator.CreateInstance(type)!; dynamic shortcut = shell.CreateShortcut(path);
            try { shortcut.TargetPath = executable; shortcut.WorkingDirectory = Path.GetDirectoryName(executable); shortcut.Description = "Monthly Profit"; shortcut.Save(); }
            finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
        catch { /* The installed executable remains usable even when shortcut creation is blocked. */ }
    }
}
