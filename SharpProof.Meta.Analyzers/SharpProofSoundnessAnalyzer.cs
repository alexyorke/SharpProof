using System.Collections.Immutable;
using System.Text;
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
        "Microsoft.CodeAnalysis.ModelExtensions",
        "Microsoft.CodeAnalysis.CSharp.CSharpCompilation",
        "Microsoft.CodeAnalysis.CSharp.CSharpSemanticModel",
        "Microsoft.CodeAnalysis.CSharp.CSharpExtensions",
        "Microsoft.CodeAnalysis.CSharp.SyntaxFactory",
        "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree",
        "Microsoft.CodeAnalysis.ISymbol",
        "Microsoft.CodeAnalysis.DiagnosticDescriptor", "System.OperationCanceledException",
        "System.Threading.CancellationToken", "SharpProof.Frontend.Host.CompilationModelProvider",
        "SharpProof.Meta.Analyzers.MetaDiagnosticDescriptors",
        "SharpProof.Analyzer.GeneratedDiagnosticDescriptors", "SharpProof.ContractForGenerator.GeneratedDiagnosticDescriptors",
        "System.String", "SharpProof.Verify.Assumption", "SharpProof.Verify.ProofKernel",
        "SharpProof.Worker.CallableEvidenceBuilder",
        "SharpProof.Worker.CallableVerifier", "SharpProof.Worker.PostconditionObligationBuilder",
        "SharpProof.Effects.EffectSummary",
        "SharpProof.Effects.EffectSummaryDomain", "SharpProof.Effects.EffectSummaryOperations",
        "SharpProof.Effects.ExternalEffectResolver", "SharpProof.Verify.ProvenOutcome",
        "SharpProof.Verify.RefutedOutcome", "SharpProof.Verify.ValidatedModel", "SharpProof.Worker.Program",
        "SharpProof.Worker.SharpProofWorker",
        "SharpProof.Worker.CallableVerificationPolicy", "SharpProof.Worker.CallableVerificationResult",
        "SharpProof.Worker.Protocol.WorkerClaimReason",
        "SharpProof.Worker.Protocol.WorkerCallableCoverageReason", "SharpProof.Worker.Protocol.WorkerVerifyRequest",
        "SharpProof.Worker.Protocol.WorkerVerifyResponse",
        "SharpProof.Worker.Protocol.WorkerResultAssembler",
        "SharpProof.Worker.Protocol.WorkerRunStatus",
        "System.Runtime.CompilerServices.RuntimeHelpers"
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
            [KnownType.SemanticModel] = Names(
                "TryGetSpeculativeSemanticModel",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo",
                "GetDiagnostics"),
            [KnownType.ModelExtensions] = Names(
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo"),
            [KnownType.CSharpSemanticModel] = Names(
                "TryGetSpeculativeSemanticModel",
                "TryGetSpeculativeSemanticModelForMethodBody",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo"),
            [KnownType.CSharpExtensions] = Names(
                "TryGetSpeculativeSemanticModel",
                "TryGetSpeculativeSemanticModelForMethodBody",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo"),
            [KnownType.RuntimeHelpers] = Names("GetUninitializedObject")
        }.ToImmutableDictionary();

    private static readonly ImmutableArray<string> CSharpExpressionFragments =
        [" is null", " is not null", " == ", " != ", " && ", " || ", "=>", "?."];
    private static readonly ImmutableHashSet<string>
        SemanticStringPredicateNames = Names(
            "Contains",
            "EndsWith",
            "Equals",
            "StartsWith");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => MetaDiagnosticDescriptors.All;

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = new KnownSymbols(startContext.Compilation);
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, symbols), OperationKind.Invocation);
            startContext.RegisterOperationAction(c => AnalyzeMethodReference(c, symbols), OperationKind.MethodReference);
            startContext.RegisterOperationAction(AnalyzeDynamicInvocation, OperationKind.DynamicInvocation);
            startContext.RegisterOperationAction(
                CacheSoundnessRules.AnalyzeAssignment,
                OperationKind.SimpleAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.CompoundAssignment);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, symbols), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(AnalyzeBinaryOperation, OperationKind.BinaryOperator);
            startContext.RegisterOperationAction(
                AnalyzeCSharpCompoundAssignment,
                OperationKind.CompoundAssignment);
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
        if (IsForbidden(method, invocation.Instance?.Type ?? method.ContainingType, context.ContainingSymbol, symbols))
        {
            Report(context, MetaDiagnosticDescriptors.ForbiddenRoslynApi, invocation.Syntax.GetLocation(), method.Name);
        }

        AnalyzeSemanticStringInvocation(context, invocation, symbols);
        if (IsStringConcat(invocation))
        {
            AnalyzeCSharpExpressionText(context, invocation);
        }
        CacheSoundnessRules.AnalyzeWrite(context, invocation);
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var methodReference = (IMethodReferenceOperation)context.Operation;
        var method = methodReference.Method.OriginalDefinition;
        if (IsForbidden(
                method,
                methodReference.Instance?.Type ?? method.ContainingType,
                context.ContainingSymbol,
                symbols))
        {
            Report(context, MetaDiagnosticDescriptors.ForbiddenRoslynApi, methodReference.Syntax.GetLocation(), method.Name);
        }
    }

    private static void AnalyzeDynamicInvocation(OperationAnalysisContext context)
    {
        Report(
            context,
            MetaDiagnosticDescriptors.ForbiddenRoslynApi,
            context.Operation.Syntax.GetLocation(),
            "dynamic invocation");
    }

    private static bool IsForbidden(
        IMethodSymbol method,
        ITypeSymbol? receiverType,
        ISymbol containingSymbol,
        KnownSymbols symbols)
    {
        if (method.Name.StartsWith("Parse", StringComparison.Ordinal) &&
            IsAnyType(
                method.ContainingType,
                symbols,
                KnownType.SyntaxFactory,
                KnownType.CSharpSyntaxTree))
        {
            return true;
        }

        foreach (var entry in ForbiddenMethods)
        {
            if (IsSameType(method.ContainingType, symbols[entry.Key]) && entry.Value.Contains(method.Name))
            {
                return true;
            }
        }

        if (method.Name == "GetSemanticModel" &&
            IsAnyType(
                method.ContainingType,
                symbols,
                KnownType.Compilation,
                KnownType.CSharpCompilation))
        {
            return !IsSameType(containingSymbol.ContainingType, symbols[KnownType.CompilationModelProvider]);
        }

        if (method.Name != "ToDisplayString")
        {
            return false;
        }

        return IsSameType(receiverType, symbols[KnownType.Symbol]) ||
               receiverType?.AllInterfaces.Any(value => IsSameType(value, symbols[KnownType.Symbol])) == true;
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var containingType = context.ContainingSymbol.ContainingType;
        if (IsSameType(creation.Type, symbols[KnownType.DiagnosticDescriptor]) &&
            !creation.Syntax.SyntaxTree.FilePath.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) &&
            !IsAnyType(
                containingType,
                symbols,
                KnownType.MetaDiagnosticDescriptors,
                KnownType.AnalyzerDiagnosticDescriptors,
                KnownType.ContractForDiagnosticDescriptors) &&
            containingType?.Name != "ContractForDiagnosticDescriptors")
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
        if (IsStringAddition(context.Operation))
        {
            AnalyzeCSharpExpressionText(context, context.Operation);
        }
    }

    private static void AnalyzeSemanticString(OperationAnalysisContext context)
    {
        if (context.Operation is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            } binary)
        {
            return;
        }

        var literalResolver = new SemanticLiteralResolver(
            binary,
            context.CancellationToken);
        var literal = literalResolver.Resolve(binary.LeftOperand) ??
            literalResolver.Resolve(binary.RightOperand);
        if (literal != null)
        {
            Report(context, MetaDiagnosticDescriptors.SemanticStringControlFlow, binary.Syntax.GetLocation(), literal);
        }
    }

    private static void AnalyzeSemanticStringInvocation(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        var method = invocation.TargetMethod;
        var isStringPredicate =
            IsSameType(method.ContainingType, symbols[KnownType.String]) &&
            SemanticStringPredicateNames.Contains(method.Name);
        var isObjectEquals =
            method.ContainingType.SpecialType == SpecialType.System_Object &&
            method.Name == "Equals";
        if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
            !isStringPredicate && !isObjectEquals)
        {
            return;
        }

        var literalResolver = new SemanticLiteralResolver(
            invocation,
            context.CancellationToken);
        var literal = invocation.Instance == null
            ? null
            : literalResolver.Resolve(invocation.Instance);
        literal ??= invocation.Arguments
            .Select(argument => literalResolver.Resolve(argument.Value))
            .FirstOrDefault(static value => value != null);
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

    private static void AnalyzeCSharpCompoundAssignment(
        OperationAnalysisContext context)
    {
        if (IsStringAddition(context.Operation))
        {
            AnalyzeCSharpExpressionText(context, context.Operation);
        }
    }

    private static void AnalyzeCSharpExpressionText(
        OperationAnalysisContext context,
        IOperation operation)
    {
        if (IsNestedCSharpExpressionConstruction(operation))
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(
            operation,
            context.CancellationToken);
        if (fragment != null)
        {
            Report(
                context,
                MetaDiagnosticDescriptors.CSharpExpressionText,
                operation.Syntax.GetLocation(),
                fragment);
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

            var fragment = GetCSharpExpressionFragment(
                text.Text,
                context.CancellationToken);
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

    private static string? GetCSharpExpressionFragment(
        IOperation operation,
        CancellationToken cancellationToken)
    {
        var shape = new StringBuilder();
        AppendCSharpExpressionShape(
            operation,
            shape,
            cancellationToken);
        var value = shape.ToString();
        return CSharpExpressionFragments.FirstOrDefault(fragment => value.IndexOf(fragment, StringComparison.Ordinal) >= 0);
    }

    private static void AppendCSharpExpressionShape(
        IOperation operation,
        StringBuilder shape,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<IOperation>();
        pending.Push(operation);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (current.ConstantValue is
                { HasValue: true, Value: string text })
            {
                shape.Append(text);
                continue;
            }

            switch (current)
            {
                case IBinaryOperation binary when
                    IsStringAddition(binary):
                    pending.Push(binary.RightOperand);
                    pending.Push(binary.LeftOperand);
                    break;
                case ICompoundAssignmentOperation assignment when
                    IsStringAddition(assignment):
                    shape.Append('\0');
                    pending.Push(assignment.Value);
                    break;
                case IInvocationOperation invocation when
                    IsStringConcat(invocation):
                    var arguments = invocation.Arguments.OrderBy(
                            static argument =>
                                argument.Parameter?.Ordinal ?? int.MaxValue)
                        .ToArray();
                    for (var index = arguments.Length - 1;
                         index >= 0;
                         index--)
                    {
                        pending.Push(arguments[index].Value);
                    }
                    break;
                case IParenthesizedOperation or
                    IConversionOperation { OperatorMethod: null }:
                    pending.Push(OperationUnwrapping.Unwrap(current, cancellationToken)!);
                    break;
                default:
                    shape.Append('\0');
                    break;
            }
        }
    }

    private static bool IsNestedCSharpExpressionConstruction(
        IOperation operation)
    {
        var parent = operation.Parent;
        while (parent is IParenthesizedOperation or
               IArgumentOperation or
               IConversionOperation { OperatorMethod: null })
        {
            parent = parent.Parent;
        }

        return IsStringAddition(parent) ||
            parent is IInvocationOperation invocation &&
            IsStringConcat(invocation);
    }

    private static bool IsStringAddition(IOperation? operation)
    {
        return operation is
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                Type.SpecialType: SpecialType.System_String
            } or
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                Type.SpecialType: SpecialType.System_String
            };
    }

    private static bool IsStringConcat(IInvocationOperation invocation)
    {
        return invocation.TargetMethod is
        {
            Name: nameof(string.Concat),
            ContainingType.SpecialType: SpecialType.System_String
        };
    }

    private static string? GetSemanticLiteral(
        IOperation operation,
        CancellationToken cancellationToken)
    {
        return new SemanticLiteralResolver(
            operation,
            cancellationToken).Resolve(operation);
    }

    private sealed class SemanticLiteralResolver
    {
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<ILocalSymbol, List<IOperation>> _assignments =
            new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, string> _literalCache =
            new(SymbolEqualityComparer.Default);

        internal SemanticLiteralResolver(
            IOperation operation,
            CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            var root = operation;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            foreach (var candidate in root.DescendantsAndSelf())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                switch (candidate)
                {
                    case IVariableDeclaratorOperation declaration
                        when declaration.Initializer?.Value is { } value:
                        AddAssignment(declaration.Symbol, value);
                        break;
                    case ISimpleAssignmentOperation
                    {
                        Target: ILocalReferenceOperation target,
                        Value: { } value
                    }:
                        AddAssignment(target.Local, value);
                        break;
                }
            }
        }

        internal string? Resolve(IOperation operation)
        {
            return Resolve(
                operation,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        }

        private string? Resolve(
            IOperation operation,
            HashSet<ILocalSymbol> visitedLocals)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (operation.ConstantValue.HasValue)
            {
                return GetSemanticLiteral(operation.ConstantValue.Value);
            }

            switch (operation)
            {
                case IArgumentOperation argument:
                    return Resolve(argument.Value, visitedLocals);
                case IConversionOperation conversion:
                    return Resolve(conversion.Operand, visitedLocals);
                case IParenthesizedOperation parenthesized:
                    return Resolve(parenthesized.Operand, visitedLocals);
            }
            if (operation is not ILocalReferenceOperation localReference ||
                !visitedLocals.Add(localReference.Local))
            {
                return null;
            }
            if (_literalCache.TryGetValue(localReference.Local, out var cached))
            {
                return cached;
            }

            if (!_assignments.TryGetValue(localReference.Local, out var values))
            {
                return null;
            }
            foreach (var value in values)
            {
                var literal = Resolve(value, visitedLocals);
                if (literal != null)
                {
                    _literalCache[localReference.Local] = literal;
                    return literal;
                }
            }

            return null;
        }

        private void AddAssignment(ILocalSymbol local, IOperation value)
        {
            if (!_assignments.TryGetValue(local, out var values))
            {
                values = [];
                _assignments.Add(local, values);
            }

            values.Add(value);
        }
    }

    private static string? GetSemanticLiteral(object? value)
    {
        return value is string text &&
            (text.StartsWith("ir.", StringComparison.Ordinal) ||
             text.StartsWith("ir_", StringComparison.Ordinal))
                ? text
                : null;
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.ContainingType?.Name == "OperationSupportCatalogData")
        {
            return;
        }
        if (field.Type.SpecialType == SpecialType.System_String &&
            field.ContainingType?.Name is not ("IrUnsupportedInfo" or "IrExceptionInfo") &&
            IsNamespaceOrNested(field.ContainingNamespace, "SharpProof", "Ir"))
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, field.Locations.FirstOrDefault(), field.Name);
        }

        if (field.IsConst || field.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if ((!field.IsReadOnly || IsMutableStorageType(
                field.Type,
                context.CancellationToken)) &&
            IsForbiddenMutableStaticStorage(field))
        {
            Report(context, MetaDiagnosticDescriptors.MutableStaticState, field.Locations.FirstOrDefault(), field.Name);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        // Abstract (including static abstract interface) accessors have no
        // storage in the declaring type. Their implementation owns any
        // state, so they must not be classified as mutable static storage.
        if (property.IsAbstract)
        {
            return;
        }
        var isAutoProperty = IsAutoProperty(
            property,
            context.CancellationToken);
        if (property.Type.SpecialType == SpecialType.System_String &&
            property.ContainingType?.Name is not ("IrUnsupportedInfo" or "IrExceptionInfo") &&
            IsNamespaceOrNested(property.ContainingNamespace, "SharpProof", "Ir") &&
            isAutoProperty)
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, property.Locations.FirstOrDefault(), property.Name);
        }
        if ((property.SetMethod != null || IsMutableStorageType(
                property.Type,
                context.CancellationToken)) &&
            IsForbiddenMutableStaticStorage(property) &&
            isAutoProperty)
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
        if (@event.IsAbstract)
        {
            return;
        }
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

    private static bool IsMutableStorageType(
        ITypeSymbol type,
        CancellationToken cancellationToken)
    {
        return IsMutableStorageType(
            type,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default),
            cancellationToken);
    }

    private static bool IsMutableStorageType(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visiting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return !typeParameter.HasValueTypeConstraint;
        }

        if (type.IsValueType ||
            type.SpecialType != SpecialType.None ||
            type is not INamedTypeSymbol named ||
            named.TypeKind == TypeKind.Delegate)
        {
            return false;
        }
        var initialTypeIsImmutable = IsKnownImmutableStorageType(named);
        var initialTypeIsWeakCache = IsCompilationScopedWeakCache(named);
        if (initialTypeIsImmutable || initialTypeIsWeakCache)
        {
            return false;
        }

        var definition = named.OriginalDefinition;
        if (!visiting.Add(definition))
        {
            return false;
        }

        try
        {
            var isInitialType = true;
            for (var current = named;
                 current != null &&
                 current.SpecialType == SpecialType.None;
                 current = current.BaseType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!isInitialType &&
                    (IsKnownImmutableStorageType(current) ||
                     IsCompilationScopedWeakCache(current)))
                {
                    continue;
                }
                isInitialType = false;

                // Metadata does not expose enough implementation detail to
                // prove that an arbitrary reference type is immutable.
                if (current.DeclaringSyntaxReferences.Length == 0)
                {
                    return true;
                }

                var members = current.GetMembers();
                foreach (var field in members.OfType<IFieldSymbol>())
                {
                    if (field.IsStatic || field.IsConst)
                    {
                        continue;
                    }

                    if (!field.IsReadOnly ||
                        IsMutableStorageType(
                            field.Type,
                            visiting,
                            cancellationToken))
                    {
                        return true;
                    }
                }

                foreach (var property in members.OfType<IPropertySymbol>())
                {
                    if (property.IsStatic)
                    {
                        continue;
                    }

                    if (property.SetMethod is { IsInitOnly: false } ||
                        (IsAutoProperty(property, cancellationToken) &&
                         IsMutableStorageType(
                             property.Type,
                             visiting,
                             cancellationToken)))
                    {
                        return true;
                    }
                }

                if (members.OfType<IEventSymbol>().Any(
                        static @event => !@event.IsStatic))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            visiting.Remove(definition);
        }
    }

    private static bool IsKnownImmutableStorageType(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.DeclaringSyntaxReferences.Length != 0)
        {
            return false;
        }

        if (IsExactNamespace(
                type.ContainingNamespace,
                "System",
                "Collections",
                "Immutable"))
        {
            return !string.Equals(
                type.Name,
                "Builder",
                StringComparison.Ordinal);
        }

        if (IsExactNamespace(
                type.ContainingNamespace,
                "System",
                "Collections",
                "Frozen"))
        {
            return true;
        }

        return IsExactNamedType(type, "Version", "System") ||
            IsExactNamedType(
                type,
                "DiagnosticDescriptor",
                "Microsoft",
                "CodeAnalysis");
    }

    private static bool IsCompilationScopedWeakCache(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.DeclaringSyntaxReferences.Length != 0 ||
            !IsExactNamedType(
                type.OriginalDefinition,
                "ConditionalWeakTable",
                "System",
                "Runtime",
                "CompilerServices") ||
            type.TypeArguments.Length != 2)
        {
            return false;
        }

        for (var current = type.TypeArguments[0] as INamedTypeSymbol;
             current != null;
             current = current.BaseType)
        {
            if (IsExactNamedType(
                    current.OriginalDefinition,
                    "Compilation",
                    "Microsoft",
                    "CodeAnalysis"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExactNamedType(
        INamedTypeSymbol type,
        string name,
        params string[] containingNamespace)
    {
        return string.Equals(type.Name, name, StringComparison.Ordinal) &&
            IsExactNamespace(type.ContainingNamespace, containingNamespace);
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
        IsNamespaceOrNested(value, "SharpProof", "Verify") ||
        IsNamespaceOrNested(value, "SharpProof", "Meta", "Analyzers") ||
        IsNamespaceOrNested(value, "SharpProof", "ContractForGenerator");
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

    internal static bool IsExactNamespace(
        INamespaceSymbol? value,
        params string[] expected)
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

    internal static bool IsSameType(ITypeSymbol? actual, INamedTypeSymbol? expected)
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
        Compilation, SemanticModel, ModelExtensions, CSharpCompilation,
        CSharpSemanticModel, CSharpExtensions, SyntaxFactory, CSharpSyntaxTree, Symbol,
        DiagnosticDescriptor,
        OperationCanceledException, CancellationToken, CompilationModelProvider,
        MetaDiagnosticDescriptors, AnalyzerDiagnosticDescriptors,
        ContractForDiagnosticDescriptors, String,
        Assumption, ProofKernel, CallableEvidenceBuilder, CallableVerifier,
        PostconditionObligationBuilder,
        EffectSummary, EffectSummaryDomain,
        EffectSummaryOperations, ExternalEffectResolver, ProvenOutcome, RefutedOutcome,
        ValidatedModel, WorkerProgram, SharpProofWorker,
        CallableVerificationPolicy, CallableVerificationResult,
        WorkerClaimReason, WorkerCallableCoverageReason, WorkerVerifyRequest,
        WorkerVerifyResponse, WorkerResultAssembler, WorkerRunStatus,
        RuntimeHelpers
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
                candidate.Parameters[0].Name == "request" &&
                candidate.Parameters[0].RefKind == RefKind.None &&
                IsSameType(candidate.Parameters[0].Type, this[KnownType.WorkerVerifyRequest]) &&
                candidate.Parameters[1].Name == "cancellationToken" &&
                candidate.Parameters[1].RefKind == RefKind.None &&
                IsSameType(candidate.Parameters[1].Type, this[KnownType.CancellationToken]));
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
