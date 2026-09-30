using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
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
        var compilation = CSharpCompilation.Create("GoldenLowering", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Target");
        var factory = new IrFactory();
        var graph = ControlFlowGraph.Create(method, compilation.GetSemanticModel(tree))!;
        var result = new RoslynProgramLowerer(factory).Lower(graph);
        var printer = new IrPrinter(factory);
        var terms = new Dictionary<IrId, IrTerm>();
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"mode: {factory.Semantics}");
        output.AppendLine(CultureInfo.InvariantCulture, $"classification: {result.Classification.Decision}/{result.Classification.Abstention}");
        output.AppendLine(CultureInfo.InvariantCulture, $"entry: {result.Program.Entry}");
        foreach (var binding in result.Variables.OrderBy(binding => binding.Variable.Value))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"variable: {Variable(binding.Variable)} source={binding.Symbol.Name}");
        }
        foreach (var block in result.Program.Blocks.OrderBy(block => block.Id.Value))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"block: {block.Id} {(block.Name is { } name ? factory.GetString(name) : "unnamed")}");
            foreach (var instruction in block.Instructions)
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"  {instruction.Id} {instruction.Operation} {Format(instruction)}");
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

        string Variable(IrVarId id)
        {
            var type = factory.GetTypeInfo(factory.GetVariableInfo(id).Type);
            return $"{id}:{factory.GetString(type.Name)}[width={type.Width},signed={type.Signed}]";
        }

        string Format(IrInstruction instruction)
        {
            return instruction switch
            {
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
