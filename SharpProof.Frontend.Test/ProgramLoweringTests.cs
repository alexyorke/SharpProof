using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ProgramLoweringTests {
    [Test]
    public void CfgLowersAssignmentsBranchesCallsAndReturns() {
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
    public void WritesAndRefCallsCarryExplicitMutationHavoc() {
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
                binding.Symbol is IParameterSymbol {
                    Name: "value"
                });
        Assert.That(havoc.Variables[0], Is.EqualTo(parameter.Variable));
    }

    [Test]
    public void OnlySpecBackedPureCallsAvoidMemoryHavoc() {
        const string source =
            """
            private static long Read(long value) => value;
            public static long Target(long value) => Read(value);
            """;
        var unknown = Lower(source);
        var known = Lower(
            source,
            static method => method.Name == "Read");

        using (Assert.EnterMultipleScope()) {
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
    public void FlowCapturesBecomeStableVariablesAndAssignments() {
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
    public void UnsupportedMutationAbstainsAndHavocsWithoutThrowing() {
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
    public void ProgramLoweringOrderAndIdentifiersAreDeterministic() {
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
    public void InheritedInstanceMembersRetainTypedProgramReceivers() {
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
        foreach (var (member, receiver) in memberReceivers) {
            Assert.That(receiver, Is.Not.Null);
            Assert.That(
                lowered.Factory.GetMemberInfo(member).DeclaringType,
                Is.EqualTo(receiver!.Type));
        }
    }

    private static string Shape(IrProgram program) =>
        string.Join(
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

    private static LoweredProgram Lower(
        string members,
        Func<IMethodSymbol, bool>? isKnownPure = null) {
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
        FrontendProgramLoweringResult result) {
        internal IrFactory Factory { get; } = factory;
        internal FrontendProgramLoweringResult Result { get; } = result;
    }

    private static ImmutableArray<MetadataReference> PlatformReferences { get; } =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(static path =>
            (MetadataReference)MetadataReference.CreateFromFile(path))];
}
