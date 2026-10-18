using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ExternAliasClosedAttributeBindingTests
{
    private static readonly ImmutableArray<int> Inputs = [-1, 0, 1];

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ActualShippedAliasAnnotationsKeepTheComponentAdmissionPolicy(bool aliased, bool total)
    {
        var source = aliased ? """
            extern alias ApiAlias;
            public static class Target {
                public static int Read([ApiAlias::SharpProof.Attributes.Positive] int value) => value;
            }
            """ : """
            public static class Target {
                public static int Read([global::SharpProof.Attributes.Positive] int value) => value;
            }
            """;
        var reference = MetadataReference.CreateFromFile(typeof(PositiveAttribute).Assembly.Location,
            MetadataReferenceProperties.Assembly.WithAliases([aliased ? "ApiAlias" : "global"]));
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create("AliasAnnotationComponent_" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.WithoutSharpProof.Add(reference),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var method = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var site = ClosedContractAttributeValidator.EnumerateValueSites(method, includeReturn: false).Single();
        Assert.That(site.IsReturn, Is.False);
        var parameter = method.Parameters[site.ParameterIndex];
        var attribute = site.Attributes.Single();
        var attributeSyntax = attribute.ApplicationSyntaxReference!.GetSyntax();
        Assert.That(attributeSyntax.SyntaxTree, Is.SameAs(tree));
        var actualAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        Assert.That(attribute.AttributeClass!.MetadataName, Is.EqualTo("PositiveAttribute"));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass.ContainingAssembly, actualAssembly), Is.True);
        Assert.That(actualAssembly.Identity.Name,
            Is.EqualTo(typeof(PositiveAttribute).Assembly.GetName().Name));
        Assert.That(parameter.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var model = compilation.GetSemanticModel(tree);
        var body = model.GetOperation(method.DeclaringSyntaxReferences.Single().GetSyntax())!;
        Assert.That(body.SemanticModel!.Compilation, Is.SameAs(compilation));
        var symbols = ContractSelectionInventory.ForCompilation(compilation);
        Assert.That(symbols.IsRejectedClosedContract(attribute), Is.EqualTo(aliased));
        Assert.That(symbols.GetClosedContractKind(attribute), Is.EqualTo(aliased
            ? ClosedContractAttributeKind.None : ClosedContractAttributeKind.Positive));
        var publicResult = new ContractBinder(compilation, new IrFactory()).Bind(method);
        Assert.That(publicResult.Failure, Is.EqualTo(aliased
            ? ContractBindingFailure.ContractApiUnavailable : ContractBindingFailure.None));

        var factory = new IrFactory(total ? IrExecutionSemantics.Total : IrExecutionSemantics.Legacy);
        IrVarId variable;
        if (total)
        {
            var context = new TotalLoweringContext(factory, method);
            Assert.That(context.Target, Is.SameAs(method));
            Assert.That(context.Parameters.Single().Parameter, Is.SameAs(parameter));
            variable = context.Parameters.Single().Entry;
        }
        else
        {
            var canonical = new ContractCanonicalization(compilation, factory)
                .CreateVariables(method, includeResult: false);
            Assert.That(canonical.ToBoundVariables().Single().Symbol, Is.SameAs(parameter));
            variable = canonical.Parameters.Single();
        }
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationRequests = 0;
        var bound = ClosedContractAttributeBinding.BindValue(
            factory, symbols, site.Attributes, site.Type, site.RefKind,
            () => { valueRequests++; return value; }, parameter.Name,
            site.IsReturn ? BoundContractKind.Ensures : BoundContractKind.Requires,
            total, syntax =>
            {
                operationRequests++;
                Assert.That(syntax?.SyntaxTree, Is.SameAs(total ? tree : null));
                if (syntax != null)
                {
                    Assert.That(syntax.Span, Is.EqualTo(attributeSyntax.Span));
                }
                var actualSyntax = syntax ?? attributeSyntax;
                var location = actualSyntax.GetLocation().GetLineSpan();
                return factory.CreateOperation("closed-attribute", new IrSourceSpan(
                    string.IsNullOrEmpty(tree.FilePath) ? "source" : tree.FilePath,
                    actualSyntax.SpanStart, actualSyntax.Span.Length,
                    location.StartLinePosition.Line + 1, location.StartLinePosition.Character + 1));
            });
        Assert.That(bound.Failure, Is.EqualTo(aliased
            ? ContractBindingFailure.InvalidClosedAttribute : ContractBindingFailure.None));
        if (aliased)
        {
            Assert.That(bound.Clauses, Is.Empty);
            Assert.That(valueRequests, Is.Zero);
            Assert.That(operationRequests, Is.Zero);
            return;
        }
        Assert.That(bound.Clauses, Has.Length.EqualTo(1));
        Assert.That(valueRequests, Is.EqualTo(1));
        Assert.That(operationRequests, Is.EqualTo(1));
        var clause = bound.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(clause.SourceSyntax!.SyntaxTree, Is.SameAs(tree));
        var operation = factory.GetOperationInfo(clause.SourceOperation);
        Assert.That(operation.SourceSpan!.Start, Is.EqualTo(attributeSyntax.SpanStart));
        Assert.That(operation.SourceSpan.Length, Is.EqualTo(attributeSyntax.Span.Length));
        Assert.That(IrTermAnalysis.CollectVariables(clause.Condition), Is.EquivalentTo(new[] { variable }));
        var runtimeMethod = System.Reflection.Assembly.Load(image.ToArray())
            .GetType("Target")!.GetMethod("Read")!;
        var interpreter = new IrInterpreter(factory);
        foreach (var input in Inputs)
        {
            var returned = (int)runtimeMethod.Invoke(null, new object[] { input })!;
            Assert.That(returned, Is.EqualTo(input));
            var values = new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValue(factory.GetVariableInfo(variable).Type, input)
            };
            Assert.That(interpreter.Evaluate(clause.Condition, values).Value!.Boolean,
                Is.EqualTo(returned > 0));
        }
        if (total)
        {
            Assert.That(clause.SafeCondition, Is.Not.Null);
            Assert.That(interpreter.Evaluate(clause.SafeCondition!).Value!.Boolean, Is.True);
        }
        else
        {
            Assert.That(clause.SafeCondition, Is.Null);
        }
    }
}
