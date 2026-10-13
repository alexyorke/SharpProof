using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275SourceCallApiCoverageTests
{
    private static readonly CSharpCompilation Compilation = TestCompilation.Create(
        "Pr275SourceCallApi", ("Subject.cs", """
            internal static class Subject {
                internal static void Caller() { }
                internal static void Callee() { }
            }
            """));

    private static bool PrepareCallee(TotalLoweringContext context)
    {
        return new ContractBinder(Compilation, context.Factory).BindTotalRequires(context).IsSuccess;
    }

    private static TotalSourceCallSession CreateSession()
    {
        return new(Compilation, PrepareCallee, null, default);
    }

    [Test]
    public void SourceSessionConstructionRequiresItsCompilationAndPreparationCallback()
    {
        var compilationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new TotalSourceCallSession(null!, PrepareCallee, null, default)));
        var callbackError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new TotalSourceCallSession(Compilation, null!, null, default)));
        var modelError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new TotalScalarCallModel(0, null!)));

        Assert.That(compilationError!.ParamName, Is.EqualTo("compilation"));
        Assert.That(callbackError!.ParamName, Is.EqualTo("prepareCallee"));
        Assert.That(modelError!.ParamName, Is.EqualTo("apply"));
    }

    [Test]
    public void ConstantAdmissionRequiresACompilerOperation()
    {
        var fieldError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CompilerConstantAdmission.IsCatalogIntegerBoundary(null!)));
        var negationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CompilerConstantAdmission.IsLiteralIntegerNegation(null!)));

        Assert.That(fieldError!.ParamName, Is.EqualTo("operation"));
        Assert.That(negationError!.ParamName, Is.EqualTo("operation"));
    }

    [TestCase("scalar call", "invocation")]
    [TestCase("enter", "method")]
    [TestCase("active", "method")]
    [TestCase("prepare IL", "method")]
    [TestCase("enter IL", "method")]
    [TestCase("empty params", "argument")]
    public void SourceSessionReportsMissingOperationOrMethod(string api, string expectedParameter)
    {
        var session = CreateSession();
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            switch (api)
            {
                case "scalar call":
                    _ = session.PrepareScalarCall(null!);
                    break;
                case "enter":
                    _ = session.Enter(null!);
                    break;
                case "active":
                    _ = session.IsActive(null!);
                    break;
                case "prepare IL":
                    _ = session.PrepareIl(null!);
                    break;
                case "enter IL":
                    _ = session.EnterIl(null!);
                    break;
                case "empty params":
                    _ = TotalSourceCallSession.IsEmptyParamsArray(null!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(api));
            }
        }));

        Assert.That(exception!.ParamName, Is.EqualTo(expectedParameter));
    }

    [TestCase("caller")]
    [TestCase("method")]
    [TestCase("arguments")]
    public void SourceFramePreparationRequiresItsContextMethodAndArgumentArray(string missing)
    {
        var method = Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Callee")
            .OfType<IMethodSymbol>().Single();
        var caller = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
        var session = CreateSession();
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = session.TryPrepare(missing == "caller" ? null! : caller,
                missing == "method" ? null! : method, null,
                missing == "arguments" ? default : ImmutableArray<IArgumentOperation>.Empty,
                out _, out _)));

        Assert.That(exception!.ParamName, Is.EqualTo(missing));
    }

    [TestCase("declaration")]
    [TestCase("method")]
    [TestCase("model")]
    public void SourceGraphCreationRequiresItsRoslynInputs(string missing)
    {
        var method = Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Callee")
            .OfType<IMethodSymbol>().Single();
        var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
        var model = Compilation.GetSemanticModel(declaration.SyntaxTree);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = TotalSourceCallSession.CreateGraph(missing == "declaration" ? null! : declaration,
                missing == "method" ? null! : method, missing == "model" ? null! : model, default)));

        Assert.That(exception!.ParamName, Is.EqualTo(missing));
    }

    [Test]
    public void ASourceMethodIsActiveOnlyBetweenEnterAndLeave()
    {
        var method = Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Callee")
            .OfType<IMethodSymbol>().Single();
        var session = CreateSession();

        Assert.That(session.IsActive(method), Is.False);
        Assert.That(session.Enter(method), Is.True);
        Assert.That(session.Enter(method), Is.False);
        Assert.That(session.IsActive(method), Is.True);
        session.Leave(method);
        Assert.That(session.IsActive(method), Is.False);
        Assert.That(session.Enter(method), Is.True);
    }

    [Test]
    public void ARealSourceCallProducesItsFrameAndControlFlowGraph()
    {
        var type = Compilation.GetTypeByMetadataName("Subject")!;
        var callerMethod = type.GetMembers("Caller").OfType<IMethodSymbol>().Single();
        var callee = type.GetMembers("Callee").OfType<IMethodSymbol>().Single();
        var caller = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), callerMethod);
        var session = CreateSession();

        var prepared = session.TryPrepare(caller, callee, null, [], out var frame, out var graph);

        Assert.That(prepared, Is.True);
        Assert.That(frame, Is.Not.Null);
        Assert.That(graph, Is.Not.Null);
    }
}
