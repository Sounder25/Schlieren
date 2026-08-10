using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Scrutor.UI.ViewModels;

namespace Scrutor.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    public MainWindow(WorkbenchViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;

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

        switch (e.Key)
        {
            case Key.F5:
                _ = vm.RunBytecodeCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
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
            tab.Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(enabled ? "#2d0060" : "#1A0A2E"));
        }
    }

    private void SetVisible(string name, bool visible)
    {
        if (this.FindControl<Control>(name) is { } c)
            c.IsVisible = visible;
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
                SuggestedFileName = "scrutor_trace.json",
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
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
                lines.Add(line);

            var path = file.TryGetLocalPath() ?? file.Name;
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
