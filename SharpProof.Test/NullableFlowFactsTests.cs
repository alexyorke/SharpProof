using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class NullableFlowFactsTests {
    [Test]
    public void NullableFlowFacts_CentralizesRoslynStateAndCodeAnalysisContracts() {
        const string source = @"
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

public sealed class ContractFixture
{
    private string? _value;

    public void Inputs([AllowNull] string allowed, [DisallowNull] string? disallowed) {
    }

    public static bool TryRead( [MaybeNullWhen(false)] out string value, [NotNullWhen(true)] string? candidate) {
        value = string.Empty;
        return candidate is not null;
    }

    public static void Mark([NotNull] string? value) {
    }

    [return: MaybeNull]
    public static string Maybe() => null;

    [return: NotNull]
    public static string? Always() => string.Empty;

    [return: NotNullIfNotNull(nameof(value))]
    public static string? Echo(string? value) => value;

    [MemberNotNull(nameof(_value))]
    public void EnsureValue() => _value = string.Empty;

    [MemberNotNullWhen(true, nameof(_value))]
    public bool HasValue() => _value is not null;

    [DoesNotReturn]
    public static void Fail() => throw new InvalidOperationException();

    public static void ThrowIf([DoesNotReturnIf(true)] bool condition) {
    }

    public static int Flow(string? value) {
        if (value is null) {
            return 0;
        }

        return value.Length;
    }

    public static void Reads() {
        _ = Always();
        _ = Maybe();
    }
}";

        var (root, semanticModel) = CreateSemanticModel(source);
        var fixture = semanticModel.Compilation.GetTypeByMetadataName("ContractFixture")!;

        var inputs = fixture.GetMembers("Inputs").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.GetParameterInputState(inputs.Parameters[0]), Is.EqualTo(NullableFlowFactState.MaybeNull));
        Assert.That(NullableFlowFacts.GetParameterInputState(inputs.Parameters[1]), Is.EqualTo(NullableFlowFactState.NotNull));

        var tryRead = fixture.GetMembers("TryRead").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.GetParameterOutputState(tryRead.Parameters[0], false), Is.EqualTo(NullableFlowFactState.MaybeNull));
        Assert.That(NullableFlowFacts.GetParameterOutputState(tryRead.Parameters[0], true), Is.EqualTo(NullableFlowFactState.NotNull));
        Assert.That(NullableFlowFacts.GetParameterOutputState(tryRead.Parameters[1], true), Is.EqualTo(NullableFlowFactState.NotNull));
        Assert.That(NullableFlowFacts.GetParameterOutputState(tryRead.Parameters[1], false), Is.EqualTo(NullableFlowFactState.Unknown));

        var mark = fixture.GetMembers("Mark").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.HasNotNullPostcondition(mark.Parameters[0]), Is.True);

        var maybe = fixture.GetMembers("Maybe").OfType<IMethodSymbol>().Single();
        var always = fixture.GetMembers("Always").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.GetMethodReturnState(maybe), Is.EqualTo(NullableFlowFactState.MaybeNull));
        Assert.That(NullableFlowFacts.GetMethodReturnState(always), Is.EqualTo(NullableFlowFactState.NotNull));

        var echo = fixture.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.TryGetNotNullIfNotNullParameterName(echo, out var parameterName), Is.True);
        Assert.That(parameterName, Is.EqualTo("value"));

        var ensureValue = fixture.GetMembers("EnsureValue").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.GetMemberNotNullTargets(ensureValue), Is.EquivalentTo(new[] { "_value" }));

        var hasValue = fixture.GetMembers("HasValue").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.GetMemberNotNullWhenTargets(hasValue, true), Is.EquivalentTo(new[] { "_value" }));
        Assert.That(NullableFlowFacts.TryResolveInstanceMemberTarget(fixture, "_value", out var member), Is.True);
        Assert.That(member.Name, Is.EqualTo("_value"));

        var fail = fixture.GetMembers("Fail").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.HasDoesNotReturn(fail), Is.True);

        var throwIf = fixture.GetMembers("ThrowIf").OfType<IMethodSymbol>().Single();
        Assert.That(NullableFlowFacts.TryGetDoesNotReturnIfValue(throwIf.Parameters[0], out var doesNotReturnIf), Is.True);
        Assert.That(doesNotReturnIf, Is.True);

        var flowValue = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(memberAccess => memberAccess.ToString() == "value.Length")
            .Expression;
        Assert.That(NullableFlowFacts.GetExpressionState(flowValue, semanticModel, CancellationToken.None),
            Is.EqualTo(NullableFlowFactState.NotNull));

        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "Always" or "Maybe" })
            .ToDictionary(invocation => invocation.Expression.ToString(), StringComparer.Ordinal);
        Assert.That(NullableFlowFacts.GetExpressionState(invocations["Always"], semanticModel, CancellationToken.None),
            Is.EqualTo(NullableFlowFactState.NotNull));
        Assert.That(NullableFlowFacts.GetExpressionState(invocations["Maybe"], semanticModel, CancellationToken.None),
            Is.EqualTo(NullableFlowFactState.MaybeNull));
    }
    private static (CompilationUnitSyntax Root, SemanticModel SemanticModel) CreateSemanticModel(string source) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), "NullableFlowFactsTests.cs");
        var compilation = CSharpCompilation.Create(
            "NullableFlowFactsTests",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return ((CompilationUnitSyntax)syntaxTree.GetRoot(), compilation.GetSemanticModel(syntaxTree));
    }
}
