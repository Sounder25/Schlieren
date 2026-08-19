namespace Schlieren.UI.ViewModels;

public sealed class HarvestEntry
{
    public string TxHash       { get; init; } = "";
    public string ShortHash    => TxHash.Length >= 12
        ? TxHash[..8] + "…" + TxHash[^6..] : TxHash;

    public long   BlockNumber  { get; init; }
    public string Fork         { get; init; } = "";
    public string CandidateType{ get; init; } = "";
    public string Outcome      { get; init; } = "";

    public long   GasMainnet   { get; init; }
    public long   GasSchlieren { get; init; }
    public long   GasDelta     => GasSchlieren - GasMainnet;

    public string InputData    { get; init; } = "";
    public string FromAddress  { get; init; } = "";
    public string ToAddress    { get; init; } = "";
    public int    PriorityScore{ get; init; }

    // Enriched fields from Etherscan + 4byte
    public string? ContractName  { get; init; }
    public bool?   IsVerified    { get; init; }
    public string? Deployer      { get; init; }
    public string? DeployedAt    { get; init; }
    public long?   DeployedBlock { get; init; }
    public string? FunctionName  { get; init; }
    public string  BlockDate     { get; init; } = "";
    public double  ValueEth      { get; init; }

    // Display helpers
    public string DisplayName => ContractName ?? CandidateType;
    public string FunctionDisplay => FunctionName is { Length: > 0 } f
        ? f.Contains('(') ? f[..f.IndexOf('(')] : f
        : "";
    public string VerifiedBadge => IsVerified == true ? "✓ verified" : IsVerified == false ? "unverified" : "";
    public string ValueDisplay  => ValueEth > 0 ? $"{ValueEth:0.######} ETH" : "";

    public string FixturePath  { get; init; } = "";
    public DateTime HarvestedAt{ get; init; }

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - HarvestedAt;
            if (diff.TotalSeconds < 90)  return "just now";
            if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours} hr ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }

    public bool IsDivergence => Outcome == "EXECUTED_DIVERGENCE";
    public bool IsPass       => Outcome == "EXECUTED_PASS";
    public bool IsFailed     => Outcome is "CAPTURE_FAILED" or "EXECUTION_FAILED";
    public bool IsDiscovered => Outcome == "DISCOVERED";

    public string BadgeText => Outcome switch
    {
        "EXECUTED_PASS"       => "Pass",
        "EXECUTED_DIVERGENCE" => "Divergence",
        "CAPTURE_FAILED"
        or "EXECUTION_FAILED" => "Failed",
        "DISCOVERED"          => "Discovered",
        _                     => Outcome
    };

    public string CalldataPreview => InputData.Length > 10
        ? InputData[..Math.Min(66, InputData.Length)] + (InputData.Length > 66 ? "…" : "")
        : "—";

    public string DeltaText   => GasDelta == 0 ? "" : $"Δ {(GasDelta > 0 ? "+" : "")}{GasDelta:N0}";
    public bool   CanLoad     => true; // all entries can be investigated
    public string GasDisplay  => GasMainnet > 0 ? $"{GasMainnet:N0}" : "—";
}

