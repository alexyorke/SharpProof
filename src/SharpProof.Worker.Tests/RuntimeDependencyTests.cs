using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RuntimeDependencyTests
{
    [Test]
    public void WorkerAssemblyHasCompilerNeutralRuntimeClosure()
    {
        var forbidden = ImmutableHashSet.Create(StringComparer.Ordinal,
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Compiler.Semantics",
            "SharpProof.Frontend"
        );
        var pending = ImmutableQueue<Assembly>.Empty;
        var queued = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        void Enqueue(Assembly assembly)
        {
            System.ArgumentNullException.ThrowIfNull(assembly);
            if (queued.Add(assembly.GetName().Name!))
            {
                pending = pending.Enqueue(assembly);
            }
        }

        Enqueue(typeof(SharpProofWorker).Assembly);
        while (!pending.IsEmpty)
        {
            pending = pending.Dequeue(out var assembly);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                Assert.That(name, Does.Not.StartWith("Microsoft.CodeAnalysis"), assembly.FullName);
                Assert.That(forbidden, Does.Not.Contain(name), assembly.FullName);
                if (name.StartsWith("SharpProof.", StringComparison.Ordinal))
                {
                    Enqueue(Assembly.Load(reference));
                }
            }
        }
    }
}
