using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using PortableMise.Controls;
using PortableMise.ViewModels;

namespace PortableMise;

public partial class MainWindow : Window
{
    private MainViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();
        Loaded += OnWindowLoaded;
        StateChanged += OnStateChanged;
    }

    private void HookViewModel()
    {
        if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel = DataContext as MainViewModel;
        if (viewModel != null) viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage)) PlayPageTransition();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => Motion.PlayRiseIn(PageHost, 6);

    /// <summary>Cross-fades and eases the page body up whenever navigation swaps the page template.</summary>
    private void PlayPageTransition()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var rise = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PageHost.Opacity = 0;
        PageHost.BeginAnimation(UIElement.OpacityProperty, fade);
        if (PageHost.RenderTransform is System.Windows.Media.TranslateTransform translate)
        {
            translate.Y = 10;
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, rise);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A borderless maximized window extends past the monitor edges by the resize frame; inset the content so nothing is clipped.
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
