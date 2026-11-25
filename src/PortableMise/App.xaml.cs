using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PortableMise.Models;
using PortableMise.Services;
using PortableMise.ViewModels;

namespace PortableMise;

public partial class App : System.Windows.Application
{
    private MainViewModel? viewModel;
    private TrayController? tray;
    private Mutex? instanceMutex;
    private DispatcherTimer? updateTimer;
    private bool exiting, shutdownStarted, choosingClose;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (await ApplicationUpdater.TryApplyUpdateModeAsync(e.Args)) { Shutdown(); return; }
            string root = AppContext.BaseDirectory;
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant())))[..24];
            instanceMutex = new Mutex(true, "Local\\PortableMise-" + key, out bool firstInstance);
            if (!firstInstance) { instanceMutex.Dispose(); instanceMutex = null; System.Windows.MessageBox.Show("此工作区已在运行，请从系统托盘打开。", "Portable mise"); Shutdown(); return; }
            var store = new SettingsStore(root);
            var settings = store.Load();
            var mise = new MiseService(root);
            var terminal = new TerminalIntegration(root, mise);
            var installer = new MiseInstaller(root);
            var updater = new ApplicationUpdater(root);
            viewModel = new MainViewModel(root, settings, store, mise, installer, terminal, updater);
            viewModel.ApplicationUpdateReady += async update => {
                try { updater.LaunchApply(update); await RequestExitAsync(); }
                catch (Exception error) { ShowError(error); }
            };
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            tray = new TrayController(ShowWindow, async () => await RequestExitAsync());
            window.Closing += OnWindowClosing;
            window.StateChanged += (_, _) => {
                if (window.WindowState == WindowState.Minimized && viewModel.Settings.MinimizeToTray) window.Hide();
            };
            DispatcherUnhandledException += (_, args) => { ShowError(args.Exception); args.Handled = true; };
            SessionEnding += (_, _) => { exiting = true; viewModel.CancelCommand.Execute(null); };
            window.Show();
            await viewModel.InitializeAsync();
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            updateTimer.Tick += async (_, _) => { if (viewModel != null && !shutdownStarted) await viewModel.CheckAutomaticUpdatesAsync(); };
            updateTimer.Start();
            await viewModel.CheckAutomaticUpdatesAsync();
        }
        catch (Exception error) { ShowError(error); exiting = true; Shutdown(1); }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (choosingClose || viewModel == null) return;
        choosingClose = true;
        try
        {
            bool closeToTray = viewModel.Settings.CloseToTray;
            if (viewModel.Settings.AskBeforeClosing)
            {
                var dialog = new CloseChoiceDialog { Owner = MainWindow };
                dialog.ShowDialog();
                if (dialog.CloseToTrayChoice is not bool choice || shutdownStarted) return;
                closeToTray = choice;
                if (dialog.RememberChoice)
                    try { await viewModel.RememberCloseChoiceAsync(choice); }
                    catch (Exception error) { ShowError(error); }
            }
            if (closeToTray) MainWindow?.Hide();
            else await RequestExitAsync();
        }
        finally { choosingClose = false; }
    }
    private void ShowWindow()
    {
        if (MainWindow == null) return;
        MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate();
    }
    private async Task RequestExitAsync()
    {
        if (shutdownStarted) return;
        shutdownStarted = true;
        updateTimer?.Stop();
        if (viewModel != null) await viewModel.ShutdownAsync();
        exiting = true;
        Shutdown();
    }
    private static void ShowError(Exception error) => System.Windows.MessageBox.Show(ProxySupport.Sanitize(error.Message), "Portable mise", MessageBoxButton.OK, MessageBoxImage.Error);
    protected override void OnExit(ExitEventArgs e)
    {
        updateTimer?.Stop();
        tray?.Dispose();
        viewModel?.Dispose();
        if (instanceMutex != null) { instanceMutex.ReleaseMutex(); instanceMutex.Dispose(); }
        base.OnExit(e);
    }
}
