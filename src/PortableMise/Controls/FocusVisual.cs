using System.Windows;
using System.Windows.Input;

namespace PortableMise.Controls;

/// <summary>
/// Tracks whether focus rings should be visible: keyboard navigation turns them on,
/// pointer interaction turns them off. Attached to the window; templates read it to
/// keep focus indicators out of mouse-driven interactions, mirroring :focus-visible.
/// </summary>
public static class FocusVisual
{
    public static readonly DependencyProperty KeyboardNavigationActiveProperty = DependencyProperty.RegisterAttached(
        "KeyboardNavigationActive", typeof(bool), typeof(FocusVisual), new PropertyMetadata(false));
    public static void SetKeyboardNavigationActive(DependencyObject target, bool value) => target.SetValue(KeyboardNavigationActiveProperty, value);
    public static bool GetKeyboardNavigationActive(DependencyObject target) => (bool)target.GetValue(KeyboardNavigationActiveProperty);

    /// <summary>Wires key and mouse preview handlers so the flag tracks the input modality.</summary>
    public static readonly DependencyProperty IsTrackingProperty = DependencyProperty.RegisterAttached(
        "IsTracking", typeof(bool), typeof(FocusVisual), new PropertyMetadata(false, OnIsTrackingChanged));
    public static void SetIsTracking(DependencyObject target, bool value) => target.SetValue(IsTrackingProperty, value);
    public static bool GetIsTracking(DependencyObject target) => (bool)target.GetValue(IsTrackingProperty);

    private static void OnIsTrackingChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window) return;
        if ((bool)e.OldValue)
        {
            window.PreviewKeyDown -= OnWindowPreviewKeyDown;
            window.PreviewMouseDown -= OnWindowPreviewMouseDown;
        }
        if ((bool)e.NewValue)
        {
            window.PreviewKeyDown += OnWindowPreviewKeyDown;
            window.PreviewMouseDown += OnWindowPreviewMouseDown;
        }
    }

    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
                SetKeyboardNavigationActive((DependencyObject)sender, true);
                break;
            case Key.Enter:
            case Key.Space:
                break;
            default:
                SetKeyboardNavigationActive((DependencyObject)sender, false);
                break;
        }
    }

    private static void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        SetKeyboardNavigationActive((DependencyObject)sender, false);
}
