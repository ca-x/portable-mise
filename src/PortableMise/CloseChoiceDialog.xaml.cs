using System.Windows;
using System.Windows.Input;

namespace PortableMise;

public partial class CloseChoiceDialog : Window
{
    public bool? CloseToTrayChoice { get; private set; }
    public bool RememberChoice => RememberChoiceBox.IsChecked == true;

    public CloseChoiceDialog(Window? owner = null)
    {
        InitializeComponent();
        Loaded += (_, _) => Controls.Motion.PlayFadeIn(this);
        if (owner != null) Owner = owner;
    }
    private void OnExitClick(object sender, RoutedEventArgs e) { CloseToTrayChoice = false; DialogResult = true; }
    private void OnTrayClick(object sender, RoutedEventArgs e) { CloseToTrayChoice = true; DialogResult = true; }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
}
