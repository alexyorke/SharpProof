using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

public abstract class SemanticOracleSmtTestBase
{
    protected const string GeneratedRegexFactorySource = @"
using System.Text.RegularExpressions;

public static partial class RegexFactories
{
    [GeneratedRegex(@""\AAB\z"")]
    public static partial Regex Ab();

    [GeneratedRegex(@""\A.\z"", RegexOptions.Singleline)]
    public static partial Regex SinglelineAny();
}";

    protected const string StaticRegexCacheSource = @"
using System.Text.RegularExpressions;

public static class RegexCache
{
    public static readonly Regex Ab = new Regex(@""\AAB\z"");

    public static Regex MutableAb = new Regex(@""\AAB\z"");
}";

    protected const string InstanceRegexCacheSource = @"
using System.Text.RegularExpressions;

public sealed class RegexBox
{
    public readonly Regex Ab = new Regex(@""\AAB\z"");

    public readonly Regex SinglelineAny = new Regex(@""\A.\z"", RegexOptions.Singleline);

    public readonly Regex MultilineAb = new Regex(@""\AAB\z"", RegexOptions.Multiline);

    public Regex MutableAb = new Regex(@""\AAB\z"");
}

public sealed class GeneratedRegexBox
{
    public readonly Regex Ab = RegexFactories.Ab();
}

public sealed class ConstructorAssignedRegexBox
{
    public readonly Regex Ab;

    public ConstructorAssignedRegexBox()
    {
        Ab = new Regex(@""\AAB\z"");
    }
}

public static class StaticCtorRegexCache
{
    public static readonly Regex Ab = new Regex(@""\AAB\z"");

    static StaticCtorRegexCache()
    {
        Ab = new Regex(@""\ACD\z"");
    }
}";

    protected const string SourcePredicateSource = @"
public static class SourcePredicates
{
    public static bool IsNullOrEmptyLike(string value)
    {
        return value == null || value.Length == 0;
    }

    public static bool HasText(string value) => value != null && value.Length > 0;

    public static bool HasTextWithGuard(string value)
    {
        if (value == null)
        {
            return false;
        }

        return value.Length > 0;
    }

    public static bool HasTextWithIfElse(string value)
    {
        if (value == null)
        {
            return false;
        }
        else
        {
            return value.Length > 0;
        }
    }

    public static bool HasTextViaLocal(string value)
    {
        var present = value != null;
        var positiveLength = value.Length > 0;
        return present && positiveLength;
    }

    public static bool HasTextViaAssignment(string value)
    {
        bool present;
        bool positiveLength;
        present = value != null;
        positiveLength = value.Length > 0;
        return present && positiveLength;
    }

    public static bool InRange(int value)
    {
        return value >= 10 && value <= 20;
    }

    public static bool IsValidIndex(int[] values, int index)
    {
        if (values == null)
        {
            return false;
        }

        if (index < 0)
        {
            return false;
        }

        return index < values.Length;
    }

    public static bool IsPositive(int value) => value > 0;

    public static bool IsZeroWithGuard(int value)
    {
        if (value != 0)
        {
            return false;
        }

        return true;
    }

    public static bool IsZeroViaLocal(int value)
    {
        var isZero = value == 0;
        return isZero;
    }

    public static bool IsZeroViaAssignment(int value)
    {
        bool isZero;
        isZero = value == 0;
        return isZero;
    }

    public static bool IsPositiveAfterLocalAssignment(int value)
    {
        var adjusted = value;
        adjusted = adjusted + 1;
        return adjusted > 0;
    }

    public static bool IsZeroWithSwitch(int value)
    {
        switch (value)
        {
            case 0:
                return true;
            default:
                return false;
        }
    }

    public static bool IsZeroWithSwitchFallback(int value)
    {
        switch (value)
        {
            case 0:
                return true;
        }

        return false;
    }

    public static bool IsSmallPositiveWithSwitch(int value)
    {
        switch (value)
        {
            case > 0 and < 10:
                return true;
            default:
                return false;
        }
    }
}

public sealed class SourcePredicateBox
{
    public SourcePredicateBox(string value, int divisor)
    {
        Value = value;
        Divisor = divisor;
    }

    public string Value { get; }

    public int Divisor { get; }

    public bool HasText => Value != null && Value.Length > 0;

    public bool HasTextMethod() => Value != null && Value.Length > 0;

    public bool IsZeroDivisor
    {
        get
        {
            var isZero = Divisor == 0;
            return isZero;
        }
    }

    public bool IsZeroDivisorMethod()
    {
        var isZero = Divisor == 0;
        return isZero;
    }
}
";

    protected const string ExtendedPropertyPatternSource = @"
public sealed class ExtendedPatternBox
{
    public ExtendedPatternBox(ExtendedPatternChild child)
    {
        Child = child;
    }

    public ExtendedPatternChild Child { get; }
}

public sealed class ExtendedPatternChild
{
    public ExtendedPatternChild(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
";

    protected const string NotNullIfNotNullSource = @"
using System.Diagnostics.CodeAnalysis;

public static class NotNullIfNotNullPredicates
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string Echo(string value) => value;
}

public sealed class NotNullIfNotNullIndexer
{
    [NotNullIfNotNull(""key"")]
    public string this[string key] => key;
}
";

    protected static readonly Type ExecutionVisibilityType = typeof(SharpProofAnalyzer).Assembly
        .GetType("SharpProof.Analyzer.Engine.ExecutionVisibility", true)!;

    private static readonly SymbolicInvariantService SharedInvariantService = new();

    private static readonly ConditionPredicateDelegate IsConditionAlwaysFalseFunc =
        (ConditionPredicateDelegate)Delegate.CreateDelegate(
            typeof(ConditionPredicateDelegate),
            ExecutionVisibilityType
                .GetMethod("IsConditionAlwaysFalseUsingSmt",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    private static readonly ConditionPredicateDelegate IsConditionAlwaysTrueFunc =
        (ConditionPredicateDelegate)Delegate.CreateDelegate(
            typeof(ConditionPredicateDelegate),
            ExecutionVisibilityType
                .GetMethod("IsConditionAlwaysTrueUsingSmt",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    private static readonly ReachabilityPredicateDelegate IsInStaticallyUnreachableBranchFunc =
        (ReachabilityPredicateDelegate)Delegate.CreateDelegate(
            typeof(ReachabilityPredicateDelegate),
            ExecutionVisibilityType
                .GetMethod("IsInStaticallyUnreachableBranchUsingSmt",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    protected static Task<ImmutableArray<Diagnostic>> GetExceptionDiagnosticsAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"),
            frameworkReferences: AnalyzerTestHost.GetMinimalFrameworkReferences(),
            concurrentAnalysis: true);
    }

    protected static bool IsConditionAlwaysFalse(string parameterList, string conditionExpression,
        string extraSource = "")
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return IsConditionAlwaysFalseFunc(context.Expression, context.SemanticModel, CancellationToken.None, null);
    }

    protected static bool IsConditionAlwaysTrue(string parameterList, string conditionExpression,
        string extraSource = "")
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return IsConditionAlwaysTrueFunc(context.Expression, context.SemanticModel, CancellationToken.None, null);
    }

    protected static bool IsStatementUnreachable(string source, string statementText)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "StatementReachabilityHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var statement = context.Root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(node => string.Equals(node.ToString(), statementText, StringComparison.Ordinal));

        return IsInStaticallyUnreachableBranchFunc(statement, context.SemanticModel, CancellationToken.None, null);
    }

    protected static bool IsExpressionUnreachable(string source, string expressionText)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "ExpressionReachabilityHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var expression = context.Root
            .DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Where(node => string.Equals(node.ToString(), expressionText, StringComparison.Ordinal))
            .OrderBy(static node => node.Span.Length)
            .First();

        return IsInStaticallyUnreachableBranchFunc(expression, context.SemanticModel, CancellationToken.None, null);
    }

    protected static string[] CollectProgramPointFacts(string source, string statementPrefix)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "ProgramPointFactHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var statement = context.Root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(node => node.ToString().StartsWith(statementPrefix, StringComparison.Ordinal));
        var snapshot = SharedInvariantService.AnalyzeAt(statement, context.SemanticModel,
            cancellationToken: CancellationToken.None);

        return snapshot.Facts.ToArray();
    }

    internal static string[] CollectCompletedLoopExitFacts(string source, string loopPrefix)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "CompletedLoopFactHost",
            AnalyzerTestHost.GetMinimalFrameworkReferences());
        var loopStatement = context.Root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Single(node => node.ToString().StartsWith(loopPrefix, StringComparison.Ordinal));

        var result = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
            loopStatement,
            new SymbolicState(),
            context.SemanticModel,
            CancellationToken.None);
        if (result is not { IsExact: true, Value: { } state })
            throw new InvalidOperationException(result.Provenance.Single().Detail);

        return state
            .Normalize()
            .PathConditions
            .Select(SymbolicFormulaDisplay.Format)
            .ToArray();
    }

    protected static string[] CollectExpressionProgramPointFacts(string source, string expressionPrefix)
    {
        var context = AnalyzerTestHost.CreateSourceContext(
            source,
            "SymbolicFactsTest",
            ImmutableArray.Create<MetadataReference>(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)),
            parseOptions: null);
        var expression = context.Root
            .DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Single(node => node.ToString().StartsWith(expressionPrefix, StringComparison.Ordinal));
        var snapshot = SharedInvariantService.AnalyzeAt(expression, context.SemanticModel,
            cancellationToken: CancellationToken.None);

        return snapshot.Facts.ToArray();
    }

    internal static int FindLine(string source, string text)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;

        throw new InvalidOperationException("Text was not found in source.");
    }

    protected static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
    {
        var options = AnalyzerConfigurationTestAccessor.Read(globalOptions).SmtOptions;
        return new SmtOptionsSnapshot(
            options.Mode.ToString(),
            (int)options.QueryTimeout.TotalMilliseconds,
            (int)options.MethodBudget.TotalMilliseconds,
            options.MaxPathConditions,
            options.MaxExpressionNodes,
            options.IsEnabled);
    }

    private delegate bool ConditionPredicateDelegate(ExpressionSyntax expression, SemanticModel semanticModel,
        CancellationToken cancellationToken, SmtAnalysisService? smtAnalysis);

    private delegate bool ReachabilityPredicateDelegate(SyntaxNode node, SemanticModel semanticModel,
        CancellationToken cancellationToken, SmtAnalysisService? smtAnalysis);

    protected readonly record struct SmtOptionsSnapshot(
        string Mode,
        int TimeoutMs,
        int MethodBudgetMs,
        int MaxPathConditions,
        int MaxExpressionNodes,
        bool IsEnabled);
}
