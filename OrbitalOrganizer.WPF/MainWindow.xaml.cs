using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrbitalOrganizer.Core;
using OrbitalOrganizer.Core.Models;
using OrbitalOrganizer.Core.Services;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using RadioButton = System.Windows.Controls.RadioButton;
using GongSolutions.Wpf.DragDrop.Utilities;

namespace OrbitalOrganizer;

public partial class MainWindow : Window, GongSolutions.Wpf.DragDrop.IDropTarget, INotifyPropertyChanged
{
    private readonly Manager _manager = new();
    private readonly AppSettings _settings;
    private bool _suppressMenuTypeChange;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; RaisePropertyChanged(); }
    }

    private bool _isUsingCustomPath;
    public bool IsUsingCustomPath
    {
        get => _isUsingCustomPath;
        set { _isUsingCustomPath = value; RaisePropertyChanged(); }
    }

    private string _customSdPath = string.Empty;
    public string CustomSdPath
    {
        get => _customSdPath;
        set { _customSdPath = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsUsingCustomPath)); RaisePropertyChanged(nameof(HasSdPath)); }
    }

    public bool HasSdPath => !string.IsNullOrEmpty(_manager.SdCardPath);

    private bool _isFilterActive;
    public bool IsFilterActive
    {
        get => _isFilterActive;
        set { _isFilterActive = value; RaisePropertyChanged(); }
    }

    public System.Collections.ObjectModel.ObservableCollection<string> KnownFolders => _manager.KnownFolders;

    public UndoManager UndoManager => _manager.UndoManager;

    private string _gamesListHeader = "N/A";
    public string GamesListHeader
    {
        get => _gamesListHeader;
        private set { _gamesListHeader = value; RaisePropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Orbital Organizer v" + Constants.Version;
        DataContext = this;

        _settings = AppSettings.Load();
        ApplySettings();

        UpdateManager.CleanupStaleStagingData();

        GameGrid.ItemsSource = _manager.ItemList;

        _manager.ItemList.CollectionChanged += (_, _) => UpdateGamesListHeader();

        _manager.OnFolderLocked = (path) =>
        {
            var result = MessageBox.Show(this,
                $"The following folder is open in another program:\n\n{path}\n\n" +
                "Close any programs using it, then click Yes to retry.",
                "File Locked", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return Task.FromResult(result == MessageBoxResult.Yes);
        };

        this.Loaded += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;

        RefreshDriveList();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var readOnlyPath = AppSettings.CheckReadOnly();
            if (readOnlyPath != null)
            {
                MessageBox.Show(this,
                    $"The settings file is marked as read-only:\n\n{readOnlyPath}\n\n" +
                    "Your preferences will not be saved until this is resolved.",
                    "Read-Only Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch { }

        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var result = await UpdateManager.CheckForUpdateAsync();
            if (result.ManualUpdateRequired && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
            {
                var manualDialog = new ManualUpdateDialog(result.LatestTag, result.LatestVersion, result.ManualReason);
                manualDialog.Owner = this;
                manualDialog.ShowDialog();
            }
            else if (result.UpdateAvailable && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
            {
                var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                dialog.Owner = this;
                dialog.ShowDialog();

                if (dialog.UserWantsUpdate)
                {
                    var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                    wizard.Owner = this;
                    wizard.ShowDialog();
                }
            }
        }
        catch { }
    }

    private void ApplySettings()
    {
        LockCheckBox.IsChecked = _settings.EnableLockCheck;

        if (!string.IsNullOrEmpty(_settings.TempFolder) && Directory.Exists(_settings.TempFolder))
            TempFolderTextBox.Text = _settings.TempFolder;
        else
            TempFolderTextBox.Text = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
    }

    private void SaveSettings()
    {
        _settings.EnableLockCheck = LockCheckBox.IsChecked == true;
        _settings.TempFolder = NormalizeTempFolderForSave(TempFolderTextBox.Text);
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Save();
    }

    private static string NormalizeTempFolderForSave(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string systemDefault = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalized, systemDefault, StringComparison.OrdinalIgnoreCase))
            return "";
        return normalized;
    }

    private string GetTempFolderRoot()
    {
        string text = TempFolderTextBox.Text ?? "";
        return !string.IsNullOrEmpty(text) && Directory.Exists(text) ? text : "";
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void UpdateGamesListHeader()
    {
        long totalBytes = _manager.ItemList
            .Where(g => !g.IsMenuItem && g.Length > 0)
            .Sum(g => g.Length);

        if (totalBytes > 0)
        {
            double gb = totalBytes / 1_000_000_000.0;
            GamesListHeader = $"{gb:F2} GB";
        }
        else
        {
            GamesListHeader = "N/A";
        }
    }

    // --- Drive selection ---

    private void RefreshDriveList()
    {
        IsUsingCustomPath = false;
        CustomSdPath = string.Empty;
        DriveComboBox.Items.Clear();

        int autoSelectIndex = -1;
        int index = 0;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.Fixed)
            {
                string label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name} ({drive.VolumeLabel})"
                    : drive.Name;
                DriveComboBox.Items.Add(label);

                if (autoSelectIndex == -1 && drive.IsReady)
                {
                    try
                    {
                        string root = drive.RootDirectory.FullName;
                        if (File.Exists(Path.Combine(root, "Rhea.ini")) ||
                            File.Exists(Path.Combine(root, "Phoebe.ini")) ||
                            File.Exists(Path.Combine(root, "rhea.ini")) ||
                            File.Exists(Path.Combine(root, "phoebe.ini")))
                        {
                            autoSelectIndex = index;
                        }
                    }
                    catch { }
                }

                index++;
            }
        }

        if (autoSelectIndex >= 0)
            DriveComboBox.SelectedIndex = autoSelectIndex;
    }

    private void ButtonRefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDriveList();

    private async void ButtonBrowseSdPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select SD card or folder",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        string folderPath = dialog.SelectedPath;

        IsUsingCustomPath = true;
        CustomSdPath = folderPath;
        DriveComboBox.SelectedIndex = -1;

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        _manager.ToolsPath = Path.Combine(appDir, "tools");
        _manager.SdCardPath = folderPath;

        RaisePropertyChanged(nameof(HasSdPath));
        await LoadCard();
    }

    private void ButtonBrowseTempFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select temporary folder",
            UseDescriptionForTitle = true,
            SelectedPath = TempFolderTextBox.Text ?? ""
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        TempFolderTextBox.Text = dialog.SelectedPath;
        SaveSettings();
    }

    private void ButtonResetTempFolder_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset the Temporary Folder path to default?",
            "Reset Temporary Folder",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        TempFolderTextBox.Text = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        SaveSettings();
    }

    private async void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveComboBox.SelectedItem == null || IsBusy) return;

        string selected = DriveComboBox.SelectedItem.ToString()!;
        string drivePath = selected.Length >= 3 ? selected[..3] : selected;

        IsUsingCustomPath = false;
        CustomSdPath = string.Empty;

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        _manager.ToolsPath = Path.Combine(appDir, "tools");
        _manager.SdCardPath = drivePath;

        RaisePropertyChanged(nameof(HasSdPath));
        await LoadCard();
    }

    private async Task LoadCard()
    {
        IsBusy = true;
        FilterTextBox.Text = string.Empty;
        IsFilterActive = false;

        try
        {
            await _manager.PrePopulateFromListIniAsync();
            await _manager.LoadItemsFromCardAsync();

            UpdateGamesListHeader();
            UpdateMenuTypeRadioButtons();
            UpdateFolderColumnVisibility();
            UpdateSortButtonTooltip();
            UpdateBatchFolderRenameVisibility();

            // Check if any items need a metadata scan (missing sidecar files)
            var itemsNeedingScan = _manager.GetItemsNeedingMetadataScan();
            if (itemsNeedingScan.Count > 0)
            {
                var scanDialog = new MetadataScanDialog(itemsNeedingScan.Count);
                scanDialog.Owner = this;
                scanDialog.ShowDialog();

                if (scanDialog.StartScan)
                {
                    await PerformMetadataScan(itemsNeedingScan);
                }
                else
                {
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PerformMetadataScan(List<SaturnGame> items)
    {
        var progressWindow = new ProgressWindow();
        progressWindow.Owner = this;
        progressWindow.TotalItems = items.Count;
        progressWindow.Show();

        var progress = new Progress<(int current, string name)>(p => Dispatcher.Invoke(() =>
        {
            progressWindow.ProcessedItems = p.current;
            progressWindow.TextContent = $"Caching metadata: {p.name}";
        }));

        try
        {
            await _manager.PerformMetadataScanAsync(items, progress);
        }
        finally
        {
            progressWindow.AllowClose();
            progressWindow.Close();
        }
    }

    // --- Menu type ---

    private void MenuType_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressMenuTypeChange) return;
        if (sender is not RadioButton rb) return;

        if (rb == RadioRmenuKai)
        {
            _manager.MenuKindSelected = MenuKind.RmenuKai;
            _manager.RemoveLegacyRmenu();
            UpdateMenuItemName("RmenuKai");
        }
        else if (rb == RadioRmenu)
        {
            _manager.MenuKindSelected = MenuKind.Rmenu;
            _manager.RemoveLegacyRmenu();
            UpdateMenuItemName("RMENU");
        }
        else if (rb == RadioBoth)
        {
            _manager.MenuKindSelected = MenuKind.Both;
            _manager.InjectLegacyRmenu();
            UpdateMenuItemName("RmenuKai");
        }

        UpdateFolderColumnVisibility();
        UpdateSortButtonTooltip();
        UpdateBatchFolderRenameVisibility();
    }

    private void UpdateSortButtonTooltip()
    {
        if (ButtonSort == null) return;
        ButtonSort.ToolTip = _manager.MenuKindSelected == MenuKind.Rmenu
            ? "Sort list by title"
            : "Sort list by folder path + title";
    }

    private void UpdateBatchFolderRenameVisibility()
    {
        if (ButtonBatchFolderRename == null) return;
        ButtonBatchFolderRename.Visibility = _manager.MenuKindSelected == MenuKind.Rmenu
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateMenuTypeRadioButtons()
    {
        _suppressMenuTypeChange = true;
        try
        {
            switch (_manager.MenuKindDetected)
            {
                case MenuKind.RmenuKai:
                    RadioRmenuKai.IsChecked = true;
                    break;
                case MenuKind.Rmenu:
                    RadioRmenu.IsChecked = true;
                    break;
                case MenuKind.Both:
                    RadioBoth.IsChecked = true;
                    break;
            }
            if (_manager.MenuKindDetected != MenuKind.None)
                _manager.MenuKindSelected = _manager.MenuKindDetected;
        }
        finally
        {
            _suppressMenuTypeChange = false;
        }
    }

    private void UpdateFolderColumnVisibility()
    {
        if (GameGrid?.Columns == null) return;

        foreach (var col in GameGrid.Columns)
        {
            if (col.Header?.ToString() == "Folder")
            {
                bool show = _manager.MenuKindSelected != MenuKind.Rmenu;
                col.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                break;
            }
        }
    }

    // --- Game list operations ---

    private void ButtonAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select disc image file(s)",
            Multiselect = true,
            Filter = "Saturn Disc Images (*.cdi;*.mdf;*.img;*.iso;*.ccd;*.cue;*.chd;*.7z;*.rar;*.zip)|*.cdi;*.mdf;*.img;*.iso;*.ccd;*.cue;*.chd;*.7z;*.rar;*.zip|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        _ = AddGamesFromPaths(dialog.FileNames);
    }

    private async Task AddGamesFromPaths(string[] paths, int insertIndex = -1)
    {
        IsBusy = true;

        ProgressWindow? progressWindow = null;
        if (paths.Length > 1)
        {
            progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Adding Disc Images";
            progressWindow.TotalItems = paths.Length;
            progressWindow.Show();
        }

        try
        {
            var progress = new Progress<string>(msg => Dispatcher.Invoke(() =>
            {
                if (progressWindow != null)
                    progressWindow.TextContent = msg;
            }));

            string tempRoot = GetTempFolderRoot();
            var added = await _manager.AddGamesAsync(paths, progress, insertIndex,
                string.IsNullOrEmpty(tempRoot) ? null : tempRoot);

            if (progressWindow != null)
                progressWindow.ProcessedItems = paths.Length;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressWindow != null)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
            }
            IsBusy = false;
        }
    }

    private void ButtonRemove_Click(object sender, RoutedEventArgs e)
    {
        var selected = GameGrid.SelectedItems?.Cast<SaturnGame>().ToList();
        if (selected == null || selected.Count == 0) return;

        _manager.RemoveItems(selected);
    }

    private void ButtonMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not SaturnGame item) return;
        if (item.IsMenuItem) return;
        int index = _manager.ItemList.IndexOf(item);
        if (index <= 0) return;

        var above = _manager.ItemList[index - 1];
        if (above.IsMenuItem) return;

        var oldOrder = _manager.ItemList.ToList();
        _manager.ItemList.Move(index, index - 1);
        _manager.UndoManager.RecordChange(new ListReorderOperation("Move Up")
        {
            ItemList = _manager.ItemList,
            OldOrder = oldOrder,
            NewOrder = _manager.ItemList.ToList()
        });
    }

    private void ButtonMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not SaturnGame item) return;
        if (item.IsMenuItem) return;

        int index = _manager.ItemList.IndexOf(item);
        if (index < 0 || index >= _manager.ItemList.Count - 1) return;

        var oldOrder = _manager.ItemList.ToList();
        _manager.ItemList.Move(index, index + 1);
        _manager.UndoManager.RecordChange(new ListReorderOperation("Move Down")
        {
            ItemList = _manager.ItemList,
            OldOrder = oldOrder,
            NewOrder = _manager.ItemList.ToList()
        });
    }

    private void ButtonBatchFolderRename_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsFilterActive) return;
            if (_manager.ItemList.Count == 0) return;

            var folderCounts = _manager.GetFolderCounts();

            if (folderCounts.Count == 0)
            {
                MessageBox.Show(this, "No folders found in the current game list.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new BatchFolderRenameWindow(folderCounts, _manager.ItemList.Count);
            window.Owner = this;

            if (window.ShowDialog() == true && window.FolderMappings != null)
            {
                var snapshots = _manager.ItemList
                    .Select(g => new BatchFolderRenameOperation.ItemSnapshot
                    {
                        Item = g,
                        OldFolder = g.Folder,
                        OldAltFolders = new List<string>(g.AlternativeFolders)
                    }).ToList();

                var (updatedCount, conflictsRemoved) = _manager.ApplyFolderMappings(window.FolderMappings);

                if (updatedCount > 0 || conflictsRemoved > 0)
                {
                    var undoOp = new BatchFolderRenameOperation();
                    foreach (var s in snapshots)
                    {
                        s.NewFolder = s.Item.Folder;
                        s.NewAltFolders = new List<string>(s.Item.AlternativeFolders);
                        if (s.OldFolder != s.NewFolder ||
                            !s.OldAltFolders.SequenceEqual(s.NewAltFolders))
                            undoOp.Snapshots.Add(s);
                    }

                    if (undoOp.Snapshots.Count > 0)
                        _manager.UndoManager.RecordChange(undoOp);

                    _manager.RefreshKnownFolders();

                    var msg = $"{updatedCount} disc image(s) updated across {window.FolderMappings.Count} folder(s).";
                    if (conflictsRemoved > 0)
                        msg += $"\n{conflictsRemoved} duplicate additional folder path(s) were automatically removed.";
                    msg += "\n\nClick 'Save Changes' to write updates to SD card.";
                    MessageBox.Show(this, msg, "Folders Renamed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this, "No changes were made.",
                        "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ButtonSort_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            "Your disc images will be automatically sorted in alphanumeric order " +
            "based on a combination of Folder and Title.\n\nProceed?",
            "Sort List", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _manager.SortList();
    }

    private void ButtonSearch_Click(object sender, RoutedEventArgs e)
    {
        string filterText = FilterTextBox.Text.Trim();
        if (_manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(filterText))
            return;

        int startIndex = GameGrid.SelectedIndex == -1 ? 0 : GameGrid.SelectedIndex;

        if (!SearchInGrid(startIndex, filterText))
        {
            if (!SearchInGrid(0, filterText))
                MessageBox.Show(this, "No matches found.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool SearchInGrid(int start, string filter)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GameGrid.ItemsSource);
        if (view == null) return false;

        var visibleItems = view.Cast<SaturnGame>().ToList();

        for (int i = start; i < visibleItems.Count; i++)
        {
            var item = visibleItems[i];
            if (GameGrid.SelectedItem != item && _manager.SearchInItem(item, filter))
            {
                GameGrid.SelectedItem = item;
                GameGrid.ScrollIntoView(item);
                return true;
            }
        }

        return false;
    }

    private void ButtonFilter_Click(object sender, RoutedEventArgs e)
    {
        string filterText = FilterTextBox.Text.Trim();
        if (string.IsNullOrEmpty(filterText))
            return;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GameGrid.ItemsSource);
        if (view == null) return;

        view.Filter = obj => obj is SaturnGame item &&
            (item.IsMenuItem ||
             (item.Name?.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
             (item.ProductId?.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0));

        IsFilterActive = true;
    }

    private void ButtonFilterReset_Click(object sender, RoutedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GameGrid.ItemsSource);
        if (view == null) return;

        view.Filter = null;
        FilterTextBox.Text = string.Empty;
        IsFilterActive = false;
    }

    private async void ButtonSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_manager.SdCardPath))
        {
            MessageBox.Show(this, "No SD card selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmResult = MessageBox.Show(this,
            $"Save changes to \"{_manager.SdCardPath}\" drive?",
            "Save", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes) return;

        // Prompt for console region if INI files need to be created
        if (_manager.NeedsIniFiles())
        {
            var regionDialog = new RegionSelectDialog();
            regionDialog.Owner = this;
            regionDialog.ShowDialog();
            _manager.PendingConsoleRegion = regionDialog.SelectedRegionCode;
        }

        IsBusy = true;

        try
        {
            if (LockCheckBox.IsChecked == true)
            {
                bool lockCheckPassed = await RunLockCheck();
                if (!lockCheckPassed)
                    return;
            }

            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.TotalItems = _manager.ItemList.Count(g => !g.IsMenuItem);
            progressWindow.Show();

            try
            {
                var progress = new Progress<string>(msg => Dispatcher.Invoke(() =>
                {
                    progressWindow.TextContent = msg;
                }));

                var itemProgress = new Progress<int>(count => Dispatcher.Invoke(() =>
                {
                    progressWindow.ProcessedItems = count;
                }));

                string tempRoot = GetTempFolderRoot();
                await _manager.SaveAsync(progress, itemProgress, string.IsNullOrEmpty(tempRoot) ? null : tempRoot);

                SaveSettings();

                progressWindow.AllowClose();
                progressWindow.Close();

                MessageBox.Show("Done!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadCard();
            }
            finally
            {
                progressWindow.AllowClose();
                if (progressWindow.IsVisible)
                    progressWindow.Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunLockCheck()
    {
        while (true)
        {
            var paths = _manager.CollectPathsToModify();

            var lockProgress = new ProgressWindow();
            lockProgress.Owner = this;
            lockProgress.TextContent = "Checking for locked files and folders...";
            lockProgress.TotalItems = paths.Count;
            lockProgress.Show();

            Dictionary<string, string> locked;
            try
            {
                var progress = new Progress<(int current, int total, string name)>(info =>
                    Dispatcher.Invoke(() =>
                    {
                        lockProgress.ProcessedItems = info.current;
                    }));

                locked = await LockChecker.CheckPathsAsync(paths, progress);
            }
            finally
            {
                lockProgress.AllowClose();
                lockProgress.Close();
            }

            if (locked.Count == 0)
                return true;

            var dialog = new LockedFilesDialog(locked);
            dialog.Owner = this;
            dialog.ShowDialog();

            if (!dialog.UserWantsRetry)
                return false;
        }
    }

    // --- Info button ---

    private void ButtonInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.CommandParameter is not SaturnGame game)
            return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Title: {game.Name}");
        sb.AppendLine($"Product ID: {(string.IsNullOrWhiteSpace(game.ProductId) ? "NA" : game.ProductId)}");
        sb.AppendLine($"Version: {game.Version}");
        sb.AppendLine($"Disc: {game.Disc}");
        sb.AppendLine($"Region: {game.Region}");
        sb.AppendLine($"Date: {game.ReleaseDate}");
        sb.AppendLine($"Format: {GetFormatDisplayString(game)}");
        sb.AppendLine($"Size: {ByteSizeLib.ByteSize.FromBytes(game.Length).ToString("0.##")}");

        if (_manager.MenuKindSelected != MenuKind.Rmenu)
        {
            var allFolders = new List<string>();
            if (!string.IsNullOrWhiteSpace(game.Folder))
                allFolders.Add(game.Folder);
            foreach (var alt in game.AlternativeFolders)
            {
                if (!string.IsNullOrWhiteSpace(alt))
                    allFolders.Add(alt);
            }

            string foldersLabel = allFolders.Count > 1 ? "Folders" : "Folder";
            sb.Append($"{foldersLabel}: {(allFolders.Count > 0 ? FormatFolderList(allFolders) : "NA")}");
        }

        MessageBox.Show(this, sb.ToString(), "Disc Image Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string FormatFolderList(List<string> folders)
    {
        if (folders.Count == 0) return "";
        if (folders.Count == 1) return folders[0];
        if (folders.Count == 2) return $"{folders[0]} and {folders[1]}";
        return string.Join(", ", folders.Take(folders.Count - 1)) + ", and " + folders[^1];
    }

    private static string GetFormatDisplayString(SaturnGame game)
    {
        var format = game.FileFormat;

        if (format == FileFormat.CueBin)
            return "CCD/IMG/SUB (conversion pending)";

        if (format == FileFormat.Compressed)
        {
            var inner = game.InnerFileFormat ?? FileFormat.Uncompressed;
            if (inner == FileFormat.CueBin)
                return "CCD/IMG/SUB (conversion pending)";
            return GetBaseFormatString(inner, game);
        }

        return GetBaseFormatString(format, game);
    }

    private static string GetBaseFormatString(FileFormat format, SaturnGame game)
    {
        if (format == FileFormat.CloneCd)
            return "CCD/IMG/SUB";

        // Uncompressed: determine specific format from file extensions
        foreach (var file in game.ImageFiles)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".cdi") return "CDI";
            if (ext == ".mdf" || ext == ".mds") return "MDS/MDF";
            if (ext == ".iso") return "ISO";
        }

        return "IMG";
    }

    // --- Context menu ---

    private void MenuItemRename_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem != null)
            GameGrid.BeginEdit();
    }

    private void MenuItemTitleCase_Click(object sender, RoutedEventArgs e)
    {
        RenameSelectedItems(name => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()));
    }

    private void MenuItemUppercase_Click(object sender, RoutedEventArgs e)
    {
        RenameSelectedItems(name => name.ToUpperInvariant());
    }

    private void MenuItemLowercase_Click(object sender, RoutedEventArgs e)
    {
        RenameSelectedItems(name => name.ToLowerInvariant());
    }

    private void RenameSelectedItems(Func<string, string> transform)
    {
        var items = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem)
            .ToList();
        if (items == null || items.Count == 0) return;

        var undoOp = new MultiPropertyEditOperation("Rename")
        {
            PropertyName = nameof(SaturnGame.Name)
        };

        foreach (var game in items)
        {
            var oldName = game.Name;
            game.Name = transform(oldName);
            if (oldName != game.Name)
            {
                undoOp.AddChange(game, oldName, game.Name);
                game.SidecarsDirty = true;
            }
        }

        if (undoOp.HasChanges)
            _manager.UndoManager.RecordChange(undoOp);
    }

    private async void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
    {
        var items = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem)
            .ToList();
        if (items == null || items.Count == 0) return;

        var undoOp = new MultiPropertyEditOperation("Rename by IP.BIN")
        {
            PropertyName = nameof(SaturnGame.Name)
        };

        var progressWindow = new ProgressWindow();
        progressWindow.Title = "Reading IP.BIN Info";
        progressWindow.TotalItems = items.Count;
        progressWindow.TextContent = "Reading IP.BIN info...";
        progressWindow.Owner = this;
        progressWindow.Show();
        GameGrid.IsEnabled = false;

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var game = items[i];
                var oldName = game.Name;
                string? newName = null;

                string? folderPath = !string.IsNullOrEmpty(game.FullFolderPath) ? game.FullFolderPath
                    : !string.IsNullOrEmpty(game.SourcePath) ? game.SourcePath : null;

                if (folderPath != null)
                {
                    Dispatcher.Invoke(() => progressWindow.TextContent = $"Reading IP.BIN: {game.Name}");
                    var result = await Task.Run(() =>
                    {
                        var (offset, filePath) = IpBinParser.FindIpBinInFolder(folderPath);
                        if (offset >= 0 && filePath != null)
                            return IpBinParser.ParseHeader(filePath, offset)?.Title;
                        return null;
                    });
                    newName = result;
                }

                if (string.IsNullOrEmpty(newName) && game.ImageFiles.Count > 0)
                    newName = Path.GetFileNameWithoutExtension(game.ImageFiles[0]);

                if (!string.IsNullOrEmpty(newName))
                {
                    game.Name = newName;
                    if (oldName != game.Name)
                    {
                        undoOp.AddChange(game, oldName, game.Name);
                        game.SidecarsDirty = true;
                    }
                }

                Dispatcher.Invoke(() => progressWindow.ProcessedItems = i + 1);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read IP.BIN: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressWindow.AllowClose();
            progressWindow.Close();
            GameGrid.IsEnabled = true;
        }

        if (undoOp.HasChanges)
            _manager.UndoManager.RecordChange(undoOp);
    }

    private void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
    {
        var items = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem && g.IsNotOnSdCard)
            .ToList();
        if (items == null || items.Count == 0) return;

        var undoOp = new MultiPropertyEditOperation("Rename by Folder")
        {
            PropertyName = nameof(SaturnGame.Name)
        };

        foreach (var game in items)
        {
            string? folderPath = !string.IsNullOrEmpty(game.SourcePath) ? game.SourcePath : null;
            if (string.IsNullOrEmpty(folderPath)) continue;

            var oldName = game.Name;
            string rawName = game.FileFormat == FileFormat.Compressed
                ? Path.GetFileNameWithoutExtension(folderPath)
                : Path.GetFileName(folderPath);
            game.Name = NameSanitizer.Sanitize(rawName);
            if (oldName != game.Name)
            {
                undoOp.AddChange(game, oldName, game.Name);
                game.SidecarsDirty = true;
            }
        }

        if (undoOp.HasChanges)
            _manager.UndoManager.RecordChange(undoOp);
    }

    private void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
    {
        var items = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem && g.IsNotOnSdCard)
            .ToList();
        if (items == null || items.Count == 0) return;

        var undoOp = new MultiPropertyEditOperation("Rename by File")
        {
            PropertyName = nameof(SaturnGame.Name)
        };

        foreach (var game in items)
        {
            if (game.ImageFiles.Count == 0) continue;

            var oldName = game.Name;
            game.Name = NameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(game.ImageFiles[0]));
            if (oldName != game.Name)
            {
                undoOp.AddChange(game, oldName, game.Name);
                game.SidecarsDirty = true;
            }
        }

        if (undoOp.HasChanges)
            _manager.UndoManager.RecordChange(undoOp);
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        int count = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Count(g => !g.IsMenuItem) ?? 0;
        bool isMultiple = count > 1;

        var renameItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "MenuItemRename");
        if (renameItem != null)
            renameItem.IsEnabled = !isMultiple;

        var autoRenameItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "MenuItemAutoRename");
        if (autoRenameItem != null)
        {
            autoRenameItem.Header = isMultiple ? "Automatically Rename Titles" : "Automatically Rename Title";

            // Folder/file rename only available when ALL selected items are off the SD card
            bool allOffSdCard = GameGrid.SelectedItems?.Cast<SaturnGame>()
                .Where(g => !g.IsMenuItem)
                .All(g => g.IsNotOnSdCard) ?? false;

            var renameFolderItem = autoRenameItem.Items.OfType<MenuItem>()
                .FirstOrDefault(m => m.Name == "MenuItemRenameFolder");
            if (renameFolderItem != null)
                renameFolderItem.IsEnabled = allOffSdCard;

            var renameFileItem = autoRenameItem.Items.OfType<MenuItem>()
                .FirstOrDefault(m => m.Name == "MenuItemRenameFile");
            if (renameFileItem != null)
                renameFileItem.IsEnabled = allOffSdCard;
        }

        var assignFolderItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "MenuItemAssignFolder");
        var assignFolderSep = menu.Items.OfType<Separator>()
            .FirstOrDefault(s => s.Name == "AssignFolderSeparator");

        bool isRmenuOnly = _manager.MenuKindSelected == MenuKind.Rmenu;
        if (assignFolderItem != null)
        {
            assignFolderItem.Visibility = isRmenuOnly ? Visibility.Collapsed : Visibility.Visible;
            if (!isRmenuOnly)
                assignFolderItem.Header = isMultiple ? "Assign Folder Paths" : "Assign Folder Path";
        }
        if (assignFolderSep != null)
            assignFolderSep.Visibility = isRmenuOnly ? Visibility.Collapsed : Visibility.Visible;

        var assignAltItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "MenuItemAssignAltFolders");
        if (assignAltItem != null)
        {
            assignAltItem.Visibility = isRmenuOnly ? Visibility.Collapsed : Visibility.Visible;
            assignAltItem.IsEnabled = !isMultiple;
        }
    }

    private void MenuItemAssignFolder_Click(object sender, RoutedEventArgs e)
    {
        GameGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        GameGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var selectedItems = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem)
            .ToList();

        if (selectedItems == null || selectedItems.Count == 0) return;

        _manager.RefreshKnownFolders();
        var dialog = new AssignFolderWindow(selectedItems.Count, _manager.KnownFolders);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderPath?.Trim() ?? string.Empty;

            var undoOp = new MultiPropertyEditOperation("Assign Folder Path")
            {
                PropertyName = nameof(SaturnGame.Folder)
            };

            foreach (var item in selectedItems)
            {
                var oldFolder = item.Folder;
                if (oldFolder != folderPath)
                {
                    undoOp.Edits.Add((item, oldFolder, folderPath));
                    item.Folder = folderPath;
                    item.SidecarsDirty = true;
                }
            }

            if (undoOp.Edits.Count > 0)
                _manager.UndoManager.RecordChange(undoOp);

            _manager.RefreshKnownFolders();
        }
    }

    private void MenuItemAssignAltFolders_Click(object sender, RoutedEventArgs e)
    {
        GameGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        GameGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var item = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .FirstOrDefault(g => !g.IsMenuItem);

        if (item == null) return;

        _manager.RefreshKnownFolders();
        var dialog = new AssignAltFoldersWindow(item, _manager.KnownFolders);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            var oldAltFolders = new List<string>(item.AlternativeFolders);
            var newAltFolders = dialog.GetAltFolders();

            if (!oldAltFolders.SequenceEqual(newAltFolders))
            {
                item.AlternativeFolders = newAltFolders;
                item.SidecarsDirty = true;
                _manager.UndoManager.RecordChange(new AltFoldersChangeOperation
                {
                    Item = item,
                    OldAltFolders = oldAltFolders,
                    NewAltFolders = new List<string>(item.AlternativeFolders)
                });

                _manager.RefreshKnownFolders();
            }
        }
    }

    // --- Drag and drop (Window-level kept for drop outside DataGrid) ---

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (IsBusy || IsFilterActive || !HasSdPath || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (IsBusy || IsFilterActive || !HasSdPath) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            await AddGamesFromPaths(paths);
        }
    }

    // --- Keyboard shortcuts ---

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_manager.UndoManager.CanUndo)
            {
                _manager.UndoManager.Undo();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_manager.UndoManager.CanRedo)
            {
                _manager.UndoManager.Redo();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete && !IsBusy)
        {
            ButtonRemove_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.F2 && !IsBusy && GameGrid.SelectedItem != null)
        {
            GameGrid.BeginEdit();
        }
    }

    // --- Cell editing ---

    private string? _editOldValue;

    private void GameGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is SaturnGame game && game.IsMenuItem)
        {
            e.Cancel = true;
            return;
        }

        // Legacy RMENU rows: only the Folder column is editable
        if (e.Row.DataContext is SaturnGame g2 && g2.IsLegacyRmenu &&
            e.Column.Header?.ToString() != "Folder")
        {
            e.Cancel = true;
            return;
        }

        if (e.Column is DataGridTextColumn col && e.Row.DataContext is SaturnGame g)
        {
            _editOldValue = col.Header?.ToString() switch
            {
                "Title" => g.Name,
                "Folder" => g.Folder,
                "Product ID" => g.ProductId,
                "Disc" => g.Disc,
                _ => null
            };
        }
    }

    private void GameGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.DataContext is not SaturnGame game) return;
        if (_editOldValue == null) return;

        string? propertyName = null;
        string? newValue = null;

        if (e.Column is DataGridTextColumn col)
        {
            propertyName = col.Header?.ToString() switch
            {
                "Title" => nameof(SaturnGame.Name),
                "Product ID" => nameof(SaturnGame.ProductId),
                "Disc" => nameof(SaturnGame.Disc),
                _ => null
            };

            if (propertyName != null && e.EditingElement is System.Windows.Controls.TextBox tb)
                newValue = tb.Text;
        }
        else if (e.Column.Header?.ToString() == "Folder")
        {
            propertyName = nameof(SaturnGame.Folder);
            if (e.EditingElement is System.Windows.Controls.ContentPresenter cp)
            {
                var combo = FindVisualChild<System.Windows.Controls.ComboBox>(cp);
                if (combo != null)
                    newValue = combo.Text;
            }
        }

        if (propertyName != null && newValue != null && _editOldValue != newValue)
        {
            _manager.UndoManager.RecordChange(new PropertyEditOperation
            {
                Item = game,
                PropertyName = propertyName,
                OldValue = _editOldValue,
                NewValue = newValue
            });

            game.SidecarsDirty = true;
            if (propertyName == nameof(SaturnGame.ProductId))
                game.ProductIdDirty = true;
        }

        _editOldValue = null;
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void GameGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
    }

    // --- Undo/Redo ---

    private void ButtonUndo_Click(object sender, RoutedEventArgs e)
    {
        _manager.UndoManager.Undo();
    }

    private void ButtonRedo_Click(object sender, RoutedEventArgs e)
    {
        _manager.UndoManager.Redo();
    }

    // --- DataGrid row drag-reorder (IDropTarget) ---

    void GongSolutions.Wpf.DragDrop.IDropTarget.DragOver(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
        if (dropInfo == null || IsFilterActive) return;

        if (dropInfo.DragInfo == null)
        {
            // External file drop
            if (dropInfo.Data is System.Windows.DataObject data && data.ContainsFileDropList())
            {
                if (dropInfo.UnfilteredInsertIndex == 0 && _manager.ItemList.Count > 0 && _manager.ItemList[0].IsMenuItem)
                    dropInfo.Effects = System.Windows.DragDropEffects.None;
                else
                    dropInfo.Effects = System.Windows.DragDropEffects.Copy;
            }
        }
        else if (GongSolutions.Wpf.DragDrop.DefaultDropHandler.CanAcceptData(dropInfo))
        {
            // Internal row reorder
            var draggedItems = GongSolutions.Wpf.DragDrop.DefaultDropHandler
                .ExtractData(dropInfo.Data).OfType<SaturnGame>().ToList();

            bool hasMenuItem = draggedItems.Any(g => g.IsMenuItem);

            if (hasMenuItem || dropInfo.UnfilteredInsertIndex == 0)
                dropInfo.Effects = System.Windows.DragDropEffects.None;
            else
                dropInfo.Effects = System.Windows.DragDropEffects.Move;
        }

        if (dropInfo.Effects != System.Windows.DragDropEffects.None)
            dropInfo.DropTargetAdorner = GongSolutions.Wpf.DragDrop.DropTargetAdorners.Insert;
    }

    async void GongSolutions.Wpf.DragDrop.IDropTarget.Drop(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
        if (dropInfo == null || IsFilterActive) return;

        if (dropInfo.DragInfo == null)
        {
            // External file drop, insert at drop position
            if (dropInfo.Data is System.Windows.DataObject data && data.ContainsFileDropList())
            {
                var paths = data.GetFileDropList().Cast<string>().ToArray();
                int dropIndex = dropInfo.UnfilteredInsertIndex;
                await AddGamesFromPaths(paths, dropIndex);
            }
            return;
        }

        // Internal row reorder
        var draggedItems = GongSolutions.Wpf.DragDrop.DefaultDropHandler
            .ExtractData(dropInfo.Data).OfType<SaturnGame>().ToList();

        if (draggedItems.Count == 0) return;

        var oldOrder = _manager.ItemList.ToList();

        var items = GongSolutions.Wpf.DragDrop.DefaultDropHandler
            .ExtractData(dropInfo.Data).OfType<object>().ToList();

        int insertIndex = dropInfo.UnfilteredInsertIndex;
        var sourceList = dropInfo.DragInfo.SourceCollection.TryGetList();
        var destList = dropInfo.TargetCollection.TryGetList();

        if (sourceList != null)
        {
            foreach (var o in items)
            {
                int index = sourceList.IndexOf(o);
                if (index != -1)
                {
                    sourceList.RemoveAt(index);
                    if (destList != null && Equals(sourceList, destList) && index < insertIndex)
                        --insertIndex;
                }
            }
        }

        if (destList != null)
        {
            foreach (var o in items)
                destList.Insert(insertIndex++, o);
        }

        var newOrder = _manager.ItemList.ToList();
        _manager.UndoManager.RecordChange(new ListReorderOperation("Move Items")
        {
            ItemList = _manager.ItemList,
            OldOrder = oldOrder,
            NewOrder = newOrder
        });
    }

    // --- About window ---

    private void ButtonAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.Owner = this;
        about.ShowDialog();
    }

    private void FolderComboBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _manager.RefreshKnownFolders();
    }

    private void UpdateMenuItemName(string name)
    {
        var menuItem = _manager.ItemList.FirstOrDefault(g => g.IsMenuItem);
        if (menuItem != null)
            menuItem.Name = name;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
