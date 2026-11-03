using System.Windows;

namespace UPM.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // StartupUri(MainWindow) 생성 전에 저장된 테마 적용
            ThemeManager.Initialize();
            base.OnStartup(e);
        }
    }
}
