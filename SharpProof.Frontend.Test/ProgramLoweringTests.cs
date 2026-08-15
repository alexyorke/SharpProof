using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ProgramLoweringTests
{
    [Test]
    public void NegativeLiteralReturnsRemainExact()
    {
        var lowered = Lower(
            """
            public static int Target(int left, int right) {
                if (left < right) {
                    return -1;
                }
                return 1;
            }
            """);

        Assert.That(
            lowered.Result.IsExact,
            Is.True,
            string.Join(
                Environment.NewLine,
                lowered.Result.Abstentions.Select(value =>
                    lowered.Factory.GetString(
                        lowered.Factory.GetOperationInfo(value.Operation)
                            .Description!.Value) +
                    ":" + value.Reason)));
    }

    [Test]
    public void CfgLowersAssignmentsBranchesCallsAndReturns()
    {
        var lowered = Lower(
            """
            private static long Next(long value) => checked(value + 1L);
            public static long Target(bool choose, long value) {
                long current = value;
                if (choose)
                    current = checked(current + 2L);
                else
                    current = checked(current - 2L);
                return Next(current);
            }
            """);
        var instructions = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .ToArray();

        Assert.That(lowered.Result.IsExact, Is.True);
        Assert.That(
            instructions.OfType<IrAssignInstruction>().Count(),
            Is.GreaterThanOrEqualTo(3));
        Assert.That(
            instructions.OfType<IrBranchInstruction>(),
            Has.Exactly(1).Items);
        Assert.That(
            instructions.OfType<IrCallInstruction>(),
            Has.Exactly(1).Items);
        Assert.That(
            instructions.OfType<IrReturnInstruction>(),
            Is.Not.Empty);
        var call = instructions.OfType<IrCallInstruction>().Single();
        var member = lowered.Factory.GetMemberInfo(call.Member);
        Assert.That(
            lowered.Factory.GetString(member.Name),
            Does.Contain("Next"));
    }

    [Test]
    public void WritesAndRefCallsCarryExplicitMutationHavoc()
    {
        var lowered = Lower(
            """
            public sealed class Box {
                public long Value;
            }
            private static void Change(ref long value) => value++;
            public static long Target(Box box, long value) {
                box.Value = value;
                Change(ref value);
                return value;
            }
            """);
        var instructions = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .ToArray();

        Assert.That(
            instructions.OfType<IrStoreInstruction>(),
            Has.Exactly(1).Items);
        Assert.That(
            instructions.OfType<IrCallInstruction>(),
            Has.Exactly(1).Items);
        var havoc = instructions
            .OfType<IrHavocInstruction>()
            .Single(static instruction =>
                instruction.HavocKind ==
                IrHavocKind.VariablesAndMemory);
        Assert.That(havoc.Variables, Has.Length.EqualTo(1));
        var parameter = lowered.Result.Variables.Single(
            static binding =>
                binding.Symbol is IParameterSymbol
                {
                    Name: "value"
                });
        Assert.That(havoc.Variables[0], Is.EqualTo(parameter.Variable));
    }

    [Test]
    public void OnlySpecBackedPureCallsAvoidMemoryHavoc()
    {
        const string source =
            """
            private static long Read(long value) => value;
            public static long Target(long value) => Read(value);
            """;
        var unknown = Lower(source);
        var known = Lower(
            source,
            static method => method.Name == "Read");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unknown.Result.Program.Blocks
                    .SelectMany(static block => block.Instructions)
                    .OfType<IrHavocInstruction>()
                    .Any(static instruction =>
                        instruction.HavocKind == IrHavocKind.Memory),
                Is.True);
            Assert.That(
                known.Result.Program.Blocks
                    .SelectMany(static block => block.Instructions)
                    .OfType<IrHavocInstruction>(),
                Is.Empty);
        }
    }

    [Test]
    public void FlowCapturesBecomeStableVariablesAndAssignments()
    {
        var lowered = Lower(
            """
            public static string Target(string? value) =>
                value ?? "fallback";
            """);

        Assert.That(lowered.Result.Captures, Is.Not.Empty);
        var assigned = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrAssignInstruction>()
            .Select(static instruction => instruction.Target)
            .ToArray();
        Assert.That(
            assigned.Intersect(lowered.Result.Captures),
            Is.Not.Empty);
        Assert.That(
            lowered.Result.Program.Blocks
                .SelectMany(static block => block.Instructions)
                .OfType<IrBranchInstruction>(),
            Is.Not.Empty);
    }

    [Test]
    public void UnsupportedMutationAbstainsAndHavocsWithoutThrowing()
    {
        FrontendProgramLoweringResult? result = null;

        Assert.DoesNotThrow(
            (Action)(() =>
                result = Lower(
                    """
                    public static long Target(long value) {
                        value++;
                        return value;
                    }
                    """).Result));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsExact, Is.False);
        Assert.That(
            result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedMutation));
        Assert.That(
            result.Program.Blocks
                .SelectMany(static block => block.Instructions)
                .OfType<IrHavocInstruction>(),
            Is.Not.Empty);
    }

    [Test]
    public void InvocationLoweringPreservesReceiverAndSourceArgumentOrder()
    {
        var lowered = Lower(
            """
            private sealed class Receiver {
            }
            private static Receiver GetReceiver() => new();
            private static long Probe(long marker, long value) => value;
            private static long Optional(long value, long fallback = 7L) =>
                checked(value + fallback);
            private static long Ext(this Receiver receiver, long value) => value;
            private static long Direct(long first, long second) =>
                checked(first + second);
            public static long Target(long value) =>
                Direct(
                    second: GetReceiver().Ext(Probe(2L, value)),
                    first: Optional(Ext(GetReceiver(), Probe(1L, value))));
            """);
        var calls = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .ToArray();
        var names = calls
            .Select(call => lowered.Factory.GetString(
                lowered.Factory.GetMemberInfo(call.Member).Name))
            .ToArray();

        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedInvocationShape));
        Assert.That(calls, Has.Length.EqualTo(8));
        string[] expectedNames = [
            "GetReceiver", "Probe", "Ext", "GetReceiver",
            "Probe", "Ext", "Optional", "Direct"
        ];
        for (var index = 0; index < expectedNames.Length; index++)
        {
            Assert.That(names[index], Does.Contain(expectedNames[index]));
        }

        var probes = calls
            .Where((_, index) => names[index].Contains(
                "Probe",
                StringComparison.Ordinal))
            .ToArray();
        long[] expectedMarkers = [2L, 1L];
        Assert.That(
            probes.Select(static call =>
                ((IrIntegerTerm)call.Arguments[0]).Value),
            Is.EqualTo(expectedMarkers));
        var extensions = calls
            .Where((_, index) => names[index].Contains(
                "Ext",
                StringComparison.Ordinal))
            .ToArray();
        Assert.That(
            extensions.Select(static call => call.Receiver),
            Is.All.Null);
        Assert.That(
            extensions.Select(static call => call.Arguments.Length),
            Is.All.EqualTo(2));
        var optional = calls
            .Where((_, index) => names[index].Contains(
                "Optional",
                StringComparison.Ordinal))
            .Single();
        Assert.That(optional.Arguments, Has.Length.EqualTo(2));
        Assert.That(
            ((IrIntegerTerm)optional.Arguments[1]).Value,
            Is.EqualTo(7L));
    }

    [Test]
    public void InvocationLoweringOrdersArgumentsByRoslynParameterOrdinal()
    {
        var lowered = Lower(
            """
            private static T Select<T>(T first, T second) => first;
            public static long Target(long first, long second) =>
                Select(second: second, first: first);
            """);
        var call = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Single();

        Assert.That(lowered.Result.IsExact, Is.True);
        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        var first = lowered.Result.Variables.Single(static binding =>
            binding.Symbol is IParameterSymbol { Name: "first" }).Variable;
        var second = lowered.Result.Variables.Single(static binding =>
            binding.Symbol is IParameterSymbol { Name: "second" }).Variable;
        Assert.That(
            call.Arguments.Select(static term => ((IrVariableTerm)term).Variable),
            Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void ProgramLoweringOrderAndIdentifiersAreDeterministic()
    {
        const string source =
            """
            public static long Target(bool choose, long left, long right) {
                long result = left;
                if (choose)
                    result = right;
                return result;
            }
            """;
        var first = Lower(source).Result.Program;
        var second = Lower(source).Result.Program;

        Assert.That(
            Shape(first),
            Is.EqualTo(Shape(second)));
    }

    [Test]
    public void InheritedInstanceMembersRetainTypedProgramReceivers()
    {
        var lowered = Lower(
            """
            private class Base {
                public long Value;
                public long Read() => Value;
            }
            private sealed class Derived : Base {
            }
            private static long Target(Derived value) {
                value.Value = 1L;
                return value.Read();
            }
            """);
        var instructions = lowered.Result.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .ToArray();
        var storedMemberReceivers = instructions
            .OfType<IrStoreInstruction>()
            .Select(static instruction =>
                (IrMemberLocation)instruction.Location)
            .Select(static location =>
                (Member: location.Member, Receiver: location.Receiver));
        var calledMemberReceivers = instructions
            .OfType<IrCallInstruction>()
            .Select(static call =>
                (Member: call.Member, Receiver: call.Receiver));
        var memberReceivers = storedMemberReceivers
            .Concat(calledMemberReceivers)
            .ToArray();

        Assert.That(lowered.Result.IsExact, Is.True);
        Assert.That(memberReceivers, Has.Length.EqualTo(2));
        foreach (var (member, receiver) in memberReceivers)
        {
            Assert.That(receiver, Is.Not.Null);
            Assert.That(
                lowered.Factory.GetMemberInfo(member).DeclaringType,
                Is.EqualTo(receiver!.Type));
        }
    }

    private static string Shape(IrProgram program)
    {
        return string.Join(
            "|",
            program.Blocks.Select(block =>
                block.Id.Value +
                ":" +
                string.Join(
                    ",",
                    block.Instructions.Select(instruction =>
                        instruction.Id.Value +
                        "-" +
                        instruction.Kind))));
    }

    private static LoweredProgram Lower(
        string members,
        Func<IMethodSymbol, bool>? isKnownPure = null)
    {
        var source =
            """
            #nullable enable
            public static class Subject {
            """ +
            Environment.NewLine +
            members +
            Environment.NewLine +
            "}";
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "SharpProof.Frontend.ProgramTests",
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                checkOverflow: false,
                nullableContextOptions: NullableContextOptions.Enable));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            diagnostics,
            Is.Empty,
            string.Join(Environment.NewLine, diagnostics.AsEnumerable()));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method =>
                method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var graph = ControlFlowGraph.Create(method, model);
        var factory = new IrFactory();
        return new LoweredProgram(
            factory,
            new RoslynProgramLowerer(
                factory,
                isKnownPure).Lower(graph!));
    }

    private sealed class LoweredProgram(
        IrFactory factory,
        FrontendProgramLoweringResult result)
    {
        internal IrFactory Factory { get; } = factory;
        internal FrontendProgramLoweringResult Result { get; } = result;
    }

    private static ImmutableArray<MetadataReference> PlatformReferences
    {
        get;
    } =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(static path =>
            (MetadataReference)MetadataReference.CreateFromFile(path))];
}
