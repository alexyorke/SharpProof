using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalContractInvocationMaterializationAdditionalMatrixTests
{
    private static readonly BoundContractKind[] ThreeClauseKinds =
    [
        BoundContractKind.Requires,
        BoundContractKind.Assume,
        BoundContractKind.Ensures
    ];

    private const string BoundedDiagnostic = "[condition exceeds the display limit]";
    private static readonly BoundContractKind[] RequiresOnlyKinds = [BoundContractKind.Requires];
    private static readonly string[] OrderedDiagnosticTexts =
    [
        BoundedDiagnostic,
        "second",
        "Contract.Result<bool>() == Contract.Old(first)"
    ];

    private const string SourceText = """
        using SharpProof.Attributes;

        public static class Ordinary {
            public static int One(int value) => value;
            public static bool One(bool value) => value;
            public static bool Opaque(bool value) => value;
        }

        public static class DirectCases {
            public static bool AtomicPrefix(bool input) {
                Contract.Requires(input);
                Contract.Assume(!input);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(input));
                bool local = input;
                _ = Ordinary.One(local);
                return input;
            }

            public static bool UnsupportedNonRequires(bool input) {
                Contract.Requires(input);
                Contract.Assume(Ordinary.Opaque(input));
                Contract.Ensures(Ordinary.Opaque(input));
                return input;
            }

        }

        public static class StaticTarget {
            public static bool Run(bool value) => value;
        }
        [ContractFor(typeof(StaticTarget))]
        public static class StaticContracts {
            public static bool Run(bool value) {
                Contract.Requires(value);
                return value;
            }
        }

        public sealed class InstanceTarget {
            public bool Run(bool value) => value;
        }
        [ContractFor(typeof(InstanceTarget))]
        public static class InstanceContracts {
            public static bool Run(InstanceTarget receiver, bool value) {
                Contract.Requires(value);
                return value;
            }
        }

        public static class TwoParameterTarget {
            public static bool Run(bool first, bool second) => first && second;
        }
        [ContractFor(typeof(TwoParameterTarget))]
        public static class TwoParameterContracts {
            public static bool Run(bool first, bool second) {
                Contract.Requires(first && second);
                return first && second;
            }
        }
        """;

    [Test]
    public void RequiresOnlySkipsUnsupportedActualAssumeAndEnsuresBeforeLowering()
    {
        var compilation = Compile(SourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, "DirectCases", "UnsupportedNonRequires");
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var candidates = Candidates(resolution);
        Assert.That(candidates.Select(static candidate => candidate.Kind), Is.EqualTo(ThreeClauseKinds));
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var intrinsics = new ContractIntrinsicValidator(api);

        var unsupportedAssume = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, target), api, intrinsics, resolution,
            OfKind(candidates, BoundContractKind.Assume), requiresOnly: false);
        Assert.That(unsupportedAssume.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(unsupportedAssume.Clauses, Is.Empty);

        var unsupportedEnsures = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, target), api, intrinsics, resolution,
            OfKind(candidates, BoundContractKind.Ensures), requiresOnly: false);
        Assert.That(unsupportedEnsures.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(unsupportedEnsures.Clauses, Is.Empty);

        var filtered = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, target), api, intrinsics, resolution,
            candidates, requiresOnly: true);
        Assert.That(filtered.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(filtered.Clauses, Has.Length.EqualTo(1));
        Assert.That(filtered.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(filtered.Clauses[0].SourceSyntax!.Span,
            Is.EqualTo(candidates[0].Invocation.Syntax.Span));

        var publicRequires = new ContractBinder(compilation, factory).BindTotalRequires(
            new TotalLoweringContext(factory, target));
        Assert.That(publicRequires.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicRequires.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(RequiresOnlyKinds));
    }

    [Test]
    public void LocalVariableRawCandidateRejectsAtomicallyAfterActualCanonicalPrefix()
    {
        var compilation = Compile(SourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, "DirectCases", "AtomicPrefix");
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var candidates = Candidates(resolution);
        Assert.That(candidates.Select(static candidate => candidate.Kind), Is.EqualTo(ThreeClauseKinds));
        var rawInvocation = FindOrdinaryInvocation(resolution, "One");
        Assert.That(rawInvocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(rawInvocation.Arguments[0].Value, Is.InstanceOf<ILocalReferenceOperation>());
        Assert.That(rawInvocation.Arguments[0].Value.Type?.SpecialType,
            Is.EqualTo(SpecialType.System_Boolean));

        var api = ContractApiSymbols.TryCreate(compilation)!;
        Assert.That(api.Clauses.GetClauseKind(rawInvocation.TargetMethod), Is.Null);
        var publicResult = new ContractBinder(compilation, factory).BindTotal(
            new TotalLoweringContext(factory, target));
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(ThreeClauseKinds));

        var withRawLocal = candidates.Add(
            new ContractInvocationCandidate(BoundContractKind.Requires, rawInvocation));
        var materialized = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, target), api, new ContractIntrinsicValidator(api),
            resolution, withRawLocal, requiresOnly: false);
        Assert.That(materialized.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(materialized.Clauses, Is.Empty);
    }

    [Test]
    public void ActualStaticAndInstanceSourceFramesRejectCrossWiredContexts()
    {
        var compilation = Compile(SourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var staticTarget = Method(compilation, "StaticTarget", "Run");
        var instanceTarget = Method(compilation, "InstanceTarget", "Run");
        Assert.That(staticTarget.IsStatic, Is.True);
        Assert.That(instanceTarget.IsStatic, Is.False);

        var resolver = EffectiveContractSourceResolver.ForCompilation(compilation);
        var staticResolution = resolver.Resolve(staticTarget);
        var instanceResolution = resolver.Resolve(instanceTarget);
        Assert.That(staticResolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(instanceResolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(staticResolution.UsesCompanion, Is.True);
        Assert.That(instanceResolution.UsesCompanion, Is.True);
        Assert.That(instanceResolution.Source.Parameters.Length, Is.EqualTo(2));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            instanceResolution.Source.Parameters[0].Type, instanceTarget.ContainingType), Is.True);

        var api = ContractApiSymbols.TryCreate(compilation)!;
        var intrinsics = new ContractIntrinsicValidator(api);
        var staticCandidates = Candidates(staticResolution);
        var instanceCandidates = Candidates(instanceResolution);
        var properStatic = new ContractBinder(compilation, factory).BindTotal(
            new TotalLoweringContext(factory, staticTarget));
        var properInstance = new ContractBinder(compilation, factory).BindTotal(
            new TotalLoweringContext(factory, instanceTarget));
        Assert.That(properStatic.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(properInstance.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(properStatic.Clauses, Has.Length.EqualTo(1));
        Assert.That(properInstance.Clauses, Has.Length.EqualTo(1));

        var staticSourceOnInstanceTarget = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, instanceTarget), api, intrinsics,
            staticResolution, staticCandidates, requiresOnly: false);
        Assert.That(staticSourceOnInstanceTarget.Failure,
            Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
        Assert.That(staticSourceOnInstanceTarget.Clauses, Is.Empty);

        var instanceSourceOnStaticTarget = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, staticTarget), api, intrinsics,
            instanceResolution, instanceCandidates, requiresOnly: false);
        Assert.That(instanceSourceOnStaticTarget.Failure,
            Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
        Assert.That(instanceSourceOnStaticTarget.Clauses, Is.Empty);
    }

    [Test]
    public void ActualCompanionParameterCountMismatchRejectsAsModuleInput()
    {
        var compilation = Compile(SourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sourceTarget = Method(compilation, "StaticTarget", "Run");
        var destinationTarget = Method(compilation, "TwoParameterTarget", "Run");
        var resolver = EffectiveContractSourceResolver.ForCompilation(compilation);
        var sourceResolution = resolver.Resolve(sourceTarget);
        var destinationResolution = resolver.Resolve(destinationTarget);
        Assert.That(sourceResolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(destinationResolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(sourceResolution.UsesCompanion, Is.True);
        Assert.That(destinationResolution.UsesCompanion, Is.True);
        Assert.That(sourceResolution.Source.Parameters.Length, Is.EqualTo(1));
        Assert.That(destinationResolution.Source.Parameters.Length, Is.EqualTo(2));
        Assert.That(destinationTarget.Parameters.Length, Is.EqualTo(2));

        var properDestination = new ContractBinder(compilation, factory).BindTotal(
            new TotalLoweringContext(factory, destinationTarget));
        Assert.That(properDestination.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(properDestination.Clauses, Has.Length.EqualTo(1));

        var api = ContractApiSymbols.TryCreate(compilation)!;
        var mismatched = TotalContractInvocationMaterialization.Bind(
            new TotalLoweringContext(factory, destinationTarget), api,
            new ContractIntrinsicValidator(api), sourceResolution,
            Candidates(sourceResolution), requiresOnly: false);
        Assert.That(mismatched.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
        Assert.That(mismatched.Clauses, Is.Empty);
    }

    [Test]
    public void CompilerClauseMetadataKeepsOrderOwnershipAndBoundedDiagnosticText()
    {
        var comment = new string('x', 2200);
        var sourceText = $$"""
            using SharpProof.Attributes;
            public static class OrderedTarget {
                public static bool Run(bool first, bool second) {
                    Contract.Requires(first && /*{{comment}}*/ true);
                    Contract.Assume(second);
                    Contract.Ensures(Contract.Result<bool>() == Contract.Old(first));
                    return first || second;
                }
            }
            """;
        var compilation = Compile(sourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, "OrderedTarget", "Run");
        var context = new TotalLoweringContext(factory, target);
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var candidates = Candidates(resolution);
        Assert.That(candidates.Select(static candidate => candidate.Kind), Is.EqualTo(ThreeClauseKinds));
        Assert.That(candidates[0].Invocation.Arguments[0].Value.Syntax.Span.Length,
            Is.GreaterThan(2048));

        var api = ContractApiSymbols.TryCreate(compilation)!;
        var result = TotalContractInvocationMaterialization.Bind(
            context, api, new ContractIntrinsicValidator(api), resolution, candidates,
            requiresOnly: false);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(ThreeClauseKinds));
        Assert.That(result.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.DiagnosticText)),
            Is.EqualTo(OrderedDiagnosticTexts));

        for (var index = 0; index < result.Clauses.Length; index++)
        {
            var clause = result.Clauses[index];
            var invocation = candidates[index].Invocation;
            var operation = factory.GetOperationInfo(clause.SourceOperation);
            Assert.That(operation.SourceSpan, Is.Not.Null);
            var span = operation.SourceSpan!;
            var location = invocation.Syntax.GetLocation().GetLineSpan().StartLinePosition;
            Assert.That(span.Start, Is.EqualTo(invocation.Syntax.SpanStart));
            Assert.That(span.Length, Is.EqualTo(invocation.Syntax.Span.Length));
            Assert.That(span.Line, Is.EqualTo(location.Line + 1));
            Assert.That(span.Column, Is.EqualTo(location.Character + 1));
            Assert.That(span.Document, Is.EqualTo(string.IsNullOrEmpty(invocation.Syntax.SyntaxTree.FilePath)
                ? "source" : invocation.Syntax.SyntaxTree.FilePath));
            Assert.That(operation.Description.HasValue, Is.True);
            Assert.That(factory.GetString(operation.Description!.Value),
                Is.EqualTo(invocation.Kind + "@" + invocation.Syntax.SpanStart));
            Assert.That(clause.SourceSyntax, Is.Not.Null);
            Assert.That(clause.SourceSyntax!.SyntaxTree, Is.SameAs(invocation.Syntax.SyntaxTree));
            Assert.That(clause.SourceSyntax.Span, Is.EqualTo(invocation.Syntax.Span));
            Assert.That(clause.Evidence, Is.EqualTo(BoundContractEvidence.CompilerBoundInvocation));
        }
    }

    [Test]
    public void RequiredMaterializationArgumentsRejectNull()
    {
        var compilation = Compile(SourceText);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, "StaticTarget", "Run");
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var candidates = Candidates(resolution);
        var context = new TotalLoweringContext(factory, target);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var intrinsics = new ContractIntrinsicValidator(api);

        var contextException = Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = TotalContractInvocationMaterialization.Bind(
                null!, api, intrinsics, resolution, candidates, requiresOnly: false);
        }));
        Assert.That(contextException!.ParamName, Is.EqualTo("context"));

        var apiException = Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = TotalContractInvocationMaterialization.Bind(
                context, null!, intrinsics, resolution, candidates, requiresOnly: false);
        }));
        Assert.That(apiException!.ParamName, Is.EqualTo("api"));

        var intrinsicException = Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = TotalContractInvocationMaterialization.Bind(
                context, api, null!, resolution, candidates, requiresOnly: false);
        }));
        Assert.That(intrinsicException!.ParamName, Is.EqualTo("intrinsics"));

        var resolutionException = Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = TotalContractInvocationMaterialization.Bind(
                context, api, intrinsics, null!, candidates, requiresOnly: false);
        }));
        Assert.That(resolutionException!.ParamName, Is.EqualTo("resolution"));
    }

    private static ImmutableArray<ContractInvocationCandidate> Candidates(
        EffectiveContractSourceResolution resolution)
    {
        System.ArgumentNullException.ThrowIfNull(resolution);
        return [.. resolution.Inventory.Clauses
            .Where(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (occurrence.IsValid))
            .Select(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (new ContractInvocationCandidate(
                occurrence.Kind, occurrence.Invocation)))];
    }

    private static ImmutableArray<ContractInvocationCandidate> OfKind(
        ImmutableArray<ContractInvocationCandidate> candidates,
        BoundContractKind kind)
    {

        if (candidates.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(candidates));
        }
        return [.. candidates.Where(candidate => candidate.Kind == kind)];
    }

    private static IInvocationOperation FindOrdinaryInvocation(
        EffectiveContractSourceResolution resolution,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(resolution);
        return resolution.Inventory.ImplementationBody!
            .DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.TargetMethod.ContainingType.Name == "Ordinary" &&
                invocation.TargetMethod.Name == methodName));
    }

    private static IMethodSymbol Method(
        CSharpCompilation compilation,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var method = compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        return method;
    }

    private static CSharpCompilation Compile(string sourceText)
    {
        System.ArgumentNullException.ThrowIfNull(sourceText);
        var compilation = TestCompilation.Create(
            "TotalContractInvocationMaterializationAdditionalMatrix",
            sourceText);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }
}
