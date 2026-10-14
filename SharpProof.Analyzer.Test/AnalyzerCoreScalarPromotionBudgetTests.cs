using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreScalarPromotionBudgetTests
{
    private const int MaximumWork = 4096;
    private static readonly FrozenCase[] s_cases =
    [
        new("promotion-8.cs", "CD9BC7DF49869F3F305F813A4A8C41D30D2D23553959BBD960A27E01ABA5E715",
            8, false, ["SP0027"], AnalyzerSemanticOutcome.Refuted),
        new("promotion-1400.cs", "CE8BDFB9CFE402264A5F8C40B9272C83EFB32D3257A64A22FC1C68A51024A396",
            1400, true, [], AnalyzerSemanticOutcome.Unknown)
    ];

    [TestCase(8)]
    [TestCase(1400)]
    public async Task DefaultContractsModeRecordsBoundedCalleeOutcomes(int valueCopies)
    {
        var testCase = s_cases.Single(candidate => candidate.ValueCopies == valueCopies);
        var observation = InspectCompiler(testCase);
        AssertCompilerGate(observation);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
                observation.Compilation,
                "contracts",
                analyzer: new SharpProofAnalyzer(factory))
            .ConfigureAwait(false);
        var actualOutcome = factory.GetNamedOutcome("Target");
        var targetOutcomeCount = factory.OutcomeCounts.TryGetValue("Target", out var count) ? count : 0;
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Session, Is.Not.Null);
        Assert.That(targetOutcomeCount, Is.EqualTo(1));
        Assert.That(actualOutcome, Is.EqualTo(testCase.ExpectedOutcome));
        AnalyzerTestHost.AssertIds(diagnostics, testCase.ExpectedDiagnosticIds);
    }

    private static CompilerObservation InspectCompiler(FrozenCase testCase)
    {
        var path = testCase.FileName;
        var source = "using SharpProof.Attributes;\npublic static class Fixture\n{\n" +
            "    private static long Need(int value)\n    {\n        Contract.Requires(value > 0);\n" +
            "        return 1L" + string.Concat(Enumerable.Repeat(" + value", testCase.ValueCopies)) + ";\n" +
            "    }\n    public static long Target() => Need(0);\n}\n";
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"], filePath: path);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "Need");
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        var operation = model.GetOperation(method);
        var syntaxNodes = method.DescendantNodesAndSelf().Count();
        var operationNodes = operation == null ? 0 : CountOperationNodes(operation);
        var implicitIntToLongConversions = operation == null ? 0 : CountImplicitIntToLongConversions(operation);
        var typeMembers = symbol.ContainingType.GetMembers();
        var typeInitializationMemberCharges = typeMembers.Length;
        var typeInitializationReferenceCharges = typeMembers
            .Where(static member => member.IsStatic && member is not IFieldSymbol { IsConst: true })
            .Sum(static member => member.DeclaringSyntaxReferences.Length);
        var typeInitializationCost = typeInitializationMemberCharges + typeInitializationReferenceCharges;
        var sourceBudgetSpentBeforePrepare = symbol.Parameters.Length + 1 +
            method.DescendantNodesAndSelf().Count() + typeInitializationCost +
            symbol.Parameters.Length * 3 + 1 + 1;
        var sourceBudgetRemainingBeforePrepare = MaximumWork - sourceBudgetSpentBeforePrepare;
        var emitted = errors.Length == 0 ? AnalyzerTestHost.EmitImage(compilation) : [];

        return new CompilerObservation(
            testCase,
            compilation,
            sourceHash,
            sourceBytes.Length,
            errors,
            syntaxNodes,
            operation?.Kind.ToString(),
            operationNodes,
            implicitIntToLongConversions,
            typeMembers.Length,
            typeInitializationReferenceCharges,
            sourceBudgetSpentBeforePrepare,
            sourceBudgetRemainingBeforePrepare,
            emitted.Length);
    }

    private static void AssertCompilerGate(CompilerObservation observation)
    {
        Assert.That(observation.SourceHash, Is.EqualTo(observation.TestCase.ExpectedSha256), observation.TestCase.FileName);
        Assert.That(observation.CompilerErrors, Is.Empty, observation.TestCase.FileName);
        Assert.That(observation.EmitBytes, Is.GreaterThan(0), observation.TestCase.FileName);
        Assert.That(observation.OperationKind, Is.EqualTo("MethodBodyOperation"), observation.TestCase.FileName);
        Assert.That(observation.NeedSyntaxNodes, Is.LessThan(MaximumWork), observation.TestCase.FileName);
        Assert.That(observation.ImplicitIntToLongConversions, Is.EqualTo(observation.TestCase.ValueCopies), observation.TestCase.FileName);
        Assert.That(observation.SourceBudgetRemainingBeforePrepare, Is.GreaterThan(0), observation.TestCase.FileName);

        if (observation.TestCase.MustExceedOperationBudget)
        {
            Assert.That(observation.OwnedOperationNodes, Is.GreaterThan(MaximumWork), observation.TestCase.FileName);
        }
        else
        {
            Assert.That(observation.OwnedOperationNodes, Is.LessThanOrEqualTo(MaximumWork), observation.TestCase.FileName);
        }
    }

    private static int CountOperationNodes(IOperation root)
    {
        var count = 0;
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            count++;
            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return count;
    }

    private static int CountImplicitIntToLongConversions(IOperation root)
    {
        var count = 0;
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current is IConversionOperation { IsImplicit: true } conversion &&
                conversion.Operand.Type?.SpecialType == SpecialType.System_Int32 &&
                conversion.Type?.SpecialType == SpecialType.System_Int64)
            {
                count++;
            }

            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return count;
    }

    private sealed record FrozenCase(
        string FileName,
        string ExpectedSha256,
        int ValueCopies,
        bool MustExceedOperationBudget,
        string[] ExpectedDiagnosticIds,
        AnalyzerSemanticOutcome ExpectedOutcome);

    private sealed record CompilerObservation(
        FrozenCase TestCase,
        CSharpCompilation Compilation,
        string SourceHash,
        int SourceBytes,
        string[] CompilerErrors,
        int NeedSyntaxNodes,
        string? OperationKind,
        int OwnedOperationNodes,
        int ImplicitIntToLongConversions,
        int ContainingTypeMemberCount,
        int TypeInitializationReferenceCharges,
        int SourceBudgetSpentBeforePrepare,
        int SourceBudgetRemainingBeforePrepare,
        int EmitBytes);

}
