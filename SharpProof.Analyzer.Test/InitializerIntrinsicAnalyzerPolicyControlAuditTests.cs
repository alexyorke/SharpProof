using System.Globalization;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class InitializerIntrinsicAnalyzerPolicyControlAuditTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DerivedConstructorProloguePreservesProofSupportDiagnostic(bool thisInitializer)
    {
        var initializer = thisInitializer ? "this(value, true)" : "base(value)";
        var source = $$"""
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject : Parent {
                public Subject(int value, bool ignored) : base(value) { }
                public Subject(int value) : {{initializer}} {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Old(value) == value);
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
        Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Is.EqualTo("SharpProof could not completely analyze selected method '.ctor': Advisory:UnsupportedCallable"));
    }

    [Test]
    public async Task PlainConstructorPrologueWithExplicitBaseRemainsQuiet()
    {
        const string source = """
            using SharpProof.Attributes;
            public sealed class Subject {
                public Subject(int value) : base() {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Old(value) == value);
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics);
    }
}
