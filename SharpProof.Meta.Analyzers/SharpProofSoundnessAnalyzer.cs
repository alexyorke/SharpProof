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
        "Microsoft.CodeAnalysis.CSharp.CSharpCompilation",
        "Microsoft.CodeAnalysis.CSharp.CSharpSemanticModel",
        "Microsoft.CodeAnalysis.CSharp.CSharpExtensions",
        "Microsoft.CodeAnalysis.CSharp.SyntaxFactory", "Microsoft.CodeAnalysis.ISymbol",
        "Microsoft.CodeAnalysis.CSharp.SymbolDisplay",
        "Microsoft.CodeAnalysis.DiagnosticDescriptor", "System.OperationCanceledException",
        "System.Threading.CancellationToken", "Microsoft.Build.Framework.ITask",
        "SharpProof.Frontend.Host.CompilationModelProvider",
        "SharpProof.Analyzer.GeneratedDiagnosticDescriptors", "SharpProof.ContractForValidation.ContractForDiagnosticDescriptors",
        "SharpProof.Meta.Analyzers.MetaDiagnosticDescriptors",
        "System.String", "SharpProof.Verify.Assumption", "SharpProof.Verify.ProofKernel",
        "SharpProof.Worker.CallableEvidenceBuilder",
        "SharpProof.Worker.CallableVerifier", "SharpProof.Worker.PostconditionObligationBuilder",
        "SharpProof.Effects.EffectSummary",
        "SharpProof.Effects.EffectSummaryDomain", "SharpProof.Effects.EffectSummaryOperations",
        "SharpProof.Effects.ExternalEffectResolver", "SharpProof.Verify.ProvenOutcome",
        "SharpProof.Verify.RefutedOutcome", "SharpProof.Verify.ValidatedModel", "SharpProof.Worker.Program",
        "SharpProof.Verify.ISemanticCache",
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
            [KnownType.SemanticModel] = Names(
                "TryGetSpeculativeSemanticModel",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo",
                "GetDiagnostics"),
            [KnownType.CSharpSemanticModel] = Names(
                "TryGetSpeculativeSemanticModelForMethodBody",
                "TryGetSpeculativeSemanticModel",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo",
                "GetDiagnostics"),
            [KnownType.CSharpExtensions] = Names(
                "TryGetSpeculativeSemanticModelForMethodBody",
                "TryGetSpeculativeSemanticModel",
                "GetSpeculativeSymbolInfo",
                "GetSpeculativeTypeInfo",
                "GetSpeculativeAliasInfo"),
            [KnownType.SyntaxFactory] = Names("ParseStatement", "ParseExpression", "ParseTypeName")
        }.ToImmutableDictionary();

    private static readonly ImmutableArray<string> CSharpExpressionFragments =
        [" is null", " is not null", " == ", " != ", " && ", " || ", "=>", "?."];
    private static readonly ImmutableHashSet<string> SemanticStringMethodCatalog = Names(
        "StartsWith",
        "Compare");
    private static readonly ImmutableHashSet<string> DisplayTextMethods = Names(
        "ToDisplayString",
        "ToDisplayParts",
        "ToMinimalDisplayString",
        "ToMinimalDisplayParts");
    private static readonly ImmutableHashSet<string> MutableCollectionNames = Names(
        "BlockingCollection",
        "ConcurrentBag",
        "ConcurrentDictionary",
        "ConcurrentQueue",
        "ConcurrentStack",
        "Dictionary",
        "HashSet",
        "LinkedList",
        "List",
        "Queue",
        "SortedDictionary",
        "SortedList",
        "SortedSet",
        "Stack");

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
            startContext.RegisterOperationAction(c => AnalyzeMethodReference(c, symbols), OperationKind.MethodReference);
            startContext.RegisterOperationAction(
                c => AnalyzeDynamicInvocation(c, symbols),
                OperationKind.DynamicInvocation);
            startContext.RegisterOperationAction(
                c => CacheSoundnessRules.AnalyzeAssignment(c, symbols),
                OperationKind.SimpleAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.DeconstructionAssignment);
            startContext.RegisterOperationAction(
                AnalyzeCompoundAssignmentExpressionText,
                OperationKind.CompoundAssignment);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, symbols), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(c => AnalyzeWith(c, symbols), OperationKind.With);
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
        if (IsForbidden(method, invocation.Instance, context.ContainingSymbol, symbols))
        {
            Report(context, MetaDiagnosticDescriptors.ForbiddenRoslynApi, invocation.Syntax.GetLocation(), method.Name);
        }

        AnalyzeSemanticEquals(context, invocation, symbols);
        AnalyzeSemanticStringMethod(context, invocation, symbols);
        CacheSoundnessRules.AnalyzeWrite(context, invocation, symbols);
        AnalyzeStringConcatExpressionText(context, invocation);
    }

    private static void AnalyzeMethodReference(
        OperationAnalysisContext context,
        KnownSymbols symbols)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        var method = reference.Method.OriginalDefinition;
        if (IsForbidden(method, reference.Instance, context.ContainingSymbol, symbols))
        {
            Report(context, MetaDiagnosticDescriptors.ForbiddenRoslynApi, reference.Syntax.GetLocation(), method.Name);
        }
    }

    private static void AnalyzeDynamicInvocation(
        OperationAnalysisContext context,
        KnownSymbols symbols)
    {
        if (context.Operation is not IDynamicInvocationOperation invocation ||
            GetDynamicMemberName(invocation) is not { } memberName ||
            !IsForbiddenDynamicMember(
                GetDynamicReceiver(invocation),
                memberName,
                context.Compilation,
                context.ContainingSymbol,
                symbols,
                context.CancellationToken))
        {
            return;
        }

        Report(
            context,
            MetaDiagnosticDescriptors.ForbiddenRoslynApi,
            invocation.Syntax.GetLocation(),
            memberName);
    }

    private static bool IsForbiddenDynamicMember(
        IOperation? receiver,
        string memberName,
        Compilation compilation,
        ISymbol containingSymbol,
        KnownSymbols symbols,
        CancellationToken cancellationToken)
    {
        var receiverType = FindDynamicReceiverType(
            receiver,
            compilation,
            symbols,
            cancellationToken,
            []);
        if (receiverType == null)
        {
            return false;
        }

        foreach (var entry in ForbiddenMethods)
        {
            if (IsSameType(receiverType, symbols[entry.Key]) &&
                entry.Value.Contains(memberName))
            {
                return true;
            }
        }

        if (memberName == "GetSemanticModel" &&
            (IsSameType(receiverType, symbols[KnownType.Compilation]) ||
             IsSameType(receiverType, symbols[KnownType.CSharpCompilation])))
        {
            return !IsSameType(
                containingSymbol.ContainingType,
                symbols[KnownType.CompilationModelProvider]);
        }

        return DisplayTextMethods.Contains(memberName) &&
            (IsSameType(receiverType, symbols[KnownType.Symbol]) ||
             receiverType.AllInterfaces.Any(value =>
                 IsSameType(value, symbols[KnownType.Symbol])));
    }

    private static ITypeSymbol? FindDynamicReceiverType(
        IOperation? operation,
        Compilation compilation,
        KnownSymbols symbols,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visited)
    {
        if (operation == null)
        {
            return null;
        }

        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        if (operation.Type is { } type && IsKnownForbiddenReceiverType(type, symbols))
        {
            return type;
        }

        if (operation is not ILocalReferenceOperation local ||
            !visited.Add(local.Local))
        {
            return null;
        }

        foreach (var reference in local.Local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
                {
                    Initializer.Value: var initializer
                })
            {
                continue;
            }

            var model = compilation.GetSemanticModel(initializer.SyntaxTree);
            var initializerOperation = model.GetOperation(initializer, cancellationToken);
            var resolved = FindDynamicReceiverType(
                initializerOperation,
                compilation,
                symbols,
                cancellationToken,
                visited);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool IsKnownForbiddenReceiverType(
        ITypeSymbol type,
        KnownSymbols symbols)
    {
        return ForbiddenMethods.Keys.Any(key => IsSameType(type, symbols[key])) ||
            IsSameType(type, symbols[KnownType.Compilation]) ||
            IsSameType(type, symbols[KnownType.CSharpCompilation]) ||
            IsSameType(type, symbols[KnownType.Symbol]);
    }

    private static string? GetDynamicMemberName(IDynamicInvocationOperation invocation)
    {
        if (invocation.Operation is IDynamicMemberReferenceOperation member)
        {
            return member.MemberName;
        }

        return invocation.Syntax switch
        {
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax memberAccess
            } => memberAccess.Name.Identifier.ValueText,
            InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax memberBinding
            } => memberBinding.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static IOperation? GetDynamicReceiver(IDynamicInvocationOperation invocation)
    {
        return invocation.Operation is IDynamicMemberReferenceOperation member
            ? member.Instance
            : null;
    }

    private static bool IsForbidden(
        IMethodSymbol method,
        IOperation? receiver,
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

        if (method.Name == "GetSemanticModel" &&
            (IsSameType(method.ContainingType, symbols[KnownType.Compilation]) ||
             IsSameType(method.ContainingType, symbols[KnownType.CSharpCompilation])))
        {
            return !IsSameType(containingSymbol.ContainingType, symbols[KnownType.CompilationModelProvider]);
        }

        if (!DisplayTextMethods.Contains(method.Name))
        {
            return false;
        }

        var receiverType = receiver?.Type ?? method.ContainingType;
        return IsSameType(method.ContainingType, symbols[KnownType.SymbolDisplay]) ||
               IsSameType(receiverType, symbols[KnownType.Symbol]) ||
               receiverType?.AllInterfaces.Any(value => IsSameType(value, symbols[KnownType.Symbol])) == true;
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var containingType = context.ContainingSymbol.ContainingType;
        if (IsSameType(creation.Type, symbols[KnownType.DiagnosticDescriptor]) &&
            !IsAnyType(containingType, symbols, KnownType.AnalyzerDiagnosticDescriptors, KnownType.ContractForDiagnosticDescriptors, KnownType.MetaDiagnosticDescriptors))
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

    private static void AnalyzeWith(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var withOperation = (IWithOperation)context.Operation;
        var containingType = context.ContainingSymbol.ContainingType;
        if (IsSameType(withOperation.Type, symbols[KnownType.EffectSummary]) &&
            !IsAnyType(
                containingType,
                symbols,
                KnownType.EffectSummary,
                KnownType.EffectSummaryDomain,
                KnownType.EffectSummaryOperations,
                KnownType.ExternalEffectResolver))
        {
            Report(
                context,
                MetaDiagnosticDescriptors.EffectSummaryConstruction,
                withOperation.Syntax.GetLocation());
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
            } binary)
        {
            return;
        }

        var literal = GetSemanticLiteral(binary.LeftOperand, context.CancellationToken) ??
            GetSemanticLiteral(binary.RightOperand, context.CancellationToken);
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
            invocation.TargetMethod.Name != "Equals")
        {
            return;
        }

        var literal = invocation.Instance == null
            ? null
            : GetSemanticLiteral(invocation.Instance, context.CancellationToken);
        literal ??= invocation.Arguments
            .Select(argument => GetSemanticLiteral(argument.Value, context.CancellationToken))
            .FirstOrDefault(value => value != null);
        if (literal != null)
        {
            Report(context, MetaDiagnosticDescriptors.SemanticStringControlFlow, invocation.Syntax.GetLocation(), literal);
        }
    }

    private static void AnalyzeSemanticStringMethod(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        KnownSymbols symbols)
    {
        if (!IsSameType(invocation.TargetMethod.ContainingType, symbols[KnownType.String]) ||
            !SemanticStringMethodCatalog.Contains(invocation.TargetMethod.Name))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (!IsSameType(argument.Parameter?.Type, symbols[KnownType.String]))
            {
                continue;
            }

            var literal = GetSemanticLiteral(argument.Value, context.CancellationToken);
            if (literal != null)
            {
                Report(context, MetaDiagnosticDescriptors.SemanticStringControlFlow, invocation.Syntax.GetLocation(), literal);
                return;
            }
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

        if (IsHumanMessageContext(binary))
        {
            return;
        }

        // A chained concatenation produces one operation for every `+`. Report
        // the outer expression only so a single source construction does not
        // become a cascade of duplicate diagnostics at the same location.
        if (binary.Parent is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                Type.SpecialType: SpecialType.System_String
            })
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(binary.LeftOperand) ?? GetCSharpExpressionFragment(binary.RightOperand);
        if (fragment != null)
        {
            Report(context, MetaDiagnosticDescriptors.CSharpExpressionText, binary.Syntax.GetLocation(), fragment);
        }
    }

    private static void AnalyzeCompoundAssignmentExpressionText(
        OperationAnalysisContext context)
    {
        if (context.Operation is not ICompoundAssignmentOperation assignment ||
            assignment.Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        if (IsHumanMessageContext(assignment))
        {
            return;
        }

        var fragment = GetCSharpExpressionFragment(assignment.Value);
        if (fragment != null)
        {
            Report(
                context,
                MetaDiagnosticDescriptors.CSharpExpressionText,
                assignment.Syntax.GetLocation(),
                fragment);
        }
    }

    private static void AnalyzeStringConcatExpressionText(
        OperationAnalysisContext context,
        IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Concat" ||
            invocation.TargetMethod.ContainingType.SpecialType !=
                SpecialType.System_String ||
            invocation.Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        if (IsHumanMessageContext(invocation))
        {
            return;
        }

        var fragment = invocation.Arguments
            .Select(static argument => argument.Value)
            .Select(GetCSharpExpressionFragment)
            .FirstOrDefault(static value => value != null);
        if (fragment != null)
        {
            Report(
                context,
                MetaDiagnosticDescriptors.CSharpExpressionText,
                invocation.Syntax.GetLocation(),
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

        if (IsHumanMessageContext(interpolated))
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
        if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string value)
        {
            var fragment = CSharpExpressionFragments.FirstOrDefault(candidate =>
                value.IndexOf(candidate, StringComparison.Ordinal) >= 0);
            if (fragment != null)
            {
                return fragment;
            }
        }

        // Array and collection expressions used by String.Concat do not carry a
        // constant value themselves, but their literal elements still contribute
        // to the emitted expression text. Walk the operation tree so those
        // fragments cannot evade the construction rule.
        foreach (var child in operation.ChildOperations)
        {
            var fragment = GetCSharpExpressionFragment(child);
            if (fragment != null)
            {
                return fragment;
            }
        }

        return null;
    }

    private static bool IsHumanMessageContext(IOperation operation)
    {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is not IArgumentOperation argument)
            {
                continue;
            }

            if (argument.Parameter?.Name is "message" or "format" or "detail" or
                "text" or "reason")
            {
                return true;
            }

            if (argument.Parent is IObjectCreationOperation creation &&
                creation.Constructor?.ContainingType.Name.EndsWith(
                    "Exception", StringComparison.Ordinal) == true)
            {
                return true;
            }

            if (argument.Parent is IInvocationOperation invocation &&
                invocation.TargetMethod.Name is "ReportDiagnostic" or "Log" or
                "Write" or "WriteLine" or "Fail")
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetSemanticLiteral(
        IOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.ConstantValue.HasValue)
        {
            return GetSemanticLiteral(operation.ConstantValue.Value);
        }

        return operation is IFieldReferenceOperation field
            ? GetSemanticFieldLiteral(field.Field, cancellationToken)
            : null;
    }

    private static string? GetSemanticFieldLiteral(
        IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        if (!field.IsStatic || !field.IsReadOnly ||
            field.Type.SpecialType != SpecialType.System_String ||
            field.ContainingType?.StaticConstructors.Any(
                static constructor => !constructor.IsImplicitlyDeclared) == true)
        {
            return null;
        }

        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
                {
                    Initializer.Value: ExpressionSyntax initializer
                } || !TryGetStringInitializer(initializer, out var value))
            {
                continue;
            }

            return GetSemanticLiteral(value);
        }

        return null;
    }

    private static bool TryGetStringInitializer(
        ExpressionSyntax expression,
        out string value)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.StringLiteralExpression):
                value = literal.Token.ValueText;
                return true;
            case ParenthesizedExpressionSyntax parenthesized:
                return TryGetStringInitializer(parenthesized.Expression, out value);
            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.AddExpression) &&
                     TryGetStringInitializer(binary.Left, out var left) &&
                     TryGetStringInitializer(binary.Right, out var right):
                value = left + right;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static string? GetSemanticLiteral(object? value)
    {
        if (value is not string text || text.Length < 3 ||
            text[0] != 'i' || text[1] != 'r' ||
            (text[2] != '.' && text[2] != '_'))
        {
            return null;
        }

        return text;
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if (!field.IsConst &&
            IsForbiddenMutableStaticStorage(field) &&
            (!field.IsReadOnly || IsMutableReferenceStorage(field.Type)))
        {
            Report(context, MetaDiagnosticDescriptors.MutableStaticState, field.Locations.FirstOrDefault(), field.Name);
        }

        if (field.Type.SpecialType == SpecialType.System_String &&
            IsNamespaceOrNested(field.ContainingNamespace, "SharpProof", "Ir"))
        {
            Report(context, MetaDiagnosticDescriptors.StringFieldInIr, field.Locations.FirstOrDefault(), field.Name);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if ((property.SetMethod != null || IsMutableReferenceStorage(property.Type)) &&
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

    private static bool IsMutableReferenceStorage(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (string.Equals(named.Name, "Builder", StringComparison.Ordinal) &&
            named.ContainingType != null &&
            IsExactNamespace(
                named.ContainingNamespace,
                "System",
                "Collections",
                "Immutable"))
        {
            return true;
        }

        if (IsExactNamespace(
                named.ContainingNamespace,
                "System",
                "Collections",
                "Immutable"))
        {
            return false;
        }

        if (MutableCollectionNames.Contains(named.Name) &&
            (IsExactNamespace(
                 named.ContainingNamespace,
                 "System",
                 "Collections",
                 "Generic") ||
             IsExactNamespace(
                 named.ContainingNamespace,
                 "System",
                 "Collections",
                 "Concurrent")))
        {
            return true;
        }

        return IsMutableCollectionInterface(named) || named.AllInterfaces.Any(interfaceType =>
            IsMutableCollectionInterface(interfaceType));
    }

    private static bool IsMutableCollectionInterface(INamedTypeSymbol type)
    {
        return IsExactNamespace(
                type.ContainingNamespace,
                "System",
                "Collections",
                "Generic") &&
            type.Name is "ICollection" or "IDictionary" or "IList" or "ISet";
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
        Compilation, SemanticModel, CSharpCompilation, CSharpSemanticModel,
        CSharpExtensions, SyntaxFactory, Symbol, SymbolDisplay, DiagnosticDescriptor,
        OperationCanceledException, CancellationToken, MsBuildTask,
        CompilationModelProvider,
        AnalyzerDiagnosticDescriptors, ContractForDiagnosticDescriptors, MetaDiagnosticDescriptors, String,
        Assumption, ProofKernel, CallableEvidenceBuilder, CallableVerifier,
        PostconditionObligationBuilder,
        EffectSummary, EffectSummaryDomain,
        EffectSummaryOperations, ExternalEffectResolver, ProvenOutcome, RefutedOutcome,
        ValidatedModel, WorkerProgram, SemanticCache, WorkerLauncherProgram, SharpProofWorker,
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
