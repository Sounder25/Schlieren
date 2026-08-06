using System;
using System.Reflection;
using System.Linq;

var asm = Assembly.LoadFrom(@"C:\Users\Erick\.nuget\packages\bouncycastle.cryptography\2.6.2\lib\net6.0\BouncyCastle.Cryptography.dll");
var bls = asm.GetTypes()
    .Where(t => t.IsPublic && (
        t.FullName?.Contains("Bls", StringComparison.OrdinalIgnoreCase) == true ||
        t.FullName?.Contains("12_381", StringComparison.OrdinalIgnoreCase) == true
    ))
    .Select(t => t.FullName)
    .Take(20)
    .ToList();

if (bls.Count == 0)
    Console.WriteLine("No BLS types in BouncyCastle");
else
    bls.ForEach(Console.WriteLine);
