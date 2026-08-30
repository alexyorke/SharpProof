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
        "SharpProof.Worker.Protocol.WorkerVerifyResponse",
        "SharpProof.Worker.Protocol.WorkerResultAssembler",
        "SharpProof.Worker.Protocol.WorkerRunStatus"
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
            startContext.RegisterOperationAction(CacheSoundnessRules.AnalyzeAssignment, OperationKind.SimpleAssignment);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, symbols), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(AnalyzeBinaryOperation, OperationKind.BinaryOperator);
            startContext.RegisterOperationAction(
                AnalyzeInterpolatedString,
                OperationKind.InterpolatedString);
            startContext.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
            startContext.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
            startContext.RegisterSymbolAction(AnalyzeEvent, SymbolKind.Event);
            startContext.RegisterSyntaxNodeAction(
                c => CancellationBoundaryAnalyzer.AnalyzeCatchClause(c, symbols),
                SyntaxKind.CatchClause);
            startContext.RegisterSyntaxNodeAction(
                AnalyzeSemanticPatternControlFlow,
                SyntaxKind.ConstantPattern,
                SyntaxKind.CaseSwitchLabel);
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
        CacheSoundnessRules.AnalyzeWrite(context, invocation);
    }

    private static bool IsForbidden(
        IMethodSymbol method,
        IInvocationOperation invocation,
        ISymbol containingSymbol,
        KnownSymbols symbols)
    {
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

    private static void AnalyzeSemanticPatternControlFlow(
        SyntaxNodeAnalysisContext context)
    {
        var expression = context.Node switch
        {
            ConstantPatternSyntax pattern => pattern.Expression,
            CaseSwitchLabelSyntax label => label.Value,
            _ => null
        };
        if (expression == null)
        {
            return;
        }

        var constant = context.SemanticModel.GetConstantValue(
            expression,
            context.CancellationToken);
        var literal = !constant.HasValue
            ? null
            : GetSemanticLiteral(constant.Value);
        if (literal != null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.SemanticStringControlFlow,
                expression.GetLocation(),
                literal));
        }
    }

    private static void AnalyzeCSharpExpressionText(OperationAnalysisContext context)
    {
        if (context.Operation is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary ||
            binary.Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(binary.LeftOperand) ?? GetCSharpExpressionFragment(binary.RightOperand);
        if (fragment != null)
        {
            Report(context, MetaDiagnosticDescriptors.CSharpExpressionText, binary.Syntax.GetLocation(), fragment);
        }
    }

    private static void AnalyzeInterpolatedString(
        OperationAnalysisContext context)
    {
        var interpolated = (IInterpolatedStringOperation)context.Operation;
        if (!interpolated.Parts.Any(static part =>
                part is IInterpolationOperation))
        {
            return;
        }

        foreach (var part in interpolated.Parts)
        {
            if (part is not IInterpolatedStringTextOperation text)
            {
                continue;
            }

            var fragment = GetCSharpExpressionFragment(text.Text);
            if (fragment != null)
            {
                Report(
                    context,
                    MetaDiagnosticDescriptors.CSharpExpressionText,
                    interpolated.Syntax.GetLocation(),
                    fragment);
                return;
            }
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

    private static string? GetSemanticLiteral(IOperation operation)
    {
        if (!operation.ConstantValue.HasValue)
        {
            return null;
        }

        return GetSemanticLiteral(operation.ConstantValue.Value);
    }

    private static string? GetSemanticLiteral(object? value)
    {
        return value is string text &&
            (text.StartsWith("ir.", StringComparison.Ordinal) ||
             text.StartsWith("ir_", StringComparison.Ordinal))
                ? text
                : null;
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
        if (field.Type.SpecialType == SpecialType.System_String &&
            IsNamespaceOrNested(field.ContainingNamespace, "SharpProof", "Ir"))
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, field.Locations.FirstOrDefault(), field.Name);
        }

        if (field.IsConst || field.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if ((!field.IsReadOnly || IsMutableStorageType(field.Type)) &&
            IsForbiddenMutableStaticStorage(field))
        {
            Report(context, MetaDiagnosticDescriptors.MutableStaticState, field.Locations.FirstOrDefault(), field.Name);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if ((property.SetMethod != null || IsMutableStorageType(property.Type)) &&
            IsForbiddenMutableStaticStorage(property) &&
            IsAutoProperty(property, context.CancellationToken))
        {
            Report(
                context,
                MetaDiagnosticDescriptors.MutableStaticState,
                property.Locations.FirstOrDefault(),
                property.Name);
        }
    }

    private static void AnalyzeEvent(SymbolAnalysisContext context)
    {
        var @event = (IEventSymbol)context.Symbol;
        if (IsForbiddenMutableStaticStorage(@event) &&
            IsFieldLikeEvent(@event, context.CancellationToken))
        {
            Report(
                context,
                MetaDiagnosticDescriptors.MutableStaticState,
                @event.Locations.FirstOrDefault(),
                @event.Name);
        }
    }

    private static bool IsForbiddenMutableStaticStorage(ISymbol symbol)
    {
        return symbol.IsStatic &&
            IsCriticalStateNamespace(symbol.ContainingNamespace);
    }

    private static bool IsMutableStorageType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None ||
            type is not INamedTypeSymbol named ||
            named.Name.StartsWith("Immutable", StringComparison.Ordinal) ||
            named.Name.StartsWith("ReadOnly", StringComparison.Ordinal))
        {
            return false;
        }

        if (named.AllInterfaces.Any(static candidate => candidate.Name is
            "ICollection" or "IDictionary" or "IList" or "ISet" or
            "IProducerConsumerCollection"))
        {
            return true;
        }

        return named.GetMembers().OfType<IFieldSymbol>().Any(static field =>
                !field.IsStatic && !field.IsConst && !field.IsReadOnly) ||
            named.GetMembers().OfType<IPropertySymbol>().Any(static property =>
                !property.IsStatic && property.SetMethod != null);
    }

    private static bool IsAutoProperty(
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        return property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax
            {
                ExpressionBody: null,
                AccessorList.Accessors: var accessors
            } &&
            accessors.All(static accessor =>
                accessor.Body == null && accessor.ExpressionBody == null));
    }

    private static bool IsFieldLikeEvent(
        IEventSymbol @event,
        CancellationToken cancellationToken)
    {
        return @event.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
            {
                Parent.Parent: EventFieldDeclarationSyntax
            });
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
        WorkerVerifyResponse, WorkerResultAssembler, WorkerRunStatus
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
