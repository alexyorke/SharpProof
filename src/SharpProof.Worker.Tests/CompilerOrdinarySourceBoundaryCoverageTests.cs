using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOrdinarySourceBoundaryCoverageTests
{
    [TestCase(1, true)]
    [TestCase(4097, false)]
    public void RealSourceParameterCountControlsShadowPreparation(int parameterCount, bool expectedPreparation)
    {
        var parameters = string.Join(",", Enumerable.Range(0, parameterCount).Select(index => $"int p{index}"));
        var source = $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static void Helper(int value) { Contract.Requires(value > 0); }
                public static void Root({{parameters}}) { Helper(1); }
            }
            """;
        var compilation = TestCompilation.Create("ShadowParameterBoundary", ("parameters.cs", source));
        EmitAndCheck(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot();
        Assert.That(source.Length, Is.LessThan(65536));
        Assert.That(syntax.DescendantNodesAndSelf().Count(), Is.LessThan(65536));
        var model = compilation.GetSemanticModel(tree);
        var rootDeclaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.ValueText == "Root"));
        var helperDeclaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.ValueText == "Helper"));
        var root = (IMethodSymbol)model.GetDeclaredSymbol(rootDeclaration)!;
        var helper = (IMethodSymbol)model.GetDeclaredSymbol(helperDeclaration)!;
        Assert.That(root.Parameters, Has.Length.EqualTo(parameterCount));
        Assert.That(root.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType == SpecialType.System_Int32 && parameter.RefKind == RefKind.None)), Is.True);
        Assert.That(model.GetOperation(rootDeclaration), Is.InstanceOf<IMethodBodyOperation>());
        var invocation = (IInvocationOperation)model.GetOperation(rootDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, helper), Is.True);
        Assert.That(invocation.Arguments.Single().Value.ConstantValue.Value, Is.EqualTo(1));
        var builder = new ClaimManifestBuilder(compilation);
        var inventory = builder.BuildPotentialCallShadow();
        Assert.That(inventory.Gaps, Is.Empty);
        Assert.That(inventory.Owners, Has.Length.EqualTo(2));
        var owned = inventory.Owners.Single(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (SymbolEqualityComparer.Default.Equals(owner.Method, root)));
        Assert.That(owned.DiscoveryComplete, Is.True);
        Assert.That(owned.Declaration.Span, Is.EqualTo(rootDeclaration.Span));
        Assert.That(ReferenceEquals(owned.SemanticModel.Compilation, compilation), Is.True);
        Assert.That(ReferenceEquals(owned.SemanticModel.SyntaxTree, tree), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(owned.Calls.Single().Target, helper), Is.True);
        Assert.That(owned.Calls.Single().Origin, Is.InstanceOf<IInvocationOperation>());
        var discovery = builder.Build();
        Assert.That(discovery.Targets.ContainsKey(root), Is.False);
        var helperTarget = discovery.Targets.Single(pair => SymbolEqualityComparer.Default.Equals(pair.Key, helper)).Value;
        Assert.That(helperTarget.Entry.Assumptions.Single().Kind, Is.EqualTo(WorkerAssumptionKind.Precondition));
        var batch = Prepare(compilation);
        if (!expectedPreparation)
        {
            Assert.That(batch.Callers, Is.Empty);
            var gap = batch.Gaps.Single();
            Assert.That(gap.Reason, Is.EqualTo("UnsupportedBody"));
            Assert.That(gap.TreeOrdinal, Is.Zero);
            Assert.That(gap.Start, Is.EqualTo(rootDeclaration.SpanStart));
            Assert.That(gap.Length, Is.EqualTo(rootDeclaration.Span.Length));
            return;
        }
        Assert.That(batch.Gaps, Is.Empty);
        var caller = batch.Callers.Single();
        Assert.That(caller.OwnerId, Is.EqualTo(owned.CallableId));
        Assert.That(caller.Body.IsBodyAbstraction, Is.False);
        Assert.That(caller.Body.Parameters, Has.Length.EqualTo(parameterCount));
        Assert.That(caller.Body.CallPreconditions.Single().ClauseOrdinal, Is.Zero);
        var entry = caller.Body.Parameters.Single().Entry;
        var factory = caller.Body.Program.Factory;
        var execution = new IrProgramInterpreter(factory).Execute(caller.Body.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 0)));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
    }

    [TestCase(1, true)]
    [TestCase(4097, false)]
    public void ActualCompilationTreeCountControlsBothSourceInventories(int treeCount, bool expectedComplete)
    {
        const string source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public static class Subject { public static void Helper(int value) { Contract.Requires(value>0); } public static void Root() { Helper(1); } }";
        var sources = Enumerable.Range(0, treeCount).Select(index => ($"tree{index}.cs", index == 0 ? source : string.Empty)).ToArray();
        var compilation = TestCompilation.Create("TreeBoundary", sources);
        EmitAndCheck(compilation);
        Assert.That(compilation.SyntaxTrees.Length, Is.EqualTo(treeCount));
        Assert.That(compilation.SyntaxTrees.Select(tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (tree.FilePath)).Distinct().Count(), Is.EqualTo(treeCount));
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        var batch = Prepare(compilation);
        if (expectedComplete)
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(2));
            Assert.That(batch.Gaps, Is.Empty);
            Assert.That(batch.Callers.Single().Body.CallPreconditions, Has.Length.EqualTo(1));
        }
        else
        {
            Assert.That(inventory.Owners, Is.Empty);
            Assert.That(inventory.Gaps.Single().Reason, Is.EqualTo("InventoryBudget"));
            Assert.That(inventory.Gaps.Single().TreeOrdinal, Is.EqualTo(4096));
            Assert.That(batch.Callers, Is.Empty);
            Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("InventoryBudget"));
        }
    }

    [TestCase(4096, true)]
    [TestCase(4097, false)]
    public void ActualOwnedMethodCountRetainsTheBoundedInventory(int methodCount, bool expectedComplete)
    {
        var methods = string.Concat(Enumerable.Range(0, methodCount).Select(index => $"public static void Owner{index}() {{ }}\n"));
        var compilation = TestCompilation.Create("OwnerBoundary", ("owners.cs", "public static class Subject {\n" + methods + "}"));
        EmitAndCheck(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var declarations = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(declarations, Has.Length.EqualTo(methodCount));
        Assert.That(tree.GetRoot().DescendantNodesAndSelf().Count(), Is.LessThan(65536));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.GetOperation(declarations[0]), Is.InstanceOf<IMethodBodyOperation>());
        Assert.That(model.GetOperation(declarations[declarations.Length - 1]), Is.InstanceOf<IMethodBodyOperation>());
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        Assert.That(inventory.Owners, Has.Length.EqualTo(4096));
        Assert.That(inventory.Owners.All(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (owner.DiscoveryComplete && owner.Method.IsStatic && owner.Method.Parameters.IsEmpty)), Is.True);
        if (expectedComplete)
        { Assert.That(inventory.Gaps, Is.Empty); }
        else
        {
            var gap = inventory.Gaps.Single();
            Assert.That(gap.Reason, Is.EqualTo("InventoryBudget"));
            Assert.That(gap.Start, Is.EqualTo(declarations[4096].SpanStart));
            Assert.That(gap.Length, Is.EqualTo(declarations[4096].Span.Length));
        }
    }

    [TestCase(32700, true)]
    [TestCase(32768, false)]
    public void CompilerProducedAttributeSyntaxObeysWholeInventoryNodeLimit(int attributeCount, bool expectedComplete)
    {
        var attributes = string.Join(",", Enumerable.Repeat("Tag", attributeCount));
        var source = "using System; [AttributeUsage(AttributeTargets.Method, AllowMultiple=true)] public sealed class TagAttribute:Attribute { } " +
            $"public static class Subject {{ [{attributes}] public static int Root() => 1; }}";
        var compilation = TestCompilation.Create("SyntaxBoundary", ("attributes.cs", source));
        EmitAndCheck(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot();
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        Assert.That(method.GetAttributes(), Has.Length.EqualTo(attributeCount));
        Assert.That(method.GetAttributes().All(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass?.Name == "TagAttribute")), Is.True);
        Assert.That(syntax.DescendantNodesAndSelf().Count() < 65536, Is.EqualTo(expectedComplete));
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        if (expectedComplete)
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(SymbolEqualityComparer.Default.Equals(inventory.Owners.Single().Method, method), Is.True);
        }
        else
        {
            Assert.That(inventory.Owners, Is.Empty);
            var gap = inventory.Gaps.Single();
            Assert.That(gap.Reason, Is.EqualTo("SyntaxBudget"));
            Assert.That(gap.Start, Is.EqualTo(syntax.SpanStart));
            Assert.That(gap.Length, Is.EqualTo(syntax.Span.Length));
        }
    }

    private static void EmitAndCheck(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
    }

    private static CompilerShadowPreparationBatch Prepare(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
    }
}
