using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorNameOfFieldAssignmentApiTests
{
    private static readonly ImmutableArray<string> ExpectedDiagnosticIds = ["CS8081"];

    [Test]
    public void ActualNameOfRecoveryAssignmentRejectsItsMissingInstanceReceiver()
    {
        const string source = """
            public static class Subject {
                public static string Target() => nameof(Cell.Value = 1);
            }
            public sealed class Cell { public int Value; }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "nameof-field-assignment-recovery.cs");
        var compilation = CSharpCompilation.Create("NameOfFieldAssignmentRecovery", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        var errors = compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        var model = compilation.GetSemanticModel(tree);
        var methodSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        var syntax = methodSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        Assert.That(errors.Single().Location.SourceSpan, Is.EqualTo(syntax.Span));
        var operation = model.GetOperation(syntax);
        TestContext.WriteLine($"Actual recovery assignment: kind={operation?.Kind}, type={operation?.Type?.TypeKind}/{operation?.Type?.SpecialType}, parent={operation?.Parent?.Kind}");
        Assert.That(operation, Is.InstanceOf<ISimpleAssignmentOperation>());
        var assignment = (ISimpleAssignmentOperation)operation!;
        Assert.That(assignment.Kind, Is.EqualTo(OperationKind.SimpleAssignment));
        Assert.That(assignment.IsRef, Is.False);
        Assert.That(assignment.IsImplicit, Is.False);
        Assert.That(assignment.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(assignment.ConstantValue.HasValue, Is.False);
        Assert.That(assignment.Parent, Is.InstanceOf<INameOfOperation>());
        var nameOf = (INameOfOperation)assignment.Parent!;
        Assert.That(nameOf.Argument, Is.SameAs(assignment));
        Assert.That(nameOf.Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(nameOf.ConstantValue.HasValue, Is.True);
        Assert.That(nameOf.ConstantValue.Value, Is.EqualTo(string.Empty));
        Assert.That(assignment.Target, Is.InstanceOf<IFieldReferenceOperation>());
        var field = (IFieldReferenceOperation)assignment.Target;
        Assert.That(field.Instance, Is.Null);
        Assert.That(field.Field.IsStatic, Is.False);
        Assert.That(field.Field.IsReadOnly, Is.False);
        Assert.That(field.Field.IsConst, Is.False);
        Assert.That(field.Field.ContainingType.IsReferenceType, Is.True);
        Assert.That(field.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(SymbolEqualityComparer.Default.Equals(field.Field.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, compilation.Assembly), Is.True);
        Assert.That(assignment.Value, Is.InstanceOf<ILiteralOperation>());
        Assert.That(assignment.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(assignment.Value.ConstantValue.HasValue, Is.True);
        Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(1));

        // This actual failed-method symbol owns only an expression API frame.
        // No CFG, candidate program, preparation status or negative replay exists.
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(context.Target, Is.SameAs(method));
        Assert.That(context.Factory, Is.SameAs(factory));
        Assert.That(context.Compilation, Is.Null);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("nameof-recovery-expression-only");
        var rejected = new RoslynTotalExpressionLowerer(context, builder).LowerBodyValue(assignment, entry);
        Assert.That(rejected.Classification.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(rejected.Continuation, Is.EqualTo(entry));
        Assert.That(rejected.Value, Is.InstanceOf<IrVariableTerm>());
        Assert.That(rejected.Value.Type, Is.EqualTo(factory.IntegerType));

        // Separate valid static store control: its scalar assignment result is
        // observed; an IR write hint is not an equivalent heap-storage oracle.
        using var control = TypedProgramSubject.Create("int Target() => Cell.Value = 1;",
            "public static class Cell { public static int Value; }");
        Assert.That(control.Invoke([]), Is.EqualTo(1));
        var whole = control.Lower();
        Assert.That(whole.IsExact, Is.True);
        Assert.That(whole.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(whole.ConstructionLimitExceeded, Is.False);
        var writes = whole.Program.Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrWriteInstruction>().ToArray();
        Assert.That(writes, Has.Length.EqualTo(1));
        Assert.That(writes[0].Region, Is.EqualTo(IrWriteRegion.Static));
        var replay = control.Execute(whole, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.One));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
