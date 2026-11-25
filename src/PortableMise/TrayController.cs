namespace PortableMise;

internal sealed class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon applicationIcon;
    private readonly System.Windows.Forms.ContextMenuStrip menu;

    public TrayController(Action show, Action exit)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mise.ico"))?.Stream
            ?? throw new InvalidOperationException("未找到程序图标。");
        using var original = new System.Drawing.Icon(resource);
        applicationIcon = (System.Drawing.Icon)original.Clone();
        menu = new System.Windows.Forms.ContextMenuStrip
        {
            ShowImageMargin = false, ShowCheckMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 10F), Padding = new System.Windows.Forms.Padding(6),
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new MenuColors())
        };
        menu.Items.Add("打开 Portable mise", null, (_, _) => show());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => exit());
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
            if (item is not System.Windows.Forms.ToolStripSeparator) { item.AutoSize = false; item.Size = new System.Drawing.Size(190, 32); item.ForeColor = System.Drawing.Color.FromArgb(29, 29, 31); }
        icon = new System.Windows.Forms.NotifyIcon { Icon = applicationIcon, Text = "Portable mise", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => show();
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); applicationIcon.Dispose(); }

    private sealed class MenuColors : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(250, 250, 252);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(234, 243, 255);
        public override System.Drawing.Color MenuItemBorder => MenuItemSelected;
        public override System.Drawing.Color ToolStripBorder => System.Drawing.Color.FromArgb(210, 210, 215);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(229, 229, 234);
        public override System.Drawing.Color SeparatorLight => ToolStripDropDownBackground;
    }
}
