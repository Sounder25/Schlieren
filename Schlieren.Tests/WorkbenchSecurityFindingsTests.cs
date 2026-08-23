using Schlieren.UI.ViewModels;

namespace Schlieren.Tests;

public sealed class WorkbenchSecurityFindingsTests
{
    [Fact]
    public void SecurityFindings_UpdatesVisibilityContractAndNotifiesBindings()
    {
        using var vm = new WorkbenchViewModel();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.False(vm.HasSecurityFindings);

        vm.SecurityFindings.Add(new SecurityFindingViewModel());

        Assert.True(vm.HasSecurityFindings);
        Assert.Contains(nameof(vm.HasSecurityFindings), notifications);

        notifications.Clear();
        vm.SecurityFindings.Clear();

        Assert.False(vm.HasSecurityFindings);
        Assert.Contains(nameof(vm.HasSecurityFindings), notifications);
    }
}
