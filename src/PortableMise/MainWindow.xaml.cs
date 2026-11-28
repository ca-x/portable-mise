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
        Loaded += (_, _) => Motion.PlayRiseIn(PageHost, 6);
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
}
