using System.Configuration;
using System.IO;
using System.Data;
using System.Windows;
using DEX.Core.Localization;

namespace DEX.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.Handled = true;
            MessageBox.Show(
                Text.T("예기치 않은 오류가 발생했습니다.", "An unexpected error occurred.") + "\n\n" + e.Exception.Message,
                "COMPIVOX",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogCrash(ex);
        };
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEX");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch
        {
            // 로그 기록 실패는 무시한다.
        }
    }
}

