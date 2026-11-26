using System.Windows;
using System.Windows.Threading;
using PortableMise.Localization;

namespace PortableMise.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        UiText.Configure("zh-CN");
        string root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "evidence"));
        Directory.CreateDirectory(root);
        int exit = 0;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PortableMise;component/Themes/AppStyles.xaml") });
        app.Dispatcher.BeginInvoke(new Action(async () => {
            try
            {
                int count = await RuntimeTests.RunAsync(Path.Combine(root, "runtime"));
                count += await InstallerTests.RunAsync(Path.Combine(root, "installer"));
                count += await UpdaterTests.RunAsync(Path.Combine(root, "updater"));
                count += await ViewModelTests.RunAsync(Path.Combine(root, "viewmodel"));
                count += await WpfControlTests.RunAsync(Path.Combine(root, "controls"));
                count += await LanguageMirrorTests.RunAsync(Path.Combine(root, "mirrors"));
                count += await PortableEcosystemTests.RunAsync(Path.Combine(root, "ecosystem"));
                UiText.Configure("en-US");
                if (UiText.T("mise工具箱") != "Mise Toolbox" ||
                    UiText.F("正在安装 {0} 项", 3) != "Installing 3 tool(s)" ||
                    UiText.T("导出工具") != "Export tools")
                    throw new InvalidOperationException("Embedded English dictionaries did not load.");
                UiText.Configure("zh-CN");
                if (UiText.T("mise工具箱") != "mise工具箱")
                    throw new InvalidOperationException("Chinese text was not restored.");
                Console.WriteLine("PASS: embedded Chinese and English text, including formatted live status");
                count++;
                if (args.Contains("--real")) count += await RealIntegrationTests.RunAsync(Path.Combine(root, "real"));
                Console.WriteLine($"TOTAL: {count} assertions passed.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
            finally { app.Shutdown(exit); }
        }), DispatcherPriority.ApplicationIdle);
        app.Run();
        return exit;
    }
}
