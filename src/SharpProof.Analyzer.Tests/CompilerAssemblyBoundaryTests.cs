using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerAssemblyBoundaryTests
{
    [Test]
    public void CompilerFailureDiagnosticRetainsItsPublishedIdentity()
    {
        var collector = new FinalCompilationCollectorAnalyzer().SupportedDiagnostics.Single();
        var analyzer = new SharpProof.Analyzer.SharpProofAnalyzer().SupportedDiagnostics
            .Single(static descriptor =>
            {
                ArgumentNullException.ThrowIfNull(descriptor);
                return descriptor.Id == "SP0049";
            });
        Assert.That(collector.Id, Is.EqualTo(analyzer.Id));
        Assert.That(collector.Title.ToString(CultureInfo.InvariantCulture), Is.EqualTo(analyzer.Title.ToString(CultureInfo.InvariantCulture)));
        Assert.That(collector.MessageFormat.ToString(CultureInfo.InvariantCulture), Is.EqualTo(analyzer.MessageFormat.ToString(CultureInfo.InvariantCulture)));
        Assert.That(collector.Category, Is.EqualTo(analyzer.Category));
        Assert.That(collector.DefaultSeverity, Is.EqualTo(analyzer.DefaultSeverity));
        Assert.That(collector.IsEnabledByDefault, Is.EqualTo(analyzer.IsEnabledByDefault));
        Assert.That(collector.Description.ToString(CultureInfo.InvariantCulture), Is.EqualTo(analyzer.Description.ToString(CultureInfo.InvariantCulture)));
        Assert.That(collector.HelpLinkUri, Is.EqualTo(analyzer.HelpLinkUri));
        Assert.That(collector.CustomTags, Is.EqualTo(analyzer.CustomTags));
    }

    [Test]
    public void CollectorAndSharedSemanticsHaveNoAnalyzerRuntimeDependency()
    {
        var pending = new Stack<Assembly>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(typeof(FinalCompilationCollectorAnalyzer).Assembly);
        while (pending.TryPop(out var assembly))
        {
            var name = assembly.GetName().Name!;
            if (!visited.Add(name))
            {
                continue;
            }
            Assert.That(name, Is.Not.EqualTo("SharpProof.Analyzer"));
            Assert.That(name, Is.Not.EqualTo("SharpProof.Analyzer.Core"));
            Assert.That(name, Is.Not.EqualTo("SharpProof.Smt"));
            Assert.That(name, Is.Not.EqualTo("SharpProof.Worker"));
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name!.StartsWith("SharpProof.", StringComparison.Ordinal))
                {
                    pending.Push(Assembly.Load(reference));
                }
            }
        }
        Assert.That(visited, Does.Contain("SharpProof.Compiler.Semantics"));
        Assert.That(visited, Does.Contain("SharpProof.CompilerArtifact"));
    }
}
