using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using PortableMise.Controls;
using PortableMise.ViewModels;

namespace PortableMise.Tests;

public static class WpfControlTests
{
    public static async Task<int> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + name);
            count++;
            Console.WriteLine("PASS: " + name);
        }
        var model = new PathState();
        var picker = new PathPicker { Caption = "目录" };
        picker.SetBinding(PathPicker.TextProperty, new Binding(nameof(PathState.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var card = new StatusCard { Caption = "mise", Value = "未安装" };
        var panel = new StackPanel();
        panel.Children.Add(picker); panel.Children.Add(card);
        var window = new Window { Content = panel, Width = 460, Height = 280, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            picker.ApplyTemplate(); card.ApplyTemplate();
            var template = picker.Template;
            var textBox = picker.Template.FindName("PART_PathTextBox", picker) as TextBox;
            var button = picker.Template.FindName("PART_BrowseButton", picker) as Button;
            Check(textBox != null && button != null, "ThemeInfo loads PathPicker template with live parts");
            if (textBox == null || button == null) throw new InvalidOperationException("Missing default template parts.");
            Check(textBox.Text == "mise", "PathPicker template receives initial dependency-property value");
            textBox.Text = "relative-tools";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Check(model.Value == "relative-tools", "PathPicker edits update the bound source");
            model.Value = "tools-from-model";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Check(textBox.Text == "tools-from-model", "bound source changes update PathPicker text part");
            int clicks = 0;
            picker.BrowseCommand = new RelayCommand(() => clicks++);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(clicks == 1, "browse part executes its command exactly once");
            picker.Template = new ControlTemplate(typeof(PathPicker));
            picker.ApplyTemplate();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            textBox.Text = "detached";
            Check(clicks == 1 && model.Value == "tools-from-model", "empty template detaches old part event handlers");
            picker.Template = WrongPartsTemplate();
            picker.ApplyTemplate();
            picker.Text = "still-usable";
            Check(picker.Text == "still-usable", "wrong template-part types are tolerated");
            picker.Template = template;
            picker.ApplyTemplate();
            var freshTextBox = (TextBox)picker.Template.FindName("PART_PathTextBox", picker);
            var freshButton = (Button)picker.Template.FindName("PART_BrowseButton", picker);
            Check(freshTextBox.Text == picker.Text, "reapplied template synchronizes dependency-property value");
            freshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(clicks == 2, "reapplied template subscribes once without duplicate handlers");
            picker.IsInvalid = true;
            Check(FindGroup(picker, "ValidationStates")?.CurrentState?.Name == "Invalid", "PathPicker validation property drives Invalid visual state");
            picker.IsInvalid = false;
            Check(FindGroup(picker, "ValidationStates")?.CurrentState?.Name == "Valid", "PathPicker returns to Valid visual state");
            card.IsReady = true;
            Check(FindGroup(card, "StatusStates")?.CurrentState?.Name == "Ready", "StatusCard uses Ready visual state");
            card.IsBusy = true;
            Check(FindGroup(card, "StatusStates")?.CurrentState?.Name == "Working", "StatusCard busy state overrides Ready");
            card.Template = new ControlTemplate(typeof(StatusCard));
            card.ApplyTemplate(); card.IsBusy = false; card.IsReady = false;
            Check(!card.IsBusy && !card.IsReady, "StatusCard tolerates an incomplete template");
            var grid = new DataGrid { ItemsSource = new[] { new { Name = "node", Version = "22.0.0", Requested = "lts", Active = true } }, Width = 400, Height = 120 };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Tool", Binding = new Binding("Name") });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Version", Binding = new Binding("Version") });
            var gridWindow = new Window { Content = grid, Width = 420, Height = 140, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                gridWindow.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var row = FindDescendant<DataGridRow>(grid);
                Check(row != null, "DataGrid materializes rows with the themed template");
                if (row != null)
                {
                    row.ApplyTemplate();
                    var presenter = FindDescendant<System.Windows.Controls.Primitives.DataGridCellsPresenter>(row);
                    Check(presenter != null && presenter.Items.Count >= 1, "themed row template keeps the cells presenter alive");
                    Check(FindDescendant<System.Windows.Controls.DataGridCell>(row) != null, "row template renders cells");
                }
            }
            finally { gridWindow.Close(); }
        }
        finally { window.Close(); }
        return count;
    }

    private static ControlTemplate WrongPartsTemplate() => (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:PortableMise.Controls;assembly=PortableMise' TargetType='{x:Type local:PathPicker}'><StackPanel><TextBlock x:Name='PART_PathTextBox'/><Border x:Name='PART_BrowseButton'/></StackPanel></ControlTemplate>");

    private static VisualStateGroup? FindGroup(DependencyObject element, string name)
    {
        if (element is FrameworkElement framework)
        {
            var group = VisualStateManager.GetVisualStateGroups(framework).OfType<VisualStateGroup>().FirstOrDefault(item => item.Name == name);
            if (group != null) return group;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var group = FindGroup(VisualTreeHelper.GetChild(element, index), name);
            if (group != null) return group;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private sealed class PathState : INotifyPropertyChanged
    {
        private string value = "mise";
        public string Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
