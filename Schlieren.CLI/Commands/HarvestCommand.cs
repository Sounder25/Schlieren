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

        cmd.SetHandler((ledger, eels, eelsVersion) =>
        {
            Console.WriteLine($"[harvest calibrate] ledger={ledger} eels={eels} version={eelsVersion}");
            Console.WriteLine("Calibration: not yet wired to live execution.");
            Environment.ExitCode = 0;
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

        cmd.SetHandler((family, count, fixtures, eels, eelsVersion, ledger) =>
        {
            Console.WriteLine($"[harvest campaign create] family={family} count={count}");
            Console.WriteLine($"  fixtures={fixtures} eels={eels} version={eelsVersion} ledger={ledger}");
            Console.WriteLine("Campaign create: not yet wired to live execution.");
            Environment.ExitCode = 0;
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
