using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class CompanionEditingRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void UnresolvedCallerTypeArgumentCannotAdmitACompanionContract(bool declared)
    {
        var declaration = declared ? "public sealed class Missing { }" : string.Empty;
        var source = $$"""
            using SharpProof.Attributes;
            {{declaration}}
            public interface ITarget { T Echo<T>(T value); }
            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static T Echo<T>(ITarget receiver, T value) {
                    Contract.Requires(true);
                    return value;
                }
            }
            public static class Caller {
                public static void Call(ITarget target) {
                    _ = target.Echo<Missing>(default!);
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create("ContractsCallerTypeEditing",
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToArray();
        if (declared)
        {
            Assert.That(errors, Is.Empty);
        }
        else
        {
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors[0].Id, Is.EqualTo("CS0246"));
            Assert.That(errors[0].Location.SourceTree, Is.SameAs(tree));
            Assert.That(tree.GetText().ToString(errors[0].Location.SourceSpan), Is.EqualTo("Missing"));
        }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Expression.ToString() == "target.Echo<Missing>"));
        var operation = model.GetOperation(syntax);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)operation!).TargetMethod;
        Assert.That(target.TypeArguments.Single().TypeKind,
            Is.EqualTo(declared ? TypeKind.Class : TypeKind.Error));
        Assert.That(target.ReturnType.TypeKind,
            Is.EqualTo(declared ? TypeKind.Class : TypeKind.Error));
        Assert.That(target.ConstructedFrom.ReturnType.TypeKind, Is.EqualTo(TypeKind.TypeParameter));
        var factory = new IrFactory();
        var binder = new ContractBinder(compilation, factory);
        var direct = binder.GetClauseInventory(target);
        Assert.That(direct.ContractApiAvailable, Is.True);
        Assert.That(direct.Clauses, Is.Empty);
        var result = binder.Bind(target);
        Assert.That(result.Failure, Is.EqualTo(declared
            ? ContractBindingFailure.None : ContractBindingFailure.CompanionSignatureMismatch));
        Assert.That(result.IsSuccess, Is.EqualTo(declared));
        if (declared)
        {
            Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
            Assert.That(result.Contracts.Clauses[0].Evidence, Is.EqualTo(BoundContractEvidence.Companion));
            var evaluation = new IrInterpreter(factory).Evaluate(result.Contracts.Clauses[0].Condition);
            Assert.That(evaluation.Value!.Boolean, Is.True);
        }
        else
        {
            Assert.That(result.Contracts, Is.Null);
        }
    }
}
