using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;

namespace ToolBox.Host;

internal sealed class TrayIconService : IDisposable
{
    private readonly LocalizationService _localization;
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private bool _notificationShown;
    private bool _disposed;

    public TrayIconService(LocalizationService localization)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _icon = LoadIcon();
        _openItem = new Forms.ToolStripMenuItem();
        _exitItem = new Forms.ToolStripMenuItem();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += OnOpen;
        _openItem.Click += OnOpen;
        _exitItem.Click += OnExit;
        _localization.LanguageChanged += OnLanguageChanged;
        ApplyText();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void ShowBackgroundNotification()
    {
        if (_disposed || _notificationShown)
        {
            return;
        }

        _notificationShown = true;
        _notifyIcon.BalloonTipTitle = _localization["TrayRunningTitle"];
        _notifyIcon.BalloonTipText = _localization["TrayRunningDescription"];
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _notifyIcon.DoubleClick -= OnOpen;
        _openItem.Click -= OnOpen;
        _exitItem.Click -= OnExit;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyText();
    }

    private void ApplyText()
    {
        _notifyIcon.Text = _localization["AppTitle"];
        _openItem.Text = _localization["TrayOpen"];
        _exitItem.Text = _localization["TrayExit"];
    }

    private static Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/ToolBox.Tray.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("The ToolBox tray icon resource is missing.");
        using (resource.Stream)
        {
            using var loadedIcon = new Icon(resource.Stream);
            return (Icon)loadedIcon.Clone();
        }
    }
}
