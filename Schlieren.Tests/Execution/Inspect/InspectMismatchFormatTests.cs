using Schlieren.Core.Execution.Inspect;
using Schlieren.Tests.Inspect;
using Xunit;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class InspectMismatchFormatTests
{
    [Fact]
    public void Balance_MatchesGoldenSenderLine()
    {
        var line = InspectMismatchFormat.Balance(
            InspectGoldenCase.SenderHex,
            InspectGoldenCase.SenderExpected,
            InspectGoldenCase.SenderActual);
        Assert.Equal(InspectGoldenCase.SenderMismatch, line);
    }
}
