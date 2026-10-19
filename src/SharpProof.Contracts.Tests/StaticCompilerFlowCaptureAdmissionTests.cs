using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class StaticCompilerFlowCaptureAdmissionTests
{
    [TestCase("int", SpecialType.System_Int32, true)]
    [TestCase("bool", SpecialType.System_Boolean, true)]
    [TestCase("string", SpecialType.System_String, true)]
    [TestCase("int", SpecialType.System_Int32, false)]
    [TestCase("bool", SpecialType.System_Boolean, false)]
    [TestCase("string", SpecialType.System_String, false)]
    public void CompilerOwnedCaptureAdmissionRetainsStaticAndInstanceFrameBehavior(
        string typeName, SpecialType expectedType, bool isStatic)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var fallback = typeName switch
        {
            "int" => "0",
            "bool" => "false",
            _ => "\"\""
        };
        var compilation = TestCompilation.Create("StaticCompilerCaptureAdmission", $$"""
            public class Target {
                public {{(isStatic ? "static " : "")}}{{typeName}} Read(bool choose, {{typeName}} value) {
                    {{typeName}} selected = choose ? value : {{fallback}};
                    return selected;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var source = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        Assert.That(source.IsStatic, Is.EqualTo(isStatic));
        Assert.That(SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(source.ReturnType.SpecialType, Is.EqualTo(expectedType));
        var body = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var graph = ControlFlowGraph.Create(body);
        Assert.That(graph.OriginalOperation, Is.SameAs(body));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf()))
            .Concat(block.BranchValue?.DescendantsAndSelf() ?? []))).ToImmutableArray();
        var references = operations.OfType<IFlowCaptureReferenceOperation>().ToImmutableArray();
        Assert.That(references, Has.Length.EqualTo(1));
        var capture = references.Single();
        Assert.That(capture.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(capture.Type!.SpecialType, Is.EqualTo(expectedType));
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(capture.Syntax.SpanStart), source), Is.True);
        Assert.That(operations.OfType<IFlowCaptureOperation>().Count(definition => definition is null ? throw new System.ArgumentNullException(nameof(definition)) : (definition.Id.Equals(capture.Id))), Is.EqualTo(2));
        var api = ContractApiSymbols.TryCreate(compilation);
        Assert.That(api, Is.Not.Null);
        var factory = new IrFactory();
        var canonicalization = new ContractCanonicalization(compilation, factory);
        var lowerer = new RoslynOperationLowerer(factory);
        var loweredCapture = lowerer.Lower(capture);
        Assert.That(loweredCapture.IsExact, Is.True);
        Assert.That(loweredCapture.Term, Is.InstanceOf<IrVariableTerm>());
        Assert.That(loweredCapture.Variables, Is.Empty);

        var originalBinder = new ContractExpressionBinder(factory, api!, source, canonicalization.CreateTypeSpecializer(source));
        var expressionSyntax = syntax.DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();
        var original = originalBinder.Bind(model.GetOperation(expressionSyntax)!);
        Assert.That(original.IsSuccess, Is.True);
        Assert.That(originalBinder.ReceiverVariables, Is.Empty);
        Assert.That(originalBinder.VariableBindings, Has.Length.EqualTo(2));
        var bindings = originalBinder.VariableBindings;
        var chooseVariable = bindings.Single(binding => SymbolEqualityComparer.Default.Equals(binding.Symbol, source.Parameters[0])).Variable;
        var valueVariable = bindings.Single(binding => SymbolEqualityComparer.Default.Equals(binding.Symbol, source.Parameters[1])).Variable;
        var interpreter = new IrInterpreter(factory);
        object[] inputs = typeName switch
        {
            "int" => [-1L, 0L, 1L],
            "bool" => [false, true],
            _ => ["", "value"]
        };
        foreach (var input in inputs)
        {
            foreach (var choose in ImmutableArray.Create(false, true))
            {
                var supplied = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(chooseVariable, factory.CreateBooleanValue(choose)).SetItem(valueVariable, typeName switch
                {
                    "int" => factory.CreateIntegerValue((long)input),
                    "bool" => factory.CreateBooleanValue((bool)input),
                    _ => factory.CreateStringValue((string)input)
                });
                var actual = interpreter.Evaluate(original.Term!, supplied).Value!;
                object value = typeName switch
                {
                    "int" => actual.Integer,
                    "bool" => actual.Boolean,
                    _ => actual.String
                };
                object expected = choose ? input : typeName switch
                {
                    "int" => 0L,
                    "bool" => false,
                    _ => ""
                };
                Assert.That(value, Is.EqualTo(expected));
            }
        }

        var captureBinder = new ContractExpressionBinder(factory, api!, source, canonicalization.CreateTypeSpecializer(source));
        var captured = captureBinder.Bind(capture);
        Assert.That(captureBinder.VariableBindings, Is.Empty);
        if (isStatic)
        {
            Assert.That(captured.IsSuccess, Is.False);
            Assert.That(captured.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
            Assert.That(captured.Term, Is.Null);
            Assert.That(captureBinder.ReceiverVariables, Is.Empty);
        }
        else
        {
            Assert.That(captured.IsSuccess, Is.True);
            Assert.That(captured.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(captured.Term, Is.InstanceOf<IrVariableTerm>());
            var capturedVariable = ((IrVariableTerm)captured.Term!).Variable;
            Assert.That(captured.Variables, Is.EquivalentTo(ImmutableArray.Create(capturedVariable)));
            Assert.That(captureBinder.ReceiverVariables, Is.EqualTo(ImmutableArray.Create(capturedVariable)));
            Assert.That(factory.GetVariableInfo(capturedVariable).Type, Is.EqualTo(captured.Term!.Type));
        }
    }
}
