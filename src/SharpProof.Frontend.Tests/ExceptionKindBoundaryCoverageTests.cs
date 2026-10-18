using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ExceptionKindBoundaryCoverageTests
{
    private static readonly IrExceptionKind[] OverflowKinds = [IrExceptionKind.Overflow];

    [Test]
    public void ActualMissingFrameworkCompilationCannotClassifyBareCatchKinds()
    {
        using var subject = TypedProgramSubject.Create("int Target(int divisor) { try { return 17 / divisor; } catch { return 23; } }");
        var missing = CSharpCompilation.Create("MissingFrameworkCatch", subject.Compilation.SyntaxTrees, references: [],
            options: (CSharpCompilationOptions)subject.Compilation.Options);
        Assert.That(missing.GetSpecialType(SpecialType.System_Object).TypeKind, Is.EqualTo(TypeKind.Error));
        Assert.That(missing.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id == "CS0518")), Is.True);
        using var image = new System.IO.MemoryStream();
        Assert.That(missing.Emit(image).Success, Is.False);
        Assert.That(CSharpOperationSemantics.TryCatchKinds(missing, null, out var rejected), Is.False);
        Assert.That(rejected.IsDefault, Is.True);
        Assert.That(CSharpOperationSemantics.TryCatchKinds(subject.Compilation, null, out var admitted), Is.True);
        Assert.That(admitted, Is.EquivalentTo(Enum.GetValues<IrExceptionKind>()));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        foreach (var divisor in new[] { 0, 1 })
        {
            var expected = divisor == 0 ? 23 : 17;
            Assert.That(subject.Invoke([divisor]), Is.EqualTo(expected));
            var replay = subject.Execute(lowering, [divisor]);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        }
    }

    [Test]
    public void ActualMissingBaseTypeCannotSupplyCoreExceptionCreation()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Derived : MissingBase { } public static class Subject { public static object Target() => new Derived(); }");
        var compilation = CSharpCompilation.Create("MissingExceptionBase", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id == "CS0246")), Is.True);
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var syntax = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var creation = (IObjectCreationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(creation.Type!.BaseType!.TypeKind, Is.EqualTo(TypeKind.Error));
        Assert.That(CSharpOperationSemantics.IsCoreExceptionCreation(creation), Is.False);
        using var supported = TypedProgramSubject.Create("object Target() => new System.Exception(\"bad\");");
        var actual = supported.Invoke([]);
        Assert.That(actual, Is.TypeOf<Exception>());
        Assert.That(((Exception)actual!).Message, Is.EqualTo("bad"));
        var supportedTree = supported.Compilation.SyntaxTrees.Single();
        var supportedSyntax = supportedTree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var supportedCreation = (IObjectCreationOperation)supported.Compilation.GetSemanticModel(supportedTree).GetOperation(supportedSyntax)!;
        Assert.That(CSharpOperationSemantics.IsCoreExceptionCreation(supportedCreation), Is.True);
    }

    [Test]
    public void MetadataNameRejectsUndefinedKindAndPreservesCanonicalOverflowName()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            CSharpOperationSemantics.ExceptionMetadataName((IrExceptionKind)int.MaxValue)));
        Assert.That(error!.ParamName, Is.EqualTo("kind"));
        Assert.That(CSharpOperationSemantics.ExceptionMetadataName(IrExceptionKind.Overflow),
            Is.EqualTo("System.OverflowException"));
    }

    [TestCase(SpecialType.System_Int32)]
    [TestCase(SpecialType.System_String)]
    public void CatchClassificationRejectsOrdinaryNonExceptionTypesAndKeepsOverflowCatch(SpecialType rejectedType)
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { try { return checked(value + 1); } catch (System.OverflowException) { return 17; } }");
        Assert.That(subject.Invoke([int.MaxValue]), Is.EqualTo(17));
        Assert.That(CSharpOperationSemantics.TryCatchKinds(subject.Compilation,
            subject.Compilation.GetSpecialType(rejectedType), out var rejectedKinds), Is.False);
        Assert.That(rejectedKinds.IsDefault, Is.True);
        var overflow = subject.Compilation.GetTypeByMetadataName("System.OverflowException")!;
        Assert.That(CSharpOperationSemantics.TryCatchKinds(subject.Compilation, overflow, out var kinds), Is.True);
        Assert.That(kinds, Is.EquivalentTo(OverflowKinds));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [int.MaxValue]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}
