using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
internal sealed class RootParameterClosurePlanTests
{
    [TestCase("bool", ResourceStorageKind.Byte)]
    [TestCase("byte", ResourceStorageKind.Byte)]
    [TestCase("sbyte", ResourceStorageKind.Byte)]
    [TestCase("short", ResourceStorageKind.Word16)]
    [TestCase("ushort", ResourceStorageKind.Word16)]
    [TestCase("char", ResourceStorageKind.Word16)]
    [TestCase("int", ResourceStorageKind.Word32)]
    [TestCase("uint", ResourceStorageKind.Word32)]
    [TestCase("long", ResourceStorageKind.Word64)]
    [TestCase("ulong", ResourceStorageKind.Word64)]
    [TestCase("object", ResourceStorageKind.Reference)]
    [TestCase("string", ResourceStorageKind.Reference)]
    public void PrimitiveCapturesHaveOwnedFieldKinds(string type, ResourceStorageKind storage)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var (compilation, method) = Compile("Action", type + " value", "=>()=>GC.KeepAlive(value);");
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.Fields.Single(), Is.EqualTo(storage));
        Assert.That(plan.Captures.Single().Name, Is.EqualTo("value"));
        Assert.That(plan.HasLocals, Is.False);
    }

    [TestCase("float")]
    [TestCase("double")]
    [TestCase("decimal")]
    public void UnmodeledValueTypesDoNotPublishPartialFields(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var (compilation, method) = Compile("Action", type + " value", "=>()=>GC.KeepAlive(value);");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void LocalCapturesFollowParametersInTheirInitializationOrder()
    {
        var (compilation, method) = Compile("Action", "int first,long second",
            "{var local=second; int unused=0; return()=>GC.KeepAlive(first+local);}");
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan, Is.Not.Null);
        string[] names = ["first", "local"];
        ResourceStorageKind[] fields = [ResourceStorageKind.Word32, ResourceStorageKind.Word64];
        Assert.That(plan!.Captures.Select(static capture =>
            capture is null ? throw new System.ArgumentNullException(nameof(capture)) : capture.Name), Is.EqualTo(names));
        Assert.That(plan.Fields, Is.EqualTo(fields));
        Assert.That(plan.HasLocals, Is.True);
    }

    [TestCase("()=>value++")]
    [TestCase("()=>value=2")]
    [TestCase("()=>Set(ref value)")]
    [TestCase("()=>SetOut(out value)")]
    public void WritesThroughAnyAdmittedCaptureFormRejectThePlan(string callback)
    {
        System.ArgumentNullException.ThrowIfNull(callback);
        var (compilation, method) = Compile("Action", "int value", "=>" + callback + ";",
            "static void Set(ref int value) {value=1;} static void SetOut(out int value) {value=1;}");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase("const int local=1;")]
    [TestCase("int local;")]
    public void PreludeDeclarationsNeedInitializedMutableLocals(string prelude)
    {
        System.ArgumentNullException.ThrowIfNull(prelude);
        var (compilation, method) = Compile("Action", "int value", "{" + prelude + "return()=>GC.KeepAlive(value);}");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase("statements")]
    [TestCase("locals")]
    [TestCase("locals-at-limit")]
    [TestCase("root-walk")]
    [TestCase("captures")]
    [TestCase("cache-fields")]
    [TestCase("cache-fields-at-limit")]
    public void StorageAndTraversalLimitsDoNotPublishPartialPlans(string budget)
    {
        System.ArgumentNullException.ThrowIfNull(budget);
        var parameters = "int value";
        var result = "Action";
        string body;
        if (budget == "statements")
        {
            var statements = string.Concat(Enumerable.Range(0, 4096).Select(static index =>
                "int local" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=0;"));
            body = "{" + statements + "return()=>GC.KeepAlive(value);}";
        }
        else if (budget is "locals" or "locals-at-limit")
        {
            var count = ResourceAllocationShape.MaximumFields + (budget == "locals" ? 1 : 0);
            var locals = string.Join(",", Enumerable.Range(0, count).Select(static index =>
                "local" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=0"));
            body = "{int " + locals + ";return()=>GC.KeepAlive(value);}";
        }
        else if (budget == "root-walk")
        { body = "{int[] unused=new int[]{" + string.Join(",", Enumerable.Repeat("0", 4097)) + "};return()=>GC.KeepAlive(value);}"; }
        else
        {
            var count = ResourceAllocationShape.MaximumFields + (budget == "captures" ? 1 : budget == "cache-fields-at-limit" ? -1 : 0);
            var names = Enumerable.Range(0, count).Select(static index => "p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            parameters = string.Join(",", names.Select(static name =>
                name is null ? throw new System.ArgumentNullException(nameof(name)) : "int " + name));
            var values = string.Join(",", names);
            if (budget == "captures")
            { body = "=>()=>GC.KeepAlive(new int[]{" + values + "});"; }
            else
            { result = "Func<Func<int[]>>"; body = "=>()=>()=>new int[]{" + values + "};"; }
        }
        var (compilation, method) = Compile(result, parameters, body);
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan != null, Is.EqualTo(budget is "locals-at-limit" or "cache-fields-at-limit"));
        if (plan != null)
        { Assert.That(plan.Fields.Length, Is.EqualTo(budget == "locals-at-limit" ? 1 : ResourceAllocationShape.MaximumFields)); }
    }

    [Test]
    public void InitializerIntroducedLocalsNeedTheirOwnInitializationPlacementRule()
    {
        var (compilation, method) = Compile("Action", "int value",
            "{bool parsed=int.TryParse(\"\",out var other);return()=>GC.KeepAlive(other);}");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void DeferredQueryCapturesDoNotAcquireOrdinaryLocalStorageEvidence()
    {
        var (compilation, method) = Compile("Action", "int value",
            "=>()=>GC.KeepAlive(from other in new[]{value} select (Action)(()=>GC.KeepAlive(other)));", imports: "using System.Linq;");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase(16, true)]
    [TestCase(17, false)]
    public void NestedFunctionLimitCountsDeferredFunctionsBeyondTheRoot(int nested, bool admitted)
    {
        var result = string.Concat(Enumerable.Repeat("Func<", nested)) + "Action" + new string('>', nested);
        var callback = string.Concat(Enumerable.Repeat("()=>", nested + 1)) + "GC.KeepAlive(value)";
        var (compilation, method) = Compile(result, "int value", "=>" + callback + ";");
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan != null, Is.EqualTo(admitted));
        if (admitted)
        { Assert.That(plan!.Fields.Length, Is.EqualTo(nested + 1)); }
    }

    [Test]
    public void DeferredSiblingFramesCannotBorrowTheSingleChainPlacementRule()
    {
        var (compilation, method) = Compile("Func<Action>", "int value",
            "=>()=>{int later=0;Action first=()=>GC.KeepAlive(value+later);Action second=()=>GC.KeepAlive(value+later);return first;};");
        Assert.That(RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void PlanMatchingUsesTheActualLambdaAndRejectsMethodGroups()
    {
        var (compilation, method) = Compile("Action", "int value", "=>()=>GC.KeepAlive(value);",
            "public static Action Other()=>Noop; static void Noop() {} ");
        var plan = RootParameterClosurePlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken)!;
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var declaration in methods.Where(static declaration =>
            declaration is null ? throw new System.ArgumentNullException(nameof(declaration)) : declaration.Identifier.ValueText is "Target" or "Other"))
        {
            var operation = model.GetOperation(declaration)!;
            var creation = operation.Descendants().OfType<IDelegateCreationOperation>().Single();
            Assert.That(plan.Matches(creation), Is.EqualTo(declaration.Identifier.ValueText == "Target"));
        }
    }

    [Test]
    public void CancellationDoesNotReturnAPartialClosurePlan()
    {
        var (compilation, method) = Compile("Action", "int value", "=>()=>GC.KeepAlive(value);");
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();
        System.Action check = () => { _ = RootParameterClosurePlan.TryCreate(method, compilation, cancellation.Token); };
        Assert.Throws<System.OperationCanceledException>(check);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) Compile(string result, string parameters, string body, string extra = "", string imports = "")
    {
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(extra);
        System.ArgumentNullException.ThrowIfNull(imports);
        var tree = CSharpSyntaxTree.ParseText("using System; " + imports + " public static class Subject {public static " + result + " Target(" + parameters + ") " + body + extra + "}");
        var compilation = CSharpCompilation.Create("RootClosureBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return (compilation, compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single());
    }
}
