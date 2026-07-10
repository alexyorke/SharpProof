using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer;
using SharpProof.Symbolic;

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
                .GetMethod("IsConditionAlwaysFalse",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    private static readonly ConditionPredicateDelegate IsConditionAlwaysTrueFunc =
        (ConditionPredicateDelegate)Delegate.CreateDelegate(
            typeof(ConditionPredicateDelegate),
            ExecutionVisibilityType
                .GetMethod("IsConditionAlwaysTrue",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!);

    private static readonly ReachabilityPredicateDelegate IsInStaticallyUnreachableBranchFunc =
        (ReachabilityPredicateDelegate)Delegate.CreateDelegate(
            typeof(ReachabilityPredicateDelegate),
            ExecutionVisibilityType
                .GetMethod("IsInStaticallyUnreachableBranch",
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
        return IsConditionAlwaysFalseFunc(context.Expression, context.SemanticModel, CancellationToken.None);
    }

    protected static bool IsConditionAlwaysTrue(string parameterList, string conditionExpression,
        string extraSource = "")
    {
        var context = AnalyzerTestHost.CreateConditionContext(parameterList, conditionExpression, extraSource);
        return IsConditionAlwaysTrueFunc(context.Expression, context.SemanticModel, CancellationToken.None);
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

        return IsInStaticallyUnreachableBranchFunc(statement, context.SemanticModel, CancellationToken.None);
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

        return IsInStaticallyUnreachableBranchFunc(expression, context.SemanticModel, CancellationToken.None);
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
        var snapshot = SharedInvariantService.GetInvariantsAt(statement, context.SemanticModel, CancellationToken.None);

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

        return SymbolicProgramPointFacts
            .CollectCompletedLoopExitInvariantFacts(loopStatement, context.SemanticModel, CancellationToken.None)
            .Select(static fact => SymbolicFormulaDisplay.Format(fact))
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
        var snapshot =
            SharedInvariantService.GetInvariantsAt(expression, context.SemanticModel, CancellationToken.None);

        return snapshot.Facts.ToArray();
    }

    protected static int FindLine(string source, string text)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;

        throw new InvalidOperationException("Text was not found in source.");
    }

    protected static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
    {
        return SmtOptionsReader.Read(globalOptions);
    }

    private delegate bool ConditionPredicateDelegate(ExpressionSyntax expression, SemanticModel semanticModel,
        CancellationToken cancellationToken);

    private delegate bool ReachabilityPredicateDelegate(SyntaxNode node, SemanticModel semanticModel,
        CancellationToken cancellationToken);

    private static class SmtOptionsReader
    {
        private static readonly MethodInfo FromOptionsMethod;
        private static readonly Func<object, string> ModeGetter;
        private static readonly Func<object, TimeSpan> QueryTimeoutGetter;
        private static readonly Func<object, TimeSpan> MethodBudgetGetter;
        private static readonly Func<object, int> MaxPathConditionsGetter;
        private static readonly Func<object, int> MaxExpressionNodesGetter;
        private static readonly Func<object, bool> IsEnabledGetter;

        static SmtOptionsReader()
        {
            var configurationType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", true)!;
            FromOptionsMethod = configurationType.GetMethod(
                "FromOptions",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            var smtOptionsType = configurationType.GetProperty("SmtOptions")!.PropertyType;
            ModeGetter = CreatePropertyGetter<string>(configurationType, "SmtOptions", smtOptionsType, "Mode");
            QueryTimeoutGetter =
                CreatePropertyGetter<TimeSpan>(configurationType, "SmtOptions", smtOptionsType, "QueryTimeout");
            MethodBudgetGetter =
                CreatePropertyGetter<TimeSpan>(configurationType, "SmtOptions", smtOptionsType, "MethodBudget");
            MaxPathConditionsGetter =
                CreatePropertyGetter<int>(configurationType, "SmtOptions", smtOptionsType, "MaxPathConditions");
            MaxExpressionNodesGetter =
                CreatePropertyGetter<int>(configurationType, "SmtOptions", smtOptionsType, "MaxExpressionNodes");
            IsEnabledGetter = CreatePropertyGetter<bool>(configurationType, "SmtOptions", smtOptionsType, "IsEnabled");
        }

        public static SmtOptionsSnapshot Read(ImmutableDictionary<string, string> globalOptions)
        {
            var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(globalOptions);
            var configuration = FromOptionsMethod.Invoke(null, new object?[] { analyzerOptions })!;
            var smtOptions = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", true)!
                .GetProperty("SmtOptions")!.GetValue(configuration)!;

            return new SmtOptionsSnapshot(
                ModeGetter(smtOptions),
                (int)QueryTimeoutGetter(smtOptions).TotalMilliseconds,
                (int)MethodBudgetGetter(smtOptions).TotalMilliseconds,
                MaxPathConditionsGetter(smtOptions),
                MaxExpressionNodesGetter(smtOptions),
                IsEnabledGetter(smtOptions));
        }

        private static Func<object, T> CreatePropertyGetter<T>(Type configurationType, string outerProperty,
            Type innerType, string innerProperty)
        {
            var outerProp = configurationType.GetProperty(outerProperty)!;
            var innerProp = innerType.GetProperty(innerProperty)!;
            return obj =>
            {
                var smtOptions = outerProp.GetValue(obj);
                return (T)innerProp.GetValue(smtOptions)!;
            };
        }
    }

    protected readonly record struct SmtOptionsSnapshot(
        string Mode,
        int TimeoutMs,
        int MethodBudgetMs,
        int MaxPathConditions,
        int MaxExpressionNodes,
        bool IsEnabled);
}