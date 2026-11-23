using System.Windows;
namespace TokenLimitWatch;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2&&e.Args[0]=="--verify-design")
        {
            Shutdown(DesignVerification.WriteReport(this,e.Args[1])?0:1);
            return;
        }
        var window = new MonitorWindow();
        MainWindow = window;
        window.Show();
    }
}
