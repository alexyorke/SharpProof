using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class RuntimeImplementationAssemblyResolverTests
{
    [Test]
    public void Resolve_SkipsDynamicAssemblies()
    {
        _ = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SharpProof.DynamicResolverProbe." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        Assert.That(
            () => RuntimeImplementationAssemblyResolver.Resolve(
                new[] { "missing-method-key" },
                null,
                new ConcurrentDictionary<string, string>(StringComparer.Ordinal),
                static (_, _) => false),
            Is.Null);
    }
}
