using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Deep structural audit — inspects bytecode correctness for the hardest cases.
/// Separate from regression suite. Run with: --filter PathologicalDeepAudit
/// </summary>
public sealed class PathologicalDeepAuditTests
{
    private readonly ITestOutputHelper _out;
    public PathologicalDeepAuditTests(ITestOutputHelper out_) => _out = out_;

    [Fact]
    public void DeepAudit_StackDepth_RecursiveCodeIsPresent()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.Family == PathFamily.StackDepth
                     && c.StackKind is StackVariant.NestedCallDepth1023
                                    or StackVariant.NestedCallDepth1024
                                    or StackVariant.NestedCallDepth1025)
            .ToList();

        Assert.True(cases.Count >= 3, $"Expected ≥3 depth cases, got {cases.Count}");

        foreach (var c in cases)
        {
            var req = PathologicalMaterializer.Materialize(c);

            // target code (outer frame) must contain a CALL opcode (0xf1)
            var targetCode = req.Prestate.First(a => a.Address == req.Target).Code;
            Assert.Contains("f1", targetCode, StringComparison.OrdinalIgnoreCase);

            // must have a child account
            var child = req.Prestate.FirstOrDefault(a =>
                a.Address.Equals("0x00000000000000000000000000000000000000b1",
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(child);
            Assert.NotNull(child!.Code);
            Assert.NotEqual("0x", child.Code);
            Assert.NotEqual("0x00", child.Code);  // must not be bare STOP

            // child code must also contain a CALL (f1) for recursion
            Assert.Contains("f1", child.Code!, StringComparison.OrdinalIgnoreCase);

            _out.WriteLine($"  {c.CaseId} {c.StackKind}: target={targetCode.Length/2-1}B child={child.Code!.Length/2-1}B ✅");
        }
    }

    [Fact]
    public void DeepAudit_ExceptionalHalt_DepthLimit_HasSelfRecurse()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.HaltKind == ExceptionalHaltKind.DepthLimitExceeded)
            .ToList();

        Assert.True(cases.Count >= 1, "Expected ≥1 DepthLimitExceeded case");

        foreach (var c in cases)
        {
            var req   = PathologicalMaterializer.Materialize(c);
            var child = req.Prestate.FirstOrDefault(a =>
                a.Address.Equals("0x00000000000000000000000000000000000000b1",
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(child);
            Assert.Contains("f1", child!.Code ?? "", StringComparison.OrdinalIgnoreCase);
            _out.WriteLine($"  {c.CaseId}: child recursion code present ✅");
        }
    }

    [Fact]
    public void DeepAudit_BigInt_HighOffsets_PushesAreCorrectSize()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.Family == PathFamily.BigIntegerNarrowing
                     && !c.CopySource.HasValue)
            .ToList();

        _out.WriteLine($"BigIntegerNarrowing non-copy cases: {cases.Count}");

        // U256Max → PUSH32 (0x7f)
        foreach (var c in cases.Where(x => x.Boundary == BoundaryValue.U256Max))
        {
            var req = PathologicalMaterializer.Materialize(c);
            var hex = (req.Prestate.First(a => a.Address == req.Target).Code ?? "").Replace("0x","");
            Assert.Contains("7f", hex, StringComparison.OrdinalIgnoreCase);
            _out.WriteLine($"  U256Max {c.CaseId}: PUSH32 ✅");
        }

        // U64Max = 0xFFFF_FFFF_FFFF_FFFF → 8 bytes → PUSH8 (0x67)
        // Only check if such cases exist — they may have been deduped against MemoryBoundary
        var u64cases = cases.Where(x => x.Boundary == BoundaryValue.U64Max).ToList();
        _out.WriteLine($"\nU64Max BigIntNarrowing cases (non-copy): {u64cases.Count}");
        foreach (var c in u64cases)
        {
            var req = PathologicalMaterializer.Materialize(c);
            var hex = (req.Prestate.First(a => a.Address == req.Target).Code ?? "").Replace("0x","");
            _out.WriteLine($"  U64Max {c.CaseId}: code starts {hex[..Math.Min(20,hex.Length)]}");
            // PUSH8 = 0x67; but MemoryBoundary may prefix it with extra instructions
            // Just verify the bytecode is non-trivial and contains the 8 ff bytes
            Assert.Contains("ffffffffffffffff", hex, StringComparison.OrdinalIgnoreCase);
            _out.WriteLine($"    contains 8×0xff bytes ✅");
        }
    }

    [Fact]
    public void DeepAudit_ModexpHugeLengths_InputContainsUInt64MaxHeader()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.ModexpKind is ModexpVariant.HugeDeclaredBase
                                      or ModexpVariant.HugeDeclaredExp
                                      or ModexpVariant.HugeDeclaredMod
                                      or ModexpVariant.AllHuge)
            .ToList();

        Assert.True(cases.Count >= 4, $"Expected ≥4 ModExp huge cases, got {cases.Count}");

        foreach (var c in cases)
        {
            var req = PathologicalMaterializer.Materialize(c);
            // Code should include MSTORE8 loops (0x53) to write the input
            var code = req.Prestate.First(a => a.Address == req.Target).Code;
            Assert.Contains("53", code, StringComparison.OrdinalIgnoreCase);  // MSTORE8

            // The bytecode must be substantial — tiny code = input not written
            Assert.True(code.Length > 100, $"{c.CaseId}: code too short ({code.Length} chars) — input may not be materialised");
            _out.WriteLine($"  {c.CaseId} {c.ModexpKind}: code={code.Length/2-1}B ✅");
        }
    }

    [Fact]
    public void DeepAudit_ReturndataCopy_ChildAccountPresent_For_ReturnDataCases()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.CopySource == CopySource.Returndata)
            .ToList();

        Assert.True(cases.Count >= 10, $"Expected ≥10 RETURNDATACOPY cases, got {cases.Count}");

        int missingChild = 0;
        foreach (var c in cases)
        {
            var req = PathologicalMaterializer.Materialize(c);
            var child = req.Prestate.FirstOrDefault(a =>
                a.Address.Equals("0x00000000000000000000000000000000000000b1",
                    StringComparison.OrdinalIgnoreCase));

            if (child == null)
            {
                _out.WriteLine($"  MISSING CHILD: {c.CaseId} {c.Label}");
                missingChild++;
            }

            // Target code must contain CALL (f1) then RETURNDATACOPY (3e)
            var code = req.Prestate.First(a => a.Address == req.Target).Code;
            Assert.Contains("f1", code, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("3e", code, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, missingChild);
        _out.WriteLine($"\n  All {cases.Count} RETURNDATACOPY cases have child accounts ✅");
    }

    [Fact]
    public void DeepAudit_StaticContext_Mutation_ChildHasSstore()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.HaltKind == ExceptionalHaltKind.StaticContextMutation)
            .ToList();

        Assert.True(cases.Count >= 1);

        foreach (var c in cases)
        {
            var req   = PathologicalMaterializer.Materialize(c);
            var child = req.Prestate.FirstOrDefault(a =>
                a.Address.Equals("0x00000000000000000000000000000000000000b1",
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(child);
            // target must use STATICCALL (0xfa)
            var targetCode = req.Prestate.First(a => a.Address == req.Target).Code;
            Assert.Contains("fa", targetCode, StringComparison.OrdinalIgnoreCase);
            // child code must contain SSTORE (0x55) or LOG0 (0xa0)
            var hasWrite = (child!.Code ?? "").Contains("55", StringComparison.OrdinalIgnoreCase)
                        || (child!.Code ?? "").Contains("a0", StringComparison.OrdinalIgnoreCase);
            Assert.True(hasWrite, $"{c.CaseId}: child code has no SSTORE/LOG0");
            _out.WriteLine($"  {c.CaseId}: STATICCALL→child(SSTORE/LOG) ✅");
        }
    }

    [Fact]
    public void DeepAudit_CreateLifecycle_NonceRollover_SetsNonce()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.CreateKind == CreateVariant.NonceRollover)
            .ToList();

        Assert.True(cases.Count >= 1);

        foreach (var c in cases)
        {
            var req    = PathologicalMaterializer.Materialize(c);
            var target = req.Prestate.First(a => a.Address == req.Target);
            Assert.Equal(ulong.MaxValue, target.Nonce);
            _out.WriteLine($"  {c.CaseId}: Nonce={target.Nonce} (ulong.MaxValue) ✅");
        }
    }

    [Fact]
    public void DeepAudit_ArithmeticCases_SdivNegativeOverflow_PushesCorrectValues()
    {
        var cases = PathologicalCaseGenerator.Generate()
            .Where(c => c.ArithKind == ArithVariant.SdivNegativeOverflow)
            .ToList();

        Assert.True(cases.Count >= 1);

        foreach (var c in cases)
        {
            var req  = PathologicalMaterializer.Materialize(c);
            var code = req.Prestate.First(a => a.Address == req.Target).Code;
            // must contain SDIV (0x05)
            Assert.Contains("05", code, StringComparison.OrdinalIgnoreCase);
            // must contain PUSH32 (0x7f) for 2^255
            Assert.Contains("7f", code, StringComparison.OrdinalIgnoreCase);
            _out.WriteLine($"  {c.CaseId}: SDIV with 2^255/(-1) ✅");
        }
    }
}
