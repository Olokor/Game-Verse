using System.Reflection;
using NetArchTest.Rules;

namespace GameVerse.Architecture.Tests;

/// <summary>
/// Finds types in one module that use a non-public type of another module. All modules share the
/// GameVerse.Platform assembly, so `internal` alone cannot stop this; this check can.
/// </summary>
public static class ModuleBoundaries
{
    public static IReadOnlyList<string> FindViolations(Assembly assembly, string rootNamespace, IEnumerable<string> modules)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var moduleList = modules.ToList();
        var violations = new List<string>();

        foreach (var owner in moduleList)
        {
            var ownerNamespace = rootNamespace + "." + owner;
            var internalTypes = assembly.GetTypes()
                .Where(t => t.FullName != null
                    && !t.IsVisible
                    && !t.Name.StartsWith('<')
                    && (t.Namespace == ownerNamespace || (t.Namespace?.StartsWith(ownerNamespace + ".", StringComparison.Ordinal) ?? false)))
                .Select(t => t.FullName!.Replace('+', '/'))
                .ToArray();

            if (internalTypes.Length == 0)
            {
                continue;
            }

            foreach (var user in moduleList.Where(m => m != owner))
            {
                var result = Types.InAssembly(assembly)
                    .That().ResideInNamespace(rootNamespace + "." + user)
                    .ShouldNot().HaveDependencyOnAny(internalTypes)
                    .GetResult();

                if (!result.IsSuccessful)
                {
                    violations.AddRange(result.FailingTypeNames.Select(t => t + " uses internals of module " + owner));
                }
            }
        }

        return violations;
    }
}
