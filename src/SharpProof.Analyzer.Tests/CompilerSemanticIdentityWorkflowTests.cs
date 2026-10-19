using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSemanticIdentityWorkflowTests
{
    [Test]
    public void MultipleQuerySelectorsRetainRolesAcrossRangeVariableRenames()
    {
        static string Fingerprint(string parameter, string rangeVariable)
        {
            System.ArgumentNullException.ThrowIfNull(parameter);
            System.ArgumentNullException.ThrowIfNull(rangeVariable);
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Root(int {{parameter}}) {
                        Contract.Ensures((from {{rangeVariable}} in new[] { {{parameter}} }
                            where {{rangeVariable}} >= 0 orderby {{rangeVariable}} descending
                            select {{rangeVariable}}).Single() == {{parameter}});
                        return {{parameter}};
                    }
                }
                """, []);
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("value", "item");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(Fingerprint("renamedValue", "renamedItem"), Is.EqualTo(first));
        }
    }

    [Test]
    public void NestedQuerySelectorsRetainTheirOwnedStructuralPositions()
    {
        static string Fingerprint(string parameter, string outer, string inner)
        {
            System.ArgumentNullException.ThrowIfNull(inner);
            System.ArgumentNullException.ThrowIfNull(outer);
            System.ArgumentNullException.ThrowIfNull(parameter);
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Root(int {{parameter}}) {
                        Contract.Ensures((from {{outer}} in new[] { {{parameter}} }
                            select (from {{inner}} in new[] { {{outer}} } select {{inner}}).Single()).Single() == {{parameter}});
                        return {{parameter}};
                    }
                }
                """, []);
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("value", "outerItem", "innerItem");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(Fingerprint("renamedValue", "renamedOuter", "renamedInner"), Is.EqualTo(first));
        }
    }

    [Test]
    public void SeparateCompilerGeneratedQueryMethodsDoNotAliasOneSiblingRole()
    {
        static ImmutableArray<string> Fingerprints(string parameter, string rangeVariable)
        {
            System.ArgumentNullException.ThrowIfNull(parameter);
            System.ArgumentNullException.ThrowIfNull(rangeVariable);
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Root(int {{parameter}}) {
                        Contract.Ensures((from {{rangeVariable}} in new[] { {{parameter}} } select {{rangeVariable}}).Single() == {{parameter}});
                        Contract.Ensures((from {{rangeVariable}} in new[] { {{parameter}} } select {{rangeVariable}}).Single() == {{parameter}});
                        return {{parameter}};
                    }
                }
                """, []);
            return InvocationFingerprints(compilation);
        }
        var first = Fingerprints("value", "item");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(2));
            Assert.That(first[0], Is.Not.EqualTo(first[1]));
            Assert.That(Fingerprints("renamedValue", "renamedItem"), Is.EqualTo(first));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void QueryTranslationUsesTheCompilerOwnedRangeVariableRole(bool querySyntax)
    {
        string Fingerprint(string parameter, string rangeVariable)
        {
            System.ArgumentNullException.ThrowIfNull(parameter);
            System.ArgumentNullException.ThrowIfNull(rangeVariable);
            var sequence = querySyntax
                ? $"(from {rangeVariable} in new[] {{ {parameter} }} select {rangeVariable})"
                : $"new[] {{ {parameter} }}.Select({rangeVariable} => {rangeVariable})";
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Root(int {{parameter}}) {
                        Contract.Ensures({{sequence}}.Single() == {{parameter}});
                        return {{parameter}};
                    }
                }
                """, []);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var ensures = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "Contract.Ensures"));
            var operation = (IInvocationOperation)model.GetOperation(ensures)!;
            var foundAnonymousParameter = false;
            var pending = ImmutableStack<IOperation>.Empty;
            pending = pending.Push(operation);
            while (!pending.IsEmpty)
            {
                var current = pending.Peek();
                pending = pending.Pop();
                if (current is IParameterReferenceOperation parameterReference &&
                    parameterReference.Parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } owner)
                {
                    foundAnonymousParameter = true;
                    var declarations = owner.DeclaringSyntaxReferences.Select(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.GetSyntax().GetType().Name));
                    TestContext.Out.WriteLine($"Compiler-owned query parameter: implicit={owner.IsImplicitlyDeclared}, declarations={string.Join(",", declarations)}");
                }
                foreach (var child in current.ChildOperations)
                {
                    pending = pending.Push(child);
                }
            }
            Assert.That(foundAnonymousParameter, Is.True, "The actual SDK predicate must contain an anonymous-function-owned parameter.");
            var method = (IMethodSymbol)model.GetEnclosingSymbol(operation.Syntax.SpanStart)!;
            return SemanticClaimIdentity.CreateInvocationFingerprint(operation, method, method, usesCompanion: false);
        }
        var first = Fingerprint("value", "item");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(Fingerprint("renamedValue", "renamedItem"), Is.EqualTo(first));
        }
    }

    [Test]
    public void DeeplyNestedPredicateTypeParametersRetainTheirEnclosingOrdinalAcrossRenames()
    {
        static string Fingerprint(string firstType, string secondType, string operand)
        {
            System.ArgumentNullException.ThrowIfNull(firstType);
            System.ArgumentNullException.ThrowIfNull(operand);
            System.ArgumentNullException.ThrowIfNull(secondType);
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static void Outer<{{firstType}}, {{secondType}}>() {
                        void First() {
                            void Second() {
                                Contract.Ensures(typeof({{operand}}) != null);
                            }
                            Second();
                        }
                        First();
                    }
                }
                """, []);
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("Left", "Right", "Left");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(Fingerprint("RenamedLeft", "RenamedRight", "RenamedLeft"), Is.EqualTo(first));
            Assert.That(Fingerprint("Left", "Right", "Right"), Is.Not.EqualTo(first));
        }
    }

    [TestCase("value > 1.25f", "renamed > 1.25f", "value > 2.25f", "float")]
    [TestCase("value > 1.25d", "renamed > 1.25d", "value > 2.25d", "double")]
    [TestCase("value > 1.25m", "renamed > 1.25m", "value > 2.25m", "decimal")]
    [TestCase("value is string", "renamed is string", "value is System.Exception", "object")]
    public void PredicateConstantsAndTypeTestsRetainTheirSemanticIdentity(
        string predicate, string renamed, string changed, string type)
    {
        System.ArgumentNullException.ThrowIfNull(changed);
        System.ArgumentNullException.ThrowIfNull(predicate);
        System.ArgumentNullException.ThrowIfNull(renamed);
        System.ArgumentNullException.ThrowIfNull(type);
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
        System.ArgumentNullException.ThrowIfNull(parameter);
        System.ArgumentNullException.ThrowIfNull(predicate);
        System.ArgumentNullException.ThrowIfNull(type);
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
            System.ArgumentNullException.ThrowIfNull(path);
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
            System.ArgumentNullException.ThrowIfNull(field);
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

    [TestCase(false)]
    [TestCase(true)]
    public void EmittedQueryValuedSelectorsRetainTheirOwnedIdentityAcrossRenames(bool extractInsideSelector)
    {
        string Fingerprint(string parameter, string outer, string inner)
        {
            System.ArgumentNullException.ThrowIfNull(inner);
            System.ArgumentNullException.ThrowIfNull(outer);
            System.ArgumentNullException.ThrowIfNull(parameter);
            var query = $"from {outer} in new[] {{ {parameter} }} select " +
                (extractInsideSelector
                    ? $"(from {inner} in new[] {{ {outer} }} select {inner}).Single()"
                    : $"from {inner} in new[] {{ {outer} }} select {inner}");
            var result = "(" + query + ").Single()" + (extractInsideSelector ? "" : ".Single()");
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                public static class QueryValueControl {
                    public static int Root(int {{parameter}}) {
                        Contract.Ensures({{result}} == {{parameter}});
                        return {{result}};
                    }
                }
                """, [], filePath: "query-valued-selector.cs");
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var image = AnalyzerTestHost.EmitImage(compilation);
            var runtime = new System.Runtime.Loader.AssemblyLoadContext("QueryValueControl", isCollectible: true);
            try
            {
                using var stream = new MemoryStream(image, writable: false);
                var method = runtime.LoadFromStream(stream).GetType("QueryValueControl", throwOnError: true)!
                    .GetMethod("Root")!;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(method.Invoke(null, [7]), Is.EqualTo(7));
                    Assert.That(method.Invoke(null, [-3]), Is.EqualTo(-3));
                }
            }
            finally
            {
                runtime.Unload();
            }
            return InvocationFingerprints(compilation).Single();
        }
        var first = Fingerprint("value", "outerValue", "innerValue");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(Fingerprint("renamedValue", "renamedOuter", "renamedInner"), Is.EqualTo(first));
        }
    }
    [Test]
    public void QueryValuedSelectorSiblingsKeepDistinctRolesAcrossRenames()
    {
        static ImmutableArray<string> Fingerprints(string parameter, string outer, string inner)
        {
            System.ArgumentNullException.ThrowIfNull(inner);
            System.ArgumentNullException.ThrowIfNull(outer);
            System.ArgumentNullException.ThrowIfNull(parameter);
            var result = $"(from {outer} in new[] {{ {parameter} }} select " +
                $"from {inner} in new[] {{ {outer} }} select {inner}).Single().Single()";
            var compilation = AnalyzerTestHost.CreateCompilation(
                $$"""
                using System.Linq;
                using SharpProof.Attributes;
                public static class DistinctQueryRoleControl {
                    public static int Root(int {{parameter}}) {
                        Contract.Ensures({{result}} == {{parameter}});
                        Contract.Ensures({{result}} == {{parameter}});
                        return {{result}};
                    }
                }
                """, [], filePath: "query-valued-selector-roles.cs");
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var image = AnalyzerTestHost.EmitImage(compilation);
            var runtime = new System.Runtime.Loader.AssemblyLoadContext("DistinctQueryRoleControl", isCollectible: true);
            try
            {
                using var stream = new MemoryStream(image, writable: false);
                var method = runtime.LoadFromStream(stream).GetType("DistinctQueryRoleControl", throwOnError: true)!
                    .GetMethod("Root")!;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(method.Invoke(null, [7]), Is.EqualTo(7));
                    Assert.That(method.Invoke(null, [-3]), Is.EqualTo(-3));
                }
            }
            finally
            {
                runtime.Unload();
            }
            return InvocationFingerprints(compilation);
        }
        var first = Fingerprints("value", "outerValue", "innerValue");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(2));
            Assert.That(first.Select(static fingerprint => fingerprint is null ? throw new System.ArgumentNullException(nameof(fingerprint)) : (fingerprint.Length)), Is.All.EqualTo(64));
            Assert.That(first, Is.Unique);
            Assert.That(Fingerprints("renamedValue", "renamedOuter", "renamedInner"), Is.EqualTo(first));
        }
    }
    private static ImmutableArray<string> InvocationFingerprints(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : ((IInvocationOperation)model.GetOperation(syntax)!))
            .Where(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.TargetMethod.Name == "Ensures"))
            .Select(invocation =>
            {
                System.ArgumentNullException.ThrowIfNull(invocation);
                var method = (IMethodSymbol)model.GetEnclosingSymbol(invocation.Syntax.SpanStart)!;
                return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, method, method,
                    usesCompanion: false);
            }).ToImmutableArray();
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
        System.ArgumentNullException.ThrowIfNull(firstType);
        System.ArgumentNullException.ThrowIfNull(operand);
        System.ArgumentNullException.ThrowIfNull(secondType);
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
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
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
