using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RuntimeDependencyTests {
    [Test]
    public void WorkerAndLauncherAssembliesHaveCompilerNeutralRuntimeClosures() {
        var forbidden = new HashSet<string>([
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Contracts",
            "SharpProof.Effects", "SharpProof.Frontend"
        ], StringComparer.Ordinal);
        var pending = new Queue<Assembly>([
            typeof(SharpProofWorker).Assembly,
            Assembly.LoadFrom(Path.Combine(
                AppContext.BaseDirectory, "SharpProof.Worker.Launcher.dll"))
        ]);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count != 0) {
            var assembly = pending.Dequeue();
            if (!visited.Add(assembly.GetName().Name!)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies()) {
                var name = reference.Name!;
                Assert.That(name, Does.Not.StartWith("Microsoft.CodeAnalysis"), assembly.FullName);
                Assert.That(forbidden, Does.Not.Contain(name), assembly.FullName);
                if (name.StartsWith("SharpProof.", StringComparison.Ordinal))
                    pending.Enqueue(Assembly.Load(reference));
            }
        }
    }
}
