using System.Security.Cryptography;
using System.Text.Json;

namespace Schlieren.Harvest.Fixtures;

/// <summary>
/// Reads EELS state-test fixture files and extracts <see cref="FixtureCaseMetadata"/>
/// without executing any EVM logic.
///
/// Supported forks are exactly those recognised by Schlieren.Core's ForkRulesFactory.
/// This list is hand-maintained here to keep Schlieren.Harvest free of a Core compile
/// dependency on the switch expression.
/// </summary>
public static class EelsFixtureReader
{
    // Mirrors ForkRulesFactory.For() — all names it maps to a real rule set.
    private static readonly HashSet<string> SupportedForks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Frontier", "Homestead",
        "TangerineWhistle", "EIP150",
        "SpuriousDragon", "EIP158",
        "Byzantium",
        "Constantinople", "ConstantinopleFix",
        "Istanbul",
        "Berlin", "London",
        "Paris", "Merge",
        "Shanghai", "Cancun", "Prague", "Osaka"
    };

    /// <summary>
    /// Reads all fixture cases from a single JSON file.
    /// Returns one <see cref="FixtureCaseMetadata"/> per (caseId, fork) pair found.
    /// If the file is malformed, returns a single rejected entry with the appropriate code.
    /// </summary>
    public static IReadOnlyList<FixtureCaseMetadata> ReadFile(
        string absolutePath,
        string canonicalRoot,
        ISet<string> seenCaseIds)
    {
        var relPath = ToRelativePath(absolutePath, canonicalRoot);

        // Compute checksum before parsing
        string sha256;
        byte[] bytes;
        try
        {
            bytes  = File.ReadAllBytes(absolutePath);
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            return Rejected(relPath, AdmissionReasonCode.MalformedJson, ex.Message);
        }

        // Parse JSON
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return Rejected(relPath, AdmissionReasonCode.MalformedJson, ex.Message);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Rejected(relPath, AdmissionReasonCode.MalformedJson, "Root is not a JSON object");

            var results = new List<FixtureCaseMetadata>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var caseId   = prop.Name;
                var caseNode = prop.Value;

                var meta = AdmitCase(caseId, caseNode, relPath, sha256, seenCaseIds);
                results.Add(meta);
            }

            if (results.Count == 0)
                return Rejected(relPath, AdmissionReasonCode.MalformedJson, "No test entries found in file");

            return results;
        }
    }

    private static FixtureCaseMetadata AdmitCase(
        string caseId, JsonElement caseNode,
        string relPath, string sha256,
        ISet<string> seenCaseIds)
    {
        // Must be an object
        if (caseNode.ValueKind != JsonValueKind.Object)
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MalformedJson, "Case is not a JSON object");

        // Check fixture format if _info present
        if (caseNode.TryGetProperty("_info", out var infoNode))
        {
            var fmt = GetString(infoNode, "fixture-format") ?? GetString(infoNode, "fixture_format");
            if (fmt is not null && !string.Equals(fmt, "state_test", StringComparison.OrdinalIgnoreCase))
                return Reject(caseId, relPath, sha256, AdmissionReasonCode.UnsupportedFormat, $"fixture-format={fmt}");
        }

        // Must have post section with at least one fork
        if (!caseNode.TryGetProperty("post", out var postNode) || postNode.ValueKind != JsonValueKind.Object)
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MalformedJson, "No post section");

        // Must have pre
        if (!caseNode.TryGetProperty("pre", out _))
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MissingPreState, "pre section absent");

        // Find the first supported fork in post
        string? admittedFork = null;
        JsonElement admittedVariant = default;
        bool ambiguous = false;

        foreach (var forkProp in postNode.EnumerateObject())
        {
            if (!SupportedForks.Contains(forkProp.Name)) continue;
            if (forkProp.Value.ValueKind != JsonValueKind.Array) continue;
            var variants = forkProp.Value.EnumerateArray().ToList();
            if (variants.Count == 0) continue;

            if (admittedFork is not null)
            {
                // More than one supported fork in the same file is ambiguous —
                // the admission must target a specific fork.
                ambiguous = true;
                break;
            }
            admittedFork    = forkProp.Name;
            admittedVariant = variants[0];
            // Multiple variants for a single fork: we take variant[0] per spec — not ambiguous
        }

        if (ambiguous)
        {
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.AmbiguousVariant,
                "Fixture contains multiple supported forks or variants; target a specific fork for admission");
        }

        if (admittedFork is null)
        {
            // All forks in the file are unsupported
            var forks = postNode.EnumerateObject().Select(p => p.Name).ToList();
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.UnsupportedFork,
                $"No supported fork in post; found: {string.Join(", ", forks)}");
        }

        // First variant must have a state entry (post-state authority)
        if (!admittedVariant.TryGetProperty("state", out var stateNode) ||
            stateNode.ValueKind != JsonValueKind.Object)
        {
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MissingPostState,
                $"Fork {admittedFork} variant[0] has no state");
        }

        // Check status authority: receipt.status must be present
        if (!admittedVariant.TryGetProperty("receipt", out var receiptNode) ||
            receiptNode.ValueKind != JsonValueKind.Object ||
            !receiptNode.TryGetProperty("status", out _))
        {
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MissingStatusAuthority,
                $"Fork {admittedFork} variant[0] receipt has no status field");
        }

        // Check gas authority: receipt.cumulativeGasUsed must be present
        if (!receiptNode.TryGetProperty("cumulativeGasUsed", out _))
        {
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.MissingGasAuthority,
                $"Fork {admittedFork} variant[0] receipt has no cumulativeGasUsed field");
        }

        // Duplicate check
        if (!seenCaseIds.Add(caseId))
            return Reject(caseId, relPath, sha256, AdmissionReasonCode.DuplicateCaseId, "Duplicate caseId");

        // Detect storage dimensions from the fixture
        var dims = DetectDimensions(caseNode, stateNode);

        return new FixtureCaseMetadata(
            CaseId:      caseId,
            RelativePath: relPath,
            SourceSha256: sha256,
            Fork:         admittedFork,
            Dimensions:   dims,
            Admission:    AdmissionReasonCode.Admitted,
            Detail:       null);
    }

    // ── Dimension detection ───────────────────────────────────────────────
    // NOTE: Dimension detection uses bytecode opcode scanning (byte-level, not
    // substring match on hex strings) plus fixture path/description keywords as
    // secondary evidence. This is sufficient to guide the greedy set-cover selector
    // but is not a formal proof of storage lifecycle coverage; Campaign 1 should
    // validate selected fixtures manually before freezing the manifest.

    private const byte Opcode_SLOAD  = 0x54;
    private const byte Opcode_SSTORE = 0x55;
    private const byte Opcode_CALL   = 0xF1;
    private const byte Opcode_STATICCALL   = 0xFA;
    private const byte Opcode_DELEGATECALL = 0xF4;
    private const byte Opcode_CALLCODE     = 0xF2;

    private static IReadOnlySet<StorageDimension> DetectDimensions(
        JsonElement caseNode, JsonElement stateNode)
    {
        var dims = new HashSet<StorageDimension>();
        var pathHint = ""; // secondary: fixture URL / description keywords

        if (caseNode.TryGetProperty("_info", out var info))
        {
            var url  = GetString(info, "url") ?? "";
            var desc = GetString(info, "description") ?? "";
            pathHint = (url + " " + desc).ToLowerInvariant();
        }

        // Pre-state bytecode scan — proper opcode byte detection
        if (caseNode.TryGetProperty("pre", out var preNode))
        {
            foreach (var acct in preNode.EnumerateObject())
            {
                var codeHex = GetString(acct.Value, "code") ?? "";
                var codeBytes = ParseHexToBytes(codeHex);

                foreach (var b in codeBytes)
                {
                    if (b == Opcode_SSTORE)       dims.Add(StorageDimension.Sstore);
                    if (b == Opcode_SLOAD)         dims.Add(StorageDimension.Sload);
                    if (b == Opcode_CALL)          dims.Add(StorageDimension.CallWithStorage);
                    if (b == Opcode_STATICCALL)    dims.Add(StorageDimension.StaticCallWithStorage);
                    if (b == Opcode_DELEGATECALL)  dims.Add(StorageDimension.DelegateCallWithStorage);
                    if (b == Opcode_CALLCODE)      dims.Add(StorageDimension.CallCodeWithStorage);
                }

                // Secondary path hint
                if (pathHint.Contains("sstore")) dims.Add(StorageDimension.Sstore);
                if (pathHint.Contains("sload"))  dims.Add(StorageDimension.Sload);

                var storage = acct.Value.TryGetProperty("storage", out var s) ? s : default;
                if (storage.ValueKind == JsonValueKind.Object && storage.EnumerateObject().Any())
                    dims.Add(StorageDimension.NonZeroInitialStorage);
            }
        }

        // Post-state storage inspection: infer value transition dimensions
        foreach (var acct in stateNode.EnumerateObject())
        {
            if (!acct.Value.TryGetProperty("storage", out var stor)) continue;
            foreach (var slot in stor.EnumerateObject())
            {
                var val = slot.Value.GetString() ?? "";
                if (val == "0x0" || val == "0x" || val == "0x00")
                    dims.Add(StorageDimension.NonzeroToZero);
                else
                    dims.Add(StorageDimension.ZeroToNonzero);
            }
        }

        // EIP-2929 warm/cold from path hint
        if (pathHint.Contains("warm") || pathHint.Contains("eip2929") || pathHint.Contains("access_list"))
        {
            dims.Add(StorageDimension.WarmAccess);
            dims.Add(StorageDimension.ColdAccess);
        }

        return dims;
    }

    private static byte[] ParseHexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x") return Array.Empty<byte>();
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (s.Length % 2 != 0) s = "0" + s;
        if (s.Length == 0) return Array.Empty<byte>();
        try { return Convert.FromHexString(s); } catch { return Array.Empty<byte>(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<FixtureCaseMetadata> Rejected(
        string relPath, AdmissionReasonCode code, string? detail)
        => new[] { new FixtureCaseMetadata("", relPath, "", "", new HashSet<StorageDimension>(), code, detail) };

    private static FixtureCaseMetadata Reject(
        string caseId, string relPath, string sha256, AdmissionReasonCode code, string? detail)
        => new(caseId, relPath, sha256, "", new HashSet<StorageDimension>(), code, detail);

    private static string ToRelativePath(string absolutePath, string canonicalRoot)
    {
        // Normalize both to forward slashes
        var norm = Path.GetFullPath(absolutePath).Replace('\\', '/');
        var root = canonicalRoot.TrimEnd('/', '\\').Replace('\\', '/') + "/";
        if (norm.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return norm[root.Length..];
        return norm; // fallback — caller already checked containment
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }
}
