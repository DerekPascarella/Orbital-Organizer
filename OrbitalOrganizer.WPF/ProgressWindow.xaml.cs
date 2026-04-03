using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OrbitalOrganizer;

public partial class ProgressWindow : Window, INotifyPropertyChanged
{
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;
    private const int WS_MINIMIZEBOX = 0x20000;
    private const int WS_MAXIMIZEBOX = 0x10000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private bool _allowClose;
    private int _totalItems;
    private int _processedItems;
    private string _textContent = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TotalItems
    {
        get => _totalItems;
        set { _totalItems = value; OnPropertyChanged(); }
    }

    public int ProcessedItems
    {
        get => _processedItems;
        set { _processedItems = value; OnPropertyChanged(); }
    }

    public string TextContent
    {
        get => _textContent;
        set { _textContent = value; OnPropertyChanged(); }
    }

    public ProgressWindow()
    {
        InitializeComponent();
        DataContext = this;
        SourceInitialized += ProgressWindow_SourceInitialized;
    }

    private void ProgressWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_SYSMENU & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX;
        SetWindowLong(hwnd, GWL_STYLE, style);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
