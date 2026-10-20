using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class ConstrainedComparableDomainTests
{
    [Test]
    public void StaticCallsAndConstantFieldsDoNotExposeManagedReceiverLocations()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { const int Value = 17; public static int Target(int value) => Other(value) + Value.CompareTo(23); static int Other(int value) => value; }");
        var compilation = CSharpCompilation.Create("ComparableCopies", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var invocation = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(context.IsVariableComparableReceiver(invocation), Is.False);
            Assert.That(context.ManagedValueReceiverLocation(invocation), Is.Null);
        }
    }

    [Test]
    public void ConstructedComparableInstanceFramesRetainTheirScalarParameterDomain()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() => new Box<int>().Compare(23); public sealed class Box<T> where T : System.IComparable<T> { public T Value; public int Compare(T replacement) => Value.CompareTo(Value = replacement); } }");
        var compilation = CSharpCompilation.Create("ComparableInstance", [tree], TestMetadataReferences.Platform.Add(MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.Contract).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        var callerGraph = ControlFlowGraph.Create(syntax, model)!;
        var invocation = callerGraph.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) :
            block.Operations.Concat(block.BranchValue is { } branch ? ImmutableArray.Create(branch) : ImmutableArray<IOperation>.Empty)).SelectMany(operation => operation is null
                ? throw new ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf()).OfType<IInvocationOperation>().Single();
        bool PrepareCallee(TotalLoweringContext frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            var binding = new ContractBinder(compilation, frame.Factory).BindTotalRequires(frame);
            Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
            return frame.HasScalarSignature;
        }
        var session = TotalSourceCallSession.CreatePrepared(compilation, PrepareCallee, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, invocation.Instance, invocation.Arguments, out var callee, out var graph), Is.True);
        Assert.That(callee!.KnownScalarType(callee.Target.Parameters[0].Type)?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(graph, Is.Not.Null);
    }

    [TestCase(SpecialType.System_SByte, true)]
    [TestCase(SpecialType.System_Byte, true)]
    [TestCase(SpecialType.System_Int16, true)]
    [TestCase(SpecialType.System_UInt16, true)]
    [TestCase(SpecialType.System_Char, true)]
    [TestCase(SpecialType.System_Int32, true)]
    [TestCase(SpecialType.System_UInt32, true)]
    [TestCase(SpecialType.System_Int64, true)]
    [TestCase(SpecialType.System_UInt64, true)]
    [TestCase(SpecialType.System_Boolean, false)]
    [TestCase(SpecialType.System_Single, false)]
    [TestCase(SpecialType.System_Double, false)]
    [TestCase(SpecialType.System_Decimal, false)]
    [TestCase(SpecialType.System_Object, false)]
    [TestCase(SpecialType.System_IntPtr, false)]
    public void OnlyAuditedIntegerTypesSupplyConstrainedComparisonSemantics(SpecialType element, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y); }");
        var compilation = CSharpCompilation.Create("ComparableDomain", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = model.GetDeclaredSymbol(syntax)!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
        foreach (var parameter in method.Parameters)
        { context.SpecializeScalarParameter(parameter, compilation.GetSpecialType(element)); }
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.IsGenericComparableDeclaration(invocation.TargetMethod), Is.True);
        Assert.That(context.IsConstrainedIntegerComparable(invocation), Is.EqualTo(expected));
        Assert.That(new RoslynTotalExpressionLowerer(context).LowerClause(invocation).Classification.IsExact, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnboundOrInterfaceReceiversDoNotGainIntegerImplementationFacts(bool throughInterface)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target<T>(" + (throughInterface ? "System.IComparable<T>" : "T") + " x, T y) where T : System.IComparable<T> => x.CompareTo(y); }");
        var compilation = CSharpCompilation.Create("ComparableDispatch", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = model.GetDeclaredSymbol(syntax)!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
        if (throughInterface)
        {
            context.SpecializeTypeParameter(method.TypeParameters[0], compilation.GetSpecialType(SpecialType.System_Int32));
            context.RefreshSourceBindingTypes();
        }
        Assert.That(context.IsConstrainedIntegerComparable((IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!), Is.False);
    }

    [Test]
    public void SourceDefinedComparableLookalikesCannotSupplyFrameworkSemantics()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System { public interface IComparable<T> { int CompareTo(T value); } } public static class Probe { public static int Target<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y); }");
        var compilation = CSharpCompilation.Create("UserComparable", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(CSharpOperationSemantics.IsGenericComparableDeclaration(invocation.TargetMethod), Is.False);
    }

    [Test]
    public void ConstrainedComparableCallsRetainTheTypeParameterReceiver()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y); }");
        var compilation = CSharpCompilation.Create("ComparableReceiver", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.OriginalDefinition.GetDocumentationCommentId(), Is.EqualTo("M:System.IComparable`1.CompareTo(`0)"));
        Assert.That(invocation.Instance!.Type!.TypeKind, Is.EqualTo(TypeKind.TypeParameter), invocation.Instance.Kind.ToString());
    }
}
