using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GoldenLoweringTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("lowering");
    }

    [TestCaseSource(nameof(Cases))]
    public void ProgramLoweringMatchesGolden(string caseName)
    {
        var fixture = GoldenTest.Load("lowering", caseName);
        var tree = CSharpSyntaxTree.ParseText(fixture.Source, new CSharpParseOptions(LanguageVersion.CSharp12), caseName + ".cs");
        var bindContracts = fixture.Source.Contains("// golden-contracts: true", StringComparison.Ordinal);
        var references = bindContracts ? TestMetadataReferences.Platform.Add(
            MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.Contract).Assembly.Location)) : TestMetadataReferences.Platform;
        var compilation = CSharpCompilation.Create("GoldenLowering", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(node => node.Identifier.ValueText == "Target").ToArray();
        var method = methods.Single(node => node.Body != null || node.ExpressionBody != null);
        var total = fixture.Source.StartsWith("// golden-mode: Total\n", StringComparison.Ordinal);
        var factory = new IrFactory(total ? IrExecutionSemantics.Total : IrExecutionSemantics.Legacy);
        var model = compilation.GetSemanticModel(tree);
        var graph = ControlFlowGraph.Create(method, model)!;
        TotalLoweringContext? context = null;
        TotalContractBindingResult? contracts = null;
        if (total)
        {
            var symbol = (IMethodSymbol)model.GetDeclaredSymbol(methods[0])!;
            if (fixture.Source.Contains("// golden-context: constructed", StringComparison.Ordinal))
            {
                symbol = symbol.Construct(compilation.GetSpecialType(SpecialType.System_String));
            }
            context = new(factory, symbol);
            if (bindContracts)
            {
                contracts = new ContractBinder(compilation, factory).BindTotal(context);
            }
        }
        var sourceCalls = fixture.Source.Contains("// golden-inline-source: true", StringComparison.Ordinal);
        var frameworkModels = fixture.Source.Contains("// golden-framework-models: true", StringComparison.Ordinal);
        var result = context == null ? new RoslynProgramLowerer(factory).Lower(graph)
            : frameworkModels ? new RoslynProgramLowerer(factory).LowerCandidate(graph, context, PrepareCallee, null,
                CancellationToken.None, FrameworkModel)
            : sourceCalls ? new RoslynProgramLowerer(factory).LowerCandidate(graph, context, PrepareCallee, CancellationToken.None)
            : new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        var printer = new IrPrinter(factory);
        var terms = new Dictionary<IrId, IrTerm>();
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"mode: {factory.Semantics}");
        output.AppendLine(CultureInfo.InvariantCulture, $"classification: {result.Classification.Decision}/{result.Classification.Abstention}");
        output.AppendLine(CultureInfo.InvariantCulture, $"entry: {result.Program.Entry}");
        if (context != null)
        {
            foreach (var parameter in context.Parameters)
            {
                output.AppendLine(CultureInfo.InvariantCulture,
                    $"parameter: {parameter.Parameter.Ordinal} entry={Variable(parameter.Entry)} current={Variable(parameter.Current)} old={Variable(parameter.PreState)}");
            }
            output.AppendLine("result: " + (context.Result is { } resultVariable ? Variable(resultVariable) : "void"));
        }
        if (contracts != null)
        {
            output.AppendLine("contract-binding: " + contracts.Failure);
            foreach (var clause in contracts.Clauses)
            {
                output.AppendLine(CultureInfo.InvariantCulture,
                    $"contract: {clause.Kind} safety={printer.Print(clause.SafeCondition)} value={printer.Print(clause.Value)}");
                RememberTerm(clause.SafeCondition);
                RememberTerm(clause.Value);
            }
        }
        foreach (var binding in result.Variables.OrderBy(binding => binding.Variable.Value))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"variable: {Variable(binding.Variable)} source={binding.Symbol.Name}");
        }
        foreach (var block in result.Program.Blocks.OrderBy(block => block.Id.Value))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"block: {block.Id} {(block.Name is { } name ? factory.GetString(name) : "unnamed")}");
            foreach (var instruction in block.Instructions)
            {
                var span = factory.GetOperationInfo(instruction.Operation).SourceSpan;
                var source = total ? span == null ? " source=none" : $" source={span.Document}:{span.Start}+{span.Length}" : "";
                output.AppendLine(CultureInfo.InvariantCulture, $"  {instruction.Id} {instruction.Operation} {Format(instruction)}{source}");
                RememberInstruction(instruction);
            }
        }
        foreach (var term in terms.Values.OrderBy(term => term.Id.Value))
        {
            var type = factory.GetTypeInfo(term.Type);
            var bits = term is IrIntegerTerm integer ? " bits=" + integer.Bits.ToString(CultureInfo.InvariantCulture) : "";
            output.AppendLine(CultureInfo.InvariantCulture, $"term: {term.Id} {term.Kind} type={term.Type}:{factory.GetString(type.Name)} width={type.Width} signed={type.Signed}{bits} value={printer.Print(term)}");
        }
        foreach (var diagnostic in result.Abstentions.OrderBy(diagnostic => diagnostic.Operation.Value))
        {
            var description = factory.GetOperationInfo(diagnostic.Operation).Description;
            output.AppendLine(CultureInfo.InvariantCulture, $"diagnostic: {diagnostic.Operation} {diagnostic.Reason} {(description is { } text ? factory.GetString(text) : "none")}");
        }
        GoldenTest.Compare(fixture, output.ToString());

        TotalScalarCallModel? FrameworkModel(IMethodSymbol target)
        {
            if (target.IsStatic && target.ContainingType.SpecialType == SpecialType.System_String &&
                target.Name == "Concat" && target.Parameters.Length == 2 &&
                target.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_String))
            { return new(2, arguments => CSharpOperationSemantics.StringConcat(factory, arguments[0], arguments[1]), stringConcatenation: true); }
            if (target.IsStatic && target.ContainingType.SpecialType == SpecialType.System_Array &&
                target.Name == "Empty" && target.Arity == 1 && target.Parameters.IsEmpty &&
                CSharpOperationSemantics.IsReferenceDomain(target.ReturnType))
            { return new(0, _ => CSharpOperationSemantics.ArrayEmpty(factory, new RoslynTypeMapper(factory).GetTypeId(target.ReturnType))); }
            return null;
        }

        bool PrepareCallee(TotalLoweringContext frame)
        {
            if (!bindContracts)
            { return true; }
            var binding = new ContractBinder(compilation, factory).BindTotal(frame);
            return binding.IsSuccess && binding.Clauses.All(clause => clause.Kind != BoundContractKind.Assume);
        }

        string Variable(IrVarId id)
        {
            var type = factory.GetTypeInfo(factory.GetVariableInfo(id).Type);
            return $"{id}:{factory.GetString(type.Name)}[width={type.Width},signed={type.Signed}]";
        }

        string Format(IrInstruction instruction)
        {
            return instruction switch
            {
                IrAllocationInstruction value => $"Allocate {value.AllocatedType}:{factory.GetString(factory.GetTypeInfo(value.AllocatedType).Name)}" +
                    (value.Target is { } target ? $" -> {Variable(target)}" : "") +
                    (value.Length is { } length ? $" length={printer.Print(length)}" : "") +
                    (!value.InitialValues.IsEmpty ? $" elements=[{string.Join(", ", value.InitialValues.Select(printer.Print))}]" : ""),
                IrWriteInstruction value => $"Write {value.Region}",
                IrLockInstruction value => $"Lock {printer.Print(value.Receiver)}",
                IrAssignInstruction value => $"Assign {Variable(value.Target)} = {printer.Print(value.Value)}",
                IrLoadInstruction value => $"Load {Variable(value.Target)} = {Location(value.Location)}",
                IrStoreInstruction value => $"Store {Location(value.Location)} = {printer.Print(value.Value)}",
                IrCallInstruction value => $"Call {(value.Target is { } target ? Variable(target) : "void")} " +
                    $"member={value.Member} receiver={(value.Receiver == null ? "none" : printer.Print(value.Receiver))} " +
                    $"args=[{string.Join(", ", value.Arguments.Select(printer.Print))}]",
                IrAssumeInstruction value => $"Assume {printer.Print(value.Condition)}",
                IrAssertInstruction value => $"Assert {printer.Print(value.Condition)}",
                IrHavocInstruction value => $"Havoc {value.HavocKind}/{value.Origin} [{string.Join(", ", value.Variables.Select(Variable))}]",
                IrBranchInstruction value => $"Branch {printer.Print(value.Condition)} ? {value.WhenTrue} : {value.WhenFalse}",
                IrGotoInstruction value => $"Goto {value.Target}",
                IrReturnInstruction value => $"Return {(value.Value == null ? "void" : printer.Print(value.Value))}",
                IrThrowInstruction value => $"Throw {value.ExceptionKind} -> {value.Target}",
                IrExceptionalExitInstruction => "ExceptionalExit",
                _ => throw new InvalidOperationException("Unknown golden instruction.")
            };
        }

        string Location(IrLocation location)
        {
            return location switch
            {
                IrMemberLocation value => $"member={value.Member} receiver={(value.Receiver == null ? "none" : printer.Print(value.Receiver))} " +
                    $"args=[{string.Join(", ", value.Arguments.Select(printer.Print))}]",
                IrSequenceLocation value => printer.Print(value.Sequence) + "[" + printer.Print(value.Index) + "]",
                _ => throw new InvalidOperationException("Unknown golden location.")
            };
        }

        void RememberInstruction(IrInstruction instruction)
        {
            switch (instruction)
            {
                case IrLockInstruction value:
                    RememberTerm(value.Receiver);
                    break;
                case IrAssignInstruction value:
                    RememberTerm(value.Value);
                    break;
                case IrLoadInstruction value:
                    RememberLocation(value.Location);
                    break;
                case IrStoreInstruction value:
                    RememberLocation(value.Location);
                    RememberTerm(value.Value);
                    break;
                case IrCallInstruction value:
                    if (value.Receiver != null)
                    {
                        RememberTerm(value.Receiver);
                    }
                    foreach (var argument in value.Arguments)
                    {
                        RememberTerm(argument);
                    }
                    break;
                case IrAssumeInstruction value:
                    RememberTerm(value.Condition);
                    break;
                case IrAssertInstruction value:
                    RememberTerm(value.Condition);
                    break;
                case IrBranchInstruction value:
                    RememberTerm(value.Condition);
                    break;
                case IrReturnInstruction { Value: { } value }:
                    RememberTerm(value);
                    break;
            }
        }

        void RememberLocation(IrLocation location)
        {
            switch (location)
            {
                case IrMemberLocation value:
                    if (value.Receiver != null)
                    {
                        RememberTerm(value.Receiver);
                    }
                    foreach (var argument in value.Arguments)
                    {
                        RememberTerm(argument);
                    }
                    break;
                case IrSequenceLocation value:
                    RememberTerm(value.Sequence);
                    RememberTerm(value.Index);
                    break;
            }
        }

        void RememberTerm(IrTerm term)
        {
            if (!terms.TryAdd(term.Id, term))
            {
                return;
            }
            IEnumerable<IrTerm> children = term switch
            {
                IrOpaqueTerm value => value.Receiver == null ? value.Arguments : value.Arguments.Insert(0, value.Receiver),
                IrUnaryTerm value => [value.Operand],
                IrBinaryTerm value => [value.Left, value.Right],
                IrConditionalTerm value => [value.Condition, value.WhenTrue, value.WhenFalse],
                IrCastTerm value => [value.Operand],
                IrLengthTerm value => [value.Value],
                IrSequenceAccessTerm value => [value.Sequence, value.Index],
                _ => []
            };
            foreach (var child in children)
            {
                RememberTerm(child);
            }
        }
    }
}
