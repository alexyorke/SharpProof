namespace SharpProof.HmInference;

internal static class CoreChecks
{
    private static HmVariable V(string name)
    {
        global::System.ArgumentNullException.ThrowIfNull(name);
        return new(name);
    }

    private static HmLambda L(string name, HmExpression body)
    {
        global::System.ArgumentNullException.ThrowIfNull(name);
        global::System.ArgumentNullException.ThrowIfNull(body);
        return new(name, body);
    }

    private static HmApply A(HmExpression function, params HmExpression[] arguments)
    {
        global::System.ArgumentNullException.ThrowIfNull(function);
        global::System.ArgumentNullException.ThrowIfNull(arguments);
        foreach (var argument in arguments)
        {
            function = new HmApply(function, argument);
        }

        return (HmApply)function;
    }

    private static readonly HmLiteral Int = new(HmLiteralKind.Number);
    private static readonly HmLiteral Bool = new(HmLiteralKind.Bool);
    private static readonly HmLiteral String = new(HmLiteralKind.Text);
    public static int Run(CancellationToken cancellationToken, bool samplesOnly = false)
    {
        var identity = L("x", V("x"));
        var map = L("f", L("xs", new HmIf(A(V("empty"), V("xs")), V("nil"), A(V("cons"), A(V("f"), A(V("head"), V("xs"))), A(V("map"), V("f"), A(V("tail"), V("xs")))))));
        (string Name, HmExpression Expression, string? Expected)[] cases = [("CoreIdentity", identity, "forall T1. (T1 -> T1)"), ("CoreConstant", L("x", L("y", V("x"))), "forall T1 T2. (T1 -> (T2 -> T1))"), ("CoreApplication", L("f", L("x", A(V("f"), V("x")))), "forall T1 T2. ((T1 -> T2) -> (T1 -> T2))"), ("CoreComposition", L("f", L("g", L("x", A(V("f"), A(V("g"), V("x")))))), "forall T1 T2 T3. ((T1 -> T2) -> ((T3 -> T1) -> (T3 -> T2)))"), ("CoreLetPolymorphism", new HmLet("id", identity, new HmTuple(A(V("id"), Int), A(V("id"), Bool))), "(int, bool)"), ("CorePolymorphicAlias", new HmLet("id", identity, new HmLet("alias", V("id"), new HmTuple(A(V("alias"), Int), A(V("id"), String)))), "(int, string)"), ("CoreSelfApplicationViaLet", new HmLet("id", identity, A(V("id"), V("id"))), "forall T1. (T1 -> T1)"), ("CoreLambdaIsMonomorphic", L("f", new HmTuple(A(V("f"), Int), A(V("f"), Bool))), null), ("CoreNoCapturedGeneralization", L("x", new HmLet("y", V("x"), new HmTuple(A(V("y"), Int), A(V("y"), Bool)))), null), ("CoreCapturedResult", L("x", new HmLet("f", L("y", V("x")), new HmTuple(A(V("f"), Int), A(V("f"), Bool)))), "forall T1. (T1 -> (T1, T1))"), ("CoreSubstitutedEnvironment", L("f", new HmLet("x", A(V("f"), Int), new HmTuple(A(V("x"), Int), A(V("x"), Bool)))), null), ("CoreShadowing", L("x", new HmLet("x", identity, new HmTuple(A(V("x"), Int), A(V("x"), Bool)))), "forall T1. (T1 -> (int, bool))"), ("CoreOccursCheck", L("x", A(V("x"), V("x"))), null), ("CoreUnbound", V("missing"), null), ("CoreBadApplication", A(Int, Bool), null), ("CoreConditional", L("b", L("x", L("y", new HmIf(V("b"), V("x"), V("y"))))), "forall T1. (bool -> (T1 -> (T1 -> T1)))"), ("CoreConditionalConflict", new HmIf(Bool, Int, String), null), ("CoreNonBooleanCondition", new HmIf(Int, Int, Int), null), ("CoreEmptyList", new HmList(), "forall T1. List<T1>"), ("CoreHomogeneousList", new HmList(Int, Int), "List<int>"), ("CoreHeterogeneousList", new HmList(Int, Bool), null), ("CoreUnit", new HmLiteral(HmLiteralKind.Unit), "unit"), ("CoreRecursiveIdentity", new HmLetRec([new("id", identity)], new HmTuple(A(V("id"), Int), A(V("id"), Bool))), "(int, bool)"), ("CoreRecursiveMap", new HmLetRec([new("map", map)], V("map")), "forall T1 T2. ((T1 -> T2) -> (List<T1> -> List<T2>))"), ("CoreMutualRecursion", new HmLetRec([new("f", L("x", A(V("g"), V("x")))), new("g", L("x", new HmIf(Bool, V("x"), A(V("f"), V("x")))))], V("f")), "forall T1. (T1 -> T1)"), ("CoreNoPolymorphicRecursion", new HmLetRec([new("f", L("x", new HmIf(Bool, V("x"), A(V("f"), new HmList(V("x"))))))], V("f")), null), ("CoreRecursiveCapture", L("x", new HmLetRec([new("f", L("y", V("x")))], new HmTuple(A(V("f"), Bool), A(V("f"), Int)))), "forall T1. (T1 -> (T1, T1))"), ("CoreDuplicateRecursiveBinding", new HmLetRec([new("f", identity), new("f", identity)], V("f")), null)];
        var passed = 0;
        foreach (var test in cases)
        {
            string? actual;
            try
            {
                actual = HmCore.PrincipalType(test.Expression, cancellationToken: cancellationToken);
            }
            catch (InferenceException)
            {
                actual = null;
            }

            if (actual != test.Expected)
            {
                throw new InvalidOperationException($"{test.Name}: expected {test.Expected ?? "type error"}; got {actual ?? "type error"}.");
            }

            Program.Log.Info($"{(samplesOnly ? test.Name : "PASS " + test.Name)}: {actual ?? "type error"}");
            passed++;
        }

        if (samplesOnly)
        {
            return passed;
        }

        var customType = HmType.Variable();
        IReadOnlyDictionary<string, HmScheme> assumptions = new Dictionary<string, HmScheme>
        {
            ["Some"] = HmScheme.Generalize(new("->", customType, new("Option", customType)), []),
            ["captured"] = HmScheme.Monomorphic(customType),
            ["alias"] = HmScheme.Monomorphic(customType)
        };
        var some = HmCore.Infer(new HmTuple(A(V("Some"), Int), A(V("Some"), Bool)), assumptions, cancellationToken: cancellationToken);
        if (some.Type.Format([]) != "(Option<int>, Option<bool>)" || !customType.Root.IsVariable)
        {
            throw new InvalidOperationException("Custom schemes are not fresh or caller assumptions were mutated.");
        }

        try
        {
            HmCore.Infer(new HmTuple(A(V("captured"), Int), A(V("alias"), Bool)), assumptions, cancellationToken: cancellationToken);
            throw new InvalidOperationException("Aliases between external assumptions were lost.");
        }
        catch (InferenceException)
        {
            passed++;
            Program.Log.Info("PASS CoreExternalAssumptions");
        }

        // Exhaustive bounded grammar: all closed terms of size <= 7 using two
        // constants, variables, lambda, application and nonrecursive let. Compare
        // inferred principal types against an independent immutable reference W.
        var generated = 0;
        for (var size = 1; size <= 7; size++)
        {
            foreach (var expression in Generate(size, []))
            {
                string? actual;
                try
                {
                    actual = HmCore.PrincipalType(expression, cancellationToken: cancellationToken);
                }
                catch (InferenceException)
                {
                    actual = null;
                }

                var reference = ReferenceW.Infer(expression);
                if (actual != reference)
                {
                    throw new InvalidOperationException($"Differential inference mismatch: {actual ?? "error"} vs {reference ?? "error"}: {expression}");
                }

                generated++;
            }
        }

        Program.Log.Info($"PASS CoreReferenceW: {generated} closed terms (exhaustive grammar, size <= 7)");
        passed += generated;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            HmCore.Infer(identity, cancellation.Token);
            throw new InvalidOperationException("Cancellation ignored.");
        }
        catch (OperationCanceledException)
        {
            passed++;
            Program.Log.Info("PASS CoreCancellation");
        }

        return passed;
    }

    private static IEnumerable<HmExpression> Generate(int size, string[] scope)
    {
        global::System.ArgumentNullException.ThrowIfNull(scope);
        if (size == 1)
        {
            yield return Int;
            yield return Bool;
            foreach (var name in scope)
            {
                yield return V(name);
            }

            yield break;
        }

        var binding = "v" + scope.Length;
        foreach (var body in Generate(size - 1, [.. scope, binding]))
        {
            yield return L(binding, body);
        }

        for (var leftSize = 1; leftSize < size - 1; leftSize++)
        {
            var rightSize = size - 1 - leftSize;
            foreach (var left in Generate(leftSize, scope))
            {
                foreach (var right in Generate(rightSize, scope))
                {
                    yield return A(left, right);
                }

                foreach (var right in Generate(rightSize, [.. scope, binding]))
                {
                    yield return new HmLet(binding, left, right);
                }
            }
        }
    }
}
