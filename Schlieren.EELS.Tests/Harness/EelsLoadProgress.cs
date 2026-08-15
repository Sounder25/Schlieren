namespace Schlieren.EELS.Tests.Harness;

/// <summary>Incremental fixture-load status for UI / harness progress.</summary>
public readonly record struct EelsLoadProgress(
    int FilesDone,
    int FilesTotal,
    int CasesLoaded,
    string CurrentFile);
