using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    private static void AnalyzeAsyncCorrectness(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        var operations = context.State.VisibleOperations;
        var method = context.MethodSymbol;
        var containsAwait = operations.Any(static operation => operation is IAwaitOperation);

        if (method.IsAsync && method.ReturnsVoid && !IsEventHandlerShape(method))
            Report(
                context,
                session,
                SharpProofDiagnostics.AsyncVoidRule,
                GetMethodIdentifierLocation(context),
                "async_void",
                method.Name);

        var nullTaskReturnReported = false;
        if (!method.IsAsync && IsTaskType(method.ReturnType))
            foreach (var returnOperation in operations.OfType<IReturnOperation>())
                if (IsNullConstant(returnOperation.ReturnedValue))
                {
                    Report(
                        context,
                        session,
                        SharpProofDiagnostics.NullTaskReturnRule,
                        returnOperation.ReturnedValue?.Syntax.GetLocation() ?? returnOperation.Syntax.GetLocation(),
                        "null_task_return",
                        method.Name);
                    nullTaskReturnReported = true;
                }

        if (!nullTaskReturnReported &&
            !method.IsAsync &&
            IsTaskType(method.ReturnType) &&
            TryGetExpressionBody(context.Node, out var taskExpressionBody) &&
            IsNullConstant(context.SemanticModel.GetOperation(taskExpressionBody, context.CancellationToken)))
            Report(
                context,
                session,
                SharpProofDiagnostics.NullTaskReturnRule,
                taskExpressionBody.GetLocation(),
                "null_task_return",
                method.Name);

        var reportedTaskTextSpans = new HashSet<int>();
        foreach (var operation in operations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IAwaitOperation awaitOperation:
                    AnalyzeAwait(context, session, awaitOperation);
                    break;
                case IInterpolationOperation interpolation:
                    AnalyzeTaskTextConversion(
                        context,
                        session,
                        interpolation.Expression,
                        reportedTaskTextSpans);
                    break;
                case IBinaryOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add,
                        Type.SpecialType: SpecialType.System_String
                    } concatenation:
                    AnalyzeTaskTextConversion(
                        context,
                        session,
                        concatenation.LeftOperand,
                        reportedTaskTextSpans);
                    AnalyzeTaskTextConversion(
                        context,
                        session,
                        concatenation.RightOperand,
                        reportedTaskTextSpans);
                    break;
                case IObjectCreationOperation creation when IsTaskCompletionSourceType(creation.Type):
                    AnalyzeTaskCompletionSource(context, session, creation);
                    break;
                case IPropertyReferenceOperation propertyReference
                    when (method.IsAsync || containsAwait) && IsBlockingResultProperty(propertyReference):
                    if (!IsKnownCompletedTask(propertyReference.Instance))
                        Report(
                            context,
                            session,
                            SharpProofDiagnostics.BlockingAsyncRule,
                            propertyReference.Syntax.GetLocation(),
                            "blocking_task_result",
                            method.Name,
                            propertyReference.Syntax.ToString());
                    break;
                case IInvocationOperation invocation when method.IsAsync || containsAwait:
                    if (TryGetBlockedTask(invocation, out var blockedTask) && !IsKnownCompletedTask(blockedTask))
                        Report(
                            context,
                            session,
                            SharpProofDiagnostics.BlockingAsyncRule,
                            invocation.Syntax.GetLocation(),
                            "blocking_task_wait",
                            method.Name,
                            invocation.Syntax.ToString());
                    break;
                case IUsingOperation usingOperation:
                    AnalyzeTaskUsingResources(
                        context,
                        session,
                        usingOperation.Resources);
                    break;
                case IUsingDeclarationOperation usingDeclaration:
                    AnalyzeTaskUsingResources(
                        context,
                        session,
                        usingDeclaration.DeclarationGroup);
                    break;
            }
        }

        AnalyzeDeferredValidation(context, session, operations);
    }

    private static void AnalyzeAwait(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IAwaitOperation awaitOperation)
    {
        if (Unwrap(awaitOperation.Operation) is not IConditionalAccessOperation conditionalAccess ||
            !IsTaskType(conditionalAccess.Type))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.AwaitNullConditionalRule,
            conditionalAccess.Syntax.GetLocation(),
            "await_null_conditional",
            conditionalAccess.Syntax.ToString());
    }

    private static void AnalyzeTaskTextConversion(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IOperation expression,
        HashSet<int> reportedSpans)
    {
        expression = Unwrap(expression)!;
        if (!IsTaskType(expression.Type) || !reportedSpans.Add(expression.Syntax.SpanStart)) return;

        Report(
            context,
            session,
            SharpProofDiagnostics.TaskConvertedToStringRule,
            expression.Syntax.GetLocation(),
            "task_to_string",
            expression.Syntax.ToString());
    }

    private static void AnalyzeTaskCompletionSource(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IObjectCreationOperation creation)
    {
        var optionsArgument = creation.Arguments.FirstOrDefault(
            static argument =>
                argument.Parameter?.Type is INamedTypeSymbol type &&
                string.Equals(type.Name, "TaskCreationOptions", StringComparison.Ordinal) &&
                string.Equals(
                    type.ContainingNamespace?.ToDisplayString(),
                    "System.Threading.Tasks",
                    StringComparison.Ordinal));
        if (optionsArgument != null)
        {
            if (!TryGetIntegralConstant(optionsArgument.Value, out var options)) return;

            var optionType = optionsArgument.Parameter!.Type as INamedTypeSymbol;
            var runAsyncField = optionType?.GetMembers("RunContinuationsAsynchronously")
                .OfType<IFieldSymbol>()
                .FirstOrDefault(static field => field.HasConstantValue);
            if (runAsyncField?.ConstantValue == null ||
                !TryConvertToInt64(runAsyncField.ConstantValue, out var runAsyncValue) ||
                (options & runAsyncValue) != 0)
                return;
        }

        Report(
            context,
            session,
            SharpProofDiagnostics.TaskCompletionSourceContinuationsRule,
            creation.Syntax.GetLocation(),
            "task_completion_source_continuations",
            creation.Syntax.ToString());
    }

    private static void AnalyzeTaskUsingResources(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IOperation resources)
    {
        var taskResource = resources.DescendantsAndSelf()
            .FirstOrDefault(static resource => IsTaskResource(resource));
        if (taskResource == null) return;

        var displayOperation = taskResource is IVariableDeclaratorOperation declarator &&
                               declarator.Initializer?.Value is { } initializer
            ? initializer
            : taskResource;
        Report(
            context,
            session,
            SharpProofDiagnostics.TaskUsedAsDisposableRule,
            displayOperation.Syntax.GetLocation(),
            "task_used_as_disposable",
            displayOperation.Syntax.ToString());
    }

    private static bool TryGetExpressionBody(SyntaxNode node, out ExpressionSyntax expression)
    {
        expression = node switch
        {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            _ => null!
        };
        return expression != null;
    }

    private static bool IsTaskResource(IOperation operation)
    {
        return IsTaskType(operation.Type) ||
               operation is IVariableDeclaratorOperation declarator && IsTaskType(declarator.Symbol.Type);
    }

    private static void AnalyzeDeferredValidation(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IReadOnlyCollection<IOperation> operations)
    {
        var method = context.MethodSymbol;
        if (!method.IsAsync || !IsExternallyVisible(method) || method.Parameters.Length == 0) return;

        var firstAwait = operations
            .OfType<IAwaitOperation>()
            .Select(static operation => operation.Syntax.SpanStart)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var validation = operations
            .Where(operation => operation.Syntax.SpanStart < firstAwait)
            .FirstOrDefault(operation => IsParameterValidation(operation, method, context));
        if (validation == null) return;

        Report(
            context,
            session,
            SharpProofDiagnostics.AsyncValidationDeferredRule,
            validation.Syntax.GetLocation(),
            "async_validation_deferred",
            method.Name);
    }

    private static bool IsParameterValidation(
        IOperation operation,
        IMethodSymbol method,
        MethodBodyAnalysisContext context)
    {
        if (operation is IInvocationOperation invocation &&
            invocation.TargetMethod.IsStatic &&
            string.Equals(invocation.TargetMethod.Name, "ThrowIfNull", StringComparison.Ordinal) &&
            IsOrDerivesFrom(invocation.TargetMethod.ContainingType, "System.ArgumentException") &&
            invocation.Arguments.Any(argument => ReferencesParameter(argument.Value, method)))
            return true;

        if (operation is not IThrowOperation throwOperation ||
            !IsOrDerivesFrom(throwOperation.Exception?.Type, "System.ArgumentException"))
            return false;

        var ifStatement = throwOperation.Syntax.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
        if (ifStatement == null) return false;

        return ifStatement.Condition.DescendantNodesAndSelf()
            .Select(node => context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol)
            .OfType<IParameterSymbol>()
            .Any(parameter => method.Parameters.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate, parameter)));
    }

    private static bool ReferencesParameter(IOperation operation, IMethodSymbol method)
    {
        return operation.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>()
            .Any(reference => method.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter, reference.Parameter)));
    }

    private static bool IsBlockingResultProperty(IPropertyReferenceOperation propertyReference)
    {
        return string.Equals(propertyReference.Property.Name, "Result", StringComparison.Ordinal) &&
               IsTaskType(propertyReference.Property.ContainingType);
    }

    private static bool TryGetBlockedTask(IInvocationOperation invocation, out IOperation? task)
    {
        task = null;
        if (string.Equals(invocation.TargetMethod.Name, "Wait", StringComparison.Ordinal) &&
            IsTaskType(invocation.TargetMethod.ContainingType))
        {
            task = invocation.Instance;
            return true;
        }

        if (!string.Equals(invocation.TargetMethod.Name, "GetResult", StringComparison.Ordinal) ||
            Unwrap(invocation.Instance) is not IInvocationOperation getAwaiter ||
            !string.Equals(getAwaiter.TargetMethod.Name, "GetAwaiter", StringComparison.Ordinal))
            return false;

        task = getAwaiter.Instance;
        return task != null;
    }

    private static bool IsKnownCompletedTask(IOperation? operation)
    {
        operation = Unwrap(operation);
        return operation switch
        {
            IInvocationOperation invocation
                when string.Equals(
                         invocation.TargetMethod.ContainingNamespace?.ToDisplayString(),
                         "System.Threading.Tasks",
                         StringComparison.Ordinal) &&
                     string.Equals(invocation.TargetMethod.ContainingType.Name, "Task", StringComparison.Ordinal) &&
                     invocation.TargetMethod.Name is "FromResult" or "FromException" or "FromCanceled" => true,
            IPropertyReferenceOperation property
                when string.Equals(property.Property.Name, "CompletedTask", StringComparison.Ordinal) &&
                     IsTaskType(property.Property.ContainingType) => true,
            _ => false
        };
    }

    private static bool IsNullConstant(IOperation? operation)
    {
        operation = Unwrap(operation);
        return operation?.ConstantValue is { HasValue: true, Value: null };
    }

    private static bool TryGetIntegralConstant(IOperation operation, out long value)
    {
        value = default;
        operation = Unwrap(operation)!;
        return operation.ConstantValue is { HasValue: true, Value: { } constant } &&
               TryConvertToInt64(constant, out value);
    }

    private static bool TryConvertToInt64(object value, out long converted)
    {
        switch (value)
        {
            case sbyte signedByte:
                converted = signedByte;
                return true;
            case byte unsignedByte:
                converted = unsignedByte;
                return true;
            case short signedShort:
                converted = signedShort;
                return true;
            case ushort unsignedShort:
                converted = unsignedShort;
                return true;
            case int signedInt:
                converted = signedInt;
                return true;
            case uint unsignedInt:
                converted = unsignedInt;
                return true;
            case long signedLong:
                converted = signedLong;
                return true;
            default:
                converted = default;
                return false;
        }
    }

    private static bool IsEventHandlerShape(IMethodSymbol method)
    {
        if (method.Parameters.Length != 2 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Object)
            return false;

        return IsOrDerivesFrom(method.Parameters[1].Type, "System.EventArgs");
    }

    private static bool IsExternallyVisible(IMethodSymbol method)
    {
        return method.DeclaredAccessibility is Accessibility.Public or
            Accessibility.Protected or
            Accessibility.ProtectedOrInternal;
    }

    private static Location GetMethodIdentifierLocation(MethodBodyAnalysisContext context)
    {
        return context.Node switch
        {
            MethodDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            _ => context.MethodSymbol.Locations.FirstOrDefault(static location => location.IsInSource) ??
                 context.Node.GetLocation()
        };
    }
}
