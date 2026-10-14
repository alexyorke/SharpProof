using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSemanticIdentityWorkflowTests
{
    [TestCase("value > 1.25f", "renamed > 1.25f", "value > 2.25f", "float")]
    [TestCase("value > 1.25d", "renamed > 1.25d", "value > 2.25d", "double")]
    [TestCase("value > 1.25m", "renamed > 1.25m", "value > 2.25m", "decimal")]
    [TestCase("value is string", "renamed is string", "value is System.Exception", "object")]
    public void PredicateConstantsAndTypeTestsRetainTheirSemanticIdentity(
        string predicate, string renamed, string changed, string type)
    {
        var first = PredicateFingerprint(predicate, type, "value");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(PredicateFingerprint(renamed, type, "renamed"),
                Is.EqualTo(first));
            Assert.That(PredicateFingerprint(changed, type, "value"), Is.Not.EqualTo(first));
        }
    }

    [Test]
    public void EventPredicatesRetainTheReferencedEventIdentity()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            internal class Subject {
                internal event System.Action First;
                internal event System.Action Second;
                internal void Run() {
                    Contract.Ensures(First != null);
                    Contract.Ensures(Second != null);
                }
            }
            """, []);
        var fingerprints = InvocationFingerprints(compilation);
        Assert.That(fingerprints, Has.Length.EqualTo(2));
        Assert.That(fingerprints[0], Is.Not.EqualTo(fingerprints[1]));
    }

    [Test]
    public void CapturedOuterParametersRemainDistinctFromLocalParameters()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static void Outer(int first, int second) {
                    void Local(int value) {
                        Contract.Ensures(first > value);
                        Contract.Ensures(second > value);
                    }
                    Local(0);
                }
            }
            """, []);
        var fingerprints = InvocationFingerprints(compilation);
        Assert.That(fingerprints, Has.Length.EqualTo(2));
        Assert.That(fingerprints[0], Is.Not.EqualTo(fingerprints[1]));
    }

    private static string PredicateFingerprint(string predicate, string type, string parameter)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                internal static void Run({{type}} {{parameter}}) {
                    Contract.Ensures({{predicate}});
                }
            }
            """, []);
        return InvocationFingerprints(compilation).Single();
    }

    [Test]
    public void TopLevelLocalPredicatesRetainTheirConstantsWithoutADeclaredMethodOwner()
    {
        static string Fingerprint(int bound)
        {
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                int value = 1;
                Contract.Ensures(value > {{bound}});
                """, [], filePath: "program.cs");
            compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint(0);
        Assert.That(Fingerprint(0), Is.EqualTo(first));
        Assert.That(Fingerprint(1), Is.Not.EqualTo(first));
    }

    [Test]
    public void FileLocalMemberPredicatesRetainTheirSourceScope()
    {
        static string Fingerprint(string path)
        {
            var compilation = AnalyzerTestHost.CreateCompilation(
                """
                using SharpProof.Attributes;
                file class Subject {
                    private int Value;
                    internal void Run() { Contract.Ensures(Value > 0); }
                }
                """, [], filePath: path);
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("first.cs");
        Assert.That(Fingerprint("first.cs"), Is.EqualTo(first));
        Assert.That(Fingerprint("second.cs"), Is.Not.EqualTo(first));
    }

    [Test]
    public void InitializerLambdaLocalTargetsRetainTheirContainingFieldIdentity()
    {
        static string Fingerprint(string field)
        {
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static System.Func<int> {{field}} = () => {
                        int Local() => 1;
                        Contract.Ensures(Local() == 1);
                        return Local();
                    };
                }
                """, []);
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("Work");
        Assert.That(Fingerprint("Work"), Is.EqualTo(first));
        Assert.That(Fingerprint("Renamed"), Is.Not.EqualTo(first));
    }

    private static string[] InvocationFingerprints(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(syntax => (IInvocationOperation)model.GetOperation(syntax)!)
            .Where(invocation => invocation.TargetMethod.Name == "Ensures")
            .Select(invocation =>
            {
                var method = (IMethodSymbol)model.GetEnclosingSymbol(invocation.Syntax.SpanStart)!;
                return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, method, method,
                    usesCompanion: false);
            }).ToArray();
    }

    [Test]
    public void NestedPredicateTypeParametersUseEnclosingRolesAcrossRenames()
    {
        var first = Fingerprint("Left", "Right", "Left");
        var renamed = Fingerprint("RenamedLeft", "RenamedRight", "RenamedLeft");
        var second = Fingerprint("Left", "Right", "Right");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(first, Has.Length.EqualTo(64));
        }
    }

    private static string Fingerprint(string firstType, string secondType, string operand)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                internal static void Outer<{{firstType}}, {{secondType}}>() {
                    void Local() {
                        Contract.Ensures(typeof({{operand}}) != null);
                    }
                    Local();
                }
            }
            """, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var local = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(local)!;
        var syntax = local.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Ensures"));
        return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, method, method,
            usesCompanion: false);
    }
}
