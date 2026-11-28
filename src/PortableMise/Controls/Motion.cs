using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PortableMise.Controls;

/// <summary>Shared animation helpers so every view uses the same motion language.</summary>
public static class Motion
{
    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(120));
    public static readonly Duration Normal = new(TimeSpan.FromMilliseconds(180));
    public static readonly Duration Slow = new(TimeSpan.FromMilliseconds(240));

    /// <summary>Plays a fade-and-rise entrance when the element loads, once per load.</summary>
    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached(
        "FadeIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFadeInChanged));
    public static void SetFadeIn(DependencyObject target, bool value) => target.SetValue(FadeInProperty, value);
    public static bool GetFadeIn(DependencyObject target) => (bool)target.GetValue(FadeInProperty);

    private static void OnFadeInChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.NewValue) element.Loaded += OnFadeInLoaded;
        else element.Loaded -= OnFadeInLoaded;
    }

    private static void OnFadeInLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnFadeInLoaded;
        PlayRiseIn(element);
    }

    /// <summary>Fades the element in while easing it up a few pixels. Reuses an existing TranslateTransform when present.</summary>
    public static void PlayRiseIn(FrameworkElement element, double fromPixels = 8)
    {
        TranslateTransform? translate = null;
        if (element.RenderTransform is TranslateTransform existing) translate = existing;
        else if (element.RenderTransform is TransformGroup group)
            translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
        else if (element.RenderTransform is null)
            element.RenderTransform = translate = new TranslateTransform();
        if (translate == null) { PlayFadeIn(element); return; }
        var fade = new DoubleAnimation(0, 1, Normal) { EasingFunction = new QuadraticEase() };
        var rise = new DoubleAnimation(fromPixels, 0, Slow) { EasingFunction = new QuadraticEase() };
        element.Opacity = 0;
        translate.Y = fromPixels;
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>Fades the element in without moving it, for elements that already carry a transform.</summary>
    public static void PlayFadeIn(FrameworkElement element)
    {
        var fade = new DoubleAnimation(0, 1, Normal) { EasingFunction = new QuadraticEase() };
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
