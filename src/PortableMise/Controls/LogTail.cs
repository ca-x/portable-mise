using System.Windows;
using System.Windows.Controls;

namespace PortableMise.Controls;

public static class LogTail
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(LogTail), new PropertyMetadata(false, OnChanged));
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox textBox) return;
        textBox.TextChanged -= OnTextChanged;
        if (e.NewValue is true) textBox.TextChanged += OnTextChanged;
    }
    private static void OnTextChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
