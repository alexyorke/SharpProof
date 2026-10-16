using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275BulkFrontendApiTests
{
    private static readonly IrExceptionKind[] ExpectedShortArrayFaults =
    {
        IrExceptionKind.NullReference,
        IrExceptionKind.IndexOutOfRange
    };

    private const string OpenTypeSource = """
        internal sealed class OpenTypeContainer<T>
        {
            internal sealed class Nested { }
        }
        """;

    private const string DelegateSource = """
        public sealed class DelegateReceiver
        {
            internal void Run() { }
            internal static void RunStatic() { }
        }
        """;

    private const string EventSource = """
        internal sealed class EventWithoutInitializer
        {
            internal event System.Action? Changed;
            internal EventWithoutInitializer() { }
        }

        internal sealed class EventWithInitializer
        {
            internal event System.Action? Changed = null;
            internal EventWithInitializer() { }
        }
        """;

    [TestCase("bool Target<T>(T value) => value is T[];", true)]
    [TestCase("bool Target<T>(T value) => value is System.Collections.Generic.List<T>;", true)]
    [TestCase("bool Target<T>(T value) => value is OpenTypeContainer<T>.Nested;", true)]
    [TestCase("bool Target<T>(T value) => value is System.Collections.Generic.Dictionary<System.Collections.Generic.List<int>, System.Collections.Generic.List<int>>;", false)]
    [TestCase("bool Target<T>(T value) => value is var _;", false)]
    public void GenericTypeTestsUseCompilerOwnedTypesForAllocationClassification(
        string targetMember,
        bool expectedMayAllocate)
    {
        using var subject = TypedProgramSubject.Create(targetMember, OpenTypeSource);
        var operation = TargetExpression(subject);

        var actualMayAllocate = CSharpOperationSemantics.OpaqueTypeTestMayAllocate(
            operation,
            subject.Compilation);

        Assert.That(actualMayAllocate, Is.EqualTo(expectedMayAllocate));
    }

    [Test]
    public void CompiledShortArrayAccessUsesTheScalarIndexRules()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int[] values, short index) => ((values))[index];");
        var method = TargetMethod(subject);
        var syntax = method.DescendantNodes()
            .OfType<ElementAccessExpressionSyntax>()
            .Single();
        var model = subject.Compilation.GetSemanticModel(method.SyntaxTree);
        var operation = model.GetOperation(syntax);

        Assert.That(operation, Is.InstanceOf<IArrayElementReferenceOperation>());
        var access = (IArrayElementReferenceOperation)operation!;
        Assert.That(CSharpOperationSemantics.IsModeledElementAccess(access), Is.True);

        var lowered = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(access);
        Assert.That(lowered.Classification.IsExact, Is.True, lowered.Classification.Abstention.ToString());

        var receiver = subject.Factory.Variable(
            subject.Context.Parameters.Single(binding => binding.Parameter.Ordinal == 0).Current);
        var index = subject.Factory.Variable(
            subject.Context.Parameters.Single(binding => binding.Parameter.Ordinal == 1).Current);
        var guard = CSharpOperationSemantics.ElementGuard(
            subject.Factory,
            receiver,
            index,
            outside: null);

        Assert.That(guard.Classification.IsExact, Is.True);
        Assert.That(
            guard.Throws.Select(static fault => fault.Kind),
            Is.EqualTo(ExpectedShortArrayFaults));
    }

    [Test]
    public void FreshArrayReceiverUnwrapsParenthesesAndRejectsMissingCompilation()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target() { var values = new int[1]; ((values))[0] = 3; return values[0]; }");
        var method = TargetMethod(subject);
        var access = method.DescendantNodes()
            .OfType<ElementAccessExpressionSyntax>()
            .Single(static element => element.Expression is ParenthesizedExpressionSyntax);
        var receiver = access.Expression;

        Assert.That(
            CSharpOperationSemantics.IsFreshReceiver(receiver, subject.Compilation),
            Is.True);
        Assert.That(
            CSharpOperationSemantics.IsFreshReceiver(receiver, compilation: null),
            Is.False);
    }

    [Test]
    public void NullableUnwrapConversionIsClassifiedAsThrowingFromItsBoundOperation()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int? value) => (int)value;");
        var operation = TargetExpression(subject);

        Assert.That(operation, Is.InstanceOf<IConversionOperation>());
        var conversion = (IConversionOperation)operation;
        Assert.That(CSharpOperationSemantics.BuiltInOperationMayThrow(conversion), Is.True);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(conversion), Is.True);
    }

    [Test]
    public void RealStaticAndInstanceMethodReferencesKeepTheirThrowClassification()
    {
        using var instanceSubject = TypedProgramSubject.Create(
            "System.Action Target(DelegateReceiver value) => value.Run;",
            DelegateSource);
        var instanceReference = TargetExpression(instanceSubject)
            .DescendantsAndSelf()
            .OfType<IMethodReferenceOperation>()
            .Single();

        Assert.That(instanceReference.Method.IsStatic, Is.False);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(instanceReference), Is.True);

        using var staticSubject = TypedProgramSubject.Create(
            "System.Action Target() => DelegateReceiver.RunStatic;",
            DelegateSource);
        var staticReference = TargetExpression(staticSubject)
            .DescendantsAndSelf()
            .OfType<IMethodReferenceOperation>()
            .Single();

        Assert.That(staticReference.Method.IsStatic, Is.True);
        Assert.That(CSharpOperationSemantics.OperationMayThrow(staticReference), Is.False);
    }

    [Test]
    public void FloatingPointEqualityRetainsItsExplicitUnsupportedClassification(
        [Values("float", "double")] string scalarType)
    {
        using var subject = TypedProgramSubject.Create(
            $"bool Target({scalarType} left, {scalarType} right) => left == right;");
        var operation = TargetExpression(subject);
        var lowered = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);

        Assert.That(
            lowered.Classification.Abstention,
            Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void PlainConstructorPolicyReadsFieldLikeEventInitializers()
    {
        using var subject = TypedProgramSubject.Create("int Target() => 0;", EventSource);

        Assert.That(IsPlainConstructor(subject, "EventWithoutInitializer"), Is.True);
        Assert.That(IsPlainConstructor(subject, "EventWithInitializer"), Is.False);
    }

    [Test]
    public void ExplicitDelegateErasureUsesTheBoundCreationAndParenthesizedValue()
    {
        using var subject = TypedProgramSubject.Create(
            "void Target() { System.Action unused = (((new System.Action(DelegateReceiver.RunStatic)))); }",
            DelegateSource);
        var method = TargetMethod(subject);
        var model = subject.Compilation.GetSemanticModel(method.SyntaxTree);
        var body = model.GetOperation(method.Body!);
        var creation = body!
            .DescendantsAndSelf()
            .OfType<IDelegateCreationOperation>()
            .Single();

        Assert.That(CSharpOperationSemantics.IsExplicitDelegateCreation(creation), Is.True);
        Assert.That(CSharpOperationSemantics.DelegateValueIsErased(creation, compilation: null), Is.False);
        Assert.That(CSharpOperationSemantics.DelegateValueIsErased(creation, subject.Compilation), Is.True);
    }

    [Test]
    public void IrIntegerRangeHelperMatchesCompilerSpecialTypesAndRejectsNarrowRanges()
    {
        using var subject = TypedProgramSubject.Create("int Target() => 0;");
        var compilation = subject.Compilation;

        AssertRange(
            compilation.GetSpecialType(SpecialType.System_Int32).SpecialType,
            int.MinValue,
            int.MaxValue);
        AssertRange(
            compilation.GetSpecialType(SpecialType.System_Int64).SpecialType,
            long.MinValue,
            long.MaxValue);

        var hasNarrowRange = CSharpOperationSemantics.TryGetIrIntegerRange(
            compilation.GetSpecialType(SpecialType.System_Int16).SpecialType,
            out var minimum,
            out var maximum);
        Assert.That(hasNarrowRange, Is.False);
        Assert.That(minimum, Is.Zero);
        Assert.That(maximum, Is.Zero);
    }

    [Test]
    public void CachedReferencedTypeApiEnumeratesSourceTypesAndHonorsCancellation()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target() => 0;",
            "namespace CachedApi { internal sealed class Outer { internal sealed class Nested { } } }");

        var typeNames = ReferencedTypeSymbols.GetAllCached(subject.Compilation)
            .Select(static type => type.ToDisplayString())
            .ToArray();
        Assert.That(typeNames, Does.Contain("CachedApi.Outer"));
        Assert.That(typeNames, Does.Contain("CachedApi.Outer.Nested"));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            _ = ReferencedTypeSymbols.GetAllCached(
                    subject.Compilation,
                    cancellation.Token)
                .ToArray();
        }));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void ReferencedTypeApisRejectNullInputsAtEnumeration()
    {
        AssertArgumentNull(
            (Action)(() =>
            {
                _ = ReferencedTypeSymbols.GetAll(null!).ToArray();
            }),
            "compilation");
        AssertArgumentNull(
            (Action)(() =>
            {
                _ = ReferencedTypeSymbols.GetAllCached(null!).ToArray();
            }),
            "compilation");

        using var subject = TypedProgramSubject.Create("int Target() => 0;");
        AssertArgumentNull(
            (Action)(() =>
            {
                _ = ReferencedTypeSymbols.GetAllCached(
                        subject.Compilation,
                        referencedAssembly: null!)
                    .ToArray();
            }),
            "referencedAssembly");
    }

    [Test]
    public void ProgramAbstentionRequiresARealOperationAndAReason()
    {
        var operation = new IrFactory().CreateOperation("frontend api fixture");

        var missingOperation = Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new FrontendProgramAbstention(
                default(OperationId),
                FrontendAbstention.UnsupportedType);
        }));
        Assert.That(missingOperation!.ParamName, Is.EqualTo("operation"));

        var missingReason = Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new FrontendProgramAbstention(
                operation,
                FrontendAbstention.None);
        }));
        Assert.That(missingReason!.ParamName, Is.EqualTo("reason"));

        var valid = new FrontendProgramAbstention(
            operation,
            FrontendAbstention.UnsupportedType);
        Assert.That(valid.Operation, Is.EqualTo(operation));
        Assert.That(valid.Reason, Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void NonNullLiteralRequiresItsCompilerType()
    {
        using var subject = TypedProgramSubject.Create("int Target() => 7;");
        var int32 = subject.Compilation.GetSpecialType(SpecialType.System_Int32);
        var factory = new IrFactory();

        _ = CSharpOperationSemantics.Literal(factory, int32, value: 7);

        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            _ = CSharpOperationSemantics.Literal(factory, type: null, value: 7);
        }));

        Assert.That(exception!.ParamName, Is.EqualTo("type"));
    }

    private static MethodDeclarationSyntax TargetMethod(TypedProgramSubject subject)
    {
        var tree = subject.Compilation.SyntaxTrees.Single();
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Target");
    }

    private static IOperation TargetExpression(TypedProgramSubject subject)
    {
        var method = TargetMethod(subject);
        var model = subject.Compilation.GetSemanticModel(method.SyntaxTree);
        return model.GetOperation(method.ExpressionBody!.Expression)!;
    }

    private static bool IsPlainConstructor(
        TypedProgramSubject subject,
        string typeName)
    {
        var type = subject.Compilation.GetTypeByMetadataName(typeName)!;
        var constructor = type.InstanceConstructors.Single();
        return CSharpOperationSemantics.IsPlainConstructor(
            constructor,
            CancellationToken.None);
    }

    private static void AssertRange(
        SpecialType type,
        long expectedMinimum,
        long expectedMaximum)
    {
        var found = CSharpOperationSemantics.TryGetIrIntegerRange(
            type,
            out var minimum,
            out var maximum);
        Assert.That(found, Is.True);
        Assert.That(minimum, Is.EqualTo(expectedMinimum));
        Assert.That(maximum, Is.EqualTo(expectedMaximum));
    }

    private static void AssertArgumentNull(Action action, string parameterName)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }
}
