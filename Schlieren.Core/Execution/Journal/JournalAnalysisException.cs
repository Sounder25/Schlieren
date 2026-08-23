namespace Schlieren.Core.Execution.Journal;

public sealed class JournalAnalysisException : InvalidOperationException
{
    public JournalAnalysisException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
