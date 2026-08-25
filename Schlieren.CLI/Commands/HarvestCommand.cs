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
            Console.WriteLine($"[harvest catalog] fixtures={fixtures} eels={eels} version={eelsVersion}");
            Console.WriteLine("Catalog: not yet wired to live execution.");
            Environment.ExitCode = 0;
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
                // 1. Catalog all fixture files from the root
                var catalog = new Schlieren.Harvest.Fixtures.FixtureCatalog(fixtures);
                var allFiles = Directory.GetFiles(fixtures, "*.json", SearchOption.AllDirectories);
                Console.WriteLine($"Scanning {allFiles.Length} fixture files...");
                var admitted = catalog.Admit(allFiles);
                var admittedCount = admitted.Count(m => m.Admission == Schlieren.Harvest.Fixtures.AdmissionReasonCode.Admitted);
                Console.WriteLine($"Admitted: {admittedCount}, Rejected: {admitted.Count - admittedCount}");

                // 2. Select cases deterministically
                var selector = new Schlieren.Harvest.Campaigns.CampaignSelector();
                var result = selector.TrySelect(admitted, count);
                if (!result.IsSuccess)
                {
                    Console.Error.WriteLine($"Insufficient coverage: {result.InsufficientReport!.Reason}");
                    Console.Error.WriteLine($"  Requested: {result.InsufficientReport.RequestedCount}, Available: {result.InsufficientReport.AvailableCount}");
                    Environment.ExitCode = 2;
                    return;
                }

                // 3. Freeze manifest with real EELS identity
                // Compute EELS executable SHA-256
                string eelsSha256;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(eels))
                    eelsSha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();

                var eelsIdentity = new Schlieren.Harvest.Campaigns.EelsIdentity(
                    ExecutableSha256: eelsSha256,
                    ReportedVersion:  eelsVersion,
                    CommitSha:        null); // populated if user supplies --eels-commit

                var manifest = Schlieren.Harvest.Campaigns.CampaignManifest.Freeze(
                    result.Cases!, $"{family}-v1", DateTime.UtcNow,
                    eelsIdentity: eelsIdentity);

                // 4. Persist to ledger
                var fileLedger = new Schlieren.Harvest.Ledger.FileRunLedger(ledger);
                var manifestJson = Schlieren.Harvest.Serialization.HarvestJson.Serialize(manifest);
                await fileLedger.StoreManifestAsync($"{family}-v1", manifest.ManifestHash, manifestJson);

                Console.WriteLine($"Campaign: {family}-v1");
                Console.WriteLine($"Cases selected: {manifest.Cases.Count}");
                Console.WriteLine($"Manifest hash: {manifest.ManifestHash}");
                Console.WriteLine($"Stored at: {Schlieren.Harvest.Ledger.LedgerPaths.ManifestPath(ledger, $"{family}-v1", manifest.ManifestHash)}");
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

        cmd.SetHandler((manifest, ledger, timeout) =>
        {
            Console.WriteLine($"[harvest campaign run] manifest={manifest} ledger={ledger} timeout={timeout}s");
            Console.WriteLine("Campaign run: not yet wired to live execution.");
            Environment.ExitCode = 0;
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

        cmd.SetHandler((before, after, ledger) =>
        {
            Console.WriteLine($"[harvest compare] before={before} after={after} ledger={ledger}");
            Console.WriteLine("Compare: not yet wired to live execution.");
            Environment.ExitCode = 0;
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

        cmd.SetHandler((family, run, ledger) =>
        {
            Console.WriteLine($"[harvest repair open] family={family} run={run} ledger={ledger}");
            Console.WriteLine("Repair open: not yet wired to live execution.");
            Environment.ExitCode = 0;
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

        cmd.SetHandler((repair, commit, run, test, ledger) =>
        {
            Console.WriteLine($"[harvest repair close] repair={repair} commit={commit} run={run} test={test}");
            Console.WriteLine("Repair close: not yet wired to live execution.");
            Environment.ExitCode = 0;
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

        cmd.SetHandler((runId, ledger, suiteGate) =>
        {
            Console.WriteLine($"[harvest certify] run={runId} ledger={ledger} suite-gate={suiteGate}");
            Console.WriteLine("Certify: not yet wired to live execution.");
            Environment.ExitCode = 0;
        }, runArg, ledgerOpt, suiteGateOpt);

        return cmd;
    }
}
