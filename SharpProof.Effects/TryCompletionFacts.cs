namespace SharpProof.Effects;

internal static class TryCompletionFacts
{
    internal static bool CanComplete(
        ITryOperation @try,
        Func<IOperation?, bool> canComplete)
    {
        if (@try.Finally != null && !canComplete(@try.Finally))
        {
            return false;
        }

        if (canComplete(@try.Body))
        {
            return true;
        }

        // A catch handler is reachable only when the try body can throw. A
        // non-throwing abrupt body such as `while (true) { }` cannot enter a
        // catch, so using the handler alone would incorrectly turn a
        // non-completing try into a normally completing operation.
        return BodyMayThrow(@try.Body) &&
            @try.Catches.Any(catchClause =>
                (catchClause.Filter == null ||
                 canComplete(catchClause.Filter)) &&
                canComplete(catchClause.Handler));
    }

    private static bool BodyMayThrow(IOperation body)
    {
        var pending = new Stack<IOperation>();
        pending.Push(body);
        while (pending.Count != 0)
        {
            var operation = pending.Pop();
            if (SharpProof.Roslyn.RoslynCfgThrowFacts.OperationMayThrow(
                    operation))
            {
                return true;
            }

            if (operation is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                continue;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return false;
    }
}
