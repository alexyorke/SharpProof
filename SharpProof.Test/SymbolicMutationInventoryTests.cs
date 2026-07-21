using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicMutationInventoryTests {
    [Test]
    public void InvalidationPlan_PreservesDirectThenDuplicateExposureOrder() {
        const string source = """
            sealed class C
            {
                private object _field = new();
                private static object Echo(object first, object second) => first;
                object M(object value)
                {
                    _field = Echo(value, value);
                    return value;
                }
            }
            """;
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(InvalidationPlan_PreservesDirectThenDuplicateExposureOrder));
        var assignment = fixture.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var value = fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<ParameterSyntax>().Single(parameter => parameter.Identifier.ValueText == "value"))!;

        var plan = SymbolicMutationInventory.Create(assignment, fixture.SemanticModel, CancellationToken.None)
            .ToInvalidationPlan();

        Assert.Multiple(() => {
            Assert.That(plan.HasUnsupportedMutation, Is.False);
            Assert.That(plan.Steps.Select(static step => step.Provenance), Is.EqualTo(new[]
            {
                "operation-transfer.mutation-invalidation",
                "operation-transfer.reference-invalidation",
                "operation-transfer.reference-invalidation"
            }));
            Assert.That(plan.Steps.SelectMany(static step => step.Targets).Select(static target => target.Key),
                Is.EqualTo(new[]
                {
                    SymbolicStateValueFacts.ImplicitThisVariableName + "._field",
                    SymbolicFactFactory.GetSmtVariableName(value),
                    SymbolicFactFactory.GetSmtVariableName(value)
                }));
        });
    }

    [Test]
    public void Inventory_ExcludesNestedCallableBodiesButIncludesPassedCaptures() {
        const string source = """
            using System;
            static class C
            {
                static void Use(Func<object> valueFactory) { }
                static object M(object value)
                {
                    object Local() { value = new object(); return value; }
                    Use(() => value);
                    return value;
                }
            }
            """;
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(Inventory_ExcludesNestedCallableBodiesButIncludesPassedCaptures));
        var block = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "M").Body!;
        var value = fixture.SemanticModel.GetDeclaredSymbol(
            block.Parent!.DescendantNodes().OfType<ParameterSyntax>().Single(parameter => parameter.Identifier.ValueText == "value"))!;

        var plan = SymbolicMutationInventory.Create(block, fixture.SemanticModel, CancellationToken.None)
            .ToInvalidationPlan();

        Assert.Multiple(() => {
            Assert.That(plan.HasUnsupportedMutation, Is.False);
            Assert.That(plan.Steps.Length, Is.EqualTo(1));
            Assert.That(plan.Steps[0].Targets.Single().Key, Is.EqualTo(SymbolicFactFactory.GetSmtVariableName(value)));
        });
    }

    [Test]
    public void Inventory_PreservesUnsupportedTupleAndStrictSpanSemantics() {
        const string source = """
            static class C
            {
                static object M(object first, object second)
                {
                    (first, second) = (second, first);
                    first = second;
                    return first;
                }
            }
            """;
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(Inventory_PreservesUnsupportedTupleAndStrictSpanSemantics));
        var block = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
        var assignments = block.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        var first = fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<ParameterSyntax>().First())!;
        var inventory = SymbolicMutationInventory.Create(block, fixture.SemanticModel, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(inventory.ToInvalidationPlan().HasUnsupportedMutation, Is.True);
            Assert.That(inventory.MutatesBetween( assignments[0].SpanStart, assignments[1].SpanStart, first), Is.False, "window endpoints must remain exclusive");
            Assert.That(inventory.MutatesBetween( assignments[0].SpanStart - 1, assignments[1].SpanStart, first), Is.True);
        });
    }

    [Test]
    public void ExposurePolicy_FiltersImmutableReferencesOnlyWhenRequested() {
        const string source = "static class C { static void Use(object value) { } static void M(string text) { Use(text); } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ExposurePolicy_FiltersImmutableReferencesOnlyWhenRequested));
        var invocation = fixture.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(candidate => candidate.Expression.ToString() == "Use");
        var text = fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<ParameterSyntax>().Single(parameter => parameter.Identifier.ValueText == "text"))!;
        var inventory = SymbolicMutationInventory.Create(invocation, fixture.SemanticModel, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(inventory.ExposesSymbol(text, mutableOnly: false), Is.True);
            Assert.That(inventory.ExposesSymbol(text, mutableOnly: true), Is.False);
        });
    }
}
