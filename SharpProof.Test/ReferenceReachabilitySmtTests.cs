using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class ReferenceReachabilitySmtTests {
    [Test]
    public void ExecutionVisibility_PriorNullConditionalAccessWhenNotNull_IsUnreachable() {
        const string source = @"
public class TestClass
{
    public int TestMethod() {
        string value = null;
        return value?.Length ?? 0;
    }
}";

        Assert.That(IsConditionalAccessWhenNotNullUnreachable(source), Is.True);
    }
    [Test]
    public void ExecutionVisibility_GuardedNullConditionalAccessWhenNotNull_IsUnreachable() {
        const string source = @"
public class TestClass
{
    public int TestMethod(string value) {
        if (value == null) {
            return value?.Length ?? 0;
        }

        return value.Length;
    }
}";

        Assert.That(IsConditionalAccessWhenNotNullUnreachable(source), Is.True);
    }
    [Test]
    public void ExecutionVisibility_ReassignedConditionalAccessWhenNotNull_RemainsReachable() {
        const string source = @"
public class TestClass
{
    public int TestMethod(string other) {
        string value = null;
        value = other;
        return value?.Length ?? 0;
    }
}";

        Assert.That(IsConditionalAccessWhenNotNullUnreachable(source), Is.False);
    }
    [Test]
    public void ExecutionVisibility_PriorNonNullCoalesceRight_IsUnreachable() {
        const string source = @"
public class TestClass
{
    public string TestMethod() {
        string value = ""safe"";
        return value ?? Throw();
    }

    private static string Throw() => throw new System.InvalidOperationException();
}";

        Assert.That(IsInvocationUnreachable(source, "Throw()"), Is.True);
    }
    [Test]
    public void ExecutionVisibility_GuardedNonNullCoalesceRight_IsUnreachable() {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value) {
        if (value != null) {
            return value ?? Throw();
        }

        return ""fallback"";
    }

    private static string Throw() => throw new System.InvalidOperationException();
}";

        Assert.That(IsInvocationUnreachable(source, "Throw()"), Is.True);
    }
    [Test]
    public void ExecutionVisibility_UnknownCoalesceRight_RemainsReachable() {
        const string source = @"
public class TestClass
{
    public string TestMethod(string value) {
        return value ?? Throw();
    }

    private static string Throw() => throw new System.InvalidOperationException();
}";

        Assert.That(IsInvocationUnreachable(source, "Throw()"), Is.False);
    }
    private static bool IsConditionalAccessWhenNotNullUnreachable(string source) {
        var (semanticModel, root) = CreateSemanticModel(source);
        var memberBinding = root
            .DescendantNodes()
            .OfType<MemberBindingExpressionSyntax>()
            .Single(binding => binding.Name.Identifier.ValueText == "Length");

        return IsUnreachable(memberBinding, semanticModel);
    }
    private static bool IsInvocationUnreachable(string source, string invocationText) {
        var (semanticModel, root) = CreateSemanticModel(source);
        var invocation = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(node => string.Equals(node.ToString(), invocationText, StringComparison.Ordinal));

        return IsUnreachable(invocation, semanticModel);
    }
    private static bool IsUnreachable(SyntaxNode node, SemanticModel semanticModel) {
        var method = typeof(SharpProofAnalyzer).Assembly
            .GetType("SharpProof.Analyzer.Engine.ExecutionVisibility", true)!
            .GetMethod(
                "IsInStaticallyUnreachableBranchUsingSmt",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [ typeof(SyntaxNode), typeof(SemanticModel), typeof(CancellationToken),
                    typeof(SharpProof.Symbolic.Smt.SmtAnalysisService) ],
                null)!;

        return (bool)method.Invoke(null, [node, semanticModel, CancellationToken.None, null])!;
    }
    private static (SemanticModel SemanticModel, SyntaxNode Root) CreateSemanticModel(string source) {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "ReferenceReachabilitySmtTests.cs");
        var compilation = CSharpCompilation.Create(
            "ReferenceReachabilitySmtTests",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (compilation.GetSemanticModel(syntaxTree), syntaxTree.GetRoot());
    }
}
