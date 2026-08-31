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
            [KnownType.SemanticModel] = Names("TryGetSpeculativeSemanticModel", "GetSpeculativeTypeInfo", "GetDiagnostics")
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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = new KnownSymbols(startContext.Compilation);
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, symbols), OperationKind.Invocation);
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
        if (IsForbidden(method, invocation, context.ContainingSymbol, symbols))
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

    private static bool IsForbidden(
        IMethodSymbol method,
        IInvocationOperation invocation,
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
            !IsAnyType(
                containingType,
                symbols,
                KnownType.MetaDiagnosticDescriptors,
                KnownType.AnalyzerDiagnosticDescriptors,
                KnownType.ContractForDiagnosticDescriptors))
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

        var literal = GetSemanticLiteral(
                binary.LeftOperand,
                context.CancellationToken) ??
            GetSemanticLiteral(
                binary.RightOperand,
                context.CancellationToken);
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

        var literal = invocation.Instance == null
            ? null
            : GetSemanticLiteral(
                invocation.Instance,
                context.CancellationToken);
        literal ??= invocation.Arguments
            .Select(argument => GetSemanticLiteral(
                argument.Value,
                context.CancellationToken))
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
                case IParenthesizedOperation parenthesized:
                    pending.Push(parenthesized.Operand);
                    break;
                case IConversionOperation { OperatorMethod: null } conversion:
                    pending.Push(conversion.Operand);
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
        var root = operation;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        return GetSemanticLiteral(
            operation,
            root,
            new HashSet<ILocalSymbol>(
                SymbolEqualityComparer.Default),
            cancellationToken);
    }

    private static string? GetSemanticLiteral(
        IOperation operation,
        IOperation root,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.ConstantValue.HasValue)
        {
            return GetSemanticLiteral(operation.ConstantValue.Value);
        }

        switch (operation)
        {
            case IArgumentOperation argument:
                return GetSemanticLiteral(
                    argument.Value,
                    root,
                    visitedLocals,
                    cancellationToken);
            case IConversionOperation conversion:
                return GetSemanticLiteral(
                    conversion.Operand,
                    root,
                    visitedLocals,
                    cancellationToken);
            case IParenthesizedOperation parenthesized:
                return GetSemanticLiteral(
                    parenthesized.Operand,
                    root,
                    visitedLocals,
                    cancellationToken);
        }
        if (operation is not ILocalReferenceOperation localReference ||
            !visitedLocals.Add(localReference.Local))
        {
            return null;
        }

        foreach (var candidate in root.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IOperation? value = candidate switch
            {
                IVariableDeclaratorOperation declaration
                    when SymbolEqualityComparer.Default.Equals(
                        declaration.Symbol,
                        localReference.Local) =>
                    declaration.Initializer?.Value,
                ISimpleAssignmentOperation
                {
                    Target: ILocalReferenceOperation target
                } assignment
                    when SymbolEqualityComparer.Default.Equals(
                        target.Local,
                        localReference.Local) =>
                    assignment.Value,
                _ => null
            };
            if (value == null)
            {
                continue;
            }

            var literal = GetSemanticLiteral(
                value,
                root,
                visitedLocals,
                cancellationToken);
            if (literal != null)
            {
                return literal;
            }
        }

        return null;
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
        if (field.Type.SpecialType == SpecialType.System_String &&
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
        if (property.Type.SpecialType == SpecialType.System_String &&
            IsNamespaceOrNested(property.ContainingNamespace, "SharpProof", "Ir") &&
            IsAutoProperty(property, context.CancellationToken))
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, property.Locations.FirstOrDefault(), property.Name);
        }

        if ((property.SetMethod != null || IsMutableStorageType(
                property.Type,
                context.CancellationToken)) &&
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
            named.TypeKind == TypeKind.Delegate ||
            IsKnownImmutableStorageType(named) ||
            IsCompilationScopedWeakCache(named))
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
            for (var current = named;
                 current != null &&
                 current.SpecialType == SpecialType.None;
                 current = current.BaseType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsKnownImmutableStorageType(current) ||
                    IsCompilationScopedWeakCache(current))
                {
                    continue;
                }

                // Metadata does not expose enough implementation detail to
                // prove that an arbitrary reference type is immutable.
                if (current.DeclaringSyntaxReferences.Length == 0)
                {
                    return true;
                }

                foreach (var field in current.GetMembers()
                             .OfType<IFieldSymbol>())
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

                foreach (var property in current.GetMembers()
                             .OfType<IPropertySymbol>())
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

                if (current.GetMembers().OfType<IEventSymbol>().Any(
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
        Compilation, SemanticModel, SyntaxFactory, CSharpSyntaxTree, Symbol,
        DiagnosticDescriptor,
        OperationCanceledException, CancellationToken, CompilationModelProvider,
        MetaDiagnosticDescriptors, AnalyzerDiagnosticDescriptors,
        ContractForDiagnosticDescriptors, String,
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
