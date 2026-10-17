using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Z3;
using SharpProof.Frontend;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using IrTypeId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrTypeTag>;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeCompletedModelInvariantTests
{
    private static IEnumerable<TestCaseData> IntegerCases()
    {
        foreach (var width in new[] { 8, 16, 32, 64 })
        {
            foreach (var signedness in new[] { false, true })
            {
                foreach (var mode in new[] { "free", "zero", "high", "ones" })
                {
                    yield return new TestCaseData(width, signedness, mode);
                }
            }
        }
    }

    [TestCaseSource(nameof(IntegerCases))]
    public void CompletedScalarKeepsItsAdmittedIntegerType(int width, bool signedness, string mode)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, signedness);
        var variable = factory.CreateVariable("input", type);
        var bits = mode == "high" ? 1UL << (width - 1)
            : mode == "ones" ? width == 64 ? ulong.MaxValue : (1UL << width) - 1 : 0;
        IrTerm[] predicates = mode == "free" ? []
            : [Equal(factory, factory.Variable(variable), factory.IntegerBits(type, bits))];
        WithNativeModel(factory, variable, predicates, (encoder, model, meter, values) =>
        {
            var value = values[variable];
            Assert.That(value.Type, Is.EqualTo(type));
            Assert.That(value.IntegerBits, Is.EqualTo(bits));
            Assert.That(value.IntegerWidth, Is.EqualTo(width));
            Assert.That(value.IntegerSigned, Is.EqualTo(signedness));
            using var evaluated = model.Evaluate(encoder.GetVariable(variable, meter), true);
            Assert.That(evaluated, Is.TypeOf<BitVecNum>());
            Assert.That(((BitVecNum)evaluated).SortSize, Is.EqualTo((uint)width));
            Assert.That(((BitVecNum)evaluated).UInt64, Is.EqualTo(bits));
        });
    }

    [TestCase("free", false)]
    [TestCase("false", false)]
    [TestCase("true", true)]
    public void CompletedScalarIsAConcreteBoolean(string mode, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("flag", factory.BooleanType);
        IrTerm[] predicates = mode == "free" ? []
            : [Equal(factory, factory.Variable(variable), factory.Boolean(expected))];
        WithNativeModel(factory, variable, predicates, (encoder, model, meter, values) =>
        {
            Assert.That(values[variable].Type, Is.EqualTo(factory.BooleanType));
            Assert.That(values[variable].Boolean, Is.EqualTo(expected));
            using var evaluated = model.Evaluate(encoder.GetVariable(variable, meter), true);
            Assert.That(evaluated.IsTrue || evaluated.IsFalse, Is.True);
            Assert.That(evaluated.IsTrue, Is.EqualTo(expected));
        });
    }

    [TestCase("string", 0)]
    [TestCase("string", 1)]
    [TestCase("string", 3)]
    [TestCase("bool-array", 0)]
    [TestCase("bool-array", 1)]
    [TestCase("bool-array", 3)]
    [TestCase("int-array", 0)]
    [TestCase("int-array", 1)]
    [TestCase("int-array", 3)]
    public void NonnullBuiltinLengthDecodesIntoTheIndependentClrShape(string kind, int count)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        var term = factory.Variable(variable);
        WithNativeModel(factory, variable,
            [Nonnull(factory, term), Equal(factory, factory.Length(term), factory.Integer(count))],
            (_, _, _, values) => AssertBuiltin(factory, values[variable], kind, count));
    }

    [TestCase("string")]
    [TestCase("bool-array")]
    [TestCase("int-array")]
    public void NativeNullBuiltinDecodesAsNull(string kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        WithNativeModel(factory, variable, [Equal(factory, factory.Variable(variable), factory.Null(type))],
            (_, _, _, values) =>
            {
                Assert.That(values[variable].Type, Is.EqualTo(type));
                Assert.That(values[variable].Kind, Is.EqualTo(IrValueKind.Null));
            });
    }

    [TestCase("string")]
    [TestCase("bool-array")]
    [TestCase("int-array")]
    public void ActualEncodedLengthCanReachInt32MaxWithoutAllocatingAWitness(string kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        var term = factory.Variable(variable);
        WithNativeModel(factory, variable,
            [Nonnull(factory, term), Equal(factory, factory.Length(term), factory.Integer(int.MaxValue))],
            (encoder, model, meter, _) =>
            {
                var equality = encoder.EncodeBoolean(Equal(factory, factory.Length(term), factory.Integer(int.MaxValue)), meter);
                Assert.That(equality.FuncDecl.DeclKind, Is.EqualTo(Z3_decl_kind.Z3_OP_EQ));
                Assert.That(equality.NumArgs, Is.EqualTo(2U));
                var children = equality.Args;
                try
                {
                    using var evaluated = model.Evaluate(children[0], true);
                    Assert.That(evaluated, Is.TypeOf<BitVecNum>());
                    Assert.That(((BitVecNum)evaluated).SortSize, Is.EqualTo(32U));
                    Assert.That(((BitVecNum)evaluated).UInt64, Is.EqualTo((ulong)int.MaxValue));
                    Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(evaluated), Is.EqualTo(int.MaxValue));
                }
                finally
                {
                    foreach (var child in children)
                    { child.Dispose(); }
                }
            }, decode: false);
    }

    public static void CompilerOwnedFieldKeepsItsDeclaredScalarType(string csharpType, int width, bool signedness)
    {
        var (factory, variable, field) = LowerField(csharpType);
        var type = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, signedness);
        Assert.That(field.Type, Is.EqualTo(type));
        var bits = width == 0 ? 0 : 1UL << (width - 1);
        IrTerm expected = width == 0 ? factory.Boolean(true) : factory.IntegerBits(type, bits);
        WithNativeModel(factory, variable, [Nonnull(factory, factory.Variable(variable)), Equal(factory, field, expected)],
            (_, _, _, values) =>
            {
                var evaluated = new IrInterpreter(factory).Evaluate(field, values);
                Assert.That(evaluated.Value, Is.Not.Null);
                Assert.That(evaluated.Value!.Type, Is.EqualTo(type));
                if (width == 0)
                { Assert.That(evaluated.Value.Boolean, Is.True); }
                else
                {
                    Assert.That(evaluated.Value.IntegerBits, Is.EqualTo(bits));
                    Assert.That(evaluated.Value.IntegerWidth, Is.EqualTo(width));
                    Assert.That(evaluated.Value.IntegerSigned, Is.EqualTo(signedness));
                }
            });
    }

    public static void CompilerOwnedBuiltinFieldRecursesIntoTheDeclaredShape(string csharpType, string kind)
    {
        var (factory, variable, field) = LowerField(csharpType);
        Assert.That(field.Type, Is.EqualTo(BuiltinType(factory, kind)));
        WithNativeModel(factory, variable,
            [Nonnull(factory, factory.Variable(variable)), Nonnull(factory, field),
                Equal(factory, factory.Length(field), factory.Integer(3))],
            (_, _, _, values) =>
            {
                var evaluated = new IrInterpreter(factory).Evaluate(field, values);
                Assert.That(evaluated.Value, Is.Not.Null);
                AssertBuiltin(factory, evaluated.Value!, kind, 3);
            });
    }

    public static void CompilerOwnedSelfFieldTerminatesAndKeepsTheActualModelIdentity()
    {
        var (factory, variable, field) = LowerField("Cell");
        var receiver = factory.Variable(variable);
        Assert.That(field.Type, Is.EqualTo(receiver.Type));
        WithNativeModel(factory, variable, [Nonnull(factory, receiver), Equal(factory, field, receiver)],
            (_, _, _, values) =>
            {
                var evaluated = new IrInterpreter(factory).Evaluate(field, values);
                Assert.That(evaluated.Value, Is.Not.Null);
                Assert.That(evaluated.Value!.Reference, Is.SameAs(values[variable].Reference));
            });
    }

    private static (IrFactory Factory, IrVarId Variable, IrTerm Field) LowerField(string csharpType)
    {
        var source = $"public sealed class Cell {{ public {csharpType} Value; }} public static class Example {{ public static {csharpType} Test(Cell cell) => cell.Value; }}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("NativeCompletedField", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var emittedAssembly = new MemoryStream();
        var emitted = compilation.Emit(emittedAssembly);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lowering = new TotalLoweringContext(factory, (IMethodSymbol)semanticModel.GetDeclaredSymbol(syntax)!);
        var graph = ControlFlowGraph.Create(syntax, semanticModel);
        Assert.That(graph, Is.Not.Null);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph!, lowering);
        Assert.That(lowered.IsExact, Is.True);
        var binding = lowering.Parameters.Single();
        var actualRead = lowered.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAssignInstruction>().Select(instruction => instruction.Value)
            .OfType<IrOpaqueTerm>().Single(term => factory.GetString(factory.GetMemberInfo(term.Member).Name)
                .StartsWith("field:", StringComparison.Ordinal));
        return (factory, binding.Entry, IrSubstitution.Substitute(factory, actualRead, binding.Current, factory.Variable(binding.Entry)));
    }

    private static void WithNativeModel(IrFactory factory, IrVarId variable, IrTerm[] predicates,
        Action<BvEncoder, Model, SmtQueryResourceMeter, Dictionary<IrVarId, IrValue>> inspect, bool decode = true)
    {
        var query = new VerificationQuery(factory,
            predicates.Select(predicate => new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [variable]);
        using var context = new Context();
        using var solver = context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            var meter = new SmtQueryResourceMeter(IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
            encoder.ValidateQuery(query, meter, TestContext.CurrentContext.CancellationToken);
            foreach (var predicate in predicates)
            { solver.Assert(encoder.EncodeBoolean(predicate, meter)); }
            foreach (var fact in encoder.ReferenceFacts)
            { solver.Assert(fact); }
            var goalSelector = owner.Own(context.MkBoolConst("actual-goal-selector"));
            solver.Assert(owner.Own(context.MkImplies(goalSelector,
                owner.Own(context.MkNot(encoder.EncodeBoolean(query.Goal.Predicate, meter))))));
            using var parameters = context.MkParams();
            SmtNativeUtilities.AddOwnedParameter(parameters, context.MkSymbol("rlimit"), meter.GetRemainingBudget());
            solver.Parameters = parameters;
            Assert.That(SmtNativeCheck.Run(solver, [goalSelector], meter), Is.EqualTo(Status.SATISFIABLE));
            using var model = solver.Model;
            Assert.That(model, Is.Not.Null);
            var values = decode ? encoder.DecodeModel(query, model, meter)! : new Dictionary<IrVarId, IrValue>();
            if (decode)
            { Assert.That(values.Keys, Is.EquivalentTo(new[] { variable })); }
            inspect(encoder, model, meter, values);
        }
        finally
        {
            solver.Dispose();
            owner.Dispose();
            encoder.DisposeReferences();
        }
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    { return factory.Binary(IrBinaryOperator.Equal, left, right); }
    private static IrTerm Nonnull(IrFactory factory, IrTerm term)
    { return factory.Binary(IrBinaryOperator.NotEqual, term, factory.Null(term.Type)); }
    private static IrTypeId BuiltinType(IrFactory factory, string kind)
    {
        return kind == "string" ? factory.StringType
            : factory.GetOrCreateSequenceType(kind == "bool-array" ? factory.BooleanType : factory.IntegerType);
    }
    private static void AssertBuiltin(IrFactory factory, IrValue value, string kind, int count)
    {
        Assert.That(value.Type, Is.EqualTo(BuiltinType(factory, kind)));
        if (kind == "string")
        { Assert.That(value.String, Is.EqualTo(new string('\0', count))); }
        else
        {
            Assert.That(value.Elements.Length, Is.EqualTo(count));
            foreach (var element in value.Elements)
            {
                Assert.That(element.Type, Is.EqualTo(kind == "bool-array" ? factory.BooleanType : factory.IntegerType));
                if (kind == "bool-array")
                { Assert.That(element.Boolean, Is.False); }
                else
                { Assert.That(element.IntegerBits, Is.Zero); }
            }
        }
    }
}
