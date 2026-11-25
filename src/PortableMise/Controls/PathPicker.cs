using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PortableMise.Controls;

[TemplatePart(Name = PartPathTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartBrowseButton, Type = typeof(Button))]
[TemplateVisualState(Name = StateFocused, GroupName = GroupFocus)]
[TemplateVisualState(Name = StateUnfocused, GroupName = GroupFocus)]
[TemplateVisualState(Name = StateValid, GroupName = GroupValidation)]
[TemplateVisualState(Name = StateInvalid, GroupName = GroupValidation)]
public sealed class PathPicker : Control
{
    private const string PartPathTextBox = "PART_PathTextBox";
    private const string PartBrowseButton = "PART_BrowseButton";
    private const string GroupFocus = "FocusStates";
    private const string GroupValidation = "ValidationStates";
    private const string StateFocused = "Focused";
    private const string StateUnfocused = "Unfocused";
    private const string StateValid = "Valid";
    private const string StateInvalid = "Invalid";
    private TextBox? _textBox;
    private Button? _browseButton;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PathPicker),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));
    public static readonly DependencyProperty BrowseCommandProperty = DependencyProperty.Register(nameof(BrowseCommand), typeof(ICommand), typeof(PathPicker));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(PathPicker), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.Register(nameof(IsInvalid), typeof(bool), typeof(PathPicker), new PropertyMetadata(false, OnStateChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public ICommand? BrowseCommand { get => (ICommand?)GetValue(BrowseCommandProperty); set => SetValue(BrowseCommandProperty, value); }
    public bool IsInvalid { get => (bool)GetValue(IsInvalidProperty); set => SetValue(IsInvalidProperty, value); }

    static PathPicker() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PathPicker), new FrameworkPropertyMetadata(typeof(PathPicker)));

    private TextBox? TextBoxElement
    {
        get => _textBox;
        set
        {
            if (_textBox != null) _textBox.TextChanged -= OnPartTextChanged;
            _textBox = value;
            if (_textBox != null) _textBox.TextChanged += OnPartTextChanged;
        }
    }
    private Button? BrowseButtonElement
    {
        get => _browseButton;
        set
        {
            if (_browseButton != null) _browseButton.Click -= OnBrowseClick;
            _browseButton = value;
            if (_browseButton != null) _browseButton.Click += OnBrowseClick;
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        TextBoxElement = GetTemplateChild(PartPathTextBox) as TextBox;
        BrowseButtonElement = GetTemplateChild(PartBrowseButton) as Button;
        if (TextBoxElement != null) TextBoxElement.Text = Text;
        UpdateStates(false);
    }
    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        TextBoxElement = null;
        BrowseButtonElement = null;
        base.OnTemplateChanged(oldTemplate, newTemplate);
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); UpdateStates(true); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); UpdateStates(true); }
    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var picker = (PathPicker)sender;
        if (picker.TextBoxElement != null && picker.TextBoxElement.Text != (string)e.NewValue)
            picker.TextBoxElement.Text = (string)e.NewValue;
    }
    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((PathPicker)sender).UpdateStates(true);
    private void OnPartTextChanged(object sender, TextChangedEventArgs e)
    {
        if (TextBoxElement != null && TextBoxElement.Text != Text) SetCurrentValue(TextProperty, TextBoxElement.Text);
    }
    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (BrowseCommand?.CanExecute(null) == true) BrowseCommand.Execute(null);
    }
    private void UpdateStates(bool transitions)
    {
        VisualStateManager.GoToState(this, IsKeyboardFocusWithin ? StateFocused : StateUnfocused, transitions);
        VisualStateManager.GoToState(this, IsInvalid ? StateInvalid : StateValid, transitions);
    }
}
