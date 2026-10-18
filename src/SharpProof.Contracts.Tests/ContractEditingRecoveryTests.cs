using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractEditingRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ConstructedContractRejectsAnUnresolvedNestedDefaultType(bool declared)
    {
        var declaration = declared ? "public sealed class Missing { }" : string.Empty;
        var source = $$"""
            using SharpProof.Attributes;
            public class Container<T> { {{declaration}} }
            public static class Target {
                public static void Read<T>() {
                    Contract.Requires(default(Container<T>.Missing) == null);
                }
            }
            public static class Caller {
                public static void Call() => Target.Read<int>();
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create("ContractsNestedTypeEditing",
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToArray();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var defaultSyntax = root.DescendantNodes().OfType<DefaultExpressionSyntax>().Single();
        if (declared)
        {
            Assert.That(errors, Is.Empty);
        }
        else
        {
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors[0].Id, Is.EqualTo("CS0426"));
            Assert.That(errors[0].Location.SourceTree, Is.SameAs(tree));
            Assert.That(tree.GetText().ToString(errors[0].Location.SourceSpan), Is.EqualTo("Missing"));
        }
        var defaultOperation = model.GetOperation(defaultSyntax);
        Assert.That(defaultOperation, Is.InstanceOf<IDefaultValueOperation>());
        Assert.That(defaultOperation!.Type!.TypeKind,
            Is.EqualTo(declared ? TypeKind.Class : TypeKind.Error));
        Assert.That(defaultOperation.Type.ContainingType!.Name, Is.EqualTo("Container"));
        var callerSyntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Expression.ToString() == "Target.Read<int>"));
        var callerOperation = (IInvocationOperation)model.GetOperation(callerSyntax)!;
        var target = callerOperation.TargetMethod;
        Assert.That(target.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var factory = new IrFactory();
        var binder = new ContractBinder(compilation, factory);
        var inventory = binder.GetClauseInventory(target);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(inventory.Clauses[0].IsValid, Is.True);
        var result = binder.Bind(target);
        Assert.That(result.Failure, Is.EqualTo(declared
            ? ContractBindingFailure.None : ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.IsSuccess, Is.EqualTo(declared));
        if (declared)
        {
            Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
            var evaluation = new IrInterpreter(factory).Evaluate(result.Contracts.Clauses[0].Condition,
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty);
            Assert.That(evaluation.Value!.Boolean, Is.True);
        }
        else
        {
            Assert.That(result.Contracts, Is.Null);
        }
    }
}
