using System.Collections.Immutable;
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
    private static readonly ImmutableArray<IrExceptionKind> OverflowKinds = [IrExceptionKind.Overflow];

    [TestCase("new System.Exception((string)null)", -1)]
    [TestCase("new System.ArgumentException((string)null)", -1)]
    [TestCase("new System.InvalidOperationException(\"message\")", 0)]
    [TestCase("new System.Collections.Generic.KeyNotFoundException(\"message\")", 0)]
    [TestCase("new System.ArgumentNullException(\"parameter\", \"message\")", 1)]
    [TestCase("new System.ArgumentNullException()", null)]
    [TestCase("new System.Security.Cryptography.CryptographicException(\"format\", \"insert\")", null)]
    public void ConstructorResourceGuardsDistinguishMessageRoles(string expression, int? expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        using var subject = TypedProgramSubject.Create("object Target() => " + expression + ";");
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var creation = (IObjectCreationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(CSharpOperationSemantics.ExceptionConstructorMessageGuard(creation.Constructor!), Is.EqualTo(expected));
    }

    [Test]
    public void ConstructorResourceGuardsRejectSourceLookalikesAndOrdinaryMethods()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System { public class ArgumentException : Exception { public ArgumentException(string message) {} } } public static class Subject { public static object Target() => new System.ArgumentException(\"message\"); }");
        var compilation = CSharpCompilation.Create("SourceExceptionLookalike", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var creation = (IObjectCreationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(CSharpOperationSemantics.IsAuditedMessageExceptionDeclaration(creation.Constructor!), Is.False);
        Assert.That(CSharpOperationSemantics.IsExceptionResourceLockDeclaration(creation.Constructor!), Is.False);
        Assert.That(CSharpOperationSemantics.ExceptionConstructorMessageGuard(creation.Constructor!), Is.Null);
        Assert.That(CSharpOperationSemantics.ExceptionInitializationKind(creation.Constructor!), Is.Null);
        var ordinary = compilation.GetSpecialType(SpecialType.System_String).GetMembers("ToString").OfType<IMethodSymbol>().First();
        Assert.That(CSharpOperationSemantics.ExceptionConstructorMessageGuard(ordinary), Is.Null);
        Assert.That(CSharpOperationSemantics.IsAuditedMessageExceptionDeclaration(ordinary), Is.False);
        Assert.That(CSharpOperationSemantics.IsExceptionResourceLockDeclaration(ordinary), Is.False);
        Assert.That(CSharpOperationSemantics.ExceptionInitializationKind(ordinary), Is.Null);
        Assert.Throws<ArgumentNullException>((Action)(() => CSharpOperationSemantics.ExceptionConstructorMessageGuard(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => CSharpOperationSemantics.IsAuditedMessageExceptionDeclaration(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => CSharpOperationSemantics.IsExceptionResourceLockDeclaration(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => CSharpOperationSemantics.ExceptionInitializationKind(null!)));
    }

    [TestCase("new System.ArgumentException()", "Argument")]
    [TestCase("new System.ArgumentNullException()", "ArgumentNull")]
    [TestCase("new System.ArgumentNullException(\"parameter\")", "ArgumentNullName")]
    [TestCase("new System.ArgumentOutOfRangeException()", "ArgumentRange")]
    [TestCase("new System.ArgumentOutOfRangeException(\"parameter\")", "ArgumentRangeName")]
    [TestCase("new System.InvalidOperationException()", "InvalidOperation")]
    [TestCase("new System.InvalidOperationException((string)null)", "InvalidOperationNull")]
    [TestCase("new System.DivideByZeroException()", "DivideByZero")]
    [TestCase("new System.IO.IOException()", "IOException")]
    [TestCase("new System.ObjectDisposedException(\"object\")", "ObjectDisposedName")]
    [TestCase("new System.AggregateException()", "Aggregate")]
    [TestCase("new System.AggregateException((string)null)", "AggregateNull")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException((string)null)", "KeyNotFoundNull")]
    [TestCase("new System.Diagnostics.UnreachableException((string)null)", null)]
    public void ExceptionReplayDescriptionsHaveFixedConstructorIdentities(string expression, string? expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        using var subject = TypedProgramSubject.Create("object Target() => " + expression + ";");
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        var creation = (IObjectCreationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(CSharpOperationSemantics.ExceptionInitializationKind(creation.Constructor!), Is.EqualTo(expected));
    }

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
        foreach (var divisor in ImmutableArray.Create(0, 1))
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
