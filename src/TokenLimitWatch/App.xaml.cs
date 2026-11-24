using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TokenLimitWatch.Poc;
namespace TokenLimitWatch;
public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2&&e.Args[0]=="--verify-design")
        {
            Shutdown(DesignVerification.WriteReport(this,e.Args[1])?0:1);
            return;
        }
        // Preview runs use sample data only, so they may run beside the real app.
        if(!e.Args.Contains("--preview") && !ClaimSingleInstance())
        {
            Shutdown(0);
            return;
        }
        DispatcherUnhandledException += OnUnhandledException;
        Loc.Lang = Loc.Default(); // MonitorWindow switches to the saved language; this covers a failed start.
        try
        {
            var window = new MonitorWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(Loc.T("앱을 시작하지 못했습니다.") + "\n" + ex.Message, MonitorWindow.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // One instance per install folder: copies in the same folder share data\webview2 and the
    // tray, so a second launch only brings the running window to the front and exits.
    private bool ClaimSingleInstance()
    {
        var folder = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        var mutexName = @"Local\TokenLimitWatch." + folder;
        var eventName = mutexName + ".Activate";
        _instance = new Mutex(true, mutexName, out var created);
        if (!created)
        {
            _instance.Dispose(); _instance = null;
            try { using var running = EventWaitHandle.OpenExisting(eventName); running.Set(); } catch { }
            return false;
        }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.InvokeAsync(() => (MainWindow as MonitorWindow)?.Restore()), null, Timeout.Infinite, false);
        return true;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray app should survive one failed action; log it and tell the user instead of exiting.
        LogError(e.Exception);
        e.Handled = true;
        try { MessageBox.Show(Loc.T("작업을 완료하지 못했습니다.") + "\n" + e.Exception.Message, MonitorWindow.AppName, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
    }

    private static void LogError(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Paths.Data, "error.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}"); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activate?.Dispose();
        if (_instance != null) { _instance.ReleaseMutex(); _instance.Dispose(); }
        base.OnExit(e);
    }
}
