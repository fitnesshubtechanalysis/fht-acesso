using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FHT.Access.App.ViewModels;

namespace FHT.Access.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private bool _allowClose;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;

        InitializeComponent();

        DataContext = _shell;
        _shell.ContentChanged += OnShellContentChanged;
        Loaded += (_, _) => ApplyChrome();
    }

    /// <summary>Brings the (possibly hidden) window back to the foreground.</summary>
    public void ShowAndActivate()
    {
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Maximized;

        Activate();
        Topmost = true;
        Topmost = false;
        _ = Focus();
    }

    public void OpenAttendant()
    {
        _shell.RequestAttendant();
        ShowAndActivate();
    }

    /// <summary>Allows the real close during application shutdown.</summary>
    public void AllowClose() => _allowClose = true;

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        // The recognition engine must keep running; the window only hides.
        e.Cancel = true;
        Hide();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            OpenAttendant();
            e.Handled = true;
        }
    }

    private void AttendantCorner_Click(object sender, MouseButtonEventArgs e)
    {
        OpenAttendant();
        e.Handled = true;
    }

    private void OnShellContentChanged(object? sender, EventArgs e)
        => _ = Dispatcher.BeginInvoke(ApplyChrome);

    private void ApplyChrome()
    {
        if (_shell.IsAttendantVisible)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        }

        WindowState = WindowState.Maximized;
    }
}
