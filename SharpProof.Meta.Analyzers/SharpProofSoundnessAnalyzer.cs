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
        "System.AggregateException",
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
            startContext.RegisterOperationAction(
                c => AnalyzeBinaryOperation(c, symbols),
                OperationKind.BinaryOperator);
            startContext.RegisterOperationAction(
                c => AnalyzeCSharpCompoundAssignment(c, symbols),
                OperationKind.CompoundAssignment);
            startContext.RegisterOperationAction(
                c => AnalyzeInterpolatedString(c, symbols),
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
        if (IsCSharpExpressionTextProducer(invocation, symbols))
        {
            AnalyzeCSharpExpressionText(context, invocation, symbols);
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

    private static void AnalyzeBinaryOperation(
        OperationAnalysisContext context,
        KnownSymbols symbols)
    {
        AnalyzeSemanticString(context, symbols);
        if (IsStringAddition(context.Operation))
        {
            AnalyzeCSharpExpressionText(context, context.Operation, symbols);
        }
    }

    private static void AnalyzeSemanticString(
        OperationAnalysisContext context,
        KnownSymbols symbols)
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
            symbols,
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
        var isEqualityComparerEquals =
            method.Name == "Equals" &&
            symbols.ImplementsGenericInterface(
                invocation.Instance?.Type ?? method.ContainingType,
                "IEqualityComparer`1");
        var isCollectionMembership =
            IsCollectionMembershipInvocation(invocation, symbols);
        var isSpanSequenceEqual =
            method.Name == "SequenceEqual" &&
            IsSameType(method.ContainingType, symbols.MemoryExtensions);
        var isStringOrdinalComparison =
            IsSameType(method.ContainingType, symbols[KnownType.String]) &&
            (method.Name.StartsWith("Compare", StringComparison.Ordinal) ||
             method.Name is "IndexOf" or "LastIndexOf") &&
            method.ReturnType.SpecialType == SpecialType.System_Int32 &&
            IsUsedInComparison(invocation);
        var isBooleanComparison =
            method.ReturnType.SpecialType == SpecialType.System_Boolean &&
            (isStringPredicate || isObjectEquals || isEqualityComparerEquals ||
             isCollectionMembership || isSpanSequenceEqual);
        if (!isBooleanComparison && !isStringOrdinalComparison)
        {
            return;
        }

        var literalResolver = new SemanticLiteralResolver(
            invocation,
            symbols,
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

    private static bool IsCollectionMembershipInvocation(
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        var method = invocation.TargetMethod;
        var receiverType = invocation.Instance?.Type ??
            invocation.Arguments.FirstOrDefault()?.Value.Type;
        if (method.Name is "ContainsKey" or "TryGetValue")
        {
            return symbols.ImplementsGenericInterface(
                    receiverType,
                    "IDictionary`2") ||
                symbols.ImplementsGenericInterface(
                    receiverType,
                    "IReadOnlyDictionary`2");
        }

        if (method.Name != "Contains")
        {
            return false;
        }

        return symbols.ImplementsGenericInterface(
                receiverType,
                "ICollection`1") ||
            symbols.ImplementsGenericInterface(receiverType, "ISet`1") ||
            symbols.ImplementsGenericInterface(
                receiverType,
                "IReadOnlySet`1") ||
            IsSameType(method.ContainingType, symbols.Enumerable) &&
            symbols.ImplementsGenericInterface(
                receiverType,
                "IEnumerable`1");
    }

    private static bool IsUsedInComparison(IOperation operation)
    {
        var parent = operation.Parent;
        while (parent is IParenthesizedOperation or
               IConversionOperation { OperatorMethod: null })
        {
            parent = parent.Parent;
        }

        return parent is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Equals or
                BinaryOperatorKind.NotEquals or
                BinaryOperatorKind.LessThan or
                BinaryOperatorKind.LessThanOrEqual or
                BinaryOperatorKind.GreaterThan or
                BinaryOperatorKind.GreaterThanOrEqual
        };
    }

    private static IOperation? GetStringAsSpanSource(
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        var method = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        if (method.Name != "AsSpan" ||
            !IsSameType(method.ContainingType, symbols.MemoryExtensions))
        {
            return null;
        }

        if (invocation.Instance?.Type?.SpecialType == SpecialType.System_String)
        {
            return invocation.Instance;
        }

        return invocation.Arguments
            .Select(static argument => argument.Value)
            .FirstOrDefault(static argument =>
                argument.Type?.SpecialType == SpecialType.System_String);
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
        OperationAnalysisContext context,
        KnownSymbols symbols)
    {
        if (IsStringAddition(context.Operation))
        {
            AnalyzeCSharpExpressionText(context, context.Operation, symbols);
        }
    }

    private static void AnalyzeCSharpExpressionText(
        OperationAnalysisContext context,
        IOperation operation,
        KnownSymbols symbols)
    {
        if (IsNestedCSharpExpressionConstruction(operation, symbols))
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(
            operation,
            symbols,
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
        OperationAnalysisContext context,
        KnownSymbols symbols)
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
                symbols,
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
        KnownSymbols symbols,
        CancellationToken cancellationToken)
    {
        var shape = new StringBuilder();
        AppendCSharpExpressionShape(
            operation,
            shape,
            symbols,
            cancellationToken);
        var value = shape.ToString();
        return CSharpExpressionFragments.FirstOrDefault(fragment => value.IndexOf(fragment, StringComparison.Ordinal) >= 0);
    }

    private static void AppendCSharpExpressionShape(
        IOperation operation,
        StringBuilder shape,
        KnownSymbols symbols,
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
                    IsCSharpExpressionTextProducer(invocation, symbols):
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
                    if (invocation.Instance != null)
                    {
                        pending.Push(invocation.Instance);
                    }
                    break;
                case IParenthesizedOperation or
                    IConversionOperation { OperatorMethod: null }:
                    pending.Push(OperationUnwrapping.Unwrap(
                        current,
                        cancellationToken)!);
                    break;
                default:
                    shape.Append('\0');
                    break;
            }
        }
    }

    private static bool IsNestedCSharpExpressionConstruction(
        IOperation operation,
        KnownSymbols symbols)
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
            IsCSharpExpressionTextProducer(invocation, symbols);
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

    private static bool IsCSharpExpressionTextProducer(
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        var method = invocation.TargetMethod;
        if (IsStringConcat(invocation))
        {
            return true;
        }

        if (method.ContainingType.SpecialType == SpecialType.System_String)
        {
            return method.Name is nameof(string.Format) or
                nameof(string.Join) or
                nameof(string.Replace);
        }

        return IsSameType(method.ContainingType, symbols.StringBuilder) &&
            method.Name is ("Append" or "AppendFormat" or "Insert");
    }

    private sealed class SemanticLiteralResolver
    {
        private readonly CancellationToken _cancellationToken;
        private readonly KnownSymbols _symbols;
        private readonly Lazy<Dictionary<ILocalSymbol, List<IOperation>>> _assignments;
        private readonly Dictionary<ILocalSymbol, string> _literalCache =
            new(SymbolEqualityComparer.Default);

        internal SemanticLiteralResolver(
            IOperation operation,
            KnownSymbols symbols,
            CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _symbols = symbols;
            _assignments = new(() => IndexAssignments(operation, cancellationToken));
        }

        private static Dictionary<ILocalSymbol, List<IOperation>> IndexAssignments(
            IOperation operation,
            CancellationToken cancellationToken)
        {
            var assignments = new Dictionary<ILocalSymbol, List<IOperation>>(
                SymbolEqualityComparer.Default);
            var root = operation;
            while (root.Parent != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                root = root.Parent;
            }

            foreach (var candidate in root.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (candidate)
                {
                    case IVariableDeclaratorOperation declaration
                        when declaration.Initializer?.Value is { } value:
                        AddAssignment(assignments, declaration.Symbol, value);
                        break;
                    case ISimpleAssignmentOperation
                    {
                        Target: ILocalReferenceOperation target,
                        Value: { } value
                    }:
                        AddAssignment(assignments, target.Local, value);
                        break;
                }
            }
            return assignments;
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
                case IInvocationOperation invocation:
                    var stringSpanSource = GetStringAsSpanSource(
                        invocation,
                        _symbols);
                    if (stringSpanSource != null)
                    {
                        return Resolve(stringSpanSource, visitedLocals);
                    }
                    break;
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

            if (!_assignments.Value.TryGetValue(localReference.Local, out var values))
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

        private static void AddAssignment(
            Dictionary<ILocalSymbol, List<IOperation>> assignments,
            ILocalSymbol local,
            IOperation value)
        {
            if (!assignments.TryGetValue(local, out var values))
            {
                values = [];
                assignments.Add(local, values);
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
            IsForbiddenMutableStaticStorage(
                field))
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
            IsForbiddenMutableStaticStorage(
                property) &&
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
        if (IsForbiddenMutableStaticStorage(
                @event) &&
            IsFieldLikeEvent(@event, context.CancellationToken))
        {
            Report(
                context,
                MetaDiagnosticDescriptors.MutableStaticState,
                @event.Locations.FirstOrDefault(),
                @event.Name);
        }
    }

    private static bool IsForbiddenMutableStaticStorage(
        ISymbol symbol)
    {
        if (!symbol.IsStatic ||
            !IsCriticalStateNamespace(symbol.ContainingNamespace))
        {
            return false;
        }

        if (symbol is IFieldSymbol field &&
            (field.GetAttributes().Any(static attribute =>
                 attribute.AttributeClass is { } attributeClass &&
                 IsExactNamedType(
                     attributeClass.OriginalDefinition,
                     "ThreadStaticAttribute",
                     "System")) ||
             IsApprovedInterlockedScopeCounter(field) ||
             IsApprovedCompilerSourceTreeBindings(field)))
        {
            return false;
        }

        return !IsApprovedImmutableStaticSingleton(symbol);
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

        if (type.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None ||
            type is not INamedTypeSymbol named ||
            named.TypeKind == TypeKind.Delegate)
        {
            return false;
        }

        if (IsScopedWeakCache(named))
        {
            return false;
        }

        var initialTypeIsImmutable = IsKnownImmutableStorageType(named);
        if (initialTypeIsImmutable || IsKnownGenericValueContainer(named))
        {
            return HasMutableTypeArgument(
                named,
                visiting,
                cancellationToken);
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            return false;
        }

        if (named.IsValueType)
        {
            return IsMutableValueType(
                named,
                visiting,
                cancellationToken);
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
                     IsScopedWeakCache(current)))
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
            return type.Name != "Builder";
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
            IsExactNamedType(type, "Type", "System") ||
            IsExactNamedType(type, "UTF8Encoding", "System", "Text") ||
            IsExactNamedType(
                type,
                "DiagnosticDescriptor",
                "Microsoft",
                "CodeAnalysis");
    }

    private static bool IsMutableValueType(
        INamedTypeSymbol type,
        HashSet<ITypeSymbol> visiting,
        CancellationToken cancellationToken)
    {
        var definition = type.OriginalDefinition;
        if (!visiting.Add(definition))
        {
            return false;
        }

        try
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!field.IsStatic &&
                    !field.IsConst &&
                    IsMutableStorageType(
                        field.Type,
                        visiting,
                        cancellationToken))
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

    private static bool HasMutableTypeArgument(
        INamedTypeSymbol type,
        HashSet<ITypeSymbol> visiting,
        CancellationToken cancellationToken)
    {
        foreach (var typeArgument in type.TypeArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMutableStorageType(
                    typeArgument,
                    visiting,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownGenericValueContainer(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.DeclaringSyntaxReferences.Length != 0)
        {
            return false;
        }

        return IsExactNamedType(
                type.OriginalDefinition,
                "KeyValuePair",
                "System",
                "Collections",
                "Generic") ||
            IsExactNamedType(
                type.OriginalDefinition,
                "Nullable",
                "System") ||
            IsExactNamedType(
                type.OriginalDefinition,
                "ValueTuple",
                "System");
    }

    private static bool IsScopedWeakCache(INamedTypeSymbol type)
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

        var keyType = type.TypeArguments[0];
        for (var current = keyType as INamedTypeSymbol;
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

        if (keyType is INamedTypeSymbol namedKey &&
            IsRoslynSymbolType(namedKey))
        {
            return true;
        }

        return keyType is ITypeParameterSymbol typeParameter &&
            typeParameter.ConstraintTypes
                .OfType<INamedTypeSymbol>()
                .Any(IsRoslynSymbolType);
    }

    private static bool IsRoslynSymbolType(INamedTypeSymbol type)
    {
        return IsExactNamedType(
                type.OriginalDefinition,
                "ISymbol",
                "Microsoft",
                "CodeAnalysis") ||
            type.AllInterfaces.Any(static @interface => IsExactNamedType(
                @interface.OriginalDefinition,
                "ISymbol",
                "Microsoft",
                "CodeAnalysis"));
    }

    private static bool IsApprovedInterlockedScopeCounter(IFieldSymbol field)
    {
        if (field.Name != "s_nextScope" ||
            field.DeclaredAccessibility != Accessibility.Private ||
            !field.IsStatic ||
            field.IsReadOnly ||
            field.IsConst ||
            field.Type.SpecialType != SpecialType.System_Int64 ||
            !IsApprovedScopeCounterOwner(field.ContainingType))
        {
            return false;
        }

        return HasSoundnessSuppression(
            field,
            "Every reference uses Interlocked.Increment(ref s_nextScope).");
    }

    private static bool IsApprovedImmutableStaticSingleton(ISymbol symbol)
    {
        var justification = symbol switch
        {
            IPropertySymbol property when
                property.Name is ("Bottom" or "Empty" or "Top") &&
                IsExactNamedType(
                    property.ContainingType,
                    "EffectSummary",
                    "SharpProof",
                    "Effects") =>
                "Immutable EffectSummary value singleton with get-only state.",
            IPropertySymbol property when
                property.Name == "Unknown" &&
                IsExactNamedType(
                    property.ContainingType,
                    "EffectThrowSet",
                    "SharpProof",
                    "Effects") =>
                "Unknown throw-set singleton stores no mutable membership cache.",
            IPropertySymbol property when
                property.Name == "Empty" &&
                IsExactNamedType(
                    property.ContainingType,
                    "EffectClaimConstraint",
                    "SharpProof",
                    "Analyzer") =>
                "Empty effect-claim constraint is an immutable value sentinel.",
            IPropertySymbol property when
                property.Name == "Instance" &&
                IsExactNamedType(
                    property.ContainingType,
                    "EffectSummaryDomain",
                    "SharpProof",
                    "Effects") =>
                "EffectSummaryDomain has no mutable instance state.",
            IPropertySymbol property when
                property.Name == "Instance" &&
                property.ContainingType.Name == "FlowDomain" &&
                property.ContainingType.ContainingType?.Name == "ManagedFlowState" &&
                IsExactNamespace(
                    property.ContainingNamespace,
                    "SharpProof",
                    "Effects") =>
                "ManagedFlowState.FlowDomain has no mutable instance state.",
            IPropertySymbol property when
                property.Name == "Missing" &&
                property.ContainingType.Name == "MetadataImportAssemblyResult" &&
                property.ContainingType.ContainingType?.Name == "EffectAnalysisSession" &&
                IsExactNamespace(
                    property.ContainingNamespace,
                    "SharpProof",
                    "Effects") =>
                "Missing metadata-import sentinel has a null assembly and no mutable state.",
            IFieldSymbol field when
                field.Name == "Comparer" &&
                IsExactNamedType(
                    field.ContainingType,
                    "CompilerDiagnosticArtifactOrdering",
                    "SharpProof",
                    "CompilerArtifact") =>
                "Comparer is a stateless immutable ordering singleton.",
            IFieldSymbol field when
                field.Name == "CapacityPriorityComparer" &&
                IsExactNamedType(
                    field.ContainingType,
                    "VerificationCache",
                    "SharpProof",
                    "Worker") =>
                "Capacity comparer is a stateless immutable ordering singleton.",
            IPropertySymbol property when
                property.Name == "Unsupported" &&
                IsExactNamedType(
                    property.ContainingType,
                    "ExpressionBindingResult",
                    "SharpProof",
                    "Contracts") =>
                "Unsupported binding result is an immutable failure sentinel.",
            IPropertySymbol property when
                property.Name == "Empty" &&
                property.ContainingType.Name == "ClauseBindingResult" &&
                property.ContainingType.ContainingType?.Name == "ContractBinder" &&
                IsExactNamespace(
                    property.ContainingNamespace,
                    "SharpProof",
                    "Contracts") =>
                "Empty binding result is an immutable value sentinel.",
            IPropertySymbol property when
                property.Name == "None" &&
                property.ContainingType.Name == "CompanionResolution" &&
                property.ContainingType.ContainingType?.Name == "ContractForSymbolMatcher" &&
                IsExactNamespace(
                    property.ContainingNamespace,
                    "SharpProof",
                    "Contracts") =>
                "None companion resolution is an immutable value sentinel.",
            IFieldSymbol field when
                field.Name == "NoValues" &&
                IsExactNamedType(
                    field.ContainingType,
                    "ManagedFlowState",
                    "SharpProof",
                    "Effects") =>
                "NoValues is an empty immutable dictionary sentinel with no object keys or mutable entries.",
            IPropertySymbol property when
                property.Name is ("Bottom" or "Empty" or "Top") &&
                IsExactNamedType(
                    property.ContainingType,
                    "ManagedFlowState",
                    "SharpProof",
                    "Effects") =>
                "ManagedFlowState instances are immutable canonical value sentinels.",
            _ => null
        };

        return justification != null &&
            HasSoundnessSuppression(symbol, justification);
    }

    private static bool IsApprovedCompilerSourceTreeBindings(IFieldSymbol field)
    {
        if (field.Name != "TreeBindings" ||
            field.DeclaredAccessibility != Accessibility.Private ||
            !field.IsStatic ||
            !IsExactNamedType(
                field.ContainingType,
                "CompilerSourceLocationAuthority",
                "SharpProof",
                "CompilerArtifact") ||
            field.Type is not INamedTypeSymbol table ||
            !IsExactNamedType(
                table.OriginalDefinition,
                "ConditionalWeakTable",
                "System",
                "Runtime",
                "CompilerServices") ||
            table.TypeArguments.Length != 2 ||
            table.TypeArguments[0] is not INamedTypeSymbol keyType ||
            !IsExactNamedType(
                keyType,
                "WorkerSourceLocation",
                "SharpProof",
                "Worker",
                "Protocol") ||
            table.TypeArguments[1] is not INamedTypeSymbol valueType ||
            valueType.Name != "TreeBinding" ||
            valueType.ContainingType?.Name != "CompilerSourceLocationAuthority" ||
            !IsExactNamespace(
                valueType.ContainingNamespace,
                "SharpProof",
                "CompilerArtifact"))
        {
            return false;
        }

        return HasSoundnessSuppression(
            field,
            "Weakly associates each source-location object with its immutable owning-tree ordinal.");
    }

    private static bool HasSoundnessSuppression(
        ISymbol symbol,
        string justification)
    {
        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { } attributeClass &&
            IsExactNamedType(
                attributeClass.OriginalDefinition,
                "SuppressMessageAttribute",
                "System",
                "Diagnostics",
                "CodeAnalysis") &&
            attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value as string == "SharpProof.Soundness" &&
            attribute.ConstructorArguments[1].Value as string == "SPMETA002" &&
            attribute.NamedArguments.Any(argument =>
                argument.Key == "Justification" &&
                argument.Value.Value as string == justification));
    }

    private static bool IsApprovedScopeCounterOwner(INamedTypeSymbol? containingType)
    {
        if (containingType == null)
        {
            return false;
        }

        return (IsExactNamespace(containingType.ContainingNamespace, "SharpProof", "Ir") &&
                containingType.Name is ("IrFactory" or "IrProgramBuilder")) ||
            (IsExactNamespace(containingType.ContainingNamespace, "SharpProof", "Specs") &&
             containingType.Name == "ApiSpecTable");
    }

    private static bool IsExactNamedType(
        INamedTypeSymbol type,
        string name,
        params string[] containingNamespace)
    {
        return type.Name == name &&
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
            IsNamespaceOrNested(value, "SharpProof", "ContractForGenerator") ||
            IsNamespaceOrNested(value, "SharpProof", "Effects") ||
            IsNamespaceOrNested(value, "SharpProof", "Contracts") ||
            IsNamespaceOrNested(value, "SharpProof", "Dataflow") ||
            IsNamespaceOrNested(value, "SharpProof", "Ir") ||
            IsNamespaceOrNested(value, "SharpProof", "Specs") ||
            IsNamespaceOrNested(value, "SharpProof", "Smt") ||
            IsNamespaceOrNested(value, "SharpProof", "Summaries") ||
            IsNamespaceOrNested(value, "SharpProof", "CompilerArtifact") ||
            IsNamespaceOrNested(value, "SharpProof", "CompilerCollector") ||
            IsNamespaceOrNested(value, "SharpProof", "Worker");
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
                current.Name != expected[index])
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
        OperationCanceledException, AggregateException, CancellationToken, CompilationModelProvider,
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
        private readonly ImmutableArray<INamedTypeSymbol?> _comparisonInterfaces;

        internal KnownSymbols(Compilation compilation)
        {
            var types = new INamedTypeSymbol?[KnownTypeNames.Length];
            for (var index = 0; index < KnownTypeNames.Length; index++)
            {
                types[index] = compilation.GetTypeByMetadataName(KnownTypeNames[index]);
            }

            types[(int)KnownType.String] = compilation.GetSpecialType(SpecialType.System_String);
            _types = [.. types];
            StringBuilder = compilation.GetTypeByMetadataName(
                "System.Text.StringBuilder");
            _comparisonInterfaces = [
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.IEqualityComparer`1"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.ICollection`1"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.ISet`1"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.IReadOnlySet`1"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.IDictionary`2"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.IReadOnlyDictionary`2"),
                compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.IEnumerable`1")];
            MemoryExtensions = compilation.GetTypeByMetadataName(
                "System.MemoryExtensions");
            Enumerable = compilation.GetTypeByMetadataName(
                "System.Linq.Enumerable");

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
                candidate.Parameters[0] is { Name: "request", RefKind: RefKind.None, Type: var requestType } &&
                IsSameType(requestType, this[KnownType.WorkerVerifyRequest]) &&
                candidate.Parameters[1] is { Name: "cancellationToken", RefKind: RefKind.None, Type: var tokenType } &&
                IsSameType(tokenType, this[KnownType.CancellationToken]));
        }

        internal INamedTypeSymbol? this[KnownType type] => _types[(int)type];
        internal INamedTypeSymbol? StringBuilder
        {
            get;
        }
        internal INamedTypeSymbol? MemoryExtensions
        {
            get;
        }
        internal INamedTypeSymbol? Enumerable
        {
            get;
        }
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

        internal bool ImplementsGenericInterface(
            ITypeSymbol? type,
            string metadataName)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var candidate in _comparisonInterfaces)
            {
                if (candidate?.MetadataName != metadataName)
                {
                    continue;
                }

                if (IsSameType(namedType, candidate) ||
                    namedType.AllInterfaces.Any(interfaceType =>
                        IsSameType(interfaceType, candidate)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
