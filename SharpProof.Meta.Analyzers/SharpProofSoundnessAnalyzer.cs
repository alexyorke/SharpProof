using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharpProofSoundnessAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> KnownTypeNames = [
        "Microsoft.CodeAnalysis.Compilation", "Microsoft.CodeAnalysis.SemanticModel",
        "Microsoft.CodeAnalysis.CSharp.SyntaxFactory", "Microsoft.CodeAnalysis.ISymbol",
        "Microsoft.CodeAnalysis.DiagnosticDescriptor", "System.OperationCanceledException",
        "System.Threading.CancellationToken", "SharpProof.Frontend.Host.CompilationModelProvider",
        "SharpProof.Analyzer.GeneratedDiagnosticDescriptors", "SharpProof.ContractForGenerator.GeneratedDiagnosticDescriptors",
        "System.String", "SharpProof.Verify.Assumption", "SharpProof.Verify.ProofKernel",
        "SharpProof.Worker.CallableEvidenceBuilder",
        "SharpProof.Worker.CallableVerifier", "SharpProof.Worker.PostconditionObligationBuilder",
        "SharpProof.Effects.EffectSummary",
        "SharpProof.Effects.EffectSummaryDomain", "SharpProof.Effects.EffectSummaryOperations",
        "SharpProof.Effects.ExternalEffectResolver", "SharpProof.Verify.ProvenOutcome",
        "SharpProof.Verify.RefutedOutcome", "SharpProof.Verify.ValidatedModel", "SharpProof.Worker.Program",
        "SharpProof.Worker.Launcher.Program", "SharpProof.Worker.SharpProofWorker",
        "SharpProof.Worker.CallableVerificationPolicy", "SharpProof.Worker.CallableVerificationResult",
        "SharpProof.Worker.Protocol.WorkerClaimReason",
        "SharpProof.Worker.Protocol.WorkerCallableCoverageReason", "SharpProof.Worker.Protocol.WorkerVerifyRequest",
        "SharpProof.Worker.Protocol.WorkerVerifyResponse"
    ];

    private static readonly ImmutableDictionary<KnownType, ImmutableHashSet<string>> ForbiddenMethods =
        new Dictionary<KnownType, ImmutableHashSet<string>>
        {
            [KnownType.Compilation] = Names(
                "ReplaceSyntaxTree",
                "AddSyntaxTrees",
                "RemoveSyntaxTrees",
                "RemoveAllSyntaxTrees",
                "GetSymbolsWithName"),
            [KnownType.SemanticModel] = Names("TryGetSpeculativeSemanticModel", "GetSpeculativeTypeInfo", "GetDiagnostics"),
            [KnownType.SyntaxFactory] = Names("ParseStatement", "ParseExpression", "ParseTypeName")
        }.ToImmutableDictionary();

    private static readonly ImmutableHashSet<string> CacheWriteMethods =
        Names("Add", "AddOrUpdate", "GetOrAdd", "Set", "TryAdd", "TryUpdate", "TryWrite", "TryWriteAsync", "Write", "WriteAsync");

    private static readonly ImmutableArray<string> CSharpExpressionFragments =
        [" is null", " is not null", " == ", " != ", " && ", " || ", "=>", "?."];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => MetaDiagnosticDescriptors.All;

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = new KnownSymbols(startContext.Compilation);
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, symbols), OperationKind.Invocation);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, symbols), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(AnalyzeBinaryOperation, OperationKind.BinaryOperator);
            startContext.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
            startContext.RegisterSyntaxNodeAction(
                c => CancellationBoundaryAnalyzer.AnalyzeCatchClause(c, symbols),
                SyntaxKind.CatchClause);
        });
    }

    private static ImmutableHashSet<string> Names(params string[] values)
    {
        return values.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod.OriginalDefinition;
        if (IsForbidden(method, invocation, context.ContainingSymbol, symbols))
        {
            Report(context, MetaDiagnosticDescriptors.ForbiddenRoslynApi, invocation.Syntax.GetLocation(), method.Name);
        }

        AnalyzeSemanticEquals(context, invocation, symbols);
        AnalyzeCacheWrite(context, invocation);
    }

    private static bool IsForbidden(
        IMethodSymbol method,
        IInvocationOperation invocation,
        ISymbol containingSymbol,
        KnownSymbols symbols)
    {
        if (!IsSemanticNamespace(containingSymbol))
        {
            return false;
        }

        foreach (var entry in ForbiddenMethods)
        {
            if (IsSameType(method.ContainingType, symbols[entry.Key]) && entry.Value.Contains(method.Name))
            {
                return true;
            }
        }

        if (method.Name == "GetSemanticModel" && IsSameType(method.ContainingType, symbols[KnownType.Compilation]))
        {
            return !IsSameType(containingSymbol.ContainingType, symbols[KnownType.CompilationModelProvider]);
        }

        if (method.Name != "ToDisplayString")
        {
            return false;
        }

        var receiverType = invocation.Instance?.Type ?? method.ContainingType;
        return IsSameType(receiverType, symbols[KnownType.Symbol]) ||
               receiverType?.AllInterfaces.Any(value => IsSameType(value, symbols[KnownType.Symbol])) == true;
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var containingType = context.ContainingSymbol.ContainingType;
        if (IsSameType(creation.Type, symbols[KnownType.DiagnosticDescriptor]) &&
            !IsExactNamespace(context.ContainingSymbol.ContainingNamespace, "SharpProof", "Meta", "Analyzers") &&
            !IsAnyType(containingType, symbols, KnownType.AnalyzerDiagnosticDescriptors, KnownType.ContractForDiagnosticDescriptors))
        {
            Report(context, MetaDiagnosticDescriptors.DescriptorConstruction, creation.Syntax.GetLocation());
        }

        if (IsSameType(creation.Type, symbols[KnownType.Assumption]) &&
            !IsAnyType(
                containingType,
                symbols,
                KnownType.ProofKernel,
                KnownType.CallableVerifier,
                KnownType.CallableEvidenceBuilder,
                KnownType.PostconditionObligationBuilder))
        {
            Report(context, MetaDiagnosticDescriptors.AssumptionConstruction, creation.Syntax.GetLocation());
        }

        if (IsSameType(creation.Type, symbols[KnownType.EffectSummary]) &&
            !IsAnyType(
                containingType,
                symbols,
                KnownType.EffectSummary,
                KnownType.EffectSummaryDomain,
                KnownType.EffectSummaryOperations,
                KnownType.ExternalEffectResolver))
        {
            Report(context, MetaDiagnosticDescriptors.EffectSummaryConstruction, creation.Syntax.GetLocation());
        }

        if (IsAnyType(creation.Type, symbols, KnownType.ProvenOutcome, KnownType.RefutedOutcome, KnownType.ValidatedModel) &&
            !IsSameType(containingType, symbols[KnownType.ProofKernel]))
        {
            Report(
                context,
                MetaDiagnosticDescriptors.ProofOutcomeConstruction,
                creation.Syntax.GetLocation(),
                creation.Type?.Name ?? string.Empty);
        }
    }

    private static bool IsAnyType(ITypeSymbol? actual, KnownSymbols symbols, params KnownType[] expected)
    {
        return expected.Any(type => IsSameType(actual, symbols[type]));
    }

    private static void AnalyzeBinaryOperation(OperationAnalysisContext context)
    {
        AnalyzeSemanticString(context);
        AnalyzeCSharpExpressionText(context);
    }

    private static void AnalyzeSemanticString(OperationAnalysisContext context)
    {
        if (context.Operation is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            } binary ||
            !IsSemanticNamespace(context.ContainingSymbol) ||
            !IsInsideCondition(binary.Syntax))
        {
            return;
        }

        var literal = GetSemanticLiteral(binary.LeftOperand) ?? GetSemanticLiteral(binary.RightOperand);
        if (literal != null)
        {
            Report(context, MetaDiagnosticDescriptors.SemanticStringControlFlow, binary.Syntax.GetLocation(), literal);
        }
    }

    private static void AnalyzeSemanticEquals(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        if (!IsSameType(invocation.TargetMethod.ContainingType, symbols[KnownType.String]) ||
            invocation.TargetMethod.Name != "Equals" ||
            !IsSemanticNamespace(context.ContainingSymbol) ||
            !IsInsideCondition(invocation.Syntax))
        {
            return;
        }

        var literal = invocation.Instance == null ? null : GetSemanticLiteral(invocation.Instance);
        literal ??= invocation.Arguments.Select(static a => GetSemanticLiteral(a.Value)).FirstOrDefault(value => value != null);
        if (literal != null)
        {
            Report(context, MetaDiagnosticDescriptors.SemanticStringControlFlow, invocation.Syntax.GetLocation(), literal);
        }
    }

    private static void AnalyzeCSharpExpressionText(OperationAnalysisContext context)
    {
        if (context.Operation is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary ||
            binary.Type?.SpecialType != SpecialType.System_String ||
            !IsSemanticNamespace(context.ContainingSymbol))
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(binary.LeftOperand) ?? GetCSharpExpressionFragment(binary.RightOperand);
        if (fragment != null)
        {
            Report(context, MetaDiagnosticDescriptors.CSharpExpressionText, binary.Syntax.GetLocation(), fragment);
        }
    }

    private static string? GetCSharpExpressionFragment(IOperation operation)
    {
        if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is not string value)
        {
            return null;
        }

        return CSharpExpressionFragments.FirstOrDefault(fragment => value.IndexOf(fragment, StringComparison.Ordinal) >= 0);
    }

    private static void AnalyzeCacheWrite(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        if (!CacheWriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheType(invocation.Instance?.Type ?? invocation.TargetMethod.ContainingType) ||
            !invocation.Arguments.Any(static argument => ContainsNonCacheableSemanticAnswer(argument.Value)))
        {
            return;
        }

        Report(context, MetaDiagnosticDescriptors.NonCacheableSemanticAnswer, invocation.Syntax.GetLocation());
    }

    private static bool IsCacheType(ITypeSymbol? type)
    {
        return type?.Name.IndexOf("Cache", StringComparison.Ordinal) >= 0;
    }

    private static bool ContainsNonCacheableSemanticAnswer(IOperation operation)
    {
        return operation.DescendantsAndSelf().Any(static descendant => descendant switch
        {
            IFieldReferenceOperation field => IsNonCacheableName(field.Field.Name),
            IPropertyReferenceOperation property => IsNonCacheableName(property.Property.Name),
            IInvocationOperation invocation => IsNonCacheableName(invocation.TargetMethod.Name),
            IObjectCreationOperation creation => IsNonCacheableName(creation.Type?.Name),
            ILocalReferenceOperation local => IsNonCacheableName(local.Local.Name),
            _ => false
        });
    }

    private static bool IsNonCacheableName(string? name)
    {
        return name != null &&
        (string.Equals(name, "Unknown", StringComparison.Ordinal) ||
         name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
         name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
         name.IndexOf("Failure", StringComparison.Ordinal) >= 0);
    }

    private static string? GetSemanticLiteral(IOperation operation)
    {
        if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is not string value)
        {
            return null;
        }

        return value.StartsWith("ir.", StringComparison.Ordinal) || value.StartsWith("ir_", StringComparison.Ordinal) ? value : null;
    }

    private static bool IsInsideCondition(SyntaxNode syntax)
    {
        return syntax.AncestorsAndSelf().Any(node => node switch
        {
            IfStatementSyntax statement => statement.Condition.Span.Contains(syntax.Span),
            WhileStatementSyntax statement => statement.Condition.Span.Contains(syntax.Span),
            DoStatementSyntax statement => statement.Condition.Span.Contains(syntax.Span),
            ForStatementSyntax { Condition: not null } statement => statement.Condition.Span.Contains(syntax.Span),
            ConditionalExpressionSyntax conditional => conditional.Condition.Span.Contains(syntax.Span),
            _ => false
        });
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsConst || field.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if (field.IsStatic && !field.IsReadOnly && IsCriticalStateNamespace(field.ContainingNamespace))
        {
            Report(context, MetaDiagnosticDescriptors.MutableStaticState, field.Locations.FirstOrDefault(), field.Name);
        }

        if (field.Type.SpecialType == SpecialType.System_String &&
            IsNamespaceOrNested(field.ContainingNamespace, "SharpProof", "Ir"))
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, field.Locations.FirstOrDefault(), field.Name);
        }
    }

    private static bool IsSemanticNamespace(ISymbol symbol)
    {
        return IsCriticalStateNamespace(symbol.ContainingNamespace) ||
        IsNamespaceOrNested(symbol.ContainingNamespace, "SharpProof", "Dataflow") ||
        IsNamespaceOrNested(symbol.ContainingNamespace, "SharpProof", "Specs");
    }

    private static bool IsCriticalStateNamespace(INamespaceSymbol? value)
    {
        return IsNamespaceOrNested(value, "SharpProof", "Analyzer") ||
        IsNamespaceOrNested(value, "SharpProof", "Frontend") ||
        IsNamespaceOrNested(value, "SharpProof", "Verify");
    }

    private static bool IsNamespaceOrNested(INamespaceSymbol? value, params string[] expectedPrefix)
    {
        for (var current = value; current != null && !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            if (IsExactNamespace(current, expectedPrefix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExactNamespace(INamespaceSymbol? value, params string[] expected)
    {
        var current = value;
        for (var index = expected.Length - 1; index >= 0; index--)
        {
            if (current == null ||
                current.IsGlobalNamespace ||
                !string.Equals(current.Name, expected[index], StringComparison.Ordinal))
            {
                return false;
            }

            current = current.ContainingNamespace;
        }
        return current?.IsGlobalNamespace == true;
    }

    private static bool IsSameType(ITypeSymbol? actual, INamedTypeSymbol? expected)
    {
        return actual != null &&
        expected != null &&
        SymbolEqualityComparer.Default.Equals(actual.OriginalDefinition, expected.OriginalDefinition);
    }

    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location? at, params object?[] args)
    {
        context.ReportDiagnostic(Diagnostic.Create(rule, at, args));
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor rule, Location? at, params object?[] args)
    {
        context.ReportDiagnostic(Diagnostic.Create(rule, at, args));
    }

    internal enum KnownType
    {
        Compilation, SemanticModel, SyntaxFactory, Symbol, DiagnosticDescriptor,
        OperationCanceledException, CancellationToken, CompilationModelProvider,
        AnalyzerDiagnosticDescriptors, ContractForDiagnosticDescriptors, String,
        Assumption, ProofKernel, CallableEvidenceBuilder, CallableVerifier,
        PostconditionObligationBuilder,
        EffectSummary, EffectSummaryDomain,
        EffectSummaryOperations, ExternalEffectResolver, ProvenOutcome, RefutedOutcome,
        ValidatedModel, WorkerProgram, WorkerLauncherProgram, SharpProofWorker,
        CallableVerificationPolicy, CallableVerificationResult,
        WorkerClaimReason, WorkerCallableCoverageReason, WorkerVerifyRequest,
        WorkerVerifyResponse
    }

    internal sealed class KnownSymbols
    {
        private readonly ImmutableArray<INamedTypeSymbol?> _types;

        internal KnownSymbols(Compilation compilation)
        {
            var types = new INamedTypeSymbol?[KnownTypeNames.Length];
            for (var index = 0; index < KnownTypeNames.Length; index++)
            {
                types[index] = compilation.GetTypeByMetadataName(KnownTypeNames[index]);
            }

            types[(int)KnownType.String] = compilation.GetSpecialType(SpecialType.System_String);
            _types = [.. types];

            var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            TaskOfInt32 = task?.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
            VerifyTargetTask = task != null && this[KnownType.CallableVerificationResult] != null
                ? task.Construct(this[KnownType.CallableVerificationResult]!)
                : null;
            var workerTask = task != null && this[KnownType.WorkerVerifyResponse] != null
                ? task.Construct(this[KnownType.WorkerVerifyResponse]!)
                : null;
            var worker = this[KnownType.SharpProofWorker];
            WorkerVerifyAsync = worker?.GetMembers("VerifyAsync").OfType<IMethodSymbol>().SingleOrDefault(candidate =>
                candidate is { IsStatic: false, Arity: 0, Parameters.Length: 2 } &&
                SymbolEqualityComparer.Default.Equals(candidate.ReturnType, workerTask) &&
                candidate.Parameters[0] is { Name: "request", Type: var requestType } &&
                IsSameType(requestType, this[KnownType.WorkerVerifyRequest]) &&
                candidate.Parameters[1] is { Name: "cancellationToken", Type: var cancellationType } &&
                IsSameType(cancellationType, this[KnownType.CancellationToken]));
        }

        internal INamedTypeSymbol? this[KnownType type] => _types[(int)type];
        internal INamedTypeSymbol? TaskOfInt32
        {
            get;
        }
        internal INamedTypeSymbol? VerifyTargetTask
        {
            get;
        }
        internal IMethodSymbol? WorkerVerifyAsync
        {
            get;
        }
    }
}
