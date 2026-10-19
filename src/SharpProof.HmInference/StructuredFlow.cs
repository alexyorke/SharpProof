using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private void BooleanCondition(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (model.GetTypeInfo(expression, cancellationToken).Type?.SpecialType == SpecialType.System_Boolean)
            { HmType.Unify(Expression(expression), new("bool")); }
            else
            {
                // Preserve user-defined bool conversions and true operators at condition boundaries.
                Anchor(expression);
            }
        }

        private void DeclareResource(VariableDeclarationSyntax declaration)
        {
            if (declaration is null)
            { throw new ArgumentNullException(nameof(declaration)); }
            // Keep concrete disposal selection, including pattern versus explicit interface methods.
            // Also retain conversions introduced by an explicitly declared resource type.
            foreach (var variable in declaration.Variables)
            {
                if (variable.Initializer is null)
                { throw new InferenceException("Uninitialized resource."); }
                Anchor(variable.Initializer.Value);
                var symbol = model.GetDeclaredSymbol(variable, cancellationToken) as ILocalSymbol
                    ?? throw new InferenceException("Unresolved resource local.");
                environment.Add(symbol, CSharpTypes.Rigid(symbol.Type));
            }
        }

        private Flow TryFlow(TryStatementSyntax statement)
        {
            if (statement is null)
            { throw new ArgumentNullException(nameof(statement)); }
            var attempted = Branch(statement.Block);
            var returns = attempted.ReturnType;
            var fallsThrough = attempted.FallsThrough;
            var breaks = attempted.Breaks;
            var continues = attempted.Continues;
            foreach (var handler in statement.Catches)
            {
                var caught = Scoped(() =>
                {
                    if (handler.Declaration is not null && model.GetDeclaredSymbol(handler.Declaration, cancellationToken) is ILocalSymbol exception)
                    {
                        environment.Add(exception, CSharpTypes.Rigid(exception.Type));
                    }
                    if (handler.Filter is not null)
                    { BooleanCondition(handler.Filter.FilterExpression); }
                    return Branch(handler.Block);
                });
                returns = MergeReturns(returns, caught.ReturnType);
                fallsThrough |= caught.FallsThrough;
                breaks |= caught.Breaks;
                continues |= caught.Continues;
            }
            if (statement.Finally is not null)
            {
                var finalizer = Branch(statement.Finally.Block);
                returns = MergeReturns(returns, finalizer.ReturnType);
                if (!finalizer.FallsThrough)
                {
                    // A throwing finalizer cancels pending exits, but their expressions still constrain types.
                    return new(returns, false);
                }
            }
            return new(returns, fallsThrough, breaks, continues);
        }
    }
}
