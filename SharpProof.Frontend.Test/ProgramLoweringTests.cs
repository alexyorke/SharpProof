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
        var instructions = lowered.Instructions;

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
        var instructions = lowered.Instructions;
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
    public void RefLocalAliasesAbstainInsteadOfBecomingIndependentValues()
    {
        var lowered = Lower(
            """
            public static long Target(long first, long second) {
                ref long alias = ref first;
                alias = 10L;
                alias = ref second;
                alias = 20L;
                return checked(first + second);
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.False);
            Assert.That(
                lowered.Result.Abstentions.Select(static value => value.Reason),
                Does.Contain(FrontendAbstention.UnsupportedMutation));
        }
    }

    [Test]
    public void NestedRefInvocationCarriesCallAndMutationHavoc()
    {
        var lowered = Lower(
            """
            private static long Mutate(ref long value) => ++value;
            public static long Target(long value) {
                long result = Mutate(ref value) + 1L;
                return value;
            }
            """);
        var instructions = lowered.Instructions;
        var parameter = lowered.Result.Variables.Single(
            static binding =>
                binding.Symbol is IParameterSymbol
                {
                    Name: "value"
                });

        Assert.That(
            instructions.OfType<IrCallInstruction>(),
            Has.Exactly(1).Items);
        var havoc = instructions
            .OfType<IrHavocInstruction>()
            .Single(static instruction =>
                instruction.HavocKind ==
                IrHavocKind.VariablesAndMemory);
        Assert.That(havoc.Variables, Is.EqualTo(new[] { parameter.Variable }));
    }

    [Test]
    public void ImpureLocalFunctionCallHavocsCapturedLocals()
    {
        var lowered = Lower(
            """
            public static long Target(long value) {
                long captured = value;
                void Mutate() {
                    captured++;
                }
                Mutate();
                return captured;
            }
            """);
        var instructions = lowered.Instructions;
        var call = instructions
            .OfType<IrCallInstruction>()
            .Single();
        var captured = lowered.Result.Variables.Single(
            static binding =>
                binding.Symbol is ILocalSymbol
                {
                    Name: "captured"
                });
        var havoc = instructions
            .OfType<IrHavocInstruction>()
            .Single(instruction => instruction.Operation == call.Operation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                havoc.HavocKind,
                Is.EqualTo(IrHavocKind.VariablesAndMemory));
            Assert.That(
                havoc.Variables,
                Does.Contain(captured.Variable));
        }
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
        var assigned = lowered.Instructions
            .OfType<IrAssignInstruction>()
            .Select(static instruction => instruction.Target)
            .ToArray();
        Assert.That(
            assigned.Intersect(lowered.Result.Captures),
            Is.Not.Empty);
        Assert.That(
            lowered.Instructions
                .OfType<IrBranchInstruction>(),
            Is.Not.Empty);
    }

    [Test]
    public void RefConditionalAssignmentCannotUpdateOnlyTheSyntheticCapture()
    {
        var lowered = Lower(
            """
            public static long Target(bool choose, long left, long right) {
                (choose ? ref left : ref right) = 42L;
                return choose ? left : right;
            }
            """);
        var parameters = lowered.Result.Variables
            .Where(static binding =>
                binding.Symbol is IParameterSymbol
                {
                    Name: "left" or "right"
                })
            .Select(static binding => binding.Variable)
            .OrderBy(static variable => variable.Value)
            .ToArray();
        var havoced = lowered.Instructions
            .OfType<IrHavocInstruction>()
            .SelectMany(static havoc => havoc.Variables)
            .Distinct()
            .OrderBy(static variable => variable.Value)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.False);
            Assert.That(
                lowered.Result.Abstentions.Select(static value => value.Reason),
                Does.Contain(FrontendAbstention.UnsupportedMutation));
            Assert.That(havoced, Does.Contain(parameters[0]));
            Assert.That(havoced, Does.Contain(parameters[1]));
        }
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
        var parameter = result.Variables.Single(static binding =>
            binding.Symbol is IParameterSymbol { Name: "value" }).Variable;
        Assert.That(
            result.Program.Blocks
                .SelectMany(static block => block.Instructions)
                .OfType<IrHavocInstruction>()
                .SelectMany(static havoc => havoc.Variables),
            Does.Contain(parameter));
    }

    [Test]
    public void MandatoryFinallyControlFlowCannotRemainExact()
    {
        var lowered = Lower(
            """
            public static long Target(long value) {
                try { value = 1L; }
                finally { value = 2L; }
                return value;
            }
            """);
        var parameter = lowered.Result.Variables.Single(static binding =>
            binding.Symbol is IParameterSymbol { Name: "value" }).Variable;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.False);
            Assert.That(
                lowered.Result.Abstentions.Select(static value => value.Reason),
                Does.Contain(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(
                lowered.Instructions
                    .OfType<IrHavocInstruction>()
                    .SelectMany(static havoc => havoc.Variables),
                Does.Contain(parameter));
        }
    }

    [Test]
    public void ReachableCatchHandlerCannotBeOmittedFromExactLowering()
    {
        var lowered = Lower(
            """
            public static long Target(long value) {
                try {
                    return 10L / value;
                }
                catch (System.DivideByZeroException) {
                    return -1L;
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.False);
            Assert.That(
                lowered.Result.Abstentions.Select(static value => value.Reason),
                Does.Contain(FrontendAbstention.UnsupportedControlFlow));
        }
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
        var calls = lowered.Instructions
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
    public void AssignmentLocationsAreEvaluatedBeforeValues()
    {
        var lowered = Lower(
            """
            private static long Probe(long marker) => marker;
            public static long Target(long[] values) {
                values[Probe(1L)] = Probe(2L);
                return values[1];
            }
            """);
        var calls = lowered.Instructions
            .OfType<IrCallInstruction>()
            .ToArray();
        long[] expectedMarkers = [1L, 2L];

        Assert.That(calls, Has.Length.EqualTo(2));
        Assert.That(
            calls.Select(static call =>
                ((IrIntegerTerm)call.Arguments[0]).Value),
            Is.EqualTo(expectedMarkers));
    }

    [Test]
    public void NestedArrayReadsLoadFromProgramMemoryAfterStores()
    {
        var lowered = Lower(
            """
            public static long Target(long[] values) {
                values[0] = 41L;
                return checked(values[0] + 1L);
            }
            """);
        var instructions = lowered.Instructions;
        var store = instructions.OfType<IrStoreInstruction>().Single();
        var load = instructions.OfType<IrLoadInstruction>().Single();
        var returned = instructions.OfType<IrReturnInstruction>()
            .Single(static instruction => instruction.Value != null);
        var sum = (IrBinaryTerm)returned.Value!;
        var loadedValue = (IrVariableTerm)sum.Left;
        var storeLocation = (IrSequenceLocation)store.Location;
        var loadLocation = (IrSequenceLocation)load.Location;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.True);
            Assert.That(
                Array.IndexOf(instructions, store),
                Is.LessThan(Array.IndexOf(instructions, load)));
            Assert.That(
                loadLocation.Sequence.Id,
                Is.EqualTo(storeLocation.Sequence.Id));
            Assert.That(
                loadLocation.Index.Id,
                Is.EqualTo(storeLocation.Index.Id));
            Assert.That(loadedValue.Variable, Is.EqualTo(load.Target));
        }
    }

    [Test]
    public void RefReturnAssignmentTargetsAreEvaluatedBeforeValues()
    {
        var lowered = Lower(
            """
            private static long cell;
            private static ref long Pick() => ref cell;
            private static long Probe() => 2L;
            public static long Target() {
                Pick() = Probe();
                return cell;
            }
            """);
        var calls = lowered.Instructions
            .OfType<IrCallInstruction>()
            .Select(call => lowered.Factory.GetString(
                lowered.Factory.GetMemberInfo(call.Member).Name))
            .ToArray();

        Assert.That(calls, Has.Length.EqualTo(2));
        Assert.That(calls[0], Does.Contain("Pick"));
        Assert.That(calls[1], Does.Contain("Probe"));
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedMutation));
    }

    [Test]
    public void OrdinaryPropertyAccessAbstainsInsteadOfModelingPassiveMemory()
    {
        var lowered = Lower(
            """
            public sealed class Box {
                private long _value;
                public long Value {
                    get { _value++; return _value; }
                    set { _value += value; }
                }
            }
            public static long Target(Box box) {
                box.Value = 1L;
                return box.Value;
            }
            """);
        var instructions = lowered.Instructions;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowered.Result.IsExact, Is.False);
            Assert.That(
                lowered.Result.Abstentions.Select(static value => value.Reason),
                Does.Contain(FrontendAbstention.UnsupportedMemberAccess));
            Assert.That(instructions.OfType<IrLoadInstruction>(), Is.Empty);
            Assert.That(instructions.OfType<IrStoreInstruction>(), Is.Empty);
        }
    }

    [Test]
    public void RejectedPropertyAssignmentStillEvaluatesTheValue()
    {
        var lowered = Lower(
            """
            public sealed class Box {
                public long Value { get; set; }
            }
            private static long Mutate(ref long value) => ++value;
            public static long Target(Box box, long value) {
                box.Value = Mutate(ref value);
                return value;
            }
            """);
        var instructions = lowered.Instructions;
        var calls = instructions.OfType<IrCallInstruction>()
            .Select(call => lowered.Factory.GetString(
                lowered.Factory.GetMemberInfo(call.Member).Name))
            .ToArray();

        Assert.That(calls, Has.Length.EqualTo(1));
        Assert.That(calls[0], Does.Contain("Mutate"));
        Assert.That(
            instructions.OfType<IrHavocInstruction>()
                .Select(static havoc => havoc.HavocKind),
            Does.Contain(IrHavocKind.VariablesAndMemory));
    }

    [Test]
    public void UnsupportedCompoundAssignmentEvaluatesLocationBeforeValue()
    {
        var lowered = Lower(
            """
            private static long Probe(long marker) => marker;
            public static long Target(long[] values) {
                values[Probe(1L)] += Probe(2L);
                return values[0];
            }
            """);
        var calls = lowered.Instructions
            .OfType<IrCallInstruction>()
            .ToArray();
        long[] expectedMarkers = [1L, 2L];

        Assert.That(
            calls.Select(static call =>
                ((IrIntegerTerm)call.Arguments[0]).Value),
            Is.EqualTo(expectedMarkers));
        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedMutation));
    }

    [Test]
    public void ValuePositionMutationHavocsStateAndProducesUnknownValue()
    {
        var lowered = Lower(
            """
            public static long Target(long value) {
                var result = (value += 1L);
                return value;
            }
            """);
        var instructions = lowered.Instructions;

        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedMutation));
        Assert.That(
            instructions.OfType<IrHavocInstruction>()
                .Select(static havoc => havoc.HavocKind),
            Does.Contain(IrHavocKind.VariablesAndMemory));
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
        var call = lowered.Instructions
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
    public void PointerValuesAbstainInsteadOfBecomingReferences()
    {
        var lowered = Lower(
            """
            public static unsafe bool Target(int* value) => value == null;
            """);

        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void UnsupportedFieldValueDomainsAbstainBeforeMemoryLoads()
    {
        var lowered = Lower(
            """
            private struct Token { public long Value; }
            private static Token value;
            private static Token Target() => value;
            """);
        var instructions = lowered.Instructions;

        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedType));
        Assert.That(instructions.OfType<IrLoadInstruction>(), Is.Empty);
    }

    [Test]
    public void UnsupportedInvocationResultsAbstain()
    {
        var lowered = Lower(
            """
            private struct Token { public long Value; }
            private static Token Make() => default;
            private static Token Target() => Make();
            """);

        Assert.That(lowered.Result.IsExact, Is.False);
        Assert.That(
            lowered.Result.Abstentions.Select(static value => value.Reason),
            Does.Contain(FrontendAbstention.UnsupportedType));
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
        var instructions = lowered.Instructions;
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

    [Test]
    public void InheritedInstanceMembersShareIdentityAcrossReceiverTypes()
    {
        var lowered = Lower(
            """
            private class Base {
                public long Value;
            }
            private sealed class Derived : Base {
            }
            private static long Target(Base baseValue, Derived derivedValue) {
                baseValue.Value = 1L;
                derivedValue.Value = 2L;
                return baseValue.Value;
            }
            """);
        var instructions = lowered.Instructions;
        var members = instructions
            .OfType<IrStoreInstruction>()
            .Select(static instruction => (IrMemberLocation)instruction.Location)
            .Select(static location => location.Member)
            .Concat(instructions
                .OfType<IrLoadInstruction>()
                .Select(static instruction => (IrMemberLocation)instruction.Location)
                .Select(static location => location.Member))
            .ToArray();

        Assert.That(members, Has.Length.EqualTo(3));
        Assert.That(members.Distinct().ToArray(), Has.Length.EqualTo(1));
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
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));
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
        internal IrInstruction[] Instructions { get; } =
            [.. result.Program.Blocks.SelectMany(
                static block => block.Instructions)];
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
