using System.Reflection;
using Schlieren.Core.Execution;

namespace Schlieren.EELS.Tests.Harness;

internal static class OpcodeCatalog
{
    public static IReadOnlyList<IOpcode> CreateAll()
    {
        // [AI-EDIT 2026-01-10] Reflect all concrete opcode implementations so the
        // harness always tracks Schlieren opcode coverage without manual lists.
        var opcodeType = typeof(IOpcode);
        var assembly = opcodeType.Assembly;

        var instances = new List<IOpcode>();
        foreach (var type in assembly.GetTypes())
        {
            if (!opcodeType.IsAssignableFrom(type) ||
                type.IsAbstract ||
                type.IsInterface)
            {
                continue;
            }

            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);
            if (ctor is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is IOpcode opcode)
            {
                instances.Add(opcode);
            }
        }

        return instances
            .OrderBy(op => op.Code)
            .ToArray();
    }
}
