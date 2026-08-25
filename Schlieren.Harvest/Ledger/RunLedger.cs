using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Ledger;

/// <summary>
/// Atomic append-only run ledger stored under a directory as one JSON file per run.
///
/// Layout:
///   {ledgerDirectory}/
///     {runId}.json         ← committed entry (ContentEnvelope{RunRecord})
///     {runId}.json.tmp     ← transient write (deleted on success or cleanup)
///
/// Atomicity contract:
///   Writes use write-to-temp → fsync → atomic rename.  A reader never sees a
///   partial file: the committed .json is either absent (run not recorded) or
///   contains a complete, hash-verified envelope.
///
/// Append-only contract:
///   AppendAsync throws <see cref="LedgerCollisionException"/> if the target
///   .json already exists.  The ledger does NOT overwrite or delete entries.
///
/// Integrity contract:
///   ReadAsync recomputes the SHA-256 content hash and throws
///   <see cref="LedgerCorruptionException"/> if it does not match the stored value.
///
/// Thread safety:
///   Per-file rename is atomic on POSIX and NTFS (File.Move with overwrite:false
///   on Windows Server 2008+ uses MoveFileEx(MOVEFILE_WRITE_THROUGH)).
///   Concurrent writes to DIFFERENT RunIds are safe.
///   Concurrent writes to the SAME RunId will race; one will win and one will
///   receive LedgerCollisionException after the other commits.
/// </summary>
public sealed class RunLedger
{
    private const string SchemaVersion = "1";
    private const string TempSuffix    = ".tmp";
    private const string EntrySuffix   = ".json";

    private readonly string _directory;

    /// <param name="ledgerDirectory">
    /// Absolute path to the directory that holds run entries.
    /// Created automatically if it does not exist.
    /// </param>
    public RunLedger(string ledgerDirectory)
    {
        if (string.IsNullOrWhiteSpace(ledgerDirectory))
            throw new ArgumentException("Ledger directory must be a non-empty path.", nameof(ledgerDirectory));

        _directory = ledgerDirectory;
        System.IO.Directory.CreateDirectory(_directory);
    }

    // ── Public directory ─────────────────────────────────────────────────

    public string Directory => _directory;

    // ── Append ───────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically appends a <see cref="RunRecord"/> to the ledger.
    ///
    /// Steps:
    ///   1. Build a <see cref="ContentEnvelope{RunRecord}"/> with createdUtc stamped now (UTC).
    ///   2. Compute the SHA-256 content hash and embed it in the envelope.
    ///   3. Write canonical JSON to a .tmp file.
    ///   4. Flush and close the file handle.
    ///   5. Atomically rename .tmp → .json (fails if target already exists → LedgerCollisionException).
    ///
    /// Throws:
    ///   <see cref="LedgerCollisionException"/> — target .json already exists.
    ///   <see cref="ArgumentException"/>        — RunId is null or whitespace.
    ///   <see cref="IOException"/>              — filesystem error during write.
    /// </summary>
    public async Task AppendAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        if (record is null)        throw new ArgumentNullException(nameof(record));
        ValidateRunId(record.RunId);

        var committed = EntryPath(record.RunId);
        var temp      = committed + TempSuffix;

        // Collision check: fail fast before paying serialization cost.
        // (A TOCTOU race here is benign — rename below is the authoritative gate.)
        if (File.Exists(committed))
            throw new LedgerCollisionException(record.RunId, committed);

        // Build and hash the envelope
        var stub = new ContentEnvelope<RunRecord>(
            SchemaVersion: SchemaVersion,
            CreatedUtc:    DateTime.UtcNow,
            ContentHash:   "",
            Payload:       record);

        var hash     = ContentHasher.Compute(stub);
        var envelope = stub with { ContentHash = hash };
        var json     = HarvestJson.Serialize(envelope);
        var bytes    = Encoding.UTF8.GetBytes(json);

        // Write to temp, then rename
        try
        {
            await WriteAllBytesAsync(temp, bytes, cancellationToken);

            // Atomic rename — File.Move with overwrite:false throws if target exists.
            // On NTFS this is effectively atomic for the .json path.
            try
            {
                File.Move(temp, committed, overwrite: false);
            }
            catch (IOException)
            {
                // Target appeared between our pre-check and the rename.
                throw new LedgerCollisionException(record.RunId, committed);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp file on any failure path.
            try { File.Delete(temp); } catch { /* ignored */ }
            throw;
        }
    }

    // ── Read ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads and verifies one run entry by RunId.
    ///
    /// Throws:
    ///   <see cref="FileNotFoundException"/>     — no entry for this RunId.
    ///   <see cref="LedgerCorruptionException"/> — content hash mismatch.
    ///   <see cref="InvalidOperationException"/> — JSON cannot be deserialized.
    /// </summary>
    public async Task<ContentEnvelope<RunRecord>> ReadAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);

        var path = EntryPath(runId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No ledger entry found for run '{runId}'.", path);

        var bytes    = await File.ReadAllBytesAsync(path, cancellationToken);
        var envelope = HarvestJson.Deserialize<ContentEnvelope<RunRecord>>(bytes)
                       ?? throw new InvalidOperationException(
                           $"Ledger entry at '{path}' deserialized to null.");

        // Verify integrity: recompute hash over a stub with contentHash cleared
        var stub     = envelope with { ContentHash = "" };
        var expected = ContentHasher.Compute(stub);
        if (!string.Equals(expected, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new LedgerCorruptionException(runId, path, expected, envelope.ContentHash);

        return envelope;
    }

    // ── Existence ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if a committed entry for <paramref name="runId"/> exists.
    /// Does not verify the content hash.
    /// </summary>
    public bool Exists(string runId)
    {
        ValidateRunId(runId);
        return File.Exists(EntryPath(runId));
    }

    // ── List ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns lightweight summary entries for all committed runs in the ledger,
    /// ordered by <see cref="RunRecord.CompletedUtc"/> ascending.
    ///
    /// Entries that fail deserialization or hash verification are skipped and
    /// reported in the returned <see cref="LedgerListResult.CorruptEntries"/> list —
    /// the caller decides how to handle corruption without aborting the listing.
    /// </summary>
    public async Task<LedgerListResult> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        var files = System.IO.Directory.GetFiles(_directory, $"*{EntrySuffix}")
                                        .Where(f => !f.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                                        .ToArray();

        var entries       = new List<RunSummaryEntry>();
        var corruptPaths  = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                var envelope = HarvestJson.Deserialize<ContentEnvelope<RunRecord>>(bytes);
                if (envelope is null) { corruptPaths.Add(file); continue; }

                // Lightweight hash check
                var stub     = envelope with { ContentHash = "" };
                var computed = ContentHasher.Compute(stub);
                if (!string.Equals(computed, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    corruptPaths.Add(file);
                    continue;
                }

                var r = envelope.Payload;
                entries.Add(new RunSummaryEntry(
                    RunId:        r.RunId,
                    CampaignId:   r.CampaignId,
                    Kind:         r.Kind,
                    State:        r.State,
                    CompletedUtc: r.CompletedUtc,
                    TotalCases:   r.Summary.Total,
                    PassCount:    r.Summary.PassCount,
                    ContentHash:  envelope.ContentHash));
            }
            catch
            {
                corruptPaths.Add(file);
            }
        }

        entries.Sort((a, b) => DateTime.Compare(a.CompletedUtc, b.CompletedUtc));
        return new LedgerListResult(entries, corruptPaths);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private string EntryPath(string runId) =>
        Path.Combine(_directory, $"{runId}{EntrySuffix}");

    private static void ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("RunId must be a non-empty string.", nameof(runId));

        // Guard against path traversal
        if (runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"RunId '{runId}' contains characters that are invalid in file names.", nameof(runId));
    }

    private static async Task WriteAllBytesAsync(
        string path, byte[] bytes, CancellationToken cancellationToken)
    {
        // Open with FileOptions.WriteThrough to ensure data hits the OS buffer
        // before we attempt the rename.
        await using var fs = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize:  65536,
            useAsync:    true);

        await fs.WriteAsync(bytes, cancellationToken);
        await fs.FlushAsync(cancellationToken);
    }
}

/// <summary>Result of <see cref="RunLedger.ListRunsAsync"/>.</summary>
public sealed record LedgerListResult(
    IReadOnlyList<RunSummaryEntry> Entries,
    IReadOnlyList<string>          CorruptEntries);
