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
public sealed class CompilerOwnedCompletedFieldModelTests
{
    [TestCase("bool", 0, false)]
    [TestCase("sbyte", 8, true)]
    [TestCase("byte", 8, false)]
    [TestCase("short", 16, true)]
    [TestCase("ushort", 16, false)]
    [TestCase("int", 32, true)]
    [TestCase("uint", 32, false)]
    [TestCase("long", 64, true)]
    [TestCase("ulong", 64, false)]
    public void CompilerOwnedFieldKeepsItsDeclaredScalarType(string csharpType, int width, bool signedness)
    {
        System.ArgumentNullException.ThrowIfNull(csharpType);
        var (factory, variable, field, declaredType) = LowerField(csharpType);
        var type = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, signedness);
        Assert.That(declaredType, Is.EqualTo(type));
        Assert.That(field.Type, Is.EqualTo(declaredType));
        var bits = width == 0 ? 0 : 1UL << (width - 1);
        IrTerm expected = width == 0 ? factory.Boolean(true) : factory.IntegerBits(type, bits);
        WithNativeModel(factory, variable, [Nonnull(factory, factory.Variable(variable)), Equal(factory, field, expected)],
            (_, _, _, values) =>
            {
                System.ArgumentNullException.ThrowIfNull(values);
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

    [TestCase("string", "string")]
    [TestCase("bool[]", "bool-array")]
    public void CompilerOwnedBuiltinFieldRecursesIntoTheDeclaredShape(string csharpType, string kind)
    {
        System.ArgumentNullException.ThrowIfNull(csharpType);
        System.ArgumentNullException.ThrowIfNull(kind);
        var (factory, variable, field, declaredType) = LowerField(csharpType);
        Assert.That(field.Type, Is.EqualTo(declaredType));
        WithNativeModel(factory, variable,
            [Nonnull(factory, factory.Variable(variable)), Nonnull(factory, field),
                Equal(factory, factory.Length(field), factory.Integer(3))],
            (_, _, _, values) =>
            {
                System.ArgumentNullException.ThrowIfNull(values);
                var evaluated = new IrInterpreter(factory).Evaluate(field, values);
                Assert.That(evaluated.Value, Is.Not.Null);
                AssertBuiltin(factory, evaluated.Value!, declaredType, kind, 3);
            });
    }

    [Test]
    public void CompilerOwnedSelfFieldTerminatesAndKeepsTheActualModelIdentity()
    {
        var (factory, variable, field, declaredType) = LowerField("Cell");
        var receiver = factory.Variable(variable);
        Assert.That(declaredType, Is.EqualTo(receiver.Type));
        Assert.That(field.Type, Is.EqualTo(declaredType));
        WithNativeModel(factory, variable, [Nonnull(factory, receiver), Equal(factory, field, receiver)],
            (_, _, _, values) =>
            {
                System.ArgumentNullException.ThrowIfNull(values);
                var evaluated = new IrInterpreter(factory).Evaluate(field, values);
                Assert.That(evaluated.Value, Is.Not.Null);
                Assert.That(evaluated.Value!.Reference, Is.SameAs(values[variable].Reference));
            });
    }

    private static (IrFactory Factory, IrVarId Variable, IrOpaqueTerm Field, IrTypeId DeclaredType) LowerField(string csharpType)
    {
        System.ArgumentNullException.ThrowIfNull(csharpType);
        var source = $"public sealed class Cell {{ public {csharpType} Value; }} public static class Example {{ public static {csharpType} Test(Cell cell) => cell.Value; }}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("CompilerOwnedCompletedField", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var emittedAssembly = new MemoryStream();
        var emitted = compilation.Emit(emittedAssembly);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var syntaxRoot = tree.GetRoot();
        var syntax = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var actualMethod = (IMethodSymbol)semanticModel.GetDeclaredSymbol(syntax)!;
        var fieldSyntax = syntaxRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var actualField = (IFieldSymbol)semanticModel.GetDeclaredSymbol(fieldSyntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(actualField.Type, actualMethod.ReturnType), Is.True);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lowering = new TotalLoweringContext(factory, actualMethod);
        Assert.That(lowering.Result, Is.Not.Null);
        // The maintained compiler mapper registers the method's actual return type.
        var declaredType = factory.GetVariableInfo(lowering.Result!.Value).Type;
        if (actualField.Type is IArrayTypeSymbol array)
        {
            Assert.That(array.IsSZArray, Is.True);
            Assert.That(array.Rank, Is.EqualTo(1));
            Assert.That(array.ElementType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            var nominalIdentity = CompilerIdentityBridge.InternType(factory, array);
            var registered = factory.GetOrCreateSequenceType(nominalIdentity, factory.BooleanType,
                CompilerIdentityBridge.CreateTypeDisplay(array));
            Assert.That(declaredType, Is.EqualTo(registered));
            Assert.That(declaredType, Is.Not.EqualTo(factory.GetOrCreateSequenceType(factory.BooleanType)));
            Assert.That(factory.GetTypeInfo(declaredType).Kind, Is.EqualTo(IrTypeKind.Sequence));
            Assert.That(factory.GetTypeInfo(declaredType).ElementType, Is.EqualTo(factory.BooleanType));
            Assert.That(factory.GetString(factory.GetTypeInfo(declaredType).Name),
                Is.EqualTo(CompilerIdentityBridge.CreateTypeDisplay(array)));
        }
        else if (csharpType == "string")
        {
            Assert.That(actualField.Type.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(declaredType, Is.EqualTo(factory.StringType));
        }
        var graph = ControlFlowGraph.Create(syntax, semanticModel);
        Assert.That(graph, Is.Not.Null);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph!, lowering);
        Assert.That(lowered.IsExact, Is.True);
        var assignments = lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrAssignInstruction>().ToArray();
        var actualRead = assignments.Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Value))
            .OfType<IrOpaqueTerm>().Single(term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (factory.GetString(factory.GetMemberInfo(term.Member).Name)
                .StartsWith("field:", StringComparison.Ordinal)));
        Assert.That(actualRead.Type, Is.EqualTo(declaredType));
        Assert.That(factory.GetMemberInfo(actualRead.Member).ReturnType, Is.EqualTo(declaredType));
        Assert.That(actualRead.Receiver, Is.TypeOf<IrCastTerm>());
        var receiverCast = (IrCastTerm)actualRead.Receiver!;
        Assert.That(receiverCast.Type, Is.EqualTo(factory.ObjectType));
        Assert.That(receiverCast.Operand, Is.TypeOf<IrVariableTerm>());
        var actualReceiver = (IrVariableTerm)receiverCast.Operand;
        Assert.That(actualReceiver.Type, Is.EqualTo(factory.GetVariableInfo(lowering.Parameters.Single().Entry).Type));
        var cursor = actualReceiver.Variable;
        var visited = new HashSet<IrVarId>();
        while (cursor != lowering.Parameters.Single().Entry)
        {
            Assert.That(visited.Add(cursor), Is.True, "Compiler receiver copy chain must terminate at the source input.");
            var copies = assignments.Where(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Target == cursor)).ToArray();
            Assert.That(copies.Length, Is.EqualTo(1));
            Assert.That(copies[0].Value, Is.TypeOf<IrVariableTerm>());
            cursor = ((IrVariableTerm)copies[0].Value).Variable;
        }
        // Use this same actual SSA receiver, not an unrelated entry variable.
        return (factory, actualReceiver.Variable, actualRead, declaredType);
    }

    private static void WithNativeModel(IrFactory factory, IrVarId variable, IrTerm[] predicates,
        Action<BvEncoder, Model, SmtQueryResourceMeter, Dictionary<IrVarId, IrValue>> inspect, bool decode = true)
    {
        System.ArgumentNullException.ThrowIfNull(predicates, nameof(predicates));
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(inspect);
        var query = new VerificationQuery(factory,
            predicates.Select(predicate => predicate is null ? throw new System.ArgumentNullException(nameof(predicate)) : (new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation())))),
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
    { System.ArgumentNullException.ThrowIfNull(factory, nameof(factory)); System.ArgumentNullException.ThrowIfNull(left, nameof(left)); System.ArgumentNullException.ThrowIfNull(right, nameof(right)); return factory.Binary(IrBinaryOperator.Equal, left, right); }
    private static IrTerm Nonnull(IrFactory factory, IrTerm term)
    { System.ArgumentNullException.ThrowIfNull(factory, nameof(factory)); System.ArgumentNullException.ThrowIfNull(term, nameof(term)); return factory.Binary(IrBinaryOperator.NotEqual, term, factory.Null(term.Type)); }
    private static void AssertBuiltin(IrFactory factory, IrValue value, IrTypeId declaredType, string kind, int count)
    {
        if (declaredType.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(declaredType));
        }
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(kind);
        System.ArgumentNullException.ThrowIfNull(value);
        Assert.That(value.Type, Is.EqualTo(declaredType));
        if (kind == "string")
        { Assert.That(value.String, Is.EqualTo(new string('\0', count))); }
        else
        {
            Assert.That(factory.GetTypeInfo(declaredType).Kind, Is.EqualTo(IrTypeKind.Sequence));
            Assert.That(factory.GetTypeInfo(declaredType).ElementType, Is.EqualTo(factory.BooleanType));
            Assert.That(value.Elements.Length, Is.EqualTo(count));
            foreach (var element in value.Elements)
            {
                Assert.That(element.Type, Is.EqualTo(factory.BooleanType));
                Assert.That(element.Boolean, Is.False);
            }
        }
    }
}
