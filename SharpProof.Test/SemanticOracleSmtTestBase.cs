using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

public abstract class SemanticOracleSmtTestBase {
    private static readonly Type ExecutionVisibilityType = typeof(SharpProofAnalyzer).Assembly
        .GetType("SharpProof.Analyzer.Engine.ExecutionVisibility", true)!;

    private static readonly ConditionPredicateDelegate IsConditionAlwaysFalseFunc =
        (ConditionPredicateDelegate)Delegate.CreateDelegate(
            typeof(ConditionPredicateDelegate),
            ExecutionVisibilityType.GetMethod(
                "IsConditionAlwaysFalseUsingSmt",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    protected static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression, string extraSource = "") {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return IsConditionAlwaysFalseFunc(context.Expression, context.SemanticModel, CancellationToken.None, null);
    }
    internal static int FindLine(string source, string text) {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;
        throw new InvalidOperationException("Text was not found in source.");
    }
    private delegate bool ConditionPredicateDelegate(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis);
}
