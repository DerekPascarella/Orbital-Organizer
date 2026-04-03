using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OrbitalOrganizer.Core;
using OrbitalOrganizer.Core.Models;
using OrbitalOrganizer.Core.Services;

namespace OrbitalOrganizer;

public partial class MainWindow : Window, INotifyPropertyChanged
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
        set { _customSdPath = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsUsingCustomPath)); RaisePropertyChanged(nameof(HasSdPath)); RaisePropertyChanged(nameof(CanModifyList)); }
    }

    public bool HasSdPath => !string.IsNullOrEmpty(_manager.SdCardPath);

    private bool _isFilterActive;
    public bool IsFilterActive
    {
        get => _isFilterActive;
        set { _isFilterActive = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(CanModifyList)); }
    }

    public bool CanModifyList => HasSdPath && !IsFilterActive;

    public ObservableCollection<string> KnownFolders => _manager.KnownFolders;

    public UndoManager UndoManager => _manager.UndoManager;

    private string _gamesListHeader = "N/A";
    public string GamesListHeader
    {
        get => _gamesListHeader;
        private set { _gamesListHeader = value; RaisePropertyChanged(); }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

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

        FilterTextBox.KeyDown += FilterTextBox_KeyDown;
        AddHandler(DragDrop.DropEvent, WindowDrop);
        KeyDown += MainWindow_KeyDown;
        Closing += MainWindow_Closing;
        Opened += MainWindow_Opened;

        _manager.OnFolderLocked = async (path) =>
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                "File Locked",
                $"The following folder is open in another program:\n\n{path}\n\n" +
                "Close any programs using it, then click Yes to retry.",
                ButtonEnum.YesNo, MsBoxIcon.Warning);
            var result = await msgBox.ShowWindowDialogAsync(this);
            return result == ButtonResult.Yes;
        };

        RefreshDriveList();
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            var readOnlyPath = AppSettings.CheckReadOnly();
            if (readOnlyPath != null)
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(
                    "Read-Only Settings",
                    $"The settings file is marked as read-only:\n\n{readOnlyPath}\n\n" +
                    "Your preferences will not be saved until this is resolved.",
                    ButtonEnum.Ok, MsBoxIcon.Warning);
                await msgBox.ShowWindowDialogAsync(this);
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
                await manualDialog.ShowDialog(this);
            }
            else if (result.UpdateAvailable && !UpdateAvailableDialog.ShouldSkipVersion(result.LatestTag))
            {
                var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                await dialog.ShowDialog(this);

                if (dialog.UserWantsUpdate)
                {
                    var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                    await wizard.ShowDialog(this);
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
            Position = new Avalonia.PixelPoint((int)_settings.WindowLeft, (int)_settings.WindowTop);
        }

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
    }

    private void SaveSettings()
    {
        _settings.EnableLockCheck = LockCheckBox.IsChecked == true;
        _settings.TempFolder = NormalizeTempFolderForSave(TempFolderTextBox.Text);
        _settings.WindowLeft = Position.X;
        _settings.WindowTop = Position.Y;
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

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (IsBusy)
        {
            e.Cancel = true;
            return;
        }
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
                        if (HasRheaPhoebeIni(drive.RootDirectory.FullName))
                            autoSelectIndex = index;
                    }
                    catch { }
                }

                index++;
            }
        }

        if (autoSelectIndex >= 0)
            DriveComboBox.SelectedIndex = autoSelectIndex;
    }

    private static bool HasRheaPhoebeIni(string root)
    {
        try
        {
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
            foreach (var file in Directory.GetFiles(root, "*.ini", options))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("Rhea.ini", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Phoebe.ini", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private void ButtonRefreshDrives_Click(object? sender, RoutedEventArgs e) => RefreshDriveList();

    private async void ButtonBrowseSdPath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select SD card or folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        string folderPath = folders[0].Path.LocalPath;

        IsUsingCustomPath = true;
        CustomSdPath = folderPath;
        DriveComboBox.SelectedIndex = -1;

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        _manager.ToolsPath = Path.Combine(appDir, "tools");
        _manager.SdCardPath = folderPath;

        RaisePropertyChanged(nameof(HasSdPath));
        RaisePropertyChanged(nameof(CanModifyList));
        await LoadCard();
    }

    private async void ButtonBrowseTempFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select temporary folder",
            AllowMultiple = false
        };

        string currentPath = TempFolderTextBox.Text ?? "";
        if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
        {
            try
            {
                options.SuggestedStartLocation = await topLevel.StorageProvider
                    .TryGetFolderFromPathAsync(new Uri("file:///" + currentPath.Replace('\\', '/')));
            }
            catch { }
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0) return;

        TempFolderTextBox.Text = folders[0].Path.LocalPath;
        SaveSettings();
    }

    private async void ButtonResetTempFolder_Click(object? sender, RoutedEventArgs e)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(
            "Reset Temporary Folder",
            "Reset the Temporary Folder path to default?",
            ButtonEnum.YesNo, MsBoxIcon.Question);
        var result = await msgBox.ShowWindowDialogAsync(this);
        if (result != ButtonResult.Yes) return;

        TempFolderTextBox.Text = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        SaveSettings();
    }

    private async void DriveList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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
        RaisePropertyChanged(nameof(CanModifyList));
        await LoadCard();
    }

    private async Task LoadCard()
    {
        IsBusy = true;
        FilterTextBox.Text = string.Empty;
        IsFilterActive = false;
        GameGrid.ItemsSource = _manager.ItemList;

        try
        {
            // Pre-populate sidecar files from LIST.INI if available
            await _manager.PrePopulateFromListIniAsync();

            // Load items from SD card (fast: reads sidecars only, no IP.BIN parsing)
            await _manager.LoadItemsFromCardAsync();
            UpdateGamesListHeader();
            UpdateMenuTypeRadioButtons();
            UpdateFolderColumnVisibility();
            UpdateSortButtonTooltip();

            // Check if any items need a metadata scan (missing sidecar files)
            var itemsNeedingScan = _manager.GetItemsNeedingMetadataScan();
            if (itemsNeedingScan.Count > 0)
            {
                var scanDialog = new MetadataScanDialog(itemsNeedingScan.Count);
                await scanDialog.ShowDialog(this);

                if (scanDialog.StartScan)
                {
                    await PerformMetadataScan(itemsNeedingScan);
                }
                else
                {
                    Close();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await msgBox.ShowWindowDialogAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PerformMetadataScan(List<SaturnGame> items)
    {
        var progressWindow = new ProgressWindow();
        progressWindow.Title = "Scanning Disc Images";
        progressWindow.TotalItems = items.Count;
        progressWindow.Show(this);

        var progress = new Progress<(int current, string name)>(p =>
        {
            progressWindow.ProcessedItems = p.current;
            progressWindow.TextContent = $"Caching metadata: {p.name}";
        });

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

    private void MenuType_Changed(object? sender, RoutedEventArgs e)
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
    }

    private void UpdateSortButtonTooltip()
    {
        if (ButtonSort == null) return;
        ToolTip.SetTip(ButtonSort, _manager.MenuKindSelected == MenuKind.Rmenu
            ? "Sort list by title"
            : "Sort list by folder path + title");
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

    private void UpdateMenuItemName(string name)
    {
        var menuItem = _manager.ItemList.FirstOrDefault(g => g.IsMenuItem);
        if (menuItem != null)
            menuItem.Name = name;
    }

    private void UpdateFolderColumnVisibility()
    {
        if (GameGrid?.Columns == null) return;

        foreach (var col in GameGrid.Columns)
        {
            if (col.Header?.ToString() == "Folder")
            {
                bool show = _manager.MenuKindSelected != MenuKind.Rmenu;
                col.IsVisible = show;
                if (show)
                {
                    col.Width = new Avalonia.Controls.DataGridLength(0, Avalonia.Controls.DataGridLengthUnitType.Auto);
                    col.Width = new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.Star);
                }
                break;
            }
        }
    }

    // --- Game list operations ---

    private async void ButtonAdd_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select disc image file(s)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Saturn Disc Images")
                {
                    Patterns = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new[] { "*.cdi", "*.mdf", "*.img", "*.iso", "*.ccd", "*.cue", "*.chd", "*.7z", "*.rar", "*.zip" }
                        : new[] { "*.cdi", "*.mdf", "*.img", "*.iso", "*.ccd", "*.7z", "*.rar", "*.zip" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0) return;

        var paths = files.Select(f => f.Path.LocalPath).ToArray();
        await AddGamesFromPaths(paths);
    }

    private async Task AddGamesFromPaths(string[] paths)
    {
        IsBusy = true;
        try
        {
            string tempRoot = GetTempFolderRoot();
            await _manager.AddGamesAsync(paths, tempFolderRoot: string.IsNullOrEmpty(tempRoot) ? null : tempRoot);
        }
        catch (Exception ex)
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await msgBox.ShowWindowDialogAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ButtonRemove_Click(object? sender, RoutedEventArgs e)
    {
        var selected = GameGrid.SelectedItems?.Cast<SaturnGame>().ToList();
        if (selected == null || selected.Count == 0) return;

        _manager.RemoveItems(selected);
    }

    private void ButtonMoveUp_Click(object? sender, RoutedEventArgs e)
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

    private void ButtonMoveDown_Click(object? sender, RoutedEventArgs e)
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

    private async void ButtonSort_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                "Sort List",
                "Your disc images will be automatically sorted in alphanumeric order " +
                "based on a combination of Folder and Title.\n\nProceed?",
                ButtonEnum.YesNo, MsBoxIcon.Question);

            var result = await msgBox.ShowWindowDialogAsync(this);
            if (result != ButtonResult.Yes) return;

            _manager.SortList();
        }
        catch { }
    }

    // --- Search/Filter ---

    private void FilterTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ButtonSearch_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void ButtonSearch_Click(object? sender, RoutedEventArgs e)
    {
        string filterText = FilterTextBox.Text?.Trim() ?? string.Empty;
        if (_manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(filterText))
            return;

        int startIndex = GameGrid.SelectedIndex == -1 ? 0 : GameGrid.SelectedIndex;

        if (!SearchInGrid(startIndex, filterText))
        {
            if (!SearchInGrid(0, filterText))
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(
                    "Search", "No matches found.", ButtonEnum.Ok, MsBoxIcon.Info);
                await msgBox.ShowWindowDialogAsync(this);
            }
        }
    }

    private bool SearchInGrid(int start, string filter)
    {
        var items = GameGrid.ItemsSource?.Cast<SaturnGame>().ToList();
        if (items == null) return false;

        for (int i = start; i < items.Count; i++)
        {
            var item = items[i];
            if (GameGrid.SelectedItem != item && _manager.SearchInItem(item, filter))
            {
                GameGrid.SelectedItem = item;
                GameGrid.ScrollIntoView(item, null);
                return true;
            }
        }

        return false;
    }

    private void ButtonFilter_Click(object? sender, RoutedEventArgs e)
    {
        string filterText = FilterTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filterText))
            return;

        var filtered = _manager.ItemList
            .Where(item => item.IsMenuItem ||
                (item.Name?.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (item.ProductId?.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        GameGrid.ItemsSource = filtered;
        IsFilterActive = true;
    }

    private void ButtonFilterReset_Click(object? sender, RoutedEventArgs e)
    {
        GameGrid.ItemsSource = _manager.ItemList;
        FilterTextBox.Text = string.Empty;
        IsFilterActive = false;
    }

    // --- Save ---

    private async void ButtonSave_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_manager.SdCardPath))
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error", "No SD card selected.", ButtonEnum.Ok, MsBoxIcon.Warning);
            await msgBox.ShowWindowDialogAsync(this);
            return;
        }

        var confirmBox = MessageBoxManager.GetMessageBoxStandard(
            "Save",
            $"Save changes to \"{_manager.SdCardPath}\" drive?",
            ButtonEnum.YesNo, MsBoxIcon.Question);

        var confirmResult = await confirmBox.ShowWindowDialogAsync(this);
        if (confirmResult != ButtonResult.Yes) return;

        // Prompt for console region if INI files need to be created
        if (_manager.NeedsIniFiles())
        {
            var regionDialog = new RegionSelectDialog();
            await regionDialog.ShowDialog(this);
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
            progressWindow.TotalItems = _manager.ItemList.Count(g => !g.IsMenuItem);
            progressWindow.Show(this);

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    progressWindow.TextContent = msg;
                });

                var itemProgress = new Progress<int>(count =>
                {
                    progressWindow.ProcessedItems = count;
                });

                string tempRoot = GetTempFolderRoot();
                await _manager.SaveAsync(progress, itemProgress, string.IsNullOrEmpty(tempRoot) ? null : tempRoot);

                SaveSettings();

                progressWindow.AllowClose();
                progressWindow.Close();

                var doneBox = MessageBoxManager.GetMessageBoxStandard("Message", "Done!", ButtonEnum.Ok, MsBoxIcon.Info);
                await doneBox.ShowWindowDialogAsync(this);

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
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await msgBox.ShowWindowDialogAsync(this);
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
            lockProgress.TextContent = "Checking for locked files and folders...";
            lockProgress.TotalItems = paths.Count;
            lockProgress.Show(this);

            Dictionary<string, string> locked;
            try
            {
                var progress = new Progress<(int current, int total, string name)>(info =>
                {
                    lockProgress.ProcessedItems = info.current;
                });

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
            await dialog.ShowDialog(this);

            if (!dialog.UserWantsRetry)
                return false;
        }
    }

    // --- Info button ---

    private async void ButtonInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not SaturnGame game)
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

        var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
            "Disc Image Info", sb.ToString(), MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Info);
        await box.ShowWindowDialogAsync(this);
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

    private void MenuItemRename_Click(object? sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem != null)
            GameGrid.BeginEdit();
    }

    private void MenuItemTitleCase_Click(object? sender, RoutedEventArgs e)
    {
        RenameSelectedItems(name => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()));
    }

    private void MenuItemUppercase_Click(object? sender, RoutedEventArgs e)
    {
        RenameSelectedItems(name => name.ToUpperInvariant());
    }

    private void MenuItemLowercase_Click(object? sender, RoutedEventArgs e)
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

    private async void MenuItemRenameIP_Click(object? sender, RoutedEventArgs e)
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
        progressWindow.Show(this);
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
                    progressWindow.TextContent = $"Reading IP.BIN: {game.Name}";
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

                progressWindow.ProcessedItems = i + 1;
            }
        }
        catch (Exception ex)
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to read IP.BIN: {ex.Message}", ButtonEnum.Ok, MsBoxIcon.Error);
            await msgBox.ShowWindowDialogAsync(this);
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

    private void MenuItemRenameFolder_Click(object? sender, RoutedEventArgs e)
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

    private void MenuItemRenameFile_Click(object? sender, RoutedEventArgs e)
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

    private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        // Block context menu on menu item rows
        if (GameGrid.SelectedItem is SaturnGame selected && selected.IsMenuItem)
        {
            e.Cancel = true;
            return;
        }

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
            assignFolderItem.IsVisible = !isRmenuOnly;
            if (!isRmenuOnly)
                assignFolderItem.Header = isMultiple ? "Assign Folder Paths" : "Assign Folder Path";
        }
        if (assignFolderSep != null)
            assignFolderSep.IsVisible = !isRmenuOnly;

        var assignAltItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "MenuItemAssignAltFolders");
        if (assignAltItem != null)
        {
            assignAltItem.IsVisible = !isRmenuOnly;
            assignAltItem.IsEnabled = !isMultiple;
        }
    }

    private async void MenuItemAssignFolder_Click(object? sender, RoutedEventArgs e)
    {
        GameGrid.CancelEdit();

        var selectedItems = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem)
            .ToList();

        if (selectedItems == null || selectedItems.Count == 0) return;

        _manager.RefreshKnownFolders();
        var dialog = new AssignFolderWindow(selectedItems.Count, _manager.KnownFolders);
        await dialog.ShowDialog(this);

        if (dialog.UserConfirmed)
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

    private async void MenuItemAssignAltFolders_Click(object? sender, RoutedEventArgs e)
    {
        GameGrid.CancelEdit();

        var selected = GameGrid.SelectedItems?.Cast<SaturnGame>()
            .Where(g => !g.IsMenuItem)
            .ToList();

        if (selected == null || selected.Count != 1) return;

        var item = selected[0];
        _manager.RefreshKnownFolders();

        var dialog = new AssignAltFoldersWindow(item, _manager.KnownFolders);
        await dialog.ShowDialog(this);

        if (dialog.UserConfirmed)
        {
            var newAltFolders = dialog.GetAltFolders();
            var oldAltFolders = item.AlternativeFolders.ToList();

            if (!oldAltFolders.SequenceEqual(newAltFolders))
            {
                _manager.UndoManager.RecordChange(new AltFoldersChangeOperation
                {
                    Item = item,
                    OldAltFolders = oldAltFolders,
                    NewAltFolders = newAltFolders.ToList()
                });

                item.AlternativeFolders = newAltFolders;
                item.SidecarsDirty = true;
                _manager.RefreshKnownFolders();
            }
        }
    }

    // --- Drag and drop ---

    private async void WindowDrop(object? sender, DragEventArgs e)
    {
        if (IsBusy) return;

        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        var paths = files.Select(f => f.Path.LocalPath).ToArray();
        await AddGamesFromPaths(paths);
    }

    // --- Keyboard shortcuts ---

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_manager.UndoManager.CanUndo)
            {
                _manager.UndoManager.Undo();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_manager.UndoManager.CanRedo)
            {
                _manager.UndoManager.Redo();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete && !IsBusy)
        {
            ButtonRemove_Click(null, new RoutedEventArgs());
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

        if (e.Row.DataContext is SaturnGame g)
        {
            _editOldValue = e.Column.Header?.ToString() switch
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

        string? propertyName = e.Column.Header?.ToString() switch
        {
            "Title" => nameof(SaturnGame.Name),
            "Folder" => nameof(SaturnGame.Folder),
            "Product ID" => nameof(SaturnGame.ProductId),
            "Disc" => nameof(SaturnGame.Disc),
            _ => null
        };

        if (propertyName != null && e.EditingElement is TextBox tb && _editOldValue != tb.Text)
        {
            _manager.UndoManager.RecordChange(new PropertyEditOperation
            {
                Item = game,
                PropertyName = propertyName,
                OldValue = _editOldValue,
                NewValue = tb.Text
            });

            game.SidecarsDirty = true;
            if (propertyName == nameof(SaturnGame.ProductId))
                game.ProductIdDirty = true;
        }

        _editOldValue = null;
    }

    // --- Undo/Redo ---

    private void ButtonUndo_Click(object? sender, RoutedEventArgs e)
    {
        _manager.UndoManager.Undo();
    }

    private void ButtonRedo_Click(object? sender, RoutedEventArgs e)
    {
        _manager.UndoManager.Redo();
    }

    // --- About window ---

    private async void ButtonAbout_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var about = new AboutWindow();
            await about.ShowDialog(this);
        }
        catch { }
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
