using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OrbitalOrganizer;

public partial class ProgressWindow : Window, INotifyPropertyChanged
{
    private bool _allowClose;
    private int _totalItems;
    private int _processedItems;
    private string _textContent = string.Empty;

    public new event PropertyChangedEventHandler? PropertyChanged;

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
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
