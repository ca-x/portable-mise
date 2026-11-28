using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PortableMise.Models;

using PortableMise.Localization;

namespace PortableMise.Converters;

public sealed class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PageSelectedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int page && int.TryParse(parameter?.ToString(), out int expected) && page == expected;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class ProxyModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ProxyMode.System => UiText.T("跟随系统代理"),
        ProxyMode.Disabled => UiText.T("直接连接"),
        ProxyMode.Custom => UiText.T("自定义 HTTP / SOCKS5"),
        _ => UiText.T("请选择代理模式")
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ActiveToolLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? UiText.T("当前使用") : UiText.T("已安装");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ToolStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ToolStatus.Active ? UiText.T("当前使用") : UiText.T("已安装");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 4 || values.Any(value => value is not double)) return 0d;
        double width = (double)values[0], value = (double)values[1], minimum = (double)values[2], maximum = (double)values[3];
        return maximum <= minimum ? 0d : Math.Clamp((value - minimum) / (maximum - minimum), 0d, 1d) * Math.Max(0d, width);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CloseBehaviorLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value.ToString() switch
    {
        "Exit" => UiText.T("退出程序"),
        "Tray" => UiText.T("收起到托盘"),
        _ => UiText.T("每次询问")
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
