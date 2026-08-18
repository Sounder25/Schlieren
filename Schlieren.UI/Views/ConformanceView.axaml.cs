using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Views;

public partial class ConformanceView : UserControl
{
    public event Action<string, string, string, string>? OpenInWorkbench;

    public ConformanceView()
    {
        InitializeComponent();
        var vm = new ConformanceViewModel();
        DataContext = vm;
        vm.OpenInWorkbenchRequested += (json, name, fork, caseId) =>
            OpenInWorkbench?.Invoke(json, name, fork, caseId);
    }

    private ConformanceViewModel? Vm => DataContext as ConformanceViewModel;

    public void Reset() => Vm?.ResetResultsCommand.Execute(null);

    private void OnFailureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConformanceFailureRow row })
            Vm?.SelectFailureCommand.Execute(row);
    }

    public async void OnOpenSuiteFixtureClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || Vm is null) return;

        try
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Open a state_test from this suite",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("State test JSON") { Patterns = ["*.json"] },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            };

            if (Vm.FixturePathValid && Directory.Exists(Vm.ResolvedFixturePath))
            {
                var folder = await top.StorageProvider.TryGetFolderFromPathAsync(Vm.ResolvedFixturePath);
                if (folder != null)
                    options.SuggestedStartLocation = folder;
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                OpenInWorkbench?.Invoke(text, files[0].Name, Vm.SelectedFork, "");
                return;
            }

            Vm.OpenFixturePath(path);
        }
        catch (Exception ex)
        {
            if (Vm != null)
                Vm.StatusMessage = $"Open fixture failed: {ex.Message}";
        }
    }

    private void OnClusterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConformanceClusterRow row })
            Vm?.SelectClusterCommand.Execute(row);
    }
}
