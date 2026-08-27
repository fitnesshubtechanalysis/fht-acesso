using System.Drawing;
using System.Windows.Forms;

namespace FHT.Access.App.Services;

/// <summary>
/// System tray presence. The app keeps recognizing while the window is hidden —
/// only "Sair do aplicativo" actually stops the engine.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconService(Action openWindow, Action openAttendant, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(openAttendant);
        ArgumentNullException.ThrowIfNull(exitApplication);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir FHT Acesso", null, (_, _) => openWindow());
        menu.Items.Add("Modo Atendente", null, (_, _) => openAttendant());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair do aplicativo (encerra reconhecimento)", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "FHT Acesso",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => openWindow();
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed)
            return;

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/tray-icon.png", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource is null)
                return SystemIcons.Application;

            using var stream = resource.Stream;
            using var bitmap = new Bitmap(stream);
            using var square = new Bitmap(bitmap, new Size(32, 32));
            var handle = square.GetHicon();
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
