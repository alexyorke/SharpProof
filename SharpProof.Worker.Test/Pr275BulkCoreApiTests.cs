using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BulkCoreApiTests
{
    [Test]
    public void OperationAncestorsFollowsTheCompilerOwnedParentChainAndRejectsNull()
    {
        var compilation = Compile(
            "Pr275BulkAncestors",
            """
            public static class Fixture
            {
                private static int Identity(int value) => value;

                public static int Call(int value)
                {
                    return Identity(value);
                }
            }
            """).Compilation;
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var invocationSyntax = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var operation = model.GetOperation(invocationSyntax)!;
        var expected = new List<IOperation>();
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            expected.Add(parent);
        }

        AssertArgumentNull(
            "operation",
            () => OperationAncestors.Of(null!));
        Assert.That(OperationAncestors.Of(operation), Is.EqualTo(expected));
    }

    [Test]
    public void CallableSubsetClassifiesCompilerSymbolsAndDeclarationShapes()
    {
        var compilation = Compile(
            "Pr275BulkCallableSubset",
            """
            using System;
            using System.Collections.Generic;

            public static class Fixture
            {
                public static int Plain(int value) => value;

                public static void DynamicArgument(List<dynamic> values) { }

                public static void RefLikeArgument(Span<int> values) { }

                public static void Generic<T>(T value) { }

                public delegate int Callback(int value);
            }
            """).Compilation;
        var fixture = compilation.GetTypeByMetadataName("Fixture")!;
        var plain = fixture.GetMembers("Plain").OfType<IMethodSymbol>().Single();
        var plainDeclaration = (MethodDeclarationSyntax)plain.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var dynamicMethod = fixture.GetMembers("DynamicArgument").OfType<IMethodSymbol>().Single();
        var dynamicDeclaration = (MethodDeclarationSyntax)dynamicMethod.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var refLikeMethod = fixture.GetMembers("RefLikeArgument").OfType<IMethodSymbol>().Single();
        var refLikeDeclaration = (MethodDeclarationSyntax)refLikeMethod.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var genericMethod = fixture.GetMembers("Generic").OfType<IMethodSymbol>().Single();
        var genericDeclaration = (MethodDeclarationSyntax)genericMethod.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var callback = fixture.GetTypeMembers("Callback").Single();
        var callbackInvoke = callback.DelegateInvokeMethod!;
        var callbackDeclaration = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes()
            .OfType<DelegateDeclarationSyntax>()
            .Single();

        AssertArgumentNull(
            "method",
            () => CallableSubset.IsSupported(null!, plainDeclaration));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CallableSubset.IsSupported(plain, plainDeclaration), Is.True);
            Assert.That(CallableSubset.IsSupported(dynamicMethod, dynamicDeclaration), Is.False);
            Assert.That(CallableSubset.IsSupported(refLikeMethod, refLikeDeclaration), Is.False);
            Assert.That(CallableSubset.IsSupported(genericMethod, genericDeclaration), Is.False);
            Assert.That(CallableSubset.IsSupported(callbackInvoke, callbackDeclaration), Is.False);
        }
    }

    [Test]
    public void ExceptionTypeIdentityUsesActualNestedGenericAndArrayTypeArguments()
    {
        var compilation = Compile(
            "Pr275BulkExceptionIdentity",
            """
            using System;

            public class ExceptionBox<T>
            {
                public sealed class NestedException<U> : Exception { }
            }

            public static class Fixture
            {
                public static void Accept(
                    ExceptionBox<string[]>.NestedException<int> error) { }
            }
            """).Compilation;
        var method = compilation.GetTypeByMetadataName("Fixture")!
            .GetMembers("Accept")
            .OfType<IMethodSymbol>()
            .Single();
        var type = (INamedTypeSymbol)method.Parameters.Single().Type;
        var exceptionBase = compilation.GetTypeByMetadataName("System.Exception")!;
        var encoded = CompilerExceptionTypeIdentity.Encode(type);
        var hierarchy = CompilerExceptionTypeIdentity.EncodeHierarchy(type);

        AssertArgumentNull(
            "type",
            () => CompilerExceptionTypeIdentity.Encode(null!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(encoded, Does.Contain("::generic-argument-assemblies["));
            Assert.That(hierarchy, Does.Contain(encoded));
            Assert.That(
                hierarchy,
                Does.Contain(CompilerExceptionTypeIdentity.Encode(exceptionBase)));
            Assert.That(CompilerExceptionTypeIdentity.EncodeHierarchy(null), Is.Empty);
        }
    }

    [Test]
    public void AnalyzerSessionLazilyCachesSpecsAndRecognizesOrdinaryMetadataMethods()
    {
        var compilation = Compile(
            "Pr275BulkSession",
            """
            public static class Fixture
            {
                public static bool IsEmpty(string? value) =>
                    string.IsNullOrEmpty(value);
            }
            """).Compilation;
        var session = NewSession(compilation);
        var method = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("IsNullOrEmpty")
            .OfType<IMethodSymbol>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Parameters.Length == 1));

        Assert.That(session.HasCreatedApiSpecs, Is.False);
        var specs = session.ApiSpecs;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.HasCreatedApiSpecs, Is.True);
            Assert.That(session.ApiSpecs, Is.SameAs(specs));
            Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(session.HasPotentialCallPreconditions(method), Is.False);
            Assert.That(session.HasRejectedMetadataPrecondition(method), Is.False);
        }
    }

    [Test]
    public void EffectDiagnosticsPreserveAdvisoryOutcomesForRealSourceClaimsAndCalls()
    {
        var compilation = Compile(
            "Pr275BulkEffectClaims",
            """
            using System;
            using System.Collections.Generic;
            using SharpProof.Attributes;

            public sealed class Box
            {
                public int Value;
            }

            public struct LocalValue
            {
                public int Value;
            }

            public class Fixture
            {
                private static int state;
                private int receiverState;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void WriteStatic() => state = 1;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static object AllocateContract() => new object();

                [EffectContract(
                    SharpProofEffect.Synchronizes,
                    Capabilities = SharpProofCapability.Synchronization,
                    Complete = true)]
                public static void Synchronize(object gate)
                {
                    lock (gate) { }
                }

                [EffectContract(
                    SharpProofEffect.ReadsReceiverState |
                    SharpProofEffect.ReadsArgumentState |
                    SharpProofEffect.ReadsCapturedState |
                    SharpProofEffect.ReadsStaticState |
                    SharpProofEffect.ReadsAmbientState,
                    Complete = true)]
                public int ReadStatic(int value) => state + receiverState + value;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int WriteLocal()
                {
                    var local = 0;
                    local = 1;
                    return local;
                }

                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(NullReferenceException) },
                    Complete = true)]
                public static void WriteArgument(Box box) => box.Value = 1;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public void WriteReceiver(int value) => receiverState = value;

                [EffectContract(
                    SharpProofEffect.Throws | SharpProofEffect.Allocates,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void AddToList(List<int> values) => values.Add(1);

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void UnknownExternalCall() => Console.WriteLine(1);

                [ZeroAllocations]
                public static object Allocate() => new object();

                [EnforcePure]
                public static void PureWrite() => state = 2;

                [DoesNotThrow]
                public static void Throw(Exception error) => throw error;

                [AllowedExceptions(typeof(Exception))]
                public static void AllowedThrow(Exception error) => throw error;

                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void DeclaredThrow(Exception error) => throw error;

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void Lock(object gate)
                {
                    lock (gate) { }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static InvalidOperationException ConstructException() =>
                    new InvalidOperationException();

                [EffectContract(SharpProofEffect.Allocates, Complete = true)]
                public static InvalidOperationException ConstructExceptionWithAllowedAllocation() =>
                    new InvalidOperationException();

                public static int NoClaim(int value) => value + 1;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int NoViolation(int value) => value + 1;
            }
            """).Compilation;
        var session = NewSession(compilation);

        var noClaimMethod = GetMethod(compilation, "NoClaim");
        var noClaimDeclaration = noClaimMethod.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var nullInputDiagnostics = new List<Diagnostic>();
        AssertArgumentNull(
            "method",
            () => EffectContractDiagnostics.Analyze(
                null!,
                noClaimDeclaration,
                session,
                nullInputDiagnostics.Add,
                CancellationToken.None));
        AssertArgumentNull(
            "session",
            () => EffectContractDiagnostics.Analyze(
                noClaimMethod,
                noClaimDeclaration,
                null!,
                nullInputDiagnostics.Add,
                CancellationToken.None));

        var write = Analyze(compilation, session, "WriteStatic");
        var allocationContract = Analyze(compilation, session, "AllocateContract");
        var synchronizationClaim = Analyze(compilation, session, "Synchronize");
        var staticRead = Analyze(compilation, session, "ReadStatic");
        var localWrite = Analyze(compilation, session, "WriteLocal");
        var argumentWrite = Analyze(compilation, session, "WriteArgument");
        var receiverWrite = Analyze(compilation, session, "WriteReceiver");
        var listCall = Analyze(compilation, session, "AddToList");
        var unknownCall = Analyze(compilation, session, "UnknownExternalCall");
        var allocation = Analyze(compilation, session, "Allocate");
        var purity = Analyze(compilation, session, "PureWrite");
        var throwing = Analyze(compilation, session, "Throw");
        var allowedThrow = Analyze(compilation, session, "AllowedThrow");
        var declaredThrow = Analyze(compilation, session, "DeclaredThrow");
        var lockCall = Analyze(compilation, session, "Lock");
        var constructorCall = Analyze(compilation, session, "ConstructException");
        var constructorEffects = Analyze(compilation, session, "ConstructExceptionWithAllowedAllocation");
        var noClaim = Analyze(compilation, session, "NoClaim");
        var noViolation = Analyze(compilation, session, "NoViolation");

        AssertArgumentNull(
            "constraint",
            () => AdvisoryEffectClaims.FindViolation(
                EffectEvaluationContractKind.EffectContract,
                null!,
                [],
                compilation));
        AssertArgumentNull(
            "compilation",
            () => AdvisoryEffectClaims.FindViolation(
                EffectEvaluationContractKind.EffectContract,
                EffectClaimConstraint.Empty,
                [],
                null!));

        using (Assert.EnterMultipleScope())
        {
            AssertOutcomeWithDiagnostic(write, "SP0052", "WritesStaticState");
            AssertOutcomeWithDiagnostic(allocationContract, "SP0052", "Allocates");
            AssertOutcomeWithoutDiagnostic(synchronizationClaim);
            AssertOutcomeWithoutDiagnostic(staticRead);
            AssertOutcomeWithoutDiagnostic(localWrite);
            AssertOutcomeWithDiagnostic(argumentWrite, "SP0052", "WritesArgumentState");
            AssertOutcomeWithDiagnostic(receiverWrite, "SP0052", "WritesReceiverState");
            AssertOutcomeWithDiagnostic(listCall, "SP0052", "WritesReceiverState");
            AssertOutcomeWithDiagnostic(unknownCall, "SP0052", "may have undeclared effects");
            AssertOutcomeWithDiagnostic(allocation, "SP0045", "may allocate");
            AssertOutcomeWithDiagnostic(purity, "SP0002", "do not prove observable purity");
            AssertOutcomeWithDiagnostic(throwing, "SP0046", "may throw");
            AssertOutcomeWithoutDiagnostic(allowedThrow);
            AssertOutcomeWithoutDiagnostic(declaredThrow);
            AssertOutcomeWithDiagnostic(lockCall, "SP0016", "Synchronization");
            AssertOutcomeWithoutDiagnostic(constructorCall);
            AssertOutcomeWithoutDiagnostic(constructorEffects);
            Assert.That(noClaim.Outcome, Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(noClaim.Diagnostics, Is.Empty);
            Assert.That(noViolation.Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
            Assert.That(noViolation.Diagnostics, Is.Empty);
        }
    }

    [Test]
    public void NestedCallableControlAttributesUseTheirOwnCompilerSemanticModel()
    {
        var compilation = Compile(
            "Pr275BulkNestedControls",
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture
            {
                public static void Local()
                {
                    [SharpProofSuppress("")]
                    static void Nested() { }
                    Nested();
                }

                public static Action Lambda() =>
                    [SharpProofTrusted(" ")] () => { };
            }
            """).Compilation;
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var session = NewSession(compilation);
        var diagnostics = new List<Diagnostic>();
        var root = tree.GetRoot();
        var localFunction = root.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Single();
        var lambda = root.DescendantNodes()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .Single();
        var ordinaryMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Local"));

        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            localFunction,
            model,
            session,
            diagnostics.Add,
            CancellationToken.None);
        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            lambda,
            model,
            session,
            diagnostics.Add,
            CancellationToken.None);
        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            ordinaryMethod,
            model,
            session,
            diagnostics.Add,
            CancellationToken.None);

        Assert.That(diagnostics, Has.Count.EqualTo(2));
        Assert.That(
            diagnostics.All(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "SP0024")),
            Is.True);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture))),
            Has.All.Contain("expected a non-empty reason"));
    }

    [Test]
    public void MetadataControlAttributeReportsWithoutInventingASourceLocation()
    {
        var producer = Compile(
            "Pr275BulkMetadataControlProducer",
            """
            using SharpProof.Attributes;

            public static class ExternalFixture
            {
                [SharpProofSuppress("")]
                public static void Suppressed() { }
            }
            """);
        var reference = MetadataReference.CreateFromImage(producer.Image);
        var consumer = Compile(
            "Pr275BulkMetadataControlConsumer",
            "public static class Consumer { public static void Call() { } }",
            reference).Compilation;
        var method = consumer.GetTypeByMetadataName("ExternalFixture")!
            .GetMembers("Suppressed")
            .OfType<IMethodSymbol>()
            .Single();
        var session = NewSession(consumer);
        var diagnostics = new List<Diagnostic>();

        var suppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method,
            session,
            diagnostics.Add,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suppressed, Is.False);
            Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo("SP0024"));
            Assert.That(diagnostics.Single().Location.IsInSource, Is.False);
        }
    }

    private static AnalyzerSession NewSession(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
    }

    private static (CSharpCompilation Compilation, byte[] Image) Compile(
        string assemblyName,
        string source,
        MetadataReference? additionalReference = null)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(assemblyName, source);
        if (additionalReference is not null)
        {
            compilation = compilation.AddReferences(additionalReference);
        }

        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.That(
            emit.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)),
            Is.Empty);
        Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));
        return (compilation, stream.ToArray());
    }

    private static (AnalyzerSemanticOutcome Outcome, ImmutableArray<Diagnostic> Diagnostics)
        Analyze(CSharpCompilation compilation, AnalyzerSession session, string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(session);
        var method = GetMethod(compilation, methodName);
        var declaration = method.DeclaringSyntaxReferences.Single()
            .GetSyntax(CancellationToken.None);
        var diagnostics = new List<Diagnostic>();
        var outcome = EffectContractDiagnostics.Analyze(
            method,
            declaration,
            session,
            diagnostics.Add,
            CancellationToken.None);
        return (outcome, diagnostics.ToImmutableArray());
    }

    private static IMethodSymbol GetMethod(CSharpCompilation compilation, string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        return compilation.GetTypeByMetadataName("Fixture")!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
    }

    private static void AssertOutcomeWithDiagnostic(
        (AnalyzerSemanticOutcome Outcome, ImmutableArray<Diagnostic> Diagnostics) result,
        string id,
        string message)
    {
        System.ArgumentNullException.ThrowIfNull(id);
        System.ArgumentNullException.ThrowIfNull(message);
        Assert.That(result.Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(result.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(result.Diagnostics[0].Id, Is.EqualTo(id));
        Assert.That(
            result.Diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            Does.Contain(message));
    }

    private static void AssertOutcomeWithoutDiagnostic(
        (AnalyzerSemanticOutcome Outcome, ImmutableArray<Diagnostic> Diagnostics) result)
    {
        Assert.That(result.Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    private static void AssertArgumentNull(string parameterName, Action action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(parameterName);
        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }
}
