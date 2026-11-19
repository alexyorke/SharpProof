using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// Which constructors the lowerer may run as part of a `: base(...)` chain:
// every base class down to object is a plain source class, an implicit
// `base()` binds one parameterless constructor, and a base constructor that
// dispatches a virtual member of `this` is refused.
[TestFixture]
public sealed class BaseConstructorChainAdmissionTests
{
    private const string Source = """
        public class Root { public Root() { } }
        public class Mid : Root { public Mid(int x) : base() { } }
        public class Leaf : Mid { public Leaf() : base(1) { } }
        public class OverException : System.Exception { }
        public class Below : OverException { }
        public class Finalizer { ~Finalizer() { } }
        public class AfterFinalizer : Finalizer { }
        public class WithOwnFinalizer : Root { ~WithOwnFinalizer() { } }
        public record class Rec { }
        public record class RecLeaf : Rec { }
        public class Primary(int x) { public int X => x; }
        public class OnPrimary : Primary { public OnPrimary() : base(1) { } }
        public class Optional { public Optional() { } public Optional(int x = 0) { } }
        public class OnOptional : Optional { }
        public class Hidden { private Hidden() { } public Hidden(int x) { } public class Nested : Hidden { } }
        public class Twice { public Twice() { } }
        public class OnTwice : Twice { }
        public struct Value { }
        public class D1 { public D1() { _ = Size; } public virtual int Size => 1; }
        public class D2 { public D2() { _ = base.ToString(); } }
        public class D3 { public D3() { System.Func<string> f = ToString; } }
        public class D4 { public virtual event System.Action V; public D4() { V += null; } }
        public class D5 : D1 { public D5() { _ = base.Size; } }
        public class D6 { public D6() : this(1) { } public D6(int x) { M(); } public virtual void M() { } }
        public class D7 { public D7() : this(1) { } public D7(int x) { N(); } public void N() { } }
        public class D8 { public D8() { _ = this.Size; } public int Size => 1; }
        """;

    [TestCase("Leaf", true)]
    [TestCase("Mid", true)]
    [TestCase("Root", true)]
    [TestCase("Below", false)]
    [TestCase("AfterFinalizer", false)]
    [TestCase("WithOwnFinalizer", false)]
    [TestCase("RecLeaf", false)]
    [TestCase("OnPrimary", false)]
    public void BaseChainsMustBeSourceClasses(string name, bool expected)
    {
        var type = Type(name);
        Assert.That(CSharpOperationSemantics.HasSourceBaseChain(type, TestContext.CurrentContext.CancellationToken), Is.EqualTo(expected));
    }

    [Test]
    public void OnlyCallersThatRunInitializersAdmitBaseChains()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var leaf = Type("Leaf").InstanceConstructors.Single();
        Assert.That(CSharpOperationSemantics.IsDirectObjectConstructor(leaf, true, cancellationToken), Is.True);
        Assert.That(CSharpOperationSemantics.IsDirectObjectConstructor(leaf, false, cancellationToken), Is.False);
        Assert.That(CSharpOperationSemantics.IsPlainConstructor(leaf, cancellationToken), Is.False);
        Assert.That(CSharpOperationSemantics.IsDirectObjectConstructor(Type("Below").InstanceConstructors.Single(), true, cancellationToken), Is.False);
        Assert.That(CSharpOperationSemantics.IsDirectObjectConstructor(Type("OnPrimary").InstanceConstructors.Single(), true, cancellationToken), Is.False);
    }

    [TestCase("Root", true, "")]
    [TestCase("OnTwice", true, "Twice")]
    [TestCase("Mid", true, "Root")]
    [TestCase("Leaf", false, "")]
    [TestCase("OnOptional", false, "")]
    [TestCase("Nested", false, "")]
    [TestCase("Below", false, "")]
    [TestCase("Value", false, "")]
    public void ImplicitBaseCallsBindOneParameterlessConstructor(string name, bool expected, string target)
    {
        System.ArgumentNullException.ThrowIfNull(target);
        var admitted = CSharpOperationSemantics.TryImplicitBaseConstructor(Type(name), TestContext.CurrentContext.CancellationToken, out var constructor);
        Assert.That(admitted, Is.EqualTo(expected));
        if (admitted)
        {
            Assert.That(constructor?.ContainingType.Name ?? "", Is.EqualTo(target));
        }
    }

    [TestCase("D1", true)]
    [TestCase("D2", false)]
    [TestCase("D3", true)]
    [TestCase("D4", true)]
    [TestCase("D5", false)]
    [TestCase("D6", true)]
    [TestCase("D7", false)]
    [TestCase("D8", false)]
    public void VirtualMembersOfThisAreDispatches(string name, bool expected)
    {
        var constructor = Type(name).InstanceConstructors.First(static candidate => candidate.Parameters.IsEmpty);
        Assert.That(CSharpOperationSemantics.ConstructorDispatchesOnThis(constructor, Compilation, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(expected));
    }

    [Test]
    public void DispatchIsAssumedWithoutTheDeclaringCompilation()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        Assert.That(CSharpOperationSemantics.ConstructorDispatchesOnThis(Type("D7").InstanceConstructors[0], null, cancellationToken), Is.True);
        Assert.That(CSharpOperationSemantics.ConstructorDispatchesOnThis(Type("OnTwice").InstanceConstructors.Single(), null, cancellationToken), Is.False);
    }

    [Test]
    public void BaseConstructorCallsNameTheDirectSourceBase()
    {
        var root = Type("Root").InstanceConstructors.Single();
        Assert.That(CSharpOperationSemantics.IsSourceBaseConstructorCall(root, Type("Mid")), Is.True);
        Assert.That(CSharpOperationSemantics.IsSourceBaseConstructorCall(root, Type("Leaf")), Is.False);
        Assert.That(CSharpOperationSemantics.IsSourceBaseConstructorCall(root, Type("Root")), Is.False);
        Assert.That(CSharpOperationSemantics.IsSourceBaseConstructorCall(root, null), Is.False);
        var exception = Type("Below").BaseType!.BaseType!.InstanceConstructors.First(static candidate => candidate.Parameters.IsEmpty);
        Assert.That(CSharpOperationSemantics.IsSourceBaseConstructorCall(exception, Type("OverException")), Is.False);
    }

    [TestCase("", true)]
    [TestCase("static Split() { System.Console.WriteLine(1); }", false)]
    public void PartialDerivedConstructionKeepsItsOpaqueCallBoundary(string typeInitializer, bool hasOpaqueCall)
    {
        System.ArgumentNullException.ThrowIfNull(typeInitializer);
        using var subject = TypedProgramSubject.Create("object Target() => new Split();",
            "public class Root { } public partial class Split : Root { public Split() { } " + typeInitializer + " } " +
            "public partial class Split { private readonly int value = 3; }");
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.EqualTo(hasOpaqueCall));
        if (hasOpaqueCall)
        {
            Assert.That(lowering.Program.Blocks.SelectMany(static block => block.Instructions).OfType<IrCallInstruction>(), Has.Exactly(1).Items);
        }
    }

    [TestCase("public class Middle<T> : Root { } public class Leaf : Middle<int> { }")]
    [TestCase("public class Leaf : Root { }")]
    public void RejectedImplicitBaseChainCanUseAnOpaqueConstruction(string chain)
    {
        System.ArgumentNullException.ThrowIfNull(chain);
        using var subject = TypedProgramSubject.Create("object Target() => new Leaf();",
            "public class Root { static Root() { System.Console.WriteLine(1); } public Root() { } } " + chain);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.Program.Blocks.SelectMany(static block => block.Instructions).OfType<IrCallInstruction>(), Has.Exactly(1).Items);
    }

    private static readonly CSharpCompilation Compilation = CSharpCompilation.Create("Chains",
        [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp12))],
        TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static INamedTypeSymbol Type(string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        Assert.That(Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return Compilation.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>().Single();
    }
}
