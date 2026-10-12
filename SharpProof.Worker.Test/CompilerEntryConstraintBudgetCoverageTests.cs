using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerEntryConstraintBudgetCoverageTests
{
    [TestCase("fields-before", 1, 1, true)]
    [TestCase("fields-before", 2048, 0, true)]
    [TestCase("fields-after", 2048, 0, true)]
    [TestCase("fields-after", 4096, 0, false)]
    [TestCase("module", 0, 1, true)]
    [TestCase("module", 4093, 0, true)]
    [TestCase("module", 4095, 0, false)]
    [TestCase("empty-body", 1, 1, false)]
    [TestCase("empty-body", 4096, 0, false)]
    [TestCase("constructor-reference", 1, 1, false)]
    [TestCase("constructor-reference", 4094, 0, false)]
    [TestCase("value-work", 1, 1, false)]
    [TestCase("value-work", 1363, 0, false)]
    [TestCase("type-work", 1, 1, false)]
    [TestCase("type-work", 1362, 0, false)]
    public async Task ActualSourceTraversalRetainsOnlyCompleteEntryExceptionConstraints(
        string shape, int count, int expectedConstraints, bool expectedEntryEffects)
    {
        const string root = "[DoesNotThrow] public static int Root() => 1;";
        var fields = string.Concat(Enumerable.Range(0, count).Select(index => $"public static int F{index};\n"));
        var constants = string.Concat(Enumerable.Range(0, count).Select(index => $"public const int C{index}=0;\n"));
        var stores = string.Concat(Enumerable.Repeat("State=0;", count));
        var extras = shape == "module" ? string.Concat(Enumerable.Range(0, count).Select(index => $"public static class Empty{index} {{ }}\n")) :
            shape == "type-work" ? "public static class Other { public static int State; }" : string.Empty;
        var members = shape switch
        {
            "fields-before" => fields + root,
            "fields-after" => root + fields,
            "module" => root,
            "empty-body" => root + "static Subject() {" + new string(';', count) + "}",
            "constructor-reference" => constants + "static Subject() { }" + root,
            "value-work" => "public static int State;" + root + "static Subject() {;" + stores + "}",
            "type-work" => "public static int State;" + root + "static Subject() {" + stores + "Other.State=0;}",
            _ => throw new AssertionException("Unknown source shape")
        };
        var source = "using SharpProof.Attributes;" + extras + "public static class Subject {" + members + "}";
        var compilation = TestCompilation.Create("EntryTraversalBoundary", ("entry-budget.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        Assert.That(syntax.DescendantNodesAndSelf().Count(), Is.LessThan(65536));
        var model = compilation.GetSemanticModel(tree);
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var owner = method.ContainingType;
        Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        Assert.That(owner.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly), Is.True);
        if (shape is "fields-before" or "fields-after")
        {
            Assert.That(owner.StaticConstructors, Is.Empty);
            Assert.That(owner.GetMembers().OfType<IFieldSymbol>().Count(), Is.EqualTo(count));
            Assert.That(owner.GetMembers(), Has.Length.EqualTo(count + 1));
            Assert.That(Array.IndexOf(owner.GetMembers().ToArray(), method), Is.EqualTo(shape == "fields-before" ? count : 0));
            Assert.That(owner.GetMembers().OfType<IFieldSymbol>().All(field => field.IsStatic && !field.IsConst && field.DeclaringSyntaxReferences.Length == 1), Is.True);
        }
        else if (shape == "module")
        {
            var types = compilation.Assembly.GlobalNamespace.GetTypeMembers();
            Assert.That(types, Has.Length.EqualTo(count + 1));
            Assert.That(types.Where(type => !SymbolEqualityComparer.Default.Equals(type, owner)).All(type => type.IsStatic && type.GetMembers().IsEmpty), Is.True);
            Assert.That(owner.GetMembers(), Has.Length.EqualTo(1));
            Assert.That(owner.StaticConstructors, Is.Empty);
        }
        else
        {
            var constructor = owner.StaticConstructors.Single();
            Assert.That(constructor.IsImplicitlyDeclared, Is.False);
            Assert.That(constructor.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            var constructorSyntax = syntax.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single();
            if (shape == "empty-body")
            {
                Assert.That(constructorSyntax.Body!.Statements, Has.Count.EqualTo(count));
                Assert.That(constructorSyntax.Body.Statements.All(statement => statement is EmptyStatementSyntax), Is.True);
            }
            if (shape == "constructor-reference")
            {
                Assert.That(owner.GetMembers().Take(count).All(member => member is IFieldSymbol { IsConst: true }), Is.True);
                Assert.That(owner.GetMembers()[count], Is.EqualTo(constructor));
                Assert.That(constructorSyntax.Body!.Statements, Is.Empty);
            }
            if (shape is "value-work" or "type-work")
            {
                Assert.That(owner.GetMembers()[0], Is.InstanceOf<IFieldSymbol>());
                Assert.That(owner.GetMembers()[1], Is.EqualTo(method));
                Assert.That(owner.GetMembers()[2], Is.EqualTo(constructor));
                Assert.That(constructorSyntax.Body!.Statements, Has.Count.EqualTo(count + 1));
                var statements = constructorSyntax.Body.Statements.OfType<ExpressionStatementSyntax>().ToArray();
                Assert.That(statements, Has.Length.EqualTo(shape == "type-work" ? count + 1 : count));
                foreach (var statement in statements)
                {
                    var operation = model.GetOperation(statement.Expression);
                    Assert.That(operation, Is.InstanceOf<ISimpleAssignmentOperation>());
                    var assignment = (ISimpleAssignmentOperation)operation!;
                    Assert.That(assignment.IsRef, Is.False);
                    Assert.That(assignment.Target, Is.InstanceOf<IFieldReferenceOperation>());
                    var target = (IFieldReferenceOperation)assignment.Target;
                    Assert.That(target.Field.IsStatic, Is.True);
                    Assert.That(target.Instance, Is.Null);
                    Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(0));
                }
                if (shape == "type-work")
                {
                    var last = (ISimpleAssignmentOperation)model.GetOperation(statements[statements.Length - 1].Expression)!;
                    var target = (IFieldReferenceOperation)last.Target;
                    Assert.That(target.Field.ContainingType.Name, Is.EqualTo("Other"));
                    Assert.That(target.Field.ContainingType.StaticConstructors, Is.Empty);
                }
            }
        }
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("EntryTraversalBoundary", isCollectible: true);
        try { Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, null), Is.EqualTo(1)); }
        finally { runtime.Unload(); }

        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var claim = artifact.Manifest.Claims.Single();
        var callable = artifact.Callables.Single();
        Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(callable.Total, Is.Not.Null);
        var total = callable.Total!;
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.ValidEffectClaimIds, Is.EqualTo(new[] { claim.ClaimId }));
        Assert.That(total.EffectsCompleteAtEntry, Is.EqualTo(expectedEntryEffects));
        Assert.That(total.ExceptionConstraints, Has.Length.EqualTo(expectedConstraints));
        if (expectedConstraints == 1)
        {
            Assert.That(total.ExceptionConstraints.Single().ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(total.ExceptionConstraints.Single().AllowedKinds, Is.Empty);
        }
        var reachable = artifact.ReachableSource!;
        Assert.That(reachable.Roots.Single().CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(reachable.Roots.Single().BodyId, Is.EqualTo(reachable.Bodies.Single().BodyId));
        Assert.That(reachable.Bodies.Single().Start, Is.EqualTo(declaration.SpanStart));
        Assert.That(reachable.Bodies.Single().Length, Is.EqualTo(declaration.Span.Length));
        Assert.That(reachable.Bodies.Single().CallsComplete, Is.True);
        Assert.That(reachable.CollectionComplete, Is.True);
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expectedConstraints == 1 ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown));
    }
}
