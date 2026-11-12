using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using GameVerse.Rules.Math;

namespace GameVerse.Rules.Tests.Guards;

/// <summary>
/// Scans the compiled GameVerse.Rules assembly for anything that breaks determinism or the zero-dependency rule
/// (CLAUDE.md section 3). The MSBuild guard in shared/Directory.Build.targets covers LangVersion, target framework
/// and package references; this covers what can only be seen in the compiled IL.
/// </summary>
public class DeterminismGuardTests
{
    private static readonly Assembly Rules = typeof(Fixed).Assembly;

    // (namespace, type, member or "*" for any member)
    private static readonly (string Ns, string Type, string Member)[] Banned =
    [
        ("System", "Random", "*"),
        ("System", "DateTime", "get_Now"),
        ("System", "DateTime", "get_UtcNow"),
        ("System", "DateTime", "get_Today"),
        ("System", "DateTimeOffset", "get_Now"),
        ("System", "DateTimeOffset", "get_UtcNow"),
        ("System", "Environment", "get_TickCount"),
        ("System", "Environment", "get_TickCount64"),
        ("System", "Guid", "NewGuid"),
        ("System.Diagnostics", "Stopwatch", "*"),
        ("System.Threading", "Thread", "*"),
        ("System.Threading.Tasks", "Task", "*"),
        ("System.Threading.Tasks", "Parallel", "*"),
    ];

    [Fact]
    public void Rules_references_only_netstandard()
    {
        Rules.GetReferencedAssemblies().Select(a => a.Name).ShouldBe(["netstandard"]);
    }

    [Fact]
    public void Rules_uses_no_nondeterministic_apis()
    {
        var violations = FindBannedReferences(Rules.Location);
        violations.ShouldBeEmpty("Non-deterministic APIs used in GameVerse.Rules: " + string.Join(", ", violations));
    }

    [Fact]
    public void Rules_has_no_float_or_double_fields()
    {
        var violations = Rules.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => IsFloatingPoint(f.FieldType))
                .Select(f => t.FullName + "." + f.Name))
            .ToList();

        violations.ShouldBeEmpty("float/double fields in GameVerse.Rules (use Fixed): " + string.Join(", ", violations));
    }

    [Fact]
    public void Scanner_detects_a_banned_api()
    {
        // Proves the scanner works: this test assembly deliberately touches DateTime.UtcNow.
        _ = DateTime.UtcNow;
        FindBannedReferences(typeof(DeterminismGuardTests).Assembly.Location).ShouldContain("System.DateTime.get_UtcNow");
    }

    private static bool IsFloatingPoint(Type type)
    {
        var t = type.IsArray ? type.GetElementType()! : type;
        return t == typeof(float) || t == typeof(double) || t == typeof(decimal);
    }

    private static List<string> FindBannedReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in md.MemberReferences)
        {
            var member = md.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = md.GetTypeReference((TypeReferenceHandle)member.Parent);
            var ns = md.GetString(type.Namespace);
            var name = md.GetString(type.Name);
            var memberName = md.GetString(member.Name);
            if (Banned.Any(b => b.Ns == ns && b.Type == name && (b.Member == "*" || b.Member == memberName)))
            {
                found.Add(ns + "." + name + "." + memberName);
            }
        }

        foreach (var handle in md.TypeReferences)
        {
            var type = md.GetTypeReference(handle);
            var ns = md.GetString(type.Namespace);
            var name = md.GetString(type.Name);
            if (Banned.Any(b => b.Ns == ns && b.Type == name && b.Member == "*"))
            {
                found.Add(ns + "." + name);
            }
        }

        return found.ToList();
    }
}
