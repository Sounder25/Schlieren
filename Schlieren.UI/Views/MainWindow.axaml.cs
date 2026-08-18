using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Schlieren.UI.Branding;
using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel + handledEventsToo: F5 must fire even when a TextBox has focus
        // (bytecode / calldata / fixture path). Bubble-only KeyDown is eaten there.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        BuildAppearanceMenu();
        ApplyWatermarkArt(SkinService.Current);
        SkinService.SkinChanged += OnSkinChanged;
        if (this.FindControl<ConformanceView>("ConformancePanel") is { } panel)
            panel.OpenInWorkbench += OnOpenFixtureInWorkbench;
    }

    private void OnOpenFixtureInWorkbench(string json, string sourceName, string fork, string caseId)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        vm.ImportContractSource(json, sourceName, fork, caseId);
        SetConformanceMode(false);
    }

    public MainWindow(WorkbenchViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnSkinChanged(UiSkin skin)
    {
        RefreshAppearanceChecks();
        ApplyWatermarkArt(skin);
    }

    private void BuildAppearanceMenu()
    {
        var menu = this.FindControl<MenuItem>("AppearanceMenu");
        if (menu is null) return;

        menu.Items.Clear();

        // Category order for eth/sounder storytelling
        var order = new[] { "Comfort", "Brand", "Sounder", "Ethereum Dev", "Utility" };
        foreach (var cat in order)
        {
            var skins = SkinCatalog.All.Where(s => s.Category == cat).ToList();
            if (skins.Count == 0) continue;

            var group = new MenuItem { Header = cat, IsEnabled = true };
            foreach (var skin in skins)
            {
                var item = new MenuItem
                {
                    Header = skin.DisplayName,
                    Tag = skin.Id,
                    ToggleType = MenuItemToggleType.Radio,
                    GroupName = "SchlierenSkin",
                };
                ToolTip.SetTip(item, skin.Description);
                item.Click += (_, _) => SkinService.Apply(skin.Id);
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Tip: Arctic Night = long sessions · Eth Violet / Void = screenshots",
            IsEnabled = false
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Sounder Field Ops uses #FF6A00 blaze + navy from Sounder brand",
            IsEnabled = false
        });

        RefreshAppearanceChecks();
    }

    private void RefreshAppearanceChecks()
    {
        var menu = this.FindControl<MenuItem>("AppearanceMenu");
        if (menu is null) return;
        var current = SkinService.Current.Id;
        foreach (var obj in menu.Items)
        {
            if (obj is not MenuItem group) continue;
            foreach (var child in group.Items)
            {
                if (child is MenuItem { Tag: string id } mi)
                    mi.IsChecked = string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Swap center-panel watermark art to match the active skin motif.</summary>
    private void ApplyWatermarkArt(UiSkin skin)
    {
        var schlieren = this.FindControl<Control>("WatermarkSchlieren");
        var eth = this.FindControl<Control>("WatermarkEth");
        var sounder = this.FindControl<Control>("WatermarkSounder");
        var voidSigil = this.FindControl<Control>("WatermarkVoid");

        void Show(Control? c, bool on)
        {
            if (c is not null) c.IsVisible = on;
        }

        Show(schlieren, false);
        Show(eth, false);
        Show(sounder, false);
        Show(voidSigil, false);

        switch (skin.ArtMotif)
        {
            case SkinArtMotif.EthDiamond:
                Show(eth, true);
                break;
            case SkinArtMotif.SounderSigil:
                Show(sounder, true);
                break;
            case SkinArtMotif.VoidSigil:
                Show(voidSigil, true);
                break;
            case SkinArtMotif.None:
                break;
            default:
                Show(schlieren, true);
                break;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        var inText = focused is TextBox || focused?.GetType().FullName == "AvaloniaEdit.TextEditor";

        if (e.Key == Key.F5)
        {
            RunFocusedSurface();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            e.Key == Key.R)
        {
            OnResetClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (DataContext is not WorkbenchViewModel vm) return;

        // Ctrl+O / Ctrl+Shift+O
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnOpenFolderClick(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O)
            {
                OnOpenFileClick(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

        // Typing keys stay with the text box; function/nav keys below do not.
        if (inText) return;

        switch (e.Key)
        {
            case Key.F10:
                vm.StepForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F11:
                vm.StepBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Space:
                vm.ToggleAutoPlayCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Home:
                vm.JumpToStartCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.End:
                vm.JumpToEndCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void RunFocusedSurface()
    {
        var panel = this.FindControl<ConformanceView>("ConformancePanel");
        if (panel is { IsVisible: true, DataContext: ConformanceViewModel cvm })
        {
            if (cvm.RunCommand.CanExecute(null))
                _ = cvm.RunCommand.ExecuteAsync(null);
            return;
        }

        if (DataContext is WorkbenchViewModel vm)
            _ = vm.RunBytecodeCommand.ExecuteAsync(null);
    }

    private void OnTabClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ProjectFileViewModel file
            && DataContext is WorkbenchViewModel vm)
        {
            vm.SelectFileCommand.Execute(file);
        }
    }

    private void OnFileClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ProjectFileViewModel file
            && DataContext is WorkbenchViewModel vm)
        {
            vm.SelectFileCommand.Execute(file);
        }
    }

    private void OnCallGraphClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        if (DataContext is WorkbenchViewModel vm)
            vm.ShowCallGraphCommand.Execute(null);
    }

    private void OnSourceClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        if (DataContext is WorkbenchViewModel vm)
            vm.ShowSourceCommand.Execute(null);
    }

    private void OnConformanceClick(object? sender, PointerPressedEventArgs e)
    {
        var panel = this.FindControl<ConformanceView>("ConformancePanel");
        if (panel is null) return;

        // Toggle: open if closed, close if already open
        SetConformanceMode(!panel.IsVisible);
    }

    /// <summary>
    /// Reset the visible surface: Conformance results when that tab is up,
    /// otherwise the bytecode workbench.
    /// </summary>
    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<ConformanceView>("ConformancePanel");
        if (panel is { IsVisible: true })
        {
            panel.Reset();
            return;
        }

        if (DataContext is WorkbenchViewModel vm)
            vm.ResetWorkbenchCommand.Execute(null);
    }

    /// <summary>
    /// Conformance and workbench use different fork state. Hide workbench-only
    /// controls (especially the top FORK combo) so they can't look "live" for the suite.
    /// </summary>
    private void SetConformanceMode(bool enabled)
    {
        if (this.FindControl<ConformanceView>("ConformancePanel") is { } panel)
            panel.IsVisible = enabled;

        if (this.FindControl<Grid>("MainWorkbenchGrid") is { } grid)
            grid.IsVisible = !enabled;

        SetVisible("WorkbenchTopCenter", !enabled);
        SetVisible("WorkbenchTopActions", !enabled);
        SetVisible("WorkbenchTabActions", !enabled);
        SetVisible("WorkbenchBytecodeBar", !enabled);
        SetVisible("WorkbenchFindingsBar", !enabled);
        SetVisible("ConformanceModeBadge", enabled);

        if (this.FindControl<Border>("ConformanceTab") is { } tab)
        {
            var hex = enabled ? SkinService.Current.SelectionBg : SkinService.Current.PanelBg;
            tab.Background = new SolidColorBrush(Color.Parse(hex));
        }
    }

    private void SetVisible(string name, bool visible)
    {
        if (this.FindControl<Control>(name) is { } c)
            c.IsVisible = visible;
    }

    private void OnCallGraphRowClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CallGraphRowViewModel row }
            && DataContext is WorkbenchViewModel vm)
        {
            vm.SelectCallGraphRowCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void OnInstructionClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is InstructionViewModel instr
            && DataContext is WorkbenchViewModel vm)
        {
            vm.JumpToInstructionCommand.Execute(instr);
            e.Handled = true;
        }
    }

    public async void OnExportTraceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export execution trace (structLog JSON)",
                DefaultExtension = "json",
                SuggestedFileName = "schlieren_trace.json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON") { Patterns = ["*.json"] }
                ]
            });

            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                // Fallback: temp export if picker has no local path
                await vm.ExportTraceJsonCommand.ExecuteAsync(null);
                return;
            }

            await vm.ExportTraceToPathAsync(path);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Trace export failed: {ex.Message}";
        }
    }

    public async void OnLoadPrestateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load fixture, pre-state, or contract JSON",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON / hex") { Patterns = ["*.json", "*.hex", "*.txt"] },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            if (stream.Length > 10 * 1024 * 1024)
            {
                vm.StatusMessage = "File exceeds 10 MB limit. Aborting load to prevent Out-Of-Memory.";
                return;
            }
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            vm.ImportContractSource(text, files[0].Name);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Pre-state load failed: {ex.Message}";
        }
    }

    public async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Contract or Bytecode File",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Ethereum / bytecode")
                    {
                        Patterns = ["*.sol", "*.yul", "*.json", "*.hex", "*.txt", "*.bin"]
                    },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            });

            if (files.Count == 0) return;

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            if (stream.Length > 10 * 1024 * 1024)
            {
                vm.StatusMessage = "File exceeds 10 MB limit. Aborting load to prevent Out-Of-Memory.";
                return;
            }
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
                lines.Add(line);

            var path = file.TryGetLocalPath() ?? file.Name;
            var text = string.Join("\n", lines);
            if (WorkbenchFixtureLoader.LooksLikeStateTest(text) ||
                WorkbenchPrestateLoader.LooksLikePrestate(text))
            {
                vm.ImportContractSource(text, file.Name);
                return;
            }

            vm.AddCustomFile(file.Name, path, lines);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Open file failed: {ex.Message}";
        }
    }

    public async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open Project Workspace Folder",
                AllowMultiple = false
            });

            if (folders.Count == 0) return;

            var dirPath = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                vm.StatusMessage = "Could not resolve folder path";
                return;
            }

            var patterns = new[] { "*.sol", "*.hex", "*.txt", "*.yul" };
            var found = patterns
                .SelectMany(p => Directory.GetFiles(dirPath, p, SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f)
                .Take(50)
                .ToList();

            foreach (var f in found)
            {
                var content = await File.ReadAllLinesAsync(f);
                vm.AddCustomFile(Path.GetFileName(f), f, content);
            }

            vm.StatusMessage = found.Count == 0
                ? "No .sol/.hex/.txt/.yul files found"
                : $"Loaded {found.Count} file(s) from folder";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Open folder failed: {ex.Message}";
        }
    }

    public async void OnExportReportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Security & Gas Audit Report",
                DefaultExtension = "md",
                SuggestedFileName = "AUDIT_REPORT.md",
                FileTypeChoices =
                [
                    new FilePickerFileType("Markdown") { Patterns = ["*.md"] }
                ]
            });

            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                vm.StatusMessage = "Could not resolve save path";
                return;
            }

            await vm.GenerateAuditReportAsync(path);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    public void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
