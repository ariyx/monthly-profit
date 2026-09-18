using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Profit.Desktop;

// The self-contained published executable doubles as a per-user installer when named *-Setup.exe.
public sealed class InstallWindow : Window
{
    public InstallWindow()
    {
        Title = "نصب شرکت متحد توزیع ایرانیان"; Width = 540; Height = 390; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; FlowDirection = FlowDirection.LeftToRight;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        var headerText = new StackPanel { FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Stretch };
        headerText.Children.Add(new TextBlock { Text = "شرکت متحد توزیع ایرانیان", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft });
        headerText.Children.Add(new TextBlock { Text = "نصب سامانه مدیریت سود ماهانه", Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 7, 0, 0), TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(27, 22, 27, 20), FlowDirection = FlowDirection.LeftToRight, Child = headerText });
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 18), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(panel, 1); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "برنامه برای همین کاربر ویندوز نصب می‌شود و اطلاعات ماه‌های قبلی شما حفظ خواهند شد.", TextWrapping = TextWrapping.Wrap, LineHeight = 27, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft });
        panel.Children.Add(new TextBlock { Text = "پس از نصب، برنامه به‌صورت خودکار اجرا می‌شود.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 10, 0, 12), TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft });
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft }; panel.Children.Add(result);
        var install = new Button { Content = "نصب و اجرای برنامه", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(install);
        var footer = new TextBlock { Text = "Powered by Webilo (Sadegh Malekzadeh)", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 6, 12, 14), FlowDirection = FlowDirection.LeftToRight }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        install.Click += (_, _) =>
        {
            install.IsEnabled = false; result.Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)); result.Text = "در حال نصب…";
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
                    result.Text = "نصب با موفقیت انجام شد؛ برنامه در حال اجرا است.";
                    installedPath = executable;
                }
                finally { probe.ReleaseMutex(); }
                Process.Start(new ProcessStartInfo { FileName = installedPath, WorkingDirectory = Path.GetDirectoryName(installedPath), UseShellExecute = true });
                Close();
            }
            catch (Exception ex) { result.Foreground = Brushes.Firebrick; result.Text = "نصب کامل نشد: " + ex.Message; install.IsEnabled = true; }
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
