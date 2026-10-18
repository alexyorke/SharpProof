using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkSourceApiDomainCoverageTests
{
    private static readonly string[] EnabledSymbols = ["ENABLED"];
    private static readonly string[] ConditionalNullErrors = ["CS0633"];

    [Test]
    public void RealRejectedConditionalAttributeCannotElideTheOwnedInvocation()
    {
        const string source = "public static class Subject { public static void Target() => Helper(); [System.Diagnostics.Conditional(null)] private static void Helper() { } }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "conditional-null.cs");
        var compilation = CSharpCompilation.Create("ConditionalNull", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ConditionalNullErrors));
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(invocation)!;
        Assert.That(operation.TargetMethod.IsConditional, Is.True);
        var policy = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(policy.IsElided(operation), Is.False);
        Assert.That(policy.IsElided(operation), Is.False);
        using var supported = TypedProgramSubject.Create("int Target() { Helper(); return 17; } [System.Diagnostics.Conditional(\"ENABLED\")] static void Helper() { }", preprocessorSymbols: EnabledSymbols);
        Assert.That(supported.Invoke([]), Is.EqualTo(17));
        var supportedTree = supported.Compilation.SyntaxTrees.Single();
        var supportedSyntax = supportedTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var supportedOperation = supported.Compilation.GetSemanticModel(supportedTree).GetOperation(supportedSyntax)!;
        Assert.That(new CSharpInvocationEmissionPolicy(supported.Compilation).IsElided(supportedOperation), Is.False);
        var lowering = supported.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = supported.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }

    [Test]
    public void WholeEmptyParamsPreparationRejectsRealExplicitArgumentAndAcceptsCompilerEmptyArray()
    {
        using var explicitSubject = TypedProgramSubject.Create("int Target(int value) => Read(value); static int Read(int value) => value;");
        Assert.That(explicitSubject.Invoke([17]), Is.EqualTo(17));
        var explicitArgument = Argument(explicitSubject);
        Assert.That(explicitArgument.ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        var explicitSession = new TotalSourceCallSession(explicitSubject.Compilation, static _ => true, null, default);
        Assert.That(explicitSession.PrepareEmptyParamsArray(explicitArgument), Is.Null);
        Assert.That(explicitSession.ConstructionLimitExceeded, Is.False);
        using var emptySubject = TypedProgramSubject.Create("int Target() => Read(); static int Read(params int[] values) => values.Length;");
        Assert.That(emptySubject.Invoke([]), Is.EqualTo(0));
        var emptyArgument = Argument(emptySubject);
        Assert.That(emptyArgument.ArgumentKind, Is.EqualTo(ArgumentKind.ParamArray));
        Assert.That(TotalSourceCallSession.IsEmptyParamsArray(emptyArgument), Is.True);
        var emptySession = new TotalSourceCallSession(emptySubject.Compilation, static _ => true, null, default, ResolveArrayEmpty);
        var prepared = emptySession.PrepareEmptyParamsArray(emptyArgument);
        Assert.That(prepared, Is.Not.Null);
        Assert.That(prepared!.Value.Method.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_Array));
        Assert.That(prepared.Value.Method.Name, Is.EqualTo("Empty"));
        var rule = prepared.Value.Model.Apply([]);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(emptySession.ConstructionLimitExceeded, Is.False);
        var lowering = emptySubject.LowerSourceCalls(true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = emptySubject.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(0)));

        TotalScalarCallModel? ResolveArrayEmpty(IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            return method.IsStatic && method.ContainingType.SpecialType == SpecialType.System_Array && method.Name == "Empty" && method.Arity == 1 && method.Parameters.IsEmpty && CSharpOperationSemantics.IsReferenceDomain(method.ReturnType)
                ? new(0, _ => CSharpOperationSemantics.ArrayEmpty(emptySubject.Factory, new RoslynTypeMapper(emptySubject.Factory).GetTypeId(method.ReturnType))) : null;
        }
    }

    private static IArgumentOperation Argument(TypedProgramSubject subject)
    {
        System.ArgumentNullException.ThrowIfNull(subject);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        return ((IInvocationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!).Arguments.Single();
    }
}
