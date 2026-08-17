using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Schlieren.Tests.Campaigns;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Pathological execution regression suite.
///
/// Invariant: Schlieren must never throw a .NET exception on a legal EVM input.
/// Any input on the EVM stack is a legal EVM input — even 2^256-1 as a memory offset.
/// The engine must respond with OOG / INVALID / REVERT / SUCCESS — never OverflowException.
///
/// ┌──────────────────────────────────────────────────────────────┐
/// │  Suite                      │  Cases  │  Oracle  │  Goal     │
/// ├──────────────────────────────────────────────────────────────┤
/// │  Run_PathologicalExecution  │  ~650   │  none    │  0 .NET   │
/// │  Run_SeedZero_ModexpOverflow│    1    │  none    │  0 .NET   │
/// └──────────────────────────────────────────────────────────────┘
/// </summary>
public sealed class PathologicalRegressionTests
{
    private readonly ITestOutputHelper _out;

    public PathologicalRegressionTests(ITestOutputHelper output) => _out = output;

    // ── Infrastructure helper ─────────────────────────────────────────────────

    private static PathologicalDifferentialRunner BuildRunner()
    {
        var machine = new Core.Execution.EvmMachine(
            typeof(Core.Execution.IOpcode).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } &&
                            typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
                .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
                .ToList());
        var harness = new SchlierenExecutionHarness(new Core.Execution.StateTransition(machine));
        return new PathologicalDifferentialRunner(harness);
    }

    // ── Seed zero: the ModExp overflow case ──────────────────────────────────
    //
    // This is the canonical first case for the pathological suite.
    // ModExp with bLen=2^64-1 must produce OOG, never OverflowException.

    [Fact]
    public async Task SeedZero_ModexpHugeDeclaredBase_MustNotThrow()
    {
        var c = new PathologicalCase
        {
            CaseId     = "PATH-SEED-0",
            Fork       = "Cancun",
            Family     = PathFamily.PrecompilePathological,
            Opcode     = PathOpcode.PrecompileModexp,
            Label      = "ModExp bLen=2^64-1 (gas overflow → OOG, never OverflowException)",
            FamilyId   = FailureFamily.OverflowModexpGas,
            ModexpKind = ModexpVariant.HugeDeclaredBase,
        };

        var runner = BuildRunner();
        var result = await runner.RunAsync(new[] { c });

        _out.WriteLine($"  Outcome : {(result.Defects == 0 ? "✅ EVM-defined halt" : "⛔ .NET EXCEPTION")}");
        if (result.Defects > 0)
        {
            var d = result.AllResults[0];
            _out.WriteLine($"  Exception: {d.ExceptionType}");
            _out.WriteLine($"  Message  : {d.ExceptionMessage}");
            _out.WriteLine($"  Stack    : {d.StackTrace?.Split('\n').FirstOrDefault()}");
        }

        Assert.True(result.Defects == 0,
            $"Seed zero (PATH-SEED-0) threw a .NET exception: " +
            $"{result.AllResults[0].ExceptionType}: {result.AllResults[0].ExceptionMessage}");
    }

    // ── Full ~650-case campaign ────────────────────────────────────────────────

    [Fact]
    public async Task Run_PathologicalExecution()
    {
        var cases  = PathologicalCaseGenerator.Generate();
        var runner = BuildRunner();
        var result = await runner.RunAsync(cases);

        var outPath = PathologicalResultPersister.Persist(result);
        var report  = FailureClassifier.BuildReport(result);

        _out.WriteLine(report);
        _out.WriteLine($"  Results: {outPath}");

        // Infrastructure invariant: cases ran (generator not empty)
        Assert.True(result.Total > 0, "Generator produced 0 cases");
        Assert.Equal(result.Total, result.Passed + result.Defects);

        // Core invariant: no .NET exceptions escaped the engine
        Assert.True(result.Defects == 0,
            $"{result.Defects}/{result.Total} cases threw .NET exceptions. " +
            $"See {outPath}/summary.json for clusters.\n\n" +
            report);
    }

    // ── Family-scoped regression gates ───────────────────────────────────────
    //
    // Each family runs independently so a fix in one family doesn't mask
    // a regression in another, and CI can parallelize by family.

    [Fact]
    public async Task Run_BigIntegerNarrowing()
    {
        await RunFamily(PathFamily.BigIntegerNarrowing, "BigInteger/narrowing");
    }

    [Fact]
    public async Task Run_MemoryBoundaries()
    {
        await RunFamily(PathFamily.MemoryBoundary, "Memory boundaries");
    }

    [Fact]
    public async Task Run_CopyReturndata()
    {
        await RunFamily(PathFamily.CopyReturndata, "Copy/returndata");
    }

    [Fact]
    public async Task Run_PrecompilePathological()
    {
        await RunFamily(PathFamily.PrecompilePathological, "Precompile pathological");
    }

    [Fact]
    public async Task Run_ExceptionalHalts()
    {
        await RunFamily(PathFamily.ExceptionalHalt, "Exceptional halts");
    }

    [Fact]
    public async Task Run_CreateLifecycle()
    {
        await RunFamily(PathFamily.CreateLifecycle, "CREATE lifecycle");
    }

    [Fact]
    public async Task Run_StackDepth()
    {
        await RunFamily(PathFamily.StackDepth, "Stack/depth pressure");
    }

    [Fact]
    public async Task Run_ArithmeticBoundaries()
    {
        await RunFamily(PathFamily.ArithmeticBoundary, "Arithmetic boundaries");
    }

    // ── Specific spot-checks ──────────────────────────────────────────────────
    //
    // These pin individual high-value cases so they stay in the regression
    // record even if the full suite is filtered.

    [Fact]
    public async Task SpotCheck_MloadU256Max()
    {
        await RunSingle(PathFamily.MemoryBoundary, PathOpcode.Mload,
            "MLOAD offset=2^256-1", modexpKind: null,
            memKind: MemoryVariant.MloadAtBoundary,
            boundary: BoundaryValue.U256Max);
    }

    [Fact]
    public async Task SpotCheck_MstoreU64Max()
    {
        await RunSingle(PathFamily.MemoryBoundary, PathOpcode.Mstore,
            "MSTORE offset=2^64-1", memKind: MemoryVariant.MstoreAtBoundary,
            boundary: BoundaryValue.U64Max);
    }

    [Fact]
    public async Task SpotCheck_CalldataCopyU256Max()
    {
        await RunSingle(PathFamily.CopyReturndata, PathOpcode.Calldatacopy,
            "CALLDATACOPY size=2^256-1",
            copyKind: CopyVariant.HugeSize, copySource: CopySource.Calldata);
    }

    [Fact]
    public async Task SpotCheck_ReturndataCopyPastBuffer()
    {
        await RunSingle(PathFamily.CopyReturndata, PathOpcode.Returndatacopy,
            "RETURNDATACOPY offset=2^256-1 (returndata=0)",
            copyKind: CopyVariant.HugeOffset, copySource: CopySource.Returndata);
    }

    [Fact]
    public async Task SpotCheck_ModexpAllHuge()
    {
        await RunSingle(PathFamily.PrecompilePathological, PathOpcode.PrecompileModexp,
            "MODEXP all lengths=2^64-1", modexpKind: ModexpVariant.AllHuge);
    }

    [Fact]
    public async Task SpotCheck_StackOverflow1025()
    {
        await RunSingle(PathFamily.StackDepth, PathOpcode.Push1,
            "Stack 1025 items → overflow", stackKind: StackVariant.Push1025Items);
    }

    [Fact]
    public async Task SpotCheck_CreateHugeRuntimeCode()
    {
        await RunSingle(PathFamily.CreateLifecycle, PathOpcode.Create,
            "CREATE returns 65536-byte runtime code",
            createKind: CreateVariant.ReturnHugeRuntimeCode, param1: 65536);
    }

    [Fact]
    public async Task SpotCheck_DivByZero()
    {
        await RunSingle(PathFamily.ArithmeticBoundary, PathOpcode.Div,
            "DIV by 0", arithKind: ArithVariant.DivByZero);
    }

    [Fact]
    public async Task SpotCheck_SdivNegativeOverflow()
    {
        await RunSingle(PathFamily.ArithmeticBoundary, PathOpcode.Sdiv,
            "SDIV (2^255) / (-1)", arithKind: ArithVariant.SdivNegativeOverflow);
    }

    // ── Runner helpers ────────────────────────────────────────────────────────

    private async Task RunFamily(PathFamily family, string label)
    {
        var all   = PathologicalCaseGenerator.Generate();
        var cases = all.Where(c => c.Family == family).ToList();
        Assert.True(cases.Count > 0, $"Generator produced 0 cases for family {family}");

        var runner = BuildRunner();
        var result = await runner.RunAsync(cases);

        _out.WriteLine($"\n  [{label}] {result.Passed}/{result.Total} passed, {result.Defects} defects");

        if (result.Defects > 0)
        {
            foreach (var d in result.AllResults.Where(r => r.IsDefect).Take(5))
                _out.WriteLine($"    ⛔ {FailureClassifier.Diagnose(d)}");
        }

        Assert.True(result.Defects == 0,
            $"[{label}] {result.Defects} cases threw .NET exceptions:\n" +
            string.Join("\n", result.AllResults
                .Where(r => r.IsDefect)
                .Take(10)
                .Select(r => $"  {r.Case.CaseId}: {r.ExceptionType}: {r.ExceptionMessage}")));
    }

    private async Task RunSingle(
        PathFamily family,
        PathOpcode opcode,
        string label,
        ModexpVariant?          modexpKind    = null,
        Bn254Variant?           bn254Kind     = null,
        Blake2fVariant?         blake2fKind   = null,
        PrecompileInputVariant? precompileInput = null,
        ExceptionalHaltKind?    haltKind      = null,
        CreateVariant?          createKind    = null,
        CopyVariant?            copyKind      = null,
        CopySource?             copySource    = null,
        MemoryVariant?          memKind       = null,
        ArithVariant?           arithKind     = null,
        StackVariant?           stackKind     = null,
        BoundaryValue?          boundary      = null,
        ulong?                  param1        = null)
    {
        var c = new PathologicalCase
        {
            CaseId       = "PATH-SPOT",
            Fork         = "Cancun",
            Family       = family,
            Opcode       = opcode,
            Label        = label,
            FamilyId     = DeriveFamily(family),
            ModexpKind   = modexpKind,
            Bn254Kind    = bn254Kind,
            Blake2fKind  = blake2fKind,
            PrecompileInput = precompileInput,
            HaltKind     = haltKind,
            CreateKind   = createKind,
            CopyKind     = copyKind,
            CopySource   = copySource,
            MemoryKind   = memKind,
            ArithKind    = arithKind,
            StackKind    = stackKind,
            Boundary     = boundary,
            Param1       = param1,
        };

        var runner = BuildRunner();
        var result = await runner.RunAsync(new[] { c });

        _out.WriteLine($"  [{label}] → {(result.Defects == 0 ? "✅ EVM halt" : $"⛔ {result.AllResults[0].ExceptionType}")}");

        Assert.True(result.Defects == 0,
            $"SpotCheck [{label}] threw {result.AllResults[0].ExceptionType}: {result.AllResults[0].ExceptionMessage}");
    }

    private static string DeriveFamily(PathFamily f) => f switch
    {
        PathFamily.PrecompilePathological => FailureFamily.PrecompileMalformed,
        PathFamily.MemoryBoundary         => FailureFamily.OverflowMemoryOffset,
        PathFamily.BigIntegerNarrowing    => FailureFamily.OverflowMemoryOffset,
        PathFamily.CopyReturndata         => FailureFamily.CopyRange,
        PathFamily.ExceptionalHalt        => FailureFamily.ExceptionalHalt,
        PathFamily.CreateLifecycle        => FailureFamily.CreateLifecycle,
        PathFamily.StackDepth             => FailureFamily.StackLimit,
        PathFamily.ArithmeticBoundary     => FailureFamily.ArithmeticBoundary,
        _                                 => FailureFamily.UnhandledEngineException,
    };
}
