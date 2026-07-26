using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharpProofSoundnessAnalyzer : DiagnosticAnalyzer {
    private static readonly ImmutableHashSet<string> ForbiddenCompilationMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ReplaceSyntaxTree",
            "AddSyntaxTrees",
            "GetSymbolsWithName");

    private static readonly ImmutableHashSet<string> ForbiddenSemanticModelMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "TryGetSpeculativeSemanticModel",
            "GetSpeculativeTypeInfo",
            "GetDiagnostics");

    private static readonly ImmutableHashSet<string> ForbiddenSyntaxFactoryMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ParseStatement",
            "ParseExpression",
            "ParseTypeName");

    private static readonly ImmutableHashSet<string> CacheWriteMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "AddOrUpdate",
            "GetOrAdd",
            "Set",
            "TryAdd",
            "TryUpdate",
            "TryWrite",
            "TryWriteAsync",
            "Write",
            "WriteAsync");

    private static readonly ImmutableArray<string> CSharpExpressionFragments = [
        " is null",
        " is not null",
        " == ",
        " != ",
        " && ",
        " || ",
        "=>",
        "?."
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        MetaDiagnosticDescriptors.All;

    public override void Initialize(AnalysisContext context) {
        if (context == null) throw new ArgumentNullException(nameof(context));
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext => {
            var knownSymbols = new KnownSymbols(startContext.Compilation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(
                    operationContext,
                    knownSymbols),
                OperationKind.Invocation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeObjectCreation(
                    operationContext,
                    knownSymbols),
                OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(
                AnalyzeBinaryOperation,
                OperationKind.BinaryOperator);
            startContext.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeCatchClause(
                    syntaxContext,
                    knownSymbols),
                SyntaxKind.CatchClause);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        KnownSymbols knownSymbols) {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod.OriginalDefinition;
        if (IsForbidden(
                method,
                invocation,
                context.ContainingSymbol,
                knownSymbols)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.ForbiddenRoslynApi,
                invocation.Syntax.GetLocation(),
                method.Name));
        }

        AnalyzeSemanticEquals(context, invocation, knownSymbols);
        AnalyzeCacheWrite(context, invocation);
    }

    private static bool IsForbidden(
        IMethodSymbol method,
        IInvocationOperation invocation,
        ISymbol containingSymbol,
        KnownSymbols knownSymbols) {
        if (!IsSemanticNamespace(containingSymbol)) return false;

        if (IsSameType(method.ContainingType, knownSymbols.Compilation) &&
            ForbiddenCompilationMethods.Contains(method.Name))
            return true;
        if (IsSameType(method.ContainingType, knownSymbols.SemanticModel) &&
            ForbiddenSemanticModelMethods.Contains(method.Name))
            return true;
        if (IsSameType(method.ContainingType, knownSymbols.SyntaxFactory) &&
            ForbiddenSyntaxFactoryMethods.Contains(method.Name))
            return true;
        if (method.Name == "GetSemanticModel" &&
            IsSameType(method.ContainingType, knownSymbols.Compilation))
            return !IsSameType(
                containingSymbol.ContainingType,
                knownSymbols.CompilationModelProvider);
        if (method.Name != "ToDisplayString") return false;

        var receiverType = invocation.Instance?.Type ?? method.ContainingType;
        return IsSameType(receiverType, knownSymbols.Symbol) ||
               receiverType?.AllInterfaces.Any(
                   value => IsSameType(value, knownSymbols.Symbol)) == true;
    }

    private static void AnalyzeObjectCreation(
        OperationAnalysisContext context,
        KnownSymbols knownSymbols) {
        var creation = (IObjectCreationOperation)context.Operation;
        var containingType = context.ContainingSymbol.ContainingType;
        if (IsSameType(creation.Type, knownSymbols.DiagnosticDescriptor) &&
            !IsExactNamespace(
                context.ContainingSymbol.ContainingNamespace,
                "SharpProof",
                "Meta",
                "Analyzers") &&
            !IsSameType(
                containingType,
                knownSymbols.AnalyzerDiagnosticDescriptors) &&
            !IsSameType(
                containingType,
                knownSymbols.ContractForDiagnosticDescriptors)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.DescriptorConstruction,
                creation.Syntax.GetLocation()));
        }

        if (IsSameType(creation.Type, knownSymbols.Assumption) &&
            !IsSameType(containingType, knownSymbols.ProofKernel) &&
            !IsSameType(containingType, knownSymbols.CallableVerifier)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.AssumptionConstruction,
                creation.Syntax.GetLocation()));
        }

        if (IsSameType(creation.Type, knownSymbols.EffectSummary) &&
            !IsSameType(containingType, knownSymbols.EffectSummary) &&
            !IsSameType(containingType, knownSymbols.EffectSummaryDomain) &&
            !IsSameType(
                containingType,
                knownSymbols.EffectSummaryOperations) &&
            !IsSameType(
                containingType,
                knownSymbols.ExternalEffectResolver)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.EffectSummaryConstruction,
                creation.Syntax.GetLocation()));
        }

        if (IsProofProducingType(creation.Type, knownSymbols) &&
            !IsSameType(containingType, knownSymbols.ProofKernel)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.ProofOutcomeConstruction,
                creation.Syntax.GetLocation(),
                creation.Type?.Name ?? string.Empty));
        }
    }

    private static bool IsProofProducingType(ITypeSymbol? type, KnownSymbols knownSymbols) =>
        IsSameType(type, knownSymbols.ProvenOutcome) ||
        IsSameType(type, knownSymbols.RefutedOutcome) ||
        IsSameType(type, knownSymbols.ValidatedModel);

    private static void AnalyzeBinaryOperation(
        OperationAnalysisContext context) {
        AnalyzeSemanticString(context);
        AnalyzeCSharpExpressionText(context);
    }

    private static void AnalyzeSemanticString(OperationAnalysisContext context) {
        if (context.Operation is not IBinaryOperation {
            OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
        } binary ||
            !IsSemanticNamespace(context.ContainingSymbol) ||
            !IsInsideCondition(binary.Syntax))
            return;

        var literal = GetSemanticLiteral(binary.LeftOperand) ??
                      GetSemanticLiteral(binary.RightOperand);
        if (literal == null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.SemanticStringControlFlow,
            binary.Syntax.GetLocation(),
            literal));
    }

    private static void AnalyzeSemanticEquals(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        KnownSymbols knownSymbols) {
        if (!IsSameType(
                invocation.TargetMethod.ContainingType,
                knownSymbols.String) ||
            invocation.TargetMethod.Name != "Equals" ||
            !IsSemanticNamespace(context.ContainingSymbol) ||
            !IsInsideCondition(invocation.Syntax))
            return;

        var literal = invocation.Instance == null
            ? null
            : GetSemanticLiteral(invocation.Instance);
        if (literal == null) {
            foreach (var argument in invocation.Arguments) {
                literal = GetSemanticLiteral(argument.Value);
                if (literal != null) break;
            }
        }
        if (literal == null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.SemanticStringControlFlow,
            invocation.Syntax.GetLocation(),
            literal));
    }

    private static void AnalyzeCSharpExpressionText(
        OperationAnalysisContext context) {
        if (context.Operation is not IBinaryOperation {
            OperatorKind: BinaryOperatorKind.Add
        } binary ||
            binary.Type?.SpecialType != SpecialType.System_String ||
            !IsSemanticNamespace(context.ContainingSymbol))
            return;

        var fragment = GetCSharpExpressionFragment(binary.LeftOperand) ??
                       GetCSharpExpressionFragment(binary.RightOperand);
        if (fragment == null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.CSharpExpressionText,
            binary.Syntax.GetLocation(),
            fragment));
    }

    private static string? GetCSharpExpressionFragment(IOperation operation) {
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value is not string value)
            return null;
        foreach (var fragment in CSharpExpressionFragments)
            if (value.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                return fragment;
        return null;
    }

    private static void AnalyzeCacheWrite(
        OperationAnalysisContext context,
        IInvocationOperation invocation) {
        if (!CacheWriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheType(
                invocation.Instance?.Type ??
                invocation.TargetMethod.ContainingType) ||
            !invocation.Arguments.Any(static argument =>
                ContainsNonCacheableSemanticAnswer(argument.Value)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.NonCacheableSemanticAnswer,
            invocation.Syntax.GetLocation()));
    }

    private static bool IsCacheType(ITypeSymbol? type) =>
        type?.Name.IndexOf("Cache", StringComparison.Ordinal) >= 0;

    private static bool ContainsNonCacheableSemanticAnswer(
        IOperation operation) =>
        operation.DescendantsAndSelf().Any(static descendant =>
            descendant switch {
                IFieldReferenceOperation field =>
                    IsNonCacheableName(field.Field.Name),
                IPropertyReferenceOperation property =>
                    IsNonCacheableName(property.Property.Name),
                IInvocationOperation invocation =>
                    IsNonCacheableName(invocation.TargetMethod.Name),
                IObjectCreationOperation creation =>
                    IsNonCacheableName(creation.Type?.Name),
                ILocalReferenceOperation local =>
                    IsNonCacheableName(local.Local.Name),
                _ => false
            });

    private static bool IsNonCacheableName(string? name) =>
        name != null &&
        (string.Equals(name, "Unknown", StringComparison.Ordinal) ||
         name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
         name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
         name.IndexOf("Failure", StringComparison.Ordinal) >= 0);

    private static string? GetSemanticLiteral(IOperation operation) {
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value is not string value)
            return null;
        return value.StartsWith("ir.", StringComparison.Ordinal) ||
               value.StartsWith("ir_", StringComparison.Ordinal)
            ? value
            : null;
    }

    private static bool IsInsideCondition(SyntaxNode syntax) =>
        syntax.AncestorsAndSelf().Any(node => node switch {
            IfStatementSyntax ifStatement =>
                ifStatement.Condition.Span.Contains(syntax.Span),
            WhileStatementSyntax whileStatement =>
                whileStatement.Condition.Span.Contains(syntax.Span),
            DoStatementSyntax doStatement =>
                doStatement.Condition.Span.Contains(syntax.Span),
            ForStatementSyntax { Condition: not null } forStatement =>
                forStatement.Condition.Span.Contains(syntax.Span),
            ConditionalExpressionSyntax conditional =>
                conditional.Condition.Span.Contains(syntax.Span),
            _ => false
        });

    private static void AnalyzeField(SymbolAnalysisContext context) {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsConst || field.ContainingType?.TypeKind == TypeKind.Enum)
            return;
        if (field.IsStatic &&
            !field.IsReadOnly &&
            IsCriticalStateNamespace(field.ContainingNamespace)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.MutableStaticState,
                field.Locations.FirstOrDefault(),
                field.Name));
        }

        if (field.Type.SpecialType == SpecialType.System_String &&
            IsNamespaceOrNested(
                field.ContainingNamespace,
                "SharpProof",
                "Ir")) {
            context.ReportDiagnostic(Diagnostic.Create(
                MetaDiagnosticDescriptors.StringFieldInIr,
                field.Locations.FirstOrDefault(),
                field.Name));
        }
    }

    private static void AnalyzeCatchClause(
        SyntaxNodeAnalysisContext context,
        KnownSymbols knownSymbols) {
        var clause = (CatchClauseSyntax)context.Node;
        if (clause.Declaration?.Type == null) return;
        var caughtType = context.SemanticModel.GetTypeInfo(
            clause.Declaration.Type,
            context.CancellationToken).Type;
        if (!IsSameType(
                caughtType,
                knownSymbols.OperationCanceledException) ||
            RethrowsCancellationImmediately(clause) ||
            IsAuditedCancellationBoundary(
                clause,
                context,
                context.ContainingSymbol,
                knownSymbols))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.SwallowedCancellation,
            clause.CatchKeyword.GetLocation()));
    }

    private static bool RethrowsCancellationImmediately(CatchClauseSyntax clause) =>
        clause.Block.Statements.FirstOrDefault() is
            ThrowStatementSyntax { Expression: null };

    private static bool IsAuditedCancellationBoundary(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        ISymbol? containingSymbol,
        KnownSymbols knownSymbols) {
        if (containingSymbol is not IMethodSymbol method) return false;
        if (IsAuditedWorkerMain(
                method,
                knownSymbols.WorkerProgram,
                knownSymbols.TaskOfInt32) ||
            IsAuditedWorkerMain(
                method,
                knownSymbols.WorkerLauncherProgram,
                knownSymbols.TaskOfInt32))
            return true;
        if (method is not {
            Name: "VerifyTargetAsync",
            IsStatic: true,
            Parameters.Length: 8
        } ||
            !IsSameType(
                method.ContainingType,
                knownSymbols.SharpProofWorker) ||
            !SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                knownSymbols.VerifyTargetTask) ||
            method.Parameters[7].Name != "callerCancellation" ||
            !IsSameType(
                method.Parameters[7].Type,
                knownSymbols.CancellationToken))
            return false;

        return ThrowsIfCallerCancellationRequested(
                   clause,
                   context,
                   method,
                   knownSymbols) ||
               ReifiesCallerCancellation(
                   clause,
                   context,
                   method,
                   knownSymbols);
    }

    private static bool ThrowsIfCallerCancellationRequested(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        KnownSymbols knownSymbols) {
        if (clause.Block.Statements.FirstOrDefault() is not
                ExpressionStatementSyntax expression ||
            context.SemanticModel.GetOperation(
                expression.Expression,
                context.CancellationToken) is not
                IInvocationOperation invocation ||
            invocation.TargetMethod.Name !=
                "ThrowIfCancellationRequested" ||
            !IsSameType(
                invocation.TargetMethod.ContainingType,
                knownSymbols.CancellationToken))
            return false;

        return ReferencesParameter(
            invocation.Instance,
            method.Parameters[7]);
    }

    private static bool ReifiesCallerCancellation(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        KnownSymbols knownSymbols) {
        if (clause.Block.Statements.FirstOrDefault() is not
                IfStatementSyntax { Else: null } cancellationIf ||
            context.SemanticModel.GetOperation(
                cancellationIf.Condition,
                context.CancellationToken) is not
                IPropertyReferenceOperation cancellationRequested ||
            cancellationRequested.Property.Name !=
                "IsCancellationRequested" ||
            !IsSameType(
                cancellationRequested.Property.ContainingType,
                knownSymbols.CancellationToken) ||
            !ReferencesParameter(
                cancellationRequested.Instance,
                method.Parameters[7]) ||
            SoleReturn(cancellationIf.Statement)?.Expression is not
                ExpressionSyntax returnExpression ||
            context.SemanticModel.GetOperation(
                returnExpression,
                context.CancellationToken) is not
                IInvocationOperation invocation ||
            invocation.TargetMethod is not {
                Name: "Unknown",
                IsStatic: true,
                Parameters.Length: 3
            } ||
            !IsSameType(
                invocation.TargetMethod.ContainingType,
                knownSymbols.SharpProofWorker) ||
            !IsSameType(
                invocation.TargetMethod.ReturnType,
                knownSymbols.CallableVerificationResult))
            return false;

        var target = invocation.Arguments.FirstOrDefault(candidate =>
            candidate.Parameter?.Ordinal == 0);
        return ReferencesParameter(target?.Value, method.Parameters[1]) &&
               IsCanceledReasonArgument(
                   invocation,
                   1,
                   knownSymbols.WorkerClaimReason) &&
               IsCanceledReasonArgument(
                   invocation,
                   2,
                   knownSymbols.WorkerCallableCoverageReason);
    }

    private static ReturnStatementSyntax? SoleReturn(
        StatementSyntax statement) =>
        statement switch {
            ReturnStatementSyntax direct => direct,
            BlockSyntax { Statements.Count: 1 } block =>
                block.Statements[0] as ReturnStatementSyntax,
            _ => null
        };

    private static bool IsCanceledReasonArgument(
        IInvocationOperation invocation,
        int parameterOrdinal,
        INamedTypeSymbol? expectedType) {
        if (expectedType == null ||
            !IsSameType(
                invocation.TargetMethod.Parameters[parameterOrdinal].Type,
                expectedType))
            return false;

        var argument = invocation.Arguments.FirstOrDefault(candidate =>
            candidate.Parameter?.Ordinal == parameterOrdinal);
        IOperation? value = argument?.Value;
        while (value is IConversionOperation conversion)
            value = conversion.Operand;
        return value is IFieldReferenceOperation field &&
               field.Field is {
                   Name: "Canceled",
                   IsStatic: true
               } &&
               IsSameType(field.Field.ContainingType, expectedType);
    }

    private static bool ReferencesParameter(
        IOperation? receiver,
        IParameterSymbol parameter) {
        while (receiver is IConversionOperation conversion)
            receiver = conversion.Operand;
        return receiver is IParameterReferenceOperation parameterReference &&
               SymbolEqualityComparer.Default.Equals(
                   parameterReference.Parameter,
                   parameter);
    }

    private static bool IsAuditedWorkerMain(
        IMethodSymbol method,
        INamedTypeSymbol? program,
        INamedTypeSymbol? taskOfInt32) =>
        method is {
            Name: "Main",
            IsStatic: true,
            Parameters.Length: 1
        } &&
        IsSameType(method.ContainingType, program) &&
        SymbolEqualityComparer.Default.Equals(
            method.ReturnType,
            taskOfInt32) &&
        method.Parameters[0].Type is IArrayTypeSymbol {
            Rank: 1
        } arguments &&
        arguments.ElementType.SpecialType == SpecialType.System_String;

    private static bool IsSemanticNamespace(ISymbol symbol) =>
        IsCriticalStateNamespace(symbol.ContainingNamespace) ||
        IsNamespaceOrNested(
            symbol.ContainingNamespace,
            "SharpProof",
            "Dataflow") ||
        IsNamespaceOrNested(
            symbol.ContainingNamespace,
            "SharpProof",
            "Specs");

    private static bool IsCriticalStateNamespace(INamespaceSymbol? namespaceSymbol) =>
        IsNamespaceOrNested(namespaceSymbol, "SharpProof", "Analyzer") ||
        IsNamespaceOrNested(namespaceSymbol, "SharpProof", "Frontend") ||
        IsNamespaceOrNested(namespaceSymbol, "SharpProof", "Verify");

    private static bool IsNamespaceOrNested(
        INamespaceSymbol? namespaceSymbol,
        params string[] expectedPrefix) {
        for (var current = namespaceSymbol;
             current != null && !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
            if (IsExactNamespace(current, expectedPrefix))
                return true;
        return false;
    }

    private static bool IsExactNamespace(
        INamespaceSymbol? namespaceSymbol,
        params string[] expected) {
        var current = namespaceSymbol;
        for (var index = expected.Length - 1; index >= 0; index--) {
            if (current == null ||
                current.IsGlobalNamespace ||
                !string.Equals(
                    current.Name,
                    expected[index],
                    StringComparison.Ordinal))
                return false;
            current = current.ContainingNamespace;
        }
        return current?.IsGlobalNamespace == true;
    }

    private static bool IsSameType(ITypeSymbol? actual, INamedTypeSymbol? expected) =>
        actual != null &&
        expected != null &&
        SymbolEqualityComparer.Default.Equals(
            actual.OriginalDefinition,
            expected.OriginalDefinition);

    private sealed class KnownSymbols {
        internal KnownSymbols(Compilation compilation) {
            Compilation = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.Compilation");
            SemanticModel = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.SemanticModel");
            SyntaxFactory = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.CSharp.SyntaxFactory");
            Symbol = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.ISymbol");
            DiagnosticDescriptor = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.DiagnosticDescriptor");
            OperationCanceledException = compilation.GetTypeByMetadataName(
                "System.OperationCanceledException");
            CancellationToken = compilation.GetTypeByMetadataName(
                "System.Threading.CancellationToken");
            CompilationModelProvider = compilation.GetTypeByMetadataName(
                "SharpProof.Frontend.Host.CompilationModelProvider");
            AnalyzerDiagnosticDescriptors = compilation.GetTypeByMetadataName(
                "SharpProof.Analyzer.GeneratedDiagnosticDescriptors");
            ContractForDiagnosticDescriptors = compilation.GetTypeByMetadataName(
                "SharpProof.ContractForGenerator.GeneratedDiagnosticDescriptors");
            String = compilation.GetSpecialType(SpecialType.System_String);
            Assumption = compilation.GetTypeByMetadataName(
                "SharpProof.Verify.Assumption");
            ProofKernel = compilation.GetTypeByMetadataName(
                "SharpProof.Verify.ProofKernel");
            CallableVerifier = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.CallableVerifier");
            EffectSummary = compilation.GetTypeByMetadataName(
                "SharpProof.Effects.EffectSummary");
            EffectSummaryDomain = compilation.GetTypeByMetadataName(
                "SharpProof.Effects.EffectSummaryDomain");
            EffectSummaryOperations = compilation.GetTypeByMetadataName(
                "SharpProof.Effects.EffectSummaryOperations");
            ExternalEffectResolver = compilation.GetTypeByMetadataName(
                "SharpProof.Effects.ExternalEffectResolver");
            ProvenOutcome = compilation.GetTypeByMetadataName(
                "SharpProof.Verify.ProvenOutcome");
            RefutedOutcome = compilation.GetTypeByMetadataName(
                "SharpProof.Verify.RefutedOutcome");
            ValidatedModel = compilation.GetTypeByMetadataName(
                "SharpProof.Verify.ValidatedModel");
            WorkerProgram = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.Program");
            WorkerLauncherProgram = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.Launcher.Program");
            SharpProofWorker = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.SharpProofWorker");
            var task = compilation.GetTypeByMetadataName(
                "System.Threading.Tasks.Task`1");
            TaskOfInt32 = task?.Construct(compilation.GetSpecialType(
                SpecialType.System_Int32));
            CallableVerificationResult = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.SharpProofWorker+CallableVerificationResult");
            WorkerClaimReason = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.Protocol.WorkerClaimReason");
            WorkerCallableCoverageReason = compilation.GetTypeByMetadataName(
                "SharpProof.Worker.Protocol.WorkerCallableCoverageReason");
            VerifyTargetTask =
                task != null &&
                CallableVerificationResult != null
                    ? task.Construct(CallableVerificationResult)
                    : null;
        }

        internal INamedTypeSymbol? Compilation { get; }
        internal INamedTypeSymbol? SemanticModel { get; }
        internal INamedTypeSymbol? SyntaxFactory { get; }
        internal INamedTypeSymbol? Symbol { get; }
        internal INamedTypeSymbol? DiagnosticDescriptor { get; }
        internal INamedTypeSymbol? OperationCanceledException { get; }
        internal INamedTypeSymbol? CancellationToken { get; }
        internal INamedTypeSymbol? CompilationModelProvider { get; }
        internal INamedTypeSymbol? AnalyzerDiagnosticDescriptors { get; }
        internal INamedTypeSymbol? ContractForDiagnosticDescriptors { get; }
        internal INamedTypeSymbol String { get; }
        internal INamedTypeSymbol? Assumption { get; }
        internal INamedTypeSymbol? ProofKernel { get; }
        internal INamedTypeSymbol? CallableVerifier { get; }
        internal INamedTypeSymbol? EffectSummary { get; }
        internal INamedTypeSymbol? EffectSummaryDomain { get; }
        internal INamedTypeSymbol? EffectSummaryOperations { get; }
        internal INamedTypeSymbol? ExternalEffectResolver { get; }
        internal INamedTypeSymbol? ProvenOutcome { get; }
        internal INamedTypeSymbol? RefutedOutcome { get; }
        internal INamedTypeSymbol? ValidatedModel { get; }
        internal INamedTypeSymbol? WorkerProgram { get; }
        internal INamedTypeSymbol? WorkerLauncherProgram { get; }
        internal INamedTypeSymbol? SharpProofWorker { get; }
        internal INamedTypeSymbol? CallableVerificationResult { get; }
        internal INamedTypeSymbol? WorkerClaimReason { get; }
        internal INamedTypeSymbol? WorkerCallableCoverageReason { get; }
        internal INamedTypeSymbol? TaskOfInt32 { get; }
        internal INamedTypeSymbol? VerifyTargetTask { get; }
    }
}
