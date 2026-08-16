using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Schlieren EVM execution harness adapter.
/// Bridges campaign framework to actual StateTransition pipeline.
/// NO EVM logic here — pure translation.
/// </summary>
public sealed class SchlierenExecutionHarness : IEvmExecutionHarness
{
    private readonly StateTransition _pipeline;

    public SchlierenExecutionHarness(StateTransition pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        // 1. Build state from prestate accounts
        var state = BuildGlobalState(request.Prestate);
        
        // Capture pre-state for diff
        var preStateStorage = CaptureStorage(state, request.Prestate);

        // 2. Build transaction
        var tx = BuildTransaction(request);

        // 3. Build block context
        var block = BuildBlockContext(request);

        // 4. Execute through existing pipeline
        var result = await _pipeline.ApplyTransactionAsync(tx, state, block, commit: true, ct);

        // 5. Convert to fingerprint
        var fingerprint = BuildFingerprint(result, request, state, preStateStorage);

        // 6. Return normalized result WITH post-execution state for consensus checks
        return new CampaignExecutionResult
        {
            Success = result.IsSuccess,  // Fixed: property, not method
            GasUsed = result.GasUsed,
            ReturnData = ToHex(result.ReturnData ?? Array.Empty<byte>()),
            Fingerprint = fingerprint,
            RawTrace = result,  // Keep full trace for debugging
            PostExecutionState = state  // ADD: For consensus-level storage inspection
        };
    }

    private GlobalState BuildGlobalState(IReadOnlyList<CampaignAccount> accounts)
    {
        var state = new GlobalState();

        foreach (var account in accounts)
        {
            var address = Address.FromHex(account.Address);

            // Set code
            if (!string.IsNullOrEmpty(account.Code) && account.Code != "0x")
            {
                var code = ParseHex(account.Code);
                state.SetCode(address, code);
            }

            // Set balance
            if (account.Balance != "0x0" && account.Balance != "0x")
            {
                if (BigInteger.TryParse(account.Balance.Replace("0x", ""), 
                    System.Globalization.NumberStyles.HexNumber, null, out var balance))
                {
                    state.SetBalance(address, balance);
                }
            }

            // Set nonce
            if (account.Nonce > 0)
            {
                state.SetNonce(address, account.Nonce);
            }

            // Set storage
            if (account.Storage.Count > 0)
            {
                foreach (var (slotHex, valueHex) in account.Storage)
                {
                    var slot = BigInteger.Parse(slotHex.Replace("0x", ""), 
                        System.Globalization.NumberStyles.HexNumber);
                    var value = BigInteger.Parse(valueHex.Replace("0x", ""), 
                        System.Globalization.NumberStyles.HexNumber);
                    state.SetStorageAt(address, slot, value);  // Fixed: SetStorageAt API
                }
            }
        }

        return state;
    }

    private Transaction BuildTransaction(CampaignExecutionRequest request)
    {
        var caller = Address.FromHex(request.Caller);
        var target = string.IsNullOrWhiteSpace(request.Target) || request.Target == "0x"
            ? (Address?)null
            : Address.FromHex(request.Target);
        var calldata = ParseHex(request.Calldata);
        var value = BigInteger.Parse(request.Value.ToString());

        return new Transaction
        {
            From = caller,
            To = target,
            Value = value,
            Data = calldata,
            GasLimit = request.GasLimit,
            GasPrice = 1,  // 1 wei
            MaxFeePerGas = 1,
            MaxPriorityFeePerGas = 0,
            TxType = 0,
            AccessList = Array.Empty<AccessListEntry>(),
            AuthorizationList = Array.Empty<Eip7702Authorization>(),
            Nonce = 0,
            Authorization = TransactionAuthorization.Impersonated,
            EnableTracing = true
        };
    }

    private BlockContext BuildBlockContext(CampaignExecutionRequest request)
    {
        var rules = ForkRulesFactory.For(request.Fork);

        return new BlockContext
        {
            ChainId = 1,
            Number = 1,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = 30_000_000,
            Coinbase = new Address(new byte[20]),
            BaseFeePerGas = 1,
            Rules = rules
        };
    }

    private Dictionary<string, Dictionary<string, string>> CaptureStorage(
        GlobalState state, 
        IReadOnlyList<CampaignAccount> accounts)
    {
        var storage = new Dictionary<string, Dictionary<string, string>>();
        
        foreach (var account in accounts)
        {
            var address = Address.FromHex(account.Address);
            var accountStorage = new Dictionary<string, string>();
            
            // Capture current storage keys
            var keys = state.GetStorageKeysAsync(address).GetAwaiter().GetResult();
            foreach (var key in keys)
            {
                var value = state.GetStorageAtAsync(address, key).GetAwaiter().GetResult();
                accountStorage[FormatBigInt(key)] = FormatBigInt(value);
            }
            
            storage[account.Address] = accountStorage;
        }
        
        return storage;
    }
    
    private Dictionary<string, string> BuildStateDiff(
        GlobalState postState,
        Dictionary<string, Dictionary<string, string>> preStateStorage,
        IReadOnlyList<CampaignAccount> accounts)
    {
        var diff = new Dictionary<string, string>();
        
        // Capture post-state storage
        var postStorage = CaptureStorage(postState, accounts);
        
        // Compare pre vs post for each account
        foreach (var account in accounts)
        {
            var address = account.Address;
            var preSlotsForAccount = preStateStorage.ContainsKey(address) 
                ? preStateStorage[address] 
                : new Dictionary<string, string>();
            var postSlotsForAccount = postStorage.ContainsKey(address)
                ? postStorage[address]
                : new Dictionary<string, string>();
            
            // Get union of all slots
            var allSlots = preSlotsForAccount.Keys.Union(postSlotsForAccount.Keys).ToHashSet();
            
            foreach (var slot in allSlots)
            {
                var before = preSlotsForAccount.ContainsKey(slot) ? preSlotsForAccount[slot] : "0x0";
                var after = postSlotsForAccount.ContainsKey(slot) ? postSlotsForAccount[slot] : "0x0";
                
                if (before != after)
                {
                    var key = $"{address}:{slot}";
                    diff[key] = $"{before} → {after}";
                }
            }
        }
        
        return diff;
    }

    private ExecutionFingerprint BuildFingerprint(
        ExecutionResult result, 
        CampaignExecutionRequest request,
        GlobalState postState,
        Dictionary<string, Dictionary<string, string>> preStateStorage)
    {
        // Extract frame tree from trace
        var frames = BuildFrameTree(result);

        // Extract access set (placeholder for now)
        var accesses = new AccessFingerprint
        {
            ColdAccounts = new List<string>(),
            WarmAccounts = new List<string>(),
            ColdSlots = new List<string>(),
            WarmSlots = new List<string>()
        };

        // Extract state diff - compare pre vs post for all accounts that were touched
        var stateDiff = BuildStateDiff(postState, preStateStorage, request.Prestate);

        // Extract logs (TransactionLog already has string properties)
        var logs = result.Logs
            .Select(log => new LogFingerprint
            {
                Address = log.Address,  // Already string
                Topics = log.Topics,    // Already List<string>
                Data = log.Data         // Already string
            })
            .ToList();

        return new ExecutionFingerprint
        {
            Success = result.IsSuccess,  // Fixed: property
            GasUsed = result.GasUsed,
            ReturnData = ToHex(result.ReturnData),
            Refund = (ulong)Math.Max(0, result.GasRefundCounter),
            FrameTree = frames,
            Accesses = accesses,
            StateDiff = stateDiff,
            Logs = logs
        };
    }

    private List<FrameFingerprint> BuildFrameTree(ExecutionResult result)
    {
        if (result.TraceSteps == null || result.TraceSteps.Count == 0)  // Fixed: TraceSteps property
            return new List<FrameFingerprint>();

        var frames = new List<FrameFingerprint>();
        var currentDepth = 1;
        var frameStarts = new Stack<int>();
        frameStarts.Push(0);

        for (int i = 0; i < result.TraceSteps.Count; i++)
        {
            var step = result.TraceSteps[i];

            // Detect depth change
            if (step.Depth > currentDepth)
            {
                // New child frame started
                frameStarts.Push(i);
                currentDepth = step.Depth;
            }
            else if (step.Depth < currentDepth)
            {
                // Frame ended - build fingerprint
                var startIdx = frameStarts.Pop();
                var frame = BuildFrameFingerprint(result.TraceSteps, startIdx, i - 1, currentDepth);
                frames.Add(frame);
                currentDepth = step.Depth;
            }
        }

        // Close remaining frames
        while (frameStarts.Count > 0)
        {
            var startIdx = frameStarts.Pop();
            var depth = result.TraceSteps[startIdx].Depth;
            var frame = BuildFrameFingerprint(result.TraceSteps, startIdx, result.TraceSteps.Count - 1, depth);
            frames.Add(frame);
        }

        return frames;
    }

    private FrameFingerprint BuildFrameFingerprint(
        IReadOnlyList<ExecutionTraceStep> trace,  // Fixed: List<ExecutionTraceStep>
        int startIdx,
        int endIdx,
        int depth)
    {
        var firstStep = trace[startIdx];
        var lastStep = trace[endIdx];

        // Sum gas for this depth only (not nested)
        var gasConsumed = 0UL;
        for (int i = startIdx; i <= endIdx && i < trace.Count; i++)
        {
            if (trace[i].Depth == depth && trace[i].GasCost.StartsWith("0x"))
            {
                if (ulong.TryParse(trace[i].GasCost.Replace("0x", ""), 
                    System.Globalization.NumberStyles.HexNumber, null, out var gas))
                {
                    gasConsumed += gas;
                }
            }
        }

        // Detect call type from opcode before this frame
        var callType = "Root";
        if (startIdx > 0)
        {
            var parentOp = trace[startIdx - 1].Op;
            callType = parentOp switch
            {
                "CALL" => "Call",
                "DELEGATECALL" => "DelegateCall",
                "STATICCALL" => "StaticCall",
                "CALLCODE" => "CallCode",
                "CREATE" => "Create",
                "CREATE2" => "Create2",
                _ => "Root"
            };
        }

        // Determine success from terminal opcode
        // STOP or RETURN → success
        // REVERT or exceptional halt → failure  
        // OOG shows as parent getting control back without explicit terminal
        var terminalOp = lastStep.Op;
        var success = terminalOp is "STOP" or "RETURN" or "SELFDESTRUCT";
        
        // If last opcode is REVERT, explicit failure
        if (terminalOp == "REVERT")
            success = false;

        // For exceptional halts (OOG, invalid opcode, stack underflow):
        // The trace doesn't continue to STOP/RETURN — it just ends.
        // Check if we returned to parent depth without reaching a terminal.
        if (endIdx < trace.Count - 1)
        {
            var nextStep = trace[endIdx + 1];
            if (nextStep.Depth < depth && terminalOp is not ("STOP" or "RETURN" or "REVERT" or "SELFDESTRUCT"))
            {
                // Abrupt return to parent without terminal opcode → exceptional halt
                success = false;
            }
        }

        return new FrameFingerprint
        {
            Depth = depth,
            CallType = callType,
            CodeAddress = firstStep.ContractAddress ?? "0x",  // Fixed: ContractAddress property
            ContextAddress = firstStep.ContractAddress ?? "0x",  // TODO: Extract from DELEGATECALL
            Caller = firstStep.CallerAddress ?? "0x01",  // Fixed: CallerAddress property
            Value = "0",      // TODO: Extract from CALL
            GasProvided = 0,  // TODO: Calculate
            GasConsumed = gasConsumed,
            Success = success,
            ReturnData = "0x" // TODO: Extract
        };
    }

    // Parsing utilities
    private static byte[] ParseHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x") return Array.Empty<byte>();
        var cleaned = hex.StartsWith("0x") ? hex[2..] : hex;
        if (cleaned.Length % 2 != 0) cleaned = "0" + cleaned;
        return Convert.FromHexString(cleaned);
    }

    private static string ToHex(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return "0x";
        return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Normalize a BigInteger to a clean hex string: 0x0, 0xAA, 0x100, etc.
    /// Uses ulong for values that fit, strips leading zeros.
    /// </summary>
    private static string FormatBigInt(BigInteger v)
    {
        if (v == BigInteger.Zero) return "0x0";
        // Convert to hex without sign-padding (BigInteger.ToString("X") can prepend 0 for sign)
        var hex = v.ToString("X").TrimStart('0');
        if (string.IsNullOrEmpty(hex)) hex = "0";
        return "0x" + hex;
    }
}
