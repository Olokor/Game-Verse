// Deliberate violation used to prove ModuleBoundaries.FindViolations catches real breaches.
// These types live only in the test assembly and never ship.
#pragma warning disable CA1812, CA1822 // Sample types exist only to be scanned, not used.

namespace GameVerse.Architecture.Tests.Samples.ModuleB
{
    internal sealed class Secret
    {
        public int Value { get; set; }
    }

    public sealed class PublicApi
    {
        public int Value { get; set; }
    }
}

namespace GameVerse.Architecture.Tests.Samples.ModuleA
{
    public sealed class ReachesIntoB
    {
        internal int Peek() => new ModuleB.Secret().Value;
    }
}

namespace GameVerse.Architecture.Tests.Samples.ModuleC
{
    public sealed class UsesPublicApiOnly
    {
        public int Peek() => new ModuleB.PublicApi().Value;
    }
}
