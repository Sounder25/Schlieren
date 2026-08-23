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
    private double _zoomLevel = 1.0;
    private const double ZoomMin  = 0.5;
    private const double ZoomMax  = 2.0;
    private const double ZoomStep = 0.1;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnWindowWheel, RoutingStrategies.Tunnel, handledEventsToo: false);
        SkinService.SkinChanged += OnSkinChanged;
        if (this.FindControl<ConformanceView>("ConformancePanel") is { } panel)
            panel.OpenInWorkbench += OnOpenFixtureInWorkbench;
    }

    private void OnWindowWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        ApplyZoomDelta(e.Delta.Y > 0 ? ZoomStep : -ZoomStep);
    }

    private void ApplyZoomDelta(double delta)
    {
        _zoomLevel = Math.Clamp(_zoomLevel + delta, ZoomMin, ZoomMax);
        _zoomLevel = Math.Round(_zoomLevel, 1);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (this.FindControl<LayoutTransformControl>("ZoomHost") is { } host)
            host.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        if (DataContext is WorkbenchViewModel vm)
            vm.StatusMessage = $"Zoom: {_zoomLevel * 100:0}%  (Ctrl+0 to reset)";
    }

    private void OnOpenFixtureInWorkbench(string json, string sourceName, string fork, string caseId)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        vm.ImportContractSource(json, sourceName, fork, caseId);
        SetConformanceMode(false);
    }

    private HarvestViewModel? _harvestVm;

    public MainWindow(WorkbenchViewModel viewModel) : this()
    {
        DataContext = viewModel;

        _harvestVm = new HarvestViewModel();
        _harvestVm.LoadFixtureRequested += OnHarvestLoadFixture;

        // Wire DataContext after window is fully loaded
        Opened += (_, _) =>
        {
            if (this.FindControl<HarvestView>("HarvestPanel") is { } hv)
                hv.DataContext = _harvestVm;
            _harvestVm.StartPolling();
            BuildAppearanceMenu();
        };
    }

    private void OnHarvestLoadFixture(string fixturePath, string txHash, string fork)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        try
        {
            var text = File.ReadAllText(fixturePath);

            // Try standard fixture formats first
            if (WorkbenchFixtureLoader.LooksLikeStateTest(text) ||
                WorkbenchPrestateLoader.LooksLikePrestate(text))
            {
                vm.ImportContractSource(text, txHash, fork, txHash);
            }
            else
            {
                // Harvest fixture — extract bytecode + calldata directly
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;

                var bytecode = root.TryGetProperty("bytecode", out var bc)
                    ? bc.GetString() ?? "" : "";
                var calldata = root.TryGetProperty("calldata", out var cd)
                    ? cd.GetString() ?? "" : "";
                var forkStr = root.TryGetProperty("fork", out var fk)
                    ? fk.GetString() ?? fork : fork;

                if (!string.IsNullOrEmpty(bytecode) && bytecode != "0x")
                {
                    if (forkStr != null && vm.AvailableForks.Contains(forkStr))
                        vm.SelectedFork = forkStr;

                    vm.BytecodeInput = bytecode;
                    if (!string.IsNullOrEmpty(calldata) && calldata != "0x")
                        vm.CallDataHex = calldata;

                    vm.StatusMessage = $"Loaded {txHash[..20]}… — press F5 to run";
                }
                else
                {
                    vm.StatusMessage = "No bytecode found in harvest fixture";
                    return;
                }
            }

            SetHarvestMode(false);
            SetActiveTab("Workbench");
        }
        catch (Exception ex)
        {
            if (DataContext is WorkbenchViewModel v)
                v.StatusMessage = $"Harvest load failed: {ex.Message}";
        }
    }

    private void OnHarvestClick(object? sender, PointerPressedEventArgs e)
    {
        var panel = this.FindControl<HarvestView>("HarvestPanel");
        if (panel is null) return;
        SetHarvestMode(!panel.IsVisible);
    }

    private void SetHarvestMode(bool enabled)
    {
        if (this.FindControl<HarvestView>("HarvestPanel") is { } panel)
            panel.IsVisible = enabled;
        if (this.FindControl<Grid>("WorkbenchView") is { } wb)
            wb.IsVisible = !enabled && !(this.FindControl<ConformanceView>("ConformancePanel")?.IsVisible ?? false);
        if (enabled) { SetActiveTab("Harvest"); }
        else         { SetActiveTab("Workbench"); }
    }

    private void OnSkinChanged(UiSkin skin)
    {
        RefreshAppearanceChecks();
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

        // Ctrl+O / Ctrl+Shift+O / Ctrl+1 / Ctrl+2 / Ctrl+3
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                _zoomLevel = 1.0;
                ApplyZoom();
                e.Handled = true; return;
            }
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                ApplyZoomDelta(ZoomStep);
                e.Handled = true; return;
            }
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                ApplyZoomDelta(-ZoomStep);
                e.Handled = true; return;
            }
            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                _zoomLevel = 1.0;
                ApplyZoom();
                e.Handled = true; return;
            }
            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                OnWorkbenchViewClick(this, new RoutedEventArgs());
                e.Handled = true; return;
            }
            if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                SetConformanceMode(!this.FindControl<ConformanceView>("ConformancePanel")?.IsVisible ?? false);
                e.Handled = true; return;
            }
            if (e.Key == Key.D3 || e.Key == Key.NumPad3)
            {
                SetHarvestMode(!this.FindControl<HarvestView>("HarvestPanel")?.IsVisible ?? false);
                e.Handled = true; return;
            }
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
        if (this.FindControl<Grid>("WorkbenchView") is { } wb)
            wb.IsVisible = !enabled && !(this.FindControl<HarvestView>("HarvestPanel")?.IsVisible ?? false);
        SetActiveTab(enabled ? "Conformance" : "Workbench");
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

    // Menu-compatible overloads (MenuItem.Click uses RoutedEventArgs)
    public void OnExitClick(object? sender, RoutedEventArgs e) => Close();
    private void OnConformanceMenuClick(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<ConformanceView>("ConformancePanel");
        if (panel is null) return;
        SetConformanceMode(!panel.IsVisible);
    }

    private void OnHarvestMenuClick(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<HarvestView>("HarvestPanel");
        if (panel is null) return;
        SetHarvestMode(!panel.IsVisible);
    }

    private void OnWorkbenchViewClick(object? sender, RoutedEventArgs e)
    {
        SetConformanceMode(false);
        SetHarvestMode(false);
    }

    private void OnWorkbenchTabClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        SetHarvestMode(false);
        SetActiveTab("Workbench");
    }

    private void OnConformanceTabClick(object? sender, PointerPressedEventArgs e)
    {
        SetHarvestMode(false);
        SetConformanceMode(true);
        SetActiveTab("Conformance");
    }

    private void OnHarvestTabClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        SetHarvestMode(true);
        SetActiveTab("Harvest");
    }

    private void OnInterferenceTabClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        SetHarvestMode(false);
        // Interference view placeholder — will show WorkbenchView until implemented
        SetActiveTab("Interference");
    }

    private void OnFlowTabClick(object? sender, PointerPressedEventArgs e)
    {
        SetConformanceMode(false);
        SetHarvestMode(false);
        // Flow view placeholder — will show WorkbenchView until implemented
        SetActiveTab("Flow");
    }

    private void SetActiveTab(string tab)
    {
        if (this.FindControl<Border>("TabWorkbench")    is { } tw)  tw.Classes.Set("active",  tab == "Workbench");
        if (this.FindControl<Border>("TabInterference") is { } ti)  ti.Classes.Set("active",  tab == "Interference");
        if (this.FindControl<Border>("TabFlow")         is { } tf)  tf.Classes.Set("active",  tab == "Flow");
        if (this.FindControl<Border>("TabConformance")  is { } tc)  tc.Classes.Set("active",  tab == "Conformance");
        if (this.FindControl<Border>("TabHarvest")      is { } th)  th.Classes.Set("active",  tab == "Harvest");
    }

    private void OnOpenN8nClick(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
              { FileName = "http://localhost:5678", UseShellExecute = true }); }
        catch { }
    }

    private void OnOpenCorpusFolderClick(object? sender, RoutedEventArgs e)
    {
        const string corpus = @"C:\projects\Schlieren\muscle\corpus";
        if (!Directory.Exists(corpus)) Directory.CreateDirectory(corpus);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
              { FileName = corpus, UseShellExecute = true }); }
        catch { }
    }

    private void OnHarvestSettingsClick(object? sender, RoutedEventArgs e)
    {
        // For now open Harvest panel — settings dialog comes later
        SetHarvestMode(true);
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchViewModel vm)
            vm.StatusMessage = "SCHLIEREN — .NET 8 Ethereum Execution & Verification Engine · github.com/schlieren";
    }
}
