using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void HashSetStateRejectsSourceDefinedFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public class HashSet<T> { public HashSet() {} public HashSet(int capacity) {} public bool Add(T value) => true; public bool Contains(T value) => true; public bool Remove(T value) => true; public int Count => -1; } }");
        var compilation = CSharpCompilation.Create("UserHashSet", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1")!;
        foreach (var member in type.GetMembers())
        {
            Assert.That(CSharpOperationSemantics.IsHashSetStateDeclaration(member), Is.False);
            if (member is IMethodSymbol method)
            { Assert.That(CSharpOperationSemantics.IsHashSetStateMember(method), Is.False); }
        }
    }

    [TestCase(SpecialType.System_Int32, true)]
    [TestCase(SpecialType.System_Boolean, true)]
    [TestCase(SpecialType.System_Double, false)]
    [TestCase(SpecialType.System_Decimal, false)]
    [TestCase(SpecialType.System_String, false)]
    [TestCase(SpecialType.System_Object, false)]
    public void HashSetMembershipRequiresAnApprovedEqualityDomain(SpecialType element, bool modeled)
    {
        var references = CSharpCompilation.Create("HashSetDomain", references: TestMetadataReferences.Platform);
        var type = references.GetTypeByMetadataName("System.Collections.Generic.HashSet`1")!.Construct(references.GetSpecialType(element));
        foreach (var name in ImmutableArray.Create("Add", "Contains", "Remove"))
        { Assert.That(CSharpOperationSemantics.IsHashSetStateMember(type.GetMembers(name).OfType<IMethodSymbol>().Single()), Is.EqualTo(modeled), name); }
        Assert.That(CSharpOperationSemantics.IsHashSetStateMember(type.GetMembers("Count").OfType<IPropertySymbol>().Single().GetMethod!), Is.True);
    }

    [TestCase(SpecialType.System_Int32, true)]
    [TestCase(SpecialType.System_Boolean, true)]
    [TestCase(SpecialType.System_Decimal, false)]
    [TestCase(SpecialType.System_String, false)]
    [TestCase(SpecialType.System_Object, false)]
    public void HashSetInterfaceMembershipRetainsTheEqualityDomainBoundary(SpecialType element, bool modeled)
    {
        var references = CSharpCompilation.Create("HashSetInterfaceDomain", references: TestMetadataReferences.Platform);
        var type = references.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")!.Construct(references.GetSpecialType(element));
        foreach (var name in ImmutableArray.Create("Add", "Contains", "Remove"))
        { Assert.That(CSharpOperationSemantics.IsHashSetStateMember(type.GetMembers(name).OfType<IMethodSymbol>().Single()), Is.EqualTo(modeled), name); }
        Assert.That(CSharpOperationSemantics.IsHashSetStateMember(type.GetMembers("Count").OfType<IPropertySymbol>().Single().GetMethod!), Is.True);
        Assert.That(TotalLoweringContext.IsHashSetReceiverType(type), Is.True);
    }

    [Test]
    public void HashSetInterfaceDeclarationsRejectFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public interface ICollection<T> { void Add(T value); bool Contains(T value); bool Remove(T value); int Count { get; } } }");
        var compilation = CSharpCompilation.Create("UserCollection", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")!;
        foreach (var member in type.GetMembers())
        {
            Assert.That(CSharpOperationSemantics.IsHashSetInterfaceStateDeclaration(member), Is.False);
            if (member is IMethodSymbol method)
            { Assert.That(CSharpOperationSemantics.IsHashSetStateMember(method), Is.False); }
        }
        Assert.That(TotalLoweringContext.IsHashSetReceiverType(type), Is.False);
        Assert.That(TotalLoweringContext.IsHashSetReceiverType(null), Is.False);
        Assert.Throws<ArgumentNullException>((Action)(() => CSharpOperationSemantics.IsHashSetInterfaceStateDeclaration(null!)));
    }

    [Test]
    public void HashSetInterfaceCountClausesRequireProvenReceiverIdentity()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(System.Collections.Generic.ICollection<int> data) => data.Count; }");
        var compilation = CSharpCompilation.Create("HashSetInterfaceClause", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = model.GetDeclaredSymbol(syntax)!;
        var operation = model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(new RoslynTotalExpressionLowerer(context).LowerClause(operation).Classification.IsExact, Is.False);
        context.SpecializeHashSetOriginParameter(method.Parameters.Single());
        Assert.That(new RoslynTotalExpressionLowerer(context).LowerClause(operation).Classification.IsExact, Is.True);
    }
    private static bool CheckHashSetStateIntrinsic(CSharpCompilation references, bool wrongClaim, bool throughInterface = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        const string interfaceCode = "public static class Probe { public static int Target() { var data = new System.Collections.Generic.HashSet<int>(1); System.Collections.Generic.ICollection<int> alias = data; var first = data.Add(17); alias.Add(23); alias.Add(17); var duplicate = data.Add(17); var removed = alias.Remove(17); var absent = alias.Remove(17); System.Collections.Generic.ICollection<bool> other = new System.Collections.Generic.HashSet<bool>(); other.Add(false); other.Add(true); other.Add(false); return first && !duplicate && removed && !absent && data.Contains(23) && !alias.Contains(17) && alias.Count == 1 && other.Count == 2 ? 1 : 0; } }";
        var tree = CSharpSyntaxTree.ParseText(throughInterface ? interfaceCode : "public static class Probe { public static int Target() { var data = new System.Collections.Generic.HashSet<int>(1); var alias = data; var first = data.Add(17); var duplicate = alias.Add(17); data.Add(23); var removed = alias.Remove(17); var absent = data.Remove(17); var other = new System.Collections.Generic.HashSet<bool>(); other.Add(false); other.Add(true); return first && !duplicate && removed && !absent && data.Contains(23) && !alias.Contains(17) && alias.Count == 1 && other.Count == 2 ? 1 : 0; } }");
        var compilation = references.AddSyntaxTrees(tree);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
        var runtime = new AssemblyLoadContext("HashSetInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
            return replay.Status == IrProgramExecutionStatus.Returned && replay.ConsumedApproximation && replay.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
        }
        finally { runtime.Unload(); }
    }
}
