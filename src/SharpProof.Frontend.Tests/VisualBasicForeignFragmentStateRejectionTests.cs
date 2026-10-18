using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class VisualBasicForeignFragmentStateRejectionTests
{
    [TestCase("HomeSubject", "home", 7)]
    [TestCase("ForeignSubject", "foreign", 17)]
    public void MatchingOwnedFragmentAndWholeBodyRemainExact(string name, string local, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(local);
        System.ArgumentNullException.ThrowIfNull(name);
        using var subject = OwnedSubject.Create(name, local, expected);
        Assert.That(subject.Invoke(), Is.EqualTo(expected));
        var fragment = subject.LowerRead(subject.Read);
        Assert.That(fragment.Classification.IsExact, Is.True);
        Assert.That(subject.Context.Variables.Any(binding =>
            SymbolEqualityComparer.Default.Equals(binding.Symbol, subject.Read.Local)), Is.True);
        var lowered = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }

    [Test]
    public void RawForeignFragmentStateIsRejectedByTheActualOwningWholeBody()
    {
        using var home = OwnedSubject.Create("HomeSubject", "home", 7);
        using var foreign = OwnedSubject.Create("ForeignSubject", "foreign", 17);
        Assert.That(home.Invoke(), Is.EqualTo(7));
        Assert.That(foreign.Invoke(), Is.EqualTo(17));
        Assert.That(SymbolEqualityComparer.Default.Equals(home.Read.Local.ContainingSymbol,
            foreign.Read.Local.ContainingSymbol), Is.False);
        Assert.That(home.Context.OwnsBody(home.Graph.OriginalOperation), Is.True);
        Assert.That(home.Context.Variables, Is.Empty);
        // Deliberately invalid raw API composition: a real foreign fragment
        // enters the real home context through expression lowering only.
        // This is never a valid compiler preparation or executable proof input.
        var fragment = home.LowerRead(foreign.Read);
        Assert.That(fragment.Classification.IsExact, Is.True);
        Assert.That(home.Context.Variables.Any(binding =>
            SymbolEqualityComparer.Default.Equals(binding.Symbol, foreign.Read.Local)), Is.True);
        Assert.That(home.Context.Variables.Any(binding =>
            SymbolEqualityComparer.Default.Equals(binding.Symbol, home.Read.Local)), Is.False);
        Assert.That(home.Context.OwnsBody(home.Graph.OriginalOperation), Is.True);
        var lowered = new RoslynProgramLowerer(home.Factory).LowerCandidate(home.Graph, home.Context);
        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        // No interpreter or proof consumes this intentionally invalid state.
    }

    private sealed class OwnedSubject : IDisposable
    {
        private readonly AssemblyLoadContext _runtime;
        private readonly MethodInfo _target;
        private OwnedSubject(AssemblyLoadContext runtime, MethodInfo target,
            ControlFlowGraph graph, TotalLoweringContext context, ILocalReferenceOperation read)
        { System.ArgumentNullException.ThrowIfNull(context, nameof(context)); System.ArgumentNullException.ThrowIfNull(graph, nameof(graph)); System.ArgumentNullException.ThrowIfNull(read, nameof(read)); System.ArgumentNullException.ThrowIfNull(runtime, nameof(runtime)); System.ArgumentNullException.ThrowIfNull(target, nameof(target)); _runtime = runtime; _target = target; Graph = graph; Context = context; Read = read; }
        internal ControlFlowGraph Graph { get; }
        internal TotalLoweringContext Context { get; }
        internal IrFactory Factory => Context.Factory;
        internal ILocalReferenceOperation Read { get; }
        internal object? Invoke()
        { return _target.Invoke(null, null); }
        internal TotalBodyValue LowerRead(ILocalReferenceOperation read)
        {
            System.ArgumentNullException.ThrowIfNull(read);
            var builder = new IrProgramBuilder(Factory);
            var entry = builder.CreateBlock("fragment:entry");
            builder.SetEntry(entry);
            return new RoslynTotalExpressionLowerer(Context, builder).LowerBodyValue(read, entry);
        }
        internal static OwnedSubject Create(string name, string local, int initial)
        {
            System.ArgumentNullException.ThrowIfNull(local);
            System.ArgumentNullException.ThrowIfNull(name);
            var source = "Public Class " + name + "\nPublic Shared Function Target() As Integer\nDim " +
                local + " As Integer = " + initial.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\nReturn " + local + "\nEnd Function\nEnd Class";
            var tree = VisualBasicSyntaxTree.ParseText(source, path: name + ".vb");
            var compilation = VisualBasicCompilation.Create(name, [tree], TestMetadataReferences.Platform,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optionStrict: OptionStrict.On, optimizationLevel: OptimizationLevel.Release));
            Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
            using var image = new MemoryStream();
            Assert.That(compilation.Emit(image).Success, Is.True);
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
            var graph = ControlFlowGraph.Create(syntax, model)!;
            var method = compilation.GetTypeByMetadataName(name)!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var returned = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            var read = (ILocalReferenceOperation)model.GetOperation(returned.Expression!)!;
            Assert.That(read.Local.IsStatic, Is.False);
            Assert.That(read.Local.RefKind, Is.EqualTo(RefKind.None));
            Assert.That(read.Local.HasConstantValue, Is.False);
            Assert.That(read.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(read.Local.ContainingSymbol, method), Is.True);
            Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
            var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            var runtime = new AssemblyLoadContext(name, isCollectible: true);
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType(name)!.GetMethod("Target")!;
            return new(runtime, target, graph, context, read);
        }
        public void Dispose()
        { _runtime.Unload(); }
    }
}
