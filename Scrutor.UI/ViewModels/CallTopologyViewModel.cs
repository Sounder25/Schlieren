using System.Collections.ObjectModel;
using Scrutor.Core.Execution;

namespace Scrutor.UI.ViewModels;

public class CallTopologyViewModel
{
    public ObservableCollection<CallNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<CallEdgeViewModel> Edges { get; } = new();
    public string Title { get; set; } = "Call Topology";
    
    public void LoadFromTrace(List<ExecutionTraceStep> steps)
    {
        Nodes.Clear();
        Edges.Clear();
        
        if (steps == null || steps.Count == 0) return;
        
        // Track unique contracts and their call depths
        var contracts = new Dictionary<string, int>();  // contract -> depth
        var callEdges = new List<(string from, string to, string op, int step)>();
        
        string? lastContract = null;
        int depth = 0;
        
        foreach (var step in steps)
        {
            var contract = GetContractName(step);
            
            // Track contract if new
            if (!contracts.ContainsKey(contract))
            {
                contracts[contract] = depth;
            }
            
            // Track call transitions
            if (lastContract != null && lastContract != contract)
            {
                callEdges.Add((lastContract, contract, step.Op, steps.IndexOf(step)));
                
                // Depth increases on CALL, decreases on RETURN
                if (step.Op.Contains("CALL"))
                    depth++;
            }
            
            lastContract = contract;
        }
        
        // Create nodes with positions based on depth
        int nodeIndex = 0;
        foreach (var kvp in contracts)
        {
            var contract = kvp.Key;
            var nodeDepth = kvp.Value;
            
            // Position nodes in a flow layout
            double x = 50 + (nodeDepth * 200);
            double y = 80 + (nodeIndex * 120);
            
            bool isAttacker = contract.Contains("Attacker") || contract == "EOA" || contract == "ROOT";
            bool isVictim = contract.Contains("Vault");
            bool hasVuln = isVictim;  // Simplified for now
            
            Nodes.Add(new CallNodeViewModel
            {
                Name = contract,
                Address = GetSampleAddress(contract),
                X = x,
                Y = y,
                IsAttacker = isAttacker,
                IsVictim = isVictim,
                HasVulnerability = hasVuln,
                Color = isAttacker ? "#FF4444" : isVictim ? "#00D4AA" : "#FFAA00"
            });
            
            nodeIndex++;
        }
        
        // Create edges
        foreach (var edge in callEdges)
        {
            Edges.Add(new CallEdgeViewModel
            {
                From = edge.from,
                To = edge.to,
                Label = edge.op,
                IsReentrancy = edge.op.Contains("CALL"),
                StepIndex = edge.step
            });
        }
    }
    
    private string GetContractName(ExecutionTraceStep step)
    {
        // Extract contract name from call type
        if (step.CallType != null)
            return step.CallType.ToString();
        
        // Fallback to address-based naming
        return "ROOT";
    }
    
    private string GetSampleAddress(string contract)
    {
        return contract switch
        {
            "Attacker" => "0xDead...Beef",
            "Vault" => "0x1234...5678",
            "Token" => "0xABCD...EF01",
            "Proxy" => "0x5678...9ABC",
            _ => "0x0000...0000"
        };
    }
}

public class CallNodeViewModel
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public bool IsAttacker { get; init; }
    public bool IsVictim { get; init; }
    public bool HasVulnerability { get; init; }
    public string Color { get; init; } = "#888";
}

public class CallEdgeViewModel
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsReentrancy { get; init; }
    public int StepIndex { get; init; }
}
