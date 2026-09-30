using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RuntimeDependencyTests
{
    [Test]
    public void WorkerAssemblyHasCompilerNeutralRuntimeClosure()
    {
        var forbidden = new HashSet<string>([
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Contracts",
            "SharpProof.Effects", "SharpProof.Frontend"
        ], StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        var queued = new HashSet<string>(StringComparer.Ordinal);
        void Enqueue(Assembly assembly)
        {
            if (queued.Add(assembly.GetName().Name!))
            {
                pending.Enqueue(assembly);
            }
        }

        Enqueue(typeof(SharpProofWorker).Assembly);
        while (pending.Count != 0)
        {
            var assembly = pending.Dequeue();
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
