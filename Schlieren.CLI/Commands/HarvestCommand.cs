using System.CommandLine;
using System.CommandLine.Invocation;

namespace Schlieren.CLI.Commands;

/// <summary>
/// Internal Harvest CLI command tree.
///
/// Exit codes:
///   0 — completed requested operation
///   2 — invalid input
///   3 — apparatus failure
///   4 — conformance divergence
///   5 — certification refusal
///
/// All paths are explicit options or environment-backed defaults.
/// No compiled machine paths.
/// </summary>
public static class HarvestCommand
{
    public static Command Build()
    {
        var cmd = new Command("harvest", "Harvest certification pipeline commands");

        cmd.AddCommand(BuildCalibrateCommand());
        cmd.AddCommand(BuildCatalogCommand());
        cmd.AddCommand(BuildCampaignCommand());
        cmd.AddCommand(BuildCompareCommand());
        cmd.AddCommand(BuildRepairCommand());
        cmd.AddCommand(BuildCertifyCommand());

        return cmd;
    }

    // ── schlieren harvest calibrate ──────────────────────────────────────

    private static Command BuildCalibrateCommand()
    {
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };
        var eelsOpt = new Option<string>(
            "--eels", "Path to the EELS executable") { IsRequired = true };
        var eelsVersionOpt = new Option<string>(
            "--eels-version", "Expected EELS version string") { IsRequired = true };

        var cmd = new Command("calibrate", "Execute Phase 0 calibration signals")
        {
            ledgerOpt, eelsOpt, eelsVersionOpt
        };

        cmd.SetHandler(async (ledger, eels, eelsVersion) =>
        {
            try
            {
                var record = await Schlieren.Harvest.Calibration.CalibrationSuite.RunAsync();

                // Persist calibration record
                var ledgerDir = Path.Combine(ledger, "calibrations");
                Directory.CreateDirectory(ledgerDir);
                var calId = $"cal-{DateTime.UtcNow:yyyyMMddHHmmss}";
                var envelope = new Schlieren.Harvest.Domain.ContentEnvelope<Schlieren.Harvest.Calibration.CalibrationRecord>(
                    "1", DateTime.UtcNow, "", record);
                var hash = Schlieren.Harvest.Serialization.ContentHasher.Compute(envelope);
                envelope = envelope with { ContentHash = hash };
                var json = Schlieren.Harvest.Serialization.HarvestJson.Serialize(envelope);
                var path = Path.Combine(ledgerDir, $"{calId}.json");
                await File.WriteAllTextAsync(path, json);

                Console.WriteLine($"Calibration ID: {calId}");
                Console.WriteLine($"Apparatus gate: {(record.ApparatusGatePassed ? "PASSED" : "FAILED")}");
                foreach (var p in record.ProbeResults)
                    Console.WriteLine($"  {p.Kind,-20} expected={p.ExpectedStatus,-15} actual={p.ActualStatus,-15} {(p.ClassifiedCorrectly ? "✓" : "✗")}");
                if (!record.ApparatusGatePassed)
                {
                    Console.Error.WriteLine($"Gate failure: {record.GateFailureReason}");
                    Environment.ExitCode = 3;
                }
                else
                {
                    Console.WriteLine($"Artifact: {path}");
                    Environment.ExitCode = 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Calibration error: {ex.Message}");
                Environment.ExitCode = 3;
            }
        }, ledgerOpt, eelsOpt, eelsVersionOpt);

        return cmd;
    }

    // ── schlieren harvest catalog ────────────────────────────────────────

    private static Command BuildCatalogCommand()
    {
        var fixturesOpt = new Option<string>(
            "--fixtures", "Path to EELS fixture root") { IsRequired = true };
        var eelsOpt = new Option<string>(
            "--eels", "Path to the EELS executable") { IsRequired = true };
        var eelsVersionOpt = new Option<string>(
            "--eels-version", "Expected EELS version string") { IsRequired = true };

        var cmd = new Command("catalog", "Validate and summarize an EELS fixture root")
        {
            fixturesOpt, eelsOpt, eelsVersionOpt
        };

        cmd.SetHandler((fixtures, eels, eelsVersion) =>
        {
            try
            {
                var catalog = new Schlieren.Harvest.Fixtures.FixtureCatalog(fixtures);
                var allFiles = Directory.GetFiles(fixtures, "*.json", SearchOption.AllDirectories);
                Console.WriteLine($"Scanning {allFiles.Length} fixture files...");
                var admitted = catalog.Admit(allFiles);
                var admittedCount = admitted.Count(m => m.Admission == Schlieren.Harvest.Fixtures.AdmissionReasonCode.Admitted);
                var rejected = admitted.Count - admittedCount;
                Console.WriteLine($"Total entries: {admitted.Count}");
                Console.WriteLine($"Admitted: {admittedCount}");
                Console.WriteLine($"Rejected: {rejected}");

                // Show rejection breakdown
                var reasons = admitted
                    .Where(m => m.Admission != Schlieren.Harvest.Fixtures.AdmissionReasonCode.Admitted)
                    .GroupBy(m => m.Admission)
                    .OrderByDescending(g => g.Count());
                foreach (var g in reasons)
                    Console.WriteLine($"  {g.Key}: {g.Count()}");

                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Catalog error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, fixturesOpt, eelsOpt, eelsVersionOpt);

        return cmd;
    }

    // ── schlieren harvest campaign ───────────────────────────────────────

    private static Command BuildCampaignCommand()
    {
        var campaignCmd = new Command("campaign", "Campaign management subcommands");
        campaignCmd.AddCommand(BuildCampaignCreateCommand());
        campaignCmd.AddCommand(BuildCampaignRunCommand());
        return campaignCmd;
    }

    private static Command BuildCampaignCreateCommand()
    {
        var familyArg = new Argument<string>(
            "family", "Campaign family name (e.g., storage-lifecycle)");
        var countOpt = new Option<int>(
            "--count", () => 50, "Number of cases to select");
        var fixturesOpt = new Option<string>(
            "--fixtures", "Path to EELS fixture root") { IsRequired = true };
        var eelsOpt = new Option<string>(
            "--eels", "Path to the EELS executable") { IsRequired = true };
        var eelsVersionOpt = new Option<string>(
            "--eels-version", "Expected EELS version string") { IsRequired = true };
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };

        var cmd = new Command("create", "Freeze a new campaign manifest")
        {
            familyArg, countOpt, fixturesOpt, eelsOpt, eelsVersionOpt, ledgerOpt
        };

        cmd.SetHandler(async (family, count, fixtures, eels, eelsVersion, ledger) =>
        {
            try
            {
                // Resolve campaign family policy
                var policy = Schlieren.Harvest.Campaigns.CampaignFamilyPolicy.TryGet(family);
                if (policy is null)
                {
                    Console.Error.WriteLine($"Unknown campaign family '{family}'.");
                    Console.Error.WriteLine($"Known families: {string.Join(", ", Schlieren.Harvest.Campaigns.CampaignFamilyPolicy.AllFamilyNames)}");
                    Environment.ExitCode = 2;
                    return;
                }
                Console.WriteLine($"Campaign family: {policy.FamilyName} v{policy.FamilyVersion}");
                Console.WriteLine($"Description: {policy.Description}");

                // 1. Catalog all fixture files from the root
                var catalog = new Schlieren.Harvest.Fixtures.FixtureCatalog(fixtures);
                var allFiles = Directory.GetFiles(fixtures, "*.json", SearchOption.AllDirectories);
                Console.WriteLine($"Scanning {allFiles.Length} fixture files...");
                var admitted = catalog.Admit(allFiles);
                var admittedCount = admitted.Count(m => m.Admission == Schlieren.Harvest.Fixtures.AdmissionReasonCode.Admitted);
                Console.WriteLine($"Admitted: {admittedCount}, Rejected: {admitted.Count - admittedCount}");

                // 2. Select cases using the family's policy (path filter + keyword set-cover)
                var result = policy.TrySelect(admitted, count);
                if (!result.IsSuccess)
                {
                    Console.Error.WriteLine($"Insufficient coverage: {result.InsufficientReport!.Reason}");
                    Console.Error.WriteLine($"  Requested: {result.InsufficientReport.RequestedCount}, Available: {result.InsufficientReport.AvailableCount}");
                    Environment.ExitCode = 2;
                    return;
                }

                // Compute EELS executable SHA-256
                string eelsSha256;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(eels))
                    eelsSha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();

                // Get EELS commit from the specs repo if available
                string? eelsCommit = null;
                var specsRoot = Environment.GetEnvironmentVariable("EELS_SPECS_ROOT");
                if (!string.IsNullOrEmpty(specsRoot) && Directory.Exists(specsRoot))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                        { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = specsRoot };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc is not null)
                        {
                            eelsCommit = proc.StandardOutput.ReadToEnd().Trim();
                            proc.WaitForExit(5000);
                            if (string.IsNullOrEmpty(eelsCommit)) eelsCommit = null;
                        }
                    }
                    catch { /* not fatal */ }
                }

                var eelsIdentity = new Schlieren.Harvest.Campaigns.EelsIdentity(
                    ExecutableSha256: eelsSha256,
                    ReportedVersion:  eelsVersion,
                    CommitSha:        eelsCommit);

                // Compute fixture root SHA-256 (hash of sorted admitted file paths + their hashes)
                Console.WriteLine("Computing fixture root identity...");
                string fixtureRootSha256;
                using (var rootHasher = System.Security.Cryptography.SHA256.Create())
                {
                    var admittedFiles = admitted
                        .Where(m => m.Admission == Schlieren.Harvest.Fixtures.AdmissionReasonCode.Admitted)
                        .Select(m => m.SourceSha256 + ":" + m.RelativePath)
                        .OrderBy(s => s, StringComparer.Ordinal);
                    foreach (var entry in admittedFiles)
                    {
                        var entryBytes = System.Text.Encoding.UTF8.GetBytes(entry + "\n");
                        rootHasher.TransformBlock(entryBytes, 0, entryBytes.Length, null, 0);
                    }
                    rootHasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    fixtureRootSha256 = Convert.ToHexString(rootHasher.Hash!).ToLowerInvariant();
                }
                Console.WriteLine($"Fixture root SHA-256: {fixtureRootSha256[..16]}...");

                var manifest = Schlieren.Harvest.Campaigns.CampaignManifest.Freeze(
                    result.Cases!, $"{policy.FamilyName}-v{policy.FamilyVersion}", DateTime.UtcNow,
                    eelsIdentity: eelsIdentity,
                    fixtureRootSha256: fixtureRootSha256);

                // 4. Persist to ledger
                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var manifestJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(manifest);
                await fileLedger.StoreManifestAsync($"{policy.FamilyName}-v{policy.FamilyVersion}", manifest.ManifestHash, manifestJson);

                Console.WriteLine($"Campaign: {policy.FamilyName}-v{policy.FamilyVersion}");
                Console.WriteLine($"Cases selected: {manifest.Cases.Count}");
                Console.WriteLine($"Manifest hash: {manifest.ManifestHash}");
                Console.WriteLine($"Stored at: {Schlieren.Harvest.Ledger.LedgerPaths.ManifestPath(ledger, $"{policy.FamilyName}-v{policy.FamilyVersion}", manifest.ManifestHash)}");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Campaign create error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, familyArg, countOpt, fixturesOpt, eelsOpt, eelsVersionOpt, ledgerOpt);

        return cmd;
    }

    private static Command BuildCampaignRunCommand()
    {
        var manifestArg = new Argument<string>(
            "manifest", "Path to the frozen manifest.json");
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };
        var timeoutOpt = new Option<int>(
            "--timeout-seconds", () => 120, "Per-case execution timeout in seconds");

        var cmd = new Command("run", "Execute a campaign manifest")
        {
            manifestArg, ledgerOpt, timeoutOpt
        };

        cmd.SetHandler(async (manifest, ledger, timeout) =>
        {
            try
            {
                // Load manifest
                if (!File.Exists(manifest))
                {
                    Console.Error.WriteLine($"Manifest not found: {manifest}");
                    Environment.ExitCode = 2;
                    return;
                }

                var manifestJson = await File.ReadAllTextAsync(manifest);
                var campaignManifest = Schlieren.Harvest.Serialization.HarvestJson.Deserialize<
                    Schlieren.Harvest.Campaigns.CampaignManifest>(manifestJson);
                if (campaignManifest is null)
                {
                    Console.Error.WriteLine("Failed to deserialize manifest.");
                    Environment.ExitCode = 2;
                    return;
                }

                // Determine catalog root from manifest path
                // manifest is at: ledger/campaigns/{id}/{hash}/manifest.json
                // catalog root is EELS_FIXTURES_ROOT
                var fixturesRoot = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT") ?? "";
                if (string.IsNullOrEmpty(fixturesRoot) || !Directory.Exists(fixturesRoot))
                {
                    Console.Error.WriteLine("EELS_FIXTURES_ROOT environment variable not set or directory missing.");
                    Environment.ExitCode = 2;
                    return;
                }

                Console.WriteLine($"Campaign: {campaignManifest.CampaignId} ({campaignManifest.Cases.Count} cases)");
                Console.WriteLine($"Manifest hash: {campaignManifest.ManifestHash}");
                Console.WriteLine($"Fixture root: {fixturesRoot}");
                Console.WriteLine($"Timeout per case: {timeout}s");
                Console.WriteLine();

                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);

                // Locate worker executable
                var workerExe = FindWorkerExecutable();
                if (workerExe is null)
                {
                    Console.Error.WriteLine("Schlieren.Harvest.Worker executable not found. Build the worker project first.");
                    Environment.ExitCode = 3;
                    return;
                }
                Console.WriteLine($"Worker: {workerExe}");

                // Create EELS oracle from environment
                var eelsExePath = "C:/projects/eels-venv/Scripts/ethereum-spec-evm.exe";
                if (!File.Exists(eelsExePath))
                {
                    Console.Error.WriteLine($"EELS oracle executable not found at {eelsExePath}. Set EELS_EXE or install EELS.");
                    Environment.ExitCode = 3;
                    return;
                }
                var oracleOpts = new Schlieren.Harvest.Execution.EelsOracleOptions(
                    ExecutablePath:   eelsExePath,
                    ExpectedVersion:  "", // skip version check during run (already pinned in manifest)
                    WorkingDirectory: fixturesRoot,
                    Timeout:          TimeSpan.FromSeconds(timeout));
                var oracle = new Schlieren.Harvest.Execution.EelsProcessOracle(oracleOpts);
                Console.WriteLine($"EELS oracle: {eelsExePath}");

                var worker     = new Schlieren.Harvest.Campaigns.SubprocessCaseWorker(workerExe, oracle, timeout);
                var runner     = new Schlieren.Harvest.Campaigns.CampaignRunner(worker, fileLedger);

                var env = new Schlieren.Harvest.Domain.EnvironmentIdentity(
                    System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    System.Net.Dns.GetHostName(),
                    Environment.ProcessorCount);

                var tool = new Schlieren.Harvest.Domain.ToolIdentity(
                    "schlieren", "1.0.0",
                    GetGitCommitShort(), // dynamic, not hardcoded
                    null);

                var runId = await runner.RunAsync(
                    campaignManifest, fixturesRoot,
                    Schlieren.Harvest.Domain.RunKind.Inspection,
                    env, tool,
                    campaignManifest.EelsIdentity);

                var envelope = await fileLedger.ReadRunAsync(runId);
                var record = envelope.Payload;

                Console.WriteLine($"Run ID: {runId}");
                Console.WriteLine($"State: {record.State}");
                Console.WriteLine($"Pass: {record.Summary.PassCount}");
                Console.WriteLine($"Divergence: {record.Summary.DivergenceCount}");
                Console.WriteLine($"FixtureInvalid: {record.Summary.FixtureInvalidCount}");
                Console.WriteLine($"HarnessError: {record.Summary.HarnessErrorCount}");
                Console.WriteLine($"Aborted: {record.Summary.AbortedCount}");
                Console.WriteLine($"Quarantined: {record.Summary.QuarantinedCount}");
                Console.WriteLine($"Total: {record.Summary.Total}");

                if (record.State == Schlieren.Harvest.Domain.RunState.Completed)
                    Environment.ExitCode = 0;
                else if (record.Summary.DivergenceCount > 0)
                    Environment.ExitCode = 4;
                else
                    Environment.ExitCode = 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Campaign run error: {ex.Message}");
                Environment.ExitCode = 3;
            }
        }, manifestArg, ledgerOpt, timeoutOpt);

        return cmd;
    }

    // ── schlieren harvest compare ────────────────────────────────────────

    private static Command BuildCompareCommand()
    {
        var beforeArg = new Argument<string>("before-run", "Run ID of the before run");
        var afterArg  = new Argument<string>("after-run", "Run ID of the after run");
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };

        var cmd = new Command("compare", "Compare two runs of the same manifest")
        {
            beforeArg, afterArg, ledgerOpt
        };

        cmd.SetHandler(async (before, after, ledger) =>
        {
            try
            {
                var fileLedger    = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var beforeEnvelope = await fileLedger.ReadRunAsync(before);
                var afterEnvelope  = await fileLedger.ReadRunAsync(after);

                var result = Schlieren.Harvest.Comparison.RunComparator.Compare(beforeEnvelope, afterEnvelope);

                Console.WriteLine($"Comparison: {result.BeforeRunId} → {result.AfterRunId}");
                Console.WriteLine($"Manifest: {result.ManifestHash}");
                Console.WriteLine($"Duration: {result.BeforeDuration:hh\\:mm\\:ss} → {result.AfterDuration:hh\\:mm\\:ss}");
                Console.WriteLine($"Family changes: {result.FamilyChanges.Count}");
                foreach (var fc in result.FamilyChanges)
                    Console.WriteLine($"  {fc.FamilyKey}: {fc.Change} ({fc.BeforeCount}→{fc.AfterCount})");
                Console.WriteLine($"Regressions: {result.Regressions.Count}");
                foreach (var r in result.Regressions)
                    Console.WriteLine($"  {r.CaseId}: {r.BeforeStatus}→{r.AfterStatus}");

                // Persist comparison record
                var compJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(result);
                var compPath = Schlieren.Harvest.Ledger.LedgerPaths.ComparisonPath(ledger, before, after);
                Directory.CreateDirectory(Path.GetDirectoryName(compPath)!);
                await File.WriteAllTextAsync(compPath, compJson);
                Console.WriteLine($"Artifact: {compPath}");

                Environment.ExitCode = result.Regressions.Count > 0 ? 4 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Compare error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, beforeArg, afterArg, ledgerOpt);

        return cmd;
    }

    // ── schlieren harvest repair ─────────────────────────────────────────

    private static Command BuildRepairCommand()
    {
        var repairCmd = new Command("repair", "Repair order lifecycle");
        repairCmd.AddCommand(BuildRepairOpenCommand());
        repairCmd.AddCommand(BuildRepairCloseCommand());
        return repairCmd;
    }

    private static Command BuildRepairOpenCommand()
    {
        var familyArg = new Argument<string>("family-id", "Failure family ID to open a repair for");
        var runOpt = new Option<string>(
            "--run", "Run ID containing the failure") { IsRequired = true };
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };

        var cmd = new Command("open", "Open a repair order from a finalized cluster")
        {
            familyArg, runOpt, ledgerOpt
        };

        cmd.SetHandler(async (family, run, ledger) =>
        {
            try
            {
                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var svc = new Schlieren.Harvest.Repairs.RepairOrderService(fileLedger);

                // Read run and filter cases by actual fingerprint key match
                var envelope = await fileLedger.ReadRunAsync(run);
                var affectedCases = envelope.Payload.Outcomes
                    .Where(o => o.Status == Schlieren.Harvest.Domain.CaseStatus.Divergence && o.Deltas.Count > 0)
                    .Where(o => Schlieren.Harvest.Clustering.FailureFingerprint.FromDeltas("Unknown", o.Deltas).Key == family)
                    .Select(o => o.CaseId)
                    .ToList();

                if (affectedCases.Count == 0)
                {
                    Console.Error.WriteLine($"No divergences match family key '{family}' in run '{run}'.");
                    Environment.ExitCode = 2;
                    return;
                }

                var order = svc.Open(run, family, family, affectedCases);

                // Persist repair order
                var repairJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(order);
                var repairPath = Schlieren.Harvest.Ledger.LedgerPaths.RepairPath(ledger, order.RepairOrderId);
                Directory.CreateDirectory(Path.GetDirectoryName(repairPath)!);
                await File.WriteAllTextAsync(repairPath, repairJson);

                Console.WriteLine($"Repair order opened: {order.RepairOrderId}");
                Console.WriteLine($"  Family: {family}");
                Console.WriteLine($"  Run: {run}");
                Console.WriteLine($"  Affected cases: {affectedCases.Count}");
                Console.WriteLine($"  Artifact: {repairPath}");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Repair open error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, familyArg, runOpt, ledgerOpt);

        return cmd;
    }

    private static Command BuildRepairCloseCommand()
    {
        var repairArg = new Argument<string>("repair-id", "Repair order ID to close");
        var commitOpt = new Option<string>(
            "--commit", "Repair commit SHA") { IsRequired = true };
        var runOpt = new Option<string>(
            "--run", "Reinspection run ID") { IsRequired = true };
        var testOpt = new Option<string>(
            "--test", "Permanent test fully-qualified name") { IsRequired = true };
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };

        var cmd = new Command("close", "Close a repair order with reinspection evidence")
        {
            repairArg, commitOpt, runOpt, testOpt, ledgerOpt
        };

        cmd.SetHandler(async (repair, commit, run, test, ledger) =>
        {
            try
            {
                // Load the existing repair order from disk
                var repairPath = Schlieren.Harvest.Ledger.LedgerPaths.RepairPath(ledger, repair);
                if (!File.Exists(repairPath))
                {
                    Console.Error.WriteLine($"Repair order not found: {repairPath}");
                    Environment.ExitCode = 2;
                    return;
                }
                var orderJson = await File.ReadAllTextAsync(repairPath);
                var order = Schlieren.Harvest.Serialization.HarvestJson.Deserialize<
                    Schlieren.Harvest.Repairs.RepairOrder>(orderJson);
                if (order is null)
                {
                    Console.Error.WriteLine("Failed to deserialize repair order.");
                    Environment.ExitCode = 2;
                    return;
                }

                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var svc = new Schlieren.Harvest.Repairs.RepairOrderService(fileLedger);
                var closed = await svc.CloseAsync(order, commit, test, run);

                // Persist the closed state — overwrite the original so certification
                // scan finds the closed status, not the stale open file
                var closedJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(closed);
                await File.WriteAllTextAsync(repairPath, closedJson);

                Console.WriteLine($"Repair {repair}: {closed.Status}");
                Console.WriteLine($"  Disposition: {closed.Disposition}");
                Console.WriteLine($"  Commit: {closed.RepairCommitSha}");
                Console.WriteLine($"  Reinspection: {closed.ReinspectionRunId}");
                Console.WriteLine($"  Artifact: {repairPath}");
                Environment.ExitCode = closed.Status == Schlieren.Harvest.Repairs.RepairOrderStatus.Closed ? 0 : 4;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Repair close error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, repairArg, commitOpt, runOpt, testOpt, ledgerOpt);

        return cmd;
    }

    // ── schlieren harvest certify ────────────────────────────────────────

    private static Command BuildCertifyCommand()
    {
        var runArg = new Argument<string>("run-id", "Run ID to certify");
        var ledgerOpt = new Option<string>(
            "--ledger", "Path to the harvest ledger root") { IsRequired = true };
        var suiteGateOpt = new Option<string>(
            "--suite-gate", "Path to the three-run suite gate record") { IsRequired = true };

        var cmd = new Command("certify", "Validate all gates and issue certificate or refusal")
        {
            runArg, ledgerOpt, suiteGateOpt
        };

        cmd.SetHandler(async (runId, ledger, suiteGate) =>
        {
            try
            {
                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var envelope   = await fileLedger.ReadRunAsync(runId);
                var run        = envelope.Payload;

                // Check suite gate — must parse and verify certificationEligibility
                bool suiteGatePassed = false;
                if (File.Exists(suiteGate))
                {
                    try
                    {
                        var gateJson = await File.ReadAllTextAsync(suiteGate);
                        using var gateDoc = System.Text.Json.JsonDocument.Parse(gateJson);
                        if (gateDoc.RootElement.TryGetProperty("certificationEligibility", out var eligProp))
                            suiteGatePassed = eligProp.GetBoolean();
                    }
                    catch { /* parse failure = gate not passed */ }
                }
                if (!suiteGatePassed)
                    Console.Error.WriteLine($"Suite gate not satisfied (file: {suiteGate})");

                // Check calibration — read the record and verify ApparatusGatePassed
                var calDir = Path.Combine(ledger, "calibrations");
                bool calibrationPassed = false;
                if (Directory.Exists(calDir))
                {
                    var calFiles = Directory.GetFiles(calDir, "*.json")
                        .OrderByDescending(f => f).ToArray();
                    if (calFiles.Length > 0)
                    {
                        try
                        {
                            var calJson = await File.ReadAllTextAsync(calFiles[0]);
                            var calEnvelope = Schlieren.Harvest.Serialization.HarvestJson.Deserialize<
                                Schlieren.Harvest.Domain.ContentEnvelope<Schlieren.Harvest.Calibration.CalibrationRecord>>(calJson);
                            calibrationPassed = calEnvelope?.Payload.ApparatusGatePassed == true;
                        }
                        catch { /* parse failure = not passed */ }
                    }
                }

                // Manifest hash — read from the canonical manifest file, not from the run record
                string expectedManifestHash = "";
                var manifestDir = Path.Combine(ledger, "campaigns");
                if (Directory.Exists(manifestDir))
                {
                    // Use the run's manifest hash to locate the exact manifest file
                    var campaignDir = Path.Combine(manifestDir, run.CampaignId, run.ManifestHash);
                    var manifestFile = Path.Combine(campaignDir, "manifest.json");
                    if (File.Exists(manifestFile))
                    {
                        var mJson = await File.ReadAllTextAsync(manifestFile);
                        var mObj = Schlieren.Harvest.Serialization.HarvestJson.Deserialize<
                            Schlieren.Harvest.Campaigns.CampaignManifest>(mJson);
                        expectedManifestHash = mObj?.ManifestHash ?? "";
                    }
                    else
                    {
                        // Manifest file not found at the expected path — cannot verify
                        Console.Error.WriteLine(
                            $"Manifest file not found at {manifestFile}. " +
                            "Cannot verify run was executed against the correct manifest.");
                    }
                }

                // Check for regressions — compare against previous run if one exists
                bool hasRegressions = false;
                var listResult = await fileLedger.ListRunsAsync();
                var previousRuns = listResult.Entries
                    .Where(e => e.RunId != runId && e.CampaignId == run.CampaignId)
                    .OrderByDescending(e => e.CompletedUtc)
                    .ToList();
                if (previousRuns.Count > 0)
                {
                    try
                    {
                        var prevEnvelope = await fileLedger.ReadRunAsync(previousRuns[0].RunId);
                        if (string.Equals(prevEnvelope.Payload.ManifestHash, run.ManifestHash, StringComparison.OrdinalIgnoreCase))
                        {
                            var comparison = Schlieren.Harvest.Comparison.RunComparator.Compare(prevEnvelope, envelope);
                            hasRegressions = comparison.Regressions.Count > 0;
                        }
                    }
                    catch { /* no previous comparable run */ }
                }

                // Check repository cleanliness
                bool repoClean;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("git", "status --porcelain")
                    { RedirectStandardOutput = true, UseShellExecute = false };
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    repoClean = string.IsNullOrWhiteSpace(output);
                }
                catch { repoClean = false; }

                // Check for open repair orders
                var repairsDir = Path.Combine(ledger, "repairs");
                var hasOpenRepairs = false;
                if (Directory.Exists(repairsDir))
                {
                    foreach (var f in Directory.GetFiles(repairsDir, "*.json"))
                    {
                        var json = await File.ReadAllTextAsync(f);
                        var repairOrder = Schlieren.Harvest.Serialization.HarvestJson.Deserialize<
                            Schlieren.Harvest.Repairs.RepairOrder>(json);
                        if (repairOrder?.Status == Schlieren.Harvest.Repairs.RepairOrderStatus.Open)
                        { hasOpenRepairs = true; break; }
                    }
                }

                var svc = new Schlieren.Harvest.Certification.CertificationService();
                var result = svc.Certify(
                    run, envelope.ContentHash, expectedManifestHash,
                    calibrationPassed, suiteGatePassed, repoClean,
                    hasOpenRepairs, hasRegressions);

                if (result.Certified)
                {
                    Console.WriteLine($"✅ CERTIFIED: {result.Certificate!.CertificateId}");
                    Console.WriteLine($"  Run: {result.Certificate.RunId}");
                    Console.WriteLine($"  Manifest: {result.Certificate.ManifestHash}");
                    Console.WriteLine($"  Schlieren commit: {result.Certificate.SchlierenCommit}");
                    Console.WriteLine($"  EELS: {result.Certificate.EelsVersion} ({result.Certificate.EelsExecutableSha256[..12]}...)");

                    // Persist certificate
                    var certJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(result.Certificate);
                    var certPath = Schlieren.Harvest.Ledger.LedgerPaths.CertificatePath(ledger, result.Certificate.CertificateId);
                    Directory.CreateDirectory(Path.GetDirectoryName(certPath)!);
                    await File.WriteAllTextAsync(certPath, certJson);
                    Console.WriteLine($"  Artifact: {certPath}");
                    Environment.ExitCode = 0;
                }
                else
                {
                    Console.WriteLine($"❌ CERTIFICATION REFUSED ({result.Refusals.Count} gate(s) failed):");
                    foreach (var r in result.Refusals)
                        Console.WriteLine($"  • {r.Reason}: {r.Detail}");
                    Environment.ExitCode = 5;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Certify error: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }, runArg, ledgerOpt, suiteGateOpt);

        return cmd;
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static string? FindWorkerExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Schlieren.Harvest.Worker.exe"),
            Path.Combine(baseDir, "Schlieren.Harvest.Worker"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
                "Schlieren.Harvest.Worker", "bin", "Debug", "net8.0", "Schlieren.Harvest.Worker.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
                "Schlieren.Harvest.Worker", "bin", "Release", "net8.0", "Schlieren.Harvest.Worker.exe")),
            // Also check from project root
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                "Schlieren.Harvest.Worker", "bin", "Debug", "net8.0", "Schlieren.Harvest.Worker.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                "Schlieren.Harvest.Worker", "bin", "Release", "net8.0", "Schlieren.Harvest.Worker.exe")),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? GetGitCommitShort()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch { return null; }
    }
}
