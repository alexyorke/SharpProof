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

        private void DeclareResource(VariableDeclarationSyntax declaration, SyntaxNode site, bool asynchronous)
        {
            if (declaration is null)
            { throw new ArgumentNullException(nameof(declaration)); }
            if (site is null)
            { throw new ArgumentNullException(nameof(site)); }
            foreach (var variable in declaration.Variables)
            {
                if (variable.Initializer is null)
                { throw new InferenceException("Uninitialized resource."); }
                var symbol = model.GetDeclaredSymbol(variable, cancellationToken) as ILocalSymbol
                    ?? throw new InferenceException("Unresolved resource local.");
                var inferred = declaration.Type.IsVar ? Expression(variable.Initializer.Value) : CSharpTypes.Rigid(symbol.Type);
                InferResource(variable.Initializer.Value, site, asynchronous, inferred, declaration.Type.IsVar ? null : symbol.Type);
                environment.Add(symbol, inferred);
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
