using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PortableMise.Services;

public interface IUserEnvironmentStore
{
    string? Read(string name);
    void Write(string name, string? value);
    void Broadcast();
}

public sealed class UserEnvironmentStore : IUserEnvironmentStore
{
    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment", false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment", true);
        if (value is null) key.DeleteValue(name, false);
        else key.SetValue(name, value, name.Equals("Path", StringComparison.OrdinalIgnoreCase) ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public void Broadcast()
    {
        _ = SendMessageTimeout(new IntPtr(0xffff), 0x1a, UIntPtr.Zero, "Environment", 2, 3000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
