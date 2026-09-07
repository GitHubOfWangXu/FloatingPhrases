using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatingPhrases;

public partial class MainWindow : Window
{
    private const string DefaultStatus = "Ctrl+Alt+Space 显隐 · Ctrl+Alt+T 穿透";
    private const int VisibilityHotkeyId = 0x4650;
    private const int ClickThroughHotkeyId = 0x4651;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkSpace = 0x20;
    private const uint VkT = 0x54;
    private const int WmHotkey = 0x0312;

    private readonly ObservableCollection<Phrase> _phrases;
    private readonly ObservableCollection<FolderShortcut> _folders;
    private readonly ObservableCollection<AppShortcut> _apps;
    private readonly PhraseStore _phraseStore = new();
    private readonly FolderStore _folderStore = new();
    private readonly AppStore _appStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly WindowBehavior _windowBehavior;
    private readonly EdgeDockController _edgeDock;
    private readonly WindowSettings _settings;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly System.Drawing.Icon _trayImage;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _idleFadeTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly DispatcherTimer _memoryTimer;
    private HwndSource? _source;
    private object? _dragCandidate;
    private System.Windows.Point _dragStart;
    private bool _initializingSettings = true;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _windowBehavior = new WindowBehavior(this);
        _settings = _settingsStore.Load();
        _edgeDock = new EdgeDockController(this, MainPanel, EdgeHandle, () => ApplyWindowBehavior(IsMouseOver));
        _edgeDock.SetEnabled(_settings.Mode == WindowMode.Floating);
        _phrases = new ObservableCollection<Phrase>(_phraseStore.Load());
        _folders = new ObservableCollection<FolderShortcut>(_folderStore.Load());
        _apps = new ObservableCollection<AppShortcut>(_appStore.Load());
        AppCategoryFilter.ItemsSource = new[] { "全部分类" }.Concat(AppCategories.All).ToList();
        AppCategoryFilter.SelectedIndex = 0;
        ApplyPhraseFilter();
        ApplyFolderFilter();
        ApplyAppFilter();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = DefaultStatus;
        };

        _idleFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _idleFadeTimer.Tick += (_, _) =>
        {
            _idleFadeTimer.Stop();
            ApplyWindowBehavior(false);
        };

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveSettings();
        };

        _initializingSettings = true;
        ModeBox.SelectedIndex = (int)_settings.Mode;
        OpacitySlider.Value = _settings.IdleOpacity * 100;
        UpdateOpacityLabel();
        _initializingSettings = false;

        using (var iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/FloatingPhrases;component/Assets/app.ico")).Stream)
        using (var icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize))
        {
            _trayImage = (System.Drawing.Icon)icon.Clone();
        }
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayImage,
            Text = "Floating Phrases",
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();

        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _memoryTimer.Tick += (_, _) => RefreshMemory();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshMemory();
                _memoryTimer.Start();
            }
            else _memoryTimer.Stop();
        };
        RefreshMemory();

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        ApplyWindowBehavior(false);
    }

    private void RefreshMemory()
    {
        var usage = SystemMemory.Read();
        MemoryText.Text = usage?.Summary ?? "内存 --";
        EdgeMemoryText.Text = usage is { } value ? $"{value.UsedPercent:0}%" : "--";
        var details = usage?.Details ?? "暂时无法读取系统内存";
        MemoryText.ToolTip = details;
        EdgeHandle.ToolTip = $"{details}\n悬停或点击展开；展开后拖动标题栏离开边缘可取消收起";
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("新增短语", null, (_, _) => Dispatcher.Invoke(AddPhrase));
        menu.Items.Add("新增文件夹", null, (_, _) => Dispatcher.Invoke(AddFolder));
        menu.Items.Add("新增软件", null, (_, _) => Dispatcher.Invoke(AddApp));
        menu.Items.Add("新增待办", null, (_, _) => Dispatcher.Invoke(ShowTodoInput));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var modes = new Forms.ToolStripMenuItem("窗口模式");
        modes.DropDownItems.Add("普通", null, (_, _) => Dispatcher.Invoke(() => SetWindowMode(WindowMode.Normal)));
        modes.DropDownItems.Add("悬浮", null, (_, _) => Dispatcher.Invoke(() => SetWindowMode(WindowMode.Floating)));
        modes.DropDownItems.Add("穿透", null, (_, _) => Dispatcher.Invoke(() => SetWindowMode(WindowMode.ClickThrough)));
        menu.Items.Add(modes);
        menu.Items.Add("恢复可操作 (Ctrl+Alt+T)", null, (_, _) => Dispatcher.Invoke(RestoreInteractiveMode));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        return menu;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workingArea = SystemParameters.WorkArea;
        Left = Math.Max(workingArea.Left + 16, workingArea.Right - ActualWidth - 24);
        Top = Math.Max(workingArea.Top + 16, workingArea.Top + (workingArea.Height - ActualHeight) / 2);
        ApplyWindowBehavior(IsMouseOver);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source.AddHook(WindowProc);
        _windowBehavior.Attach(helper.Handle);

        var visibilityRegistered = RegisterHotKey(
            helper.Handle, VisibilityHotkeyId, ModControl | ModAlt, VkSpace);
        var clickThroughRegistered = RegisterHotKey(
            helper.Handle, ClickThroughHotkeyId, ModControl | ModAlt, VkT);
        if (!visibilityRegistered || !clickThroughRegistered)
        {
            StatusText.Text = "部分全局快捷键已被其他程序占用";
            _statusTimer.Stop();
        }

        ApplyWindowBehavior(IsMouseOver);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == VisibilityHotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }
        else if (msg == WmHotkey && wParam.ToInt32() == ClickThroughHotkeyId)
        {
            ToggleClickThroughMode();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        if (_edgeDock.IsCollapsed)
        {
            _edgeDock.Expand();
            Show();
            Activate();
            FocusCurrentSearch();
            return;
        }

        if (IsVisible)
        {
            _idleFadeTimer.Stop();
            Hide();
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
        ApplyWindowBehavior(IsMouseOver);
        FocusCurrentSearch();
    }

    private void FocusCurrentSearch()
    {
        if (TodoTab.IsSelected)
        {
            TodoView.FocusInput();
        }
        else if (MainTabs.SelectedIndex == 2)
        {
            AppSearchBox.Focus();
        }
        else if (MainTabs.SelectedIndex == 1)
        {
            FolderSearchBox.Focus();
        }
        else
        {
            SearchBox.Focus();
        }
    }

    private void ApplyPhraseFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = _phrases
            .Where(phrase => string.IsNullOrEmpty(query)
                || phrase.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || phrase.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || phrase.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(phrase => phrase.IsPinned)
            .ToList();

        PhraseList.ItemsSource = filtered;
        EmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFolderFilter()
    {
        var query = FolderSearchBox.Text.Trim();
        var filtered = _folders
            .Where(folder => string.IsNullOrEmpty(query)
                || folder.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || folder.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(folder => folder.IsPinned)
            .ToList();

        FolderList.ItemsSource = filtered;
        FolderEmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderEmptyHint.Text = _folders.Count == 0 ? "还没有添加文件夹" : "没有匹配的文件夹";
    }

    private void ApplyAppFilter()
    {
        var query = AppSearchBox.Text.Trim();
        var selectedCategory = AppCategoryFilter.SelectedItem as string;
        var filtered = _apps
            .Where(app => (string.IsNullOrEmpty(query)
                    || app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || app.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || app.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                && (AppCategoryFilter.SelectedIndex <= 0 || app.Category == selectedCategory))
            .OrderByDescending(app => app.IsPinned)
            .ToList();

        AppList.ItemsSource = filtered;
        AppEmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppEmptyHint.Text = _apps.Count == 0 ? "还没有添加软件" : "没有匹配的软件";
    }

    private void AddPhrase()
    {
        _edgeDock.Expand();
        MainTabs.SelectedIndex = 0;
        var editor = new PhraseEditorWindow { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _phrases.Add(new Phrase
        {
            Title = string.IsNullOrWhiteSpace(editor.PhraseTitle) ? CreateTitle(editor.PhraseText) : editor.PhraseTitle,
            Group = string.IsNullOrWhiteSpace(editor.PhraseGroup) ? "常用" : editor.PhraseGroup,
            Text = editor.PhraseText,
            IsPinned = editor.IsPinned
        });
        SavePhrases("短语已添加");
    }

    private void AddFolder()
    {
        _edgeDock.Expand();
        MainTabs.SelectedIndex = 1;
        var editor = new FolderEditorWindow { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _folders.Add(new FolderShortcut
        {
            Name = editor.ShortcutName,
            Path = editor.FolderPath,
            IsPinned = editor.IsPinned
        });
        SaveFolders("文件夹已添加");
    }

    private void AddApp()
    {
        _edgeDock.Expand();
        MainTabs.SelectedIndex = 2;
        var editor = new AppEditorWindow { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _apps.Add(new AppShortcut
        {
            Name = editor.ShortcutName,
            Path = editor.AppPath,
            Category = editor.Category,
            IsPinned = editor.IsPinned
        });
        SaveApps("软件已添加");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (TodoTab.IsSelected)
        {
            ShowTodoInput();
        }
        else if (MainTabs.SelectedIndex == 2)
        {
            AddApp();
        }
        else if (MainTabs.SelectedIndex == 1)
        {
            AddFolder();
        }
        else
        {
            AddPhrase();
        }
    }

    private void AddPhrase_Click(object sender, RoutedEventArgs e) => AddPhrase();

    private void ShowTodoInput()
    {
        if (_settings.Mode == WindowMode.ClickThrough) SetWindowMode(WindowMode.Floating);
        _edgeDock.Expand();
        Show();
        Activate();
        MainTabs.SelectedItem = TodoTab;
        Dispatcher.BeginInvoke(new Action(TodoView.FocusInput), DispatcherPriority.Input);
    }
    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddFolder();
    private void AddApp_Click(object sender, RoutedEventArgs e) => AddApp();

    private void EditPhrase_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Phrase phrase)
        {
            return;
        }

        var editor = new PhraseEditorWindow(phrase) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        phrase.Title = string.IsNullOrWhiteSpace(editor.PhraseTitle) ? CreateTitle(editor.PhraseText) : editor.PhraseTitle;
        phrase.Group = string.IsNullOrWhiteSpace(editor.PhraseGroup) ? "常用" : editor.PhraseGroup;
        phrase.Text = editor.PhraseText;
        phrase.IsPinned = editor.IsPinned;
        SavePhrases("短语已更新");
    }

    private void DeletePhrase_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Phrase phrase)
        {
            return;
        }

        if (ConfirmDelete($"确定删除短语“{phrase.Title}”吗？"))
        {
            _phrases.Remove(phrase);
            SavePhrases("短语已删除");
        }
    }

    private void EditFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderShortcut folder)
        {
            return;
        }

        var editor = new FolderEditorWindow(folder) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        folder.Name = editor.ShortcutName;
        folder.Path = editor.FolderPath;
        folder.IsPinned = editor.IsPinned;
        SaveFolders("文件夹已更新");
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderShortcut folder)
        {
            return;
        }

        if (ConfirmDelete($"确定删除文件夹快捷入口“{folder.Name}”吗？"))
        {
            _folders.Remove(folder);
            SaveFolders("文件夹快捷入口已删除");
        }
    }

    private void EditApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppShortcut app)
        {
            return;
        }

        var editor = new AppEditorWindow(app) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        app.Name = editor.ShortcutName;
        app.Path = editor.AppPath;
        app.Category = editor.Category;
        app.IsPinned = editor.IsPinned;
        SaveApps("软件已更新");
    }

    private void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppShortcut app)
        {
            return;
        }

        if (ConfirmDelete($"确定删除软件快捷入口“{app.Name}”吗？"))
        {
            _apps.Remove(app);
            SaveApps("软件快捷入口已删除");
        }
    }

    private void ImportDesktopApps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var existingPaths = _apps
                .Select(app => app.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var imported = DesktopAppImporter.FindApps()
                .Where(app => existingPaths.Add(app.Path))
                .ToList();

            foreach (var app in imported)
            {
                _apps.Add(app);
            }

            if (imported.Count == 0)
            {
                SetStatus("桌面没有新的软件快捷方式");
                return;
            }

            SaveApps($"已导入 {imported.Count} 个桌面软件");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            System.Windows.MessageBox.Show(this, $"无法读取桌面快捷方式：{ex.Message}", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmDelete(string message) => System.Windows.MessageBox.Show(
        this,
        message,
        "确认删除",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Phrase phrase)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(phrase.Text);
            SetStatus($"已复制：{phrase.Title}");
        }
        catch (ExternalException)
        {
            SetStatus("剪贴板暂时被占用，请重试");
        }
    }

    private async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        var screenBounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        BitmapSource? screenshot = null;
        string? resultStatus = null;
        Exception? failure = null;

        _idleFadeTimer.Stop();
        Hide();

        try
        {
            await Task.Delay(180);
            screenshot = ScreenshotWindow.CaptureScreen(screenBounds);
            var selector = new ScreenshotWindow(screenshot, screenBounds);

            if (selector.ShowDialog() == true && selector.SelectedImage is not null)
            {
                try
                {
                    System.Windows.Clipboard.SetImage(selector.SelectedImage);
                    resultStatus = $"截图已复制到剪贴板（{selector.SelectedImage.PixelWidth} × {selector.SelectedImage.PixelHeight}）";
                }
                catch (ExternalException)
                {
                    resultStatus = "截图已完成，但剪贴板暂时被占用，请重试";
                }
            }
            else
            {
                resultStatus = "已取消截图";
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            failure = ex;
        }
        finally
        {
            screenshot = null;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            ApplyWindowBehavior(IsMouseOver);
        }

        if (failure is not null)
        {
            System.Windows.MessageBox.Show(
                this,
                $"无法完成截图：{failure.Message}",
                "Floating Phrases",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        SetStatus(resultStatus ?? "已取消截图");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderShortcut folder)
        {
            return;
        }

        if (!Directory.Exists(folder.Path))
        {
            System.Windows.MessageBox.Show(this, "该文件夹已不存在，请编辑或删除这个快捷入口。", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder.Path) { UseShellExecute = true });
            SetStatus($"已打开：{folder.Name}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(this, $"无法打开文件夹：{ex.Message}", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppShortcut app)
        {
            return;
        }

        if (!File.Exists(app.Path))
        {
            System.Windows.MessageBox.Show(this, "该程序或快捷方式已不存在，请编辑或删除这个入口。", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            SetStatus($"已启动：{app.Name}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(this, $"无法启动软件：{ex.Message}", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = (sender as FrameworkElement)?.Tag;
        _dragStart = e.GetPosition(this);
    }

    private void Item_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragCandidate;
        _dragCandidate = null;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, item, System.Windows.DragDropEffects.Move);
    }

    private void Item_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var target = (sender as FrameworkElement)?.Tag;
        var dragged = GetDraggedItem(e);
        e.Effects = CanReorder(dragged, target)
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Item_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement targetElement)
        {
            return;
        }

        var target = targetElement.Tag;
        var dragged = GetDraggedItem(e);
        if (!CanReorder(dragged, target))
        {
            SetStatus(dragged is AppShortcut
                ? "同一分类、同一置顶状态的软件才能互相排序"
                : "置顶项和普通项请分别排序");
            e.Handled = true;
            return;
        }

        var insertAfter = e.GetPosition(targetElement).Y > targetElement.ActualHeight / 2;
        if (dragged is Phrase phrase && target is Phrase targetPhrase
            && ItemOrder.Move(_phrases, phrase, targetPhrase, insertAfter))
        {
            SavePhrases("短语顺序已保存");
        }
        else if (dragged is FolderShortcut folder && target is FolderShortcut targetFolder
            && ItemOrder.Move(_folders, folder, targetFolder, insertAfter))
        {
            SaveFolders("文件夹顺序已保存");
        }
        else if (dragged is AppShortcut app && target is AppShortcut targetApp
            && ItemOrder.Move(_apps, app, targetApp, insertAfter))
        {
            SaveApps("软件顺序已保存");
        }

        e.Handled = true;
    }

    private static object? GetDraggedItem(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Phrase)))
        {
            return e.Data.GetData(typeof(Phrase));
        }

        if (e.Data.GetDataPresent(typeof(FolderShortcut)))
        {
            return e.Data.GetData(typeof(FolderShortcut));
        }

        return e.Data.GetDataPresent(typeof(AppShortcut))
            ? e.Data.GetData(typeof(AppShortcut))
            : null;
    }

    private static bool CanReorder(object? dragged, object? target) => (dragged, target) switch
    {
        (Phrase source, Phrase destination) => source != destination && source.IsPinned == destination.IsPinned,
        (FolderShortcut source, FolderShortcut destination) => source != destination && source.IsPinned == destination.IsPinned,
        (AppShortcut source, AppShortcut destination) => source != destination
            && source.IsPinned == destination.IsPinned
            && source.Category == destination.Category,
        _ => false
    };

    private void SavePhrases(string message)
    {
        try
        {
            _phraseStore.Save(_phrases);
            ApplyPhraseFilter();
            SetStatus(message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowSaveError(ex);
        }
    }

    private void SaveFolders(string message)
    {
        try
        {
            _folderStore.Save(_folders);
            ApplyFolderFilter();
            SetStatus(message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowSaveError(ex);
        }
    }

    private void SaveApps(string message)
    {
        try
        {
            _appStore.Save(_apps);
            ApplyAppFilter();
            SetStatus(message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowSaveError(ex);
        }
    }

    private void ShowSaveError(Exception ex) => System.Windows.MessageBox.Show(
        this,
        $"保存失败：{ex.Message}",
        "Floating Phrases",
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static string CreateTitle(string text)
    {
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "未命名";
        return firstLine.Length <= 24 ? firstLine : firstLine[..24] + "…";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_phrases is not null)
        {
            ApplyPhraseFilter();
        }
    }

    private void FolderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_folders is not null)
        {
            ApplyFolderFilter();
        }
    }

    private void AppSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_apps is not null)
        {
            ApplyAppFilter();
        }
    }

    private void AppCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_apps is not null)
        {
            ApplyAppFilter();
        }
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingSettings || ModeBox.SelectedIndex < 0)
        {
            return;
        }

        SetWindowMode((WindowMode)ModeBox.SelectedIndex);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.IdleOpacity = OpacitySlider.Value / 100;
        UpdateOpacityLabel();
        ApplyWindowBehavior(IsMouseOver);
        ScheduleSettingsSave();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_settings.Mode != WindowMode.Floating)
        {
            return;
        }

        _idleFadeTimer.Stop();
        ApplyWindowBehavior(true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_settings.Mode == WindowMode.Floating)
        {
            _idleFadeTimer.Stop();
            _idleFadeTimer.Start();
        }
    }

    private void SetWindowMode(WindowMode mode)
    {
        _settings.Mode = mode;
        _edgeDock.SetEnabled(mode == WindowMode.Floating);
        if (ModeBox.SelectedIndex != (int)mode)
        {
            _initializingSettings = true;
            ModeBox.SelectedIndex = (int)mode;
            _initializingSettings = false;
        }

        _idleFadeTimer.Stop();
        ApplyWindowBehavior(IsMouseOver);
        ScheduleSettingsSave();
        SetStatus(mode switch
        {
            WindowMode.Normal => "已切换到普通模式",
            WindowMode.Floating => "已切换到悬浮模式",
            _ => "已开启鼠标穿透，按 Ctrl+Alt+T 恢复"
        });
    }

    private void ToggleClickThroughMode() => SetWindowMode(
        _settings.Mode == WindowMode.ClickThrough
            ? WindowMode.Floating
            : WindowMode.ClickThrough);

    private void RestoreInteractiveMode()
    {
        SetWindowMode(WindowMode.Floating);
        _edgeDock.Expand();
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    private void ApplyWindowBehavior(bool pointerInside)
    {
        OpacitySlider.IsEnabled = _settings.Mode != WindowMode.Normal;
        _windowBehavior.Apply(_settings.Mode, _settings.IdleOpacity,
            pointerInside || _edgeDock.IsDocked || _edgeDock.IsAnimating);
    }

    private void UpdateOpacityLabel() =>
        OpacityValueText.Text = $"{Math.Round(OpacitySlider.Value):0}%";

    private void ScheduleSettingsSave()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"设置保存失败：{ex.Message}");
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _edgeDock.BeginDrag();
            try { DragMove(); }
            finally { _edgeDock.EndDrag(); }
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        _idleFadeTimer.Stop();
        Hide();
    }

    private void ExitApplication()
    {
        _settingsSaveTimer.Stop();
        SaveSettings();
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _memoryTimer.Stop();
        _edgeDock.Dispose();
        if (_source is not null)
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, VisibilityHotkeyId);
            UnregisterHotKey(handle, ClickThroughHotkeyId);
            _source.RemoveHook(WindowProc);
        }

        _idleFadeTimer.Stop();
        _settingsSaveTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayImage.Dispose();
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
