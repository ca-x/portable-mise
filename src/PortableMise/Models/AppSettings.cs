using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PortableMise.Models;

public enum ProxyMode { System, Disabled, Custom }
public enum CloseBehavior { Ask, Exit, Tray }

public sealed class AppSettings : INotifyPropertyChanged
{
    private string miseDirectory = "mise", toolsDirectory = "tools", proxyAddress = "http://127.0.0.1:7890";
    private ProxyMode proxyMode = ProxyMode.System;
    private bool askBeforeClosing = true, closeToTray = true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string MiseDirectory { get => miseDirectory; set => Set(ref miseDirectory, value); }
    public string ToolsDirectory { get => toolsDirectory; set => Set(ref toolsDirectory, value); }
    public ProxyMode ProxyMode { get => proxyMode; set => Set(ref proxyMode, value); }
    public string ProxyAddress { get => proxyAddress; set => Set(ref proxyAddress, value); }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get => closeToTray; set => Set(ref closeToTray, value); }
    public bool AskBeforeClosing { get => askBeforeClosing; set => Set(ref askBeforeClosing, value); }
    public bool AutoUpdateTools { get; set; }
    public bool AutoUpdateMise { get; set; }
    public bool AutoUpdateApplication { get; set; }
    public int UpdateIntervalHours { get; set; } = 24;
    public DateTimeOffset? LastToolsUpdateUtc { get; set; }
    public DateTimeOffset? LastMiseUpdateUtc { get; set; }
    public DateTimeOffset? LastApplicationCheckUtc { get; set; }
    public List<string> LastRegisteredPaths { get; set; } = [];

    public AppSettings Copy()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.PropertyChanged = null;
        copy.LastRegisteredPaths = [.. LastRegisteredPaths];
        return copy;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
