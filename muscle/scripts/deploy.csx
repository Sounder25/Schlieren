// schlieren deploy script — runs with: schlieren run scripts/deploy.csx
// 'node' and 'accounts' are injected by the script host.

var contract = await node.Deploy("Counter");
Console.WriteLine($"Counter deployed at {contract.Address}");