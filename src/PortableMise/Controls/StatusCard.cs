using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortableMise.Controls;

[TemplateVisualState(Name = StateReady, GroupName = GroupStatus)]
[TemplateVisualState(Name = StateMissing, GroupName = GroupStatus)]
[TemplateVisualState(Name = StateWorking, GroupName = GroupStatus)]
public sealed class StatusCard : Control
{
    private const string GroupStatus = "StatusStates";
    private const string StateReady = "Ready";
    private const string StateMissing = "Missing";
    private const string StateWorking = "Working";
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatusCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatusCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(StatusCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(StatusCard), new PropertyMetadata(null));
    public static readonly DependencyProperty IsReadyProperty = DependencyProperty.Register(nameof(IsReady), typeof(bool), typeof(StatusCard), new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(StatusCard), new PropertyMetadata(false, OnStateChanged));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool IsReady { get => (bool)GetValue(IsReadyProperty); set => SetValue(IsReadyProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    static StatusCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusCard), new FrameworkPropertyMetadata(typeof(StatusCard)));
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(false); }
    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((StatusCard)sender).UpdateStates(true);
    private void UpdateStates(bool transitions) => VisualStateManager.GoToState(this, IsBusy ? StateWorking : IsReady ? StateReady : StateMissing, transitions);
}
