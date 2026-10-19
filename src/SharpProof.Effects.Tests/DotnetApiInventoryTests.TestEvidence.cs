using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static readonly string IntrinsicEvidenceTest = typeof(DotnetApiInventoryTests).FullName + "." + nameof(PinnedFrameworkApiSurfaceAndSupportEvidenceMatchReviewedBaseline);
    private static readonly (string? Left, string? Right)[] EqualityEdges = [("same", "same"), ("left", "right"), ("", ""), (null, null), (null, "value"), ("value", null), ("a\0b", "a\0b"), ("\u00e9", "e")];
    private static void WriteIntrinsicTestEvidence(string root, CSharpCompilation compilation, DotnetApiInventory.Row[] inventory)
    {
        global::System.ArgumentNullException.ThrowIfNull(root);
        global::System.ArgumentNullException.ThrowIfNull(compilation);
        global::System.ArgumentNullException.ThrowIfNull(inventory);
        var rows = inventory.Where(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Evidence.Contains("intrinsic=", StringComparison.Ordinal))).Select(row =>
        {
            if (row.Evidence.Contains("intrinsic=object-reference-identity", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "nulls, aliases and distinct references; named arguments; identity rather than virtual equality; native proof/refutation controls in ReferenceEqualsApi tests",
                    () => CheckReferenceIdentityIntrinsic(compilation, false), () => CheckReferenceIdentityIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=integer-comparison", StringComparison.Ordinal) || row.Evidence.Contains("intrinsic=default-integer-comparer-only", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "all ordered pairs of minimum, negative one, zero, one and maximum; signed/unsigned 8/16/32/64 bit and UTF-16 code units; exact promoted differences; named argument reordering; compiled IR versus runtime", () => CheckIntegerComparisonIntrinsic(compilation, row.Id, false), () => CheckIntegerComparisonIntrinsic(compilation, row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=fresh-default-integer-key-dictionary-state", StringComparison.Ordinal))
            {
                var genericKeys = row.Evidence.Contains("intrinsic=fresh-default-dictionary-construction-state", StringComparison.Ordinal);
                var opaqueState = row.Id is "M:System.Collections.Generic.Dictionary`2.#ctor" or
                    "M:System.Collections.Generic.Dictionary`2.ContainsKey(`0)" or "P:System.Collections.Generic.Dictionary`2.Count";
                var scope = "compiled Int32-key dictionary state with scalar/reference values, aliases, identity/null, Count and membership; nine integer key boundaries and CLR/IR replay checked by DictionaryStateOracleTests" +
                    (opaqueState ? "; empty generic-value construction/Count/membership versus decimal runtime without value interpretation" : "") +
                    (genericKeys ? "; generic-key constructor/Count normal state versus string runtime, with initialization effects retained" : "");
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, scope,
                    () => CheckDictionaryStateIntrinsic(compilation, false) && CheckDictionaryStateIntrinsic(compilation, false, referenceValues: true) &&
                        (!opaqueState || CheckDictionaryStateIntrinsic(compilation, false, opaqueValues: true)) &&
                        (!genericKeys || CheckDictionaryStateIntrinsic(compilation, false, genericKeys: true)),
                    () => CheckDictionaryStateIntrinsic(compilation, true) || CheckDictionaryStateIntrinsic(compilation, true, referenceValues: true) ||
                        opaqueState && CheckDictionaryStateIntrinsic(compilation, true, opaqueValues: true) ||
                        genericKeys && CheckDictionaryStateIntrinsic(compilation, true, genericKeys: true));
            }

            if (row.Evidence.Contains("intrinsic=concrete-queue-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "default/capacity constructors; FIFO shared aliases and null/reference values; compiled IR versus CLR; native incoming, Old, scalar boundaries, opaque generic values, unknown writer and effect controls in QueueState tests", () => CheckQueueStateIntrinsic(compilation, false), () => CheckQueueStateIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=fresh-list-state", StringComparison.Ordinal))
            {
                var arrays = row.Evidence.Contains("intrinsic=scalar-array-backed-interface", StringComparison.Ordinal);
                var nonnegative = row.Evidence.Contains("intrinsic=concrete-count-nonnegative", StringComparison.Ordinal);
                var empty = row.Id is "M:System.Collections.Generic.List`1.#ctor" or "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.ICollection`1.Count";
                var incoming = row.Evidence.Contains("intrinsic=concrete-list-state", StringComparison.Ordinal);
                var opaqueStorage = row.Evidence.Contains("intrinsic=opaque-list-value-storage", StringComparison.Ordinal);
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "compiled owned List construction, Count, Add and scalar/reference content state through concrete/interface aliases versus runtime; incoming concrete List Count, set, Add and get versus runtime where declared; optional initialization/growth and bounds faults retained; immutable generic IList/ICollection source parameters share caller scalar/reference storage versus runtime; native entry-cell proof/refutation controls; opaque generic value storage and source-helper get/set compared with decimal CLR bits where declared; existing array and nonnegative probes retained",
                    () => CheckListStateIntrinsic(compilation, false) && CheckListStateIntrinsic(compilation, false, referenceValues: true) &&
                        CheckListStateIntrinsic(compilation, false, sourceParameters: true) && CheckListStateIntrinsic(compilation, false, referenceValues: true, sourceParameters: true) &&
                        (!empty || CheckListStateIntrinsic(compilation, false, opaqueValues: true)) &&
                        (!incoming || CheckListStateIntrinsic(compilation, false, incoming: true)) &&
                        (!opaqueStorage || CheckOpaqueListStorageIntrinsic(compilation, false) && CheckOpaqueListStorageIntrinsic(compilation, false, direct: true)) &&
                        (!arrays || CheckInterfaceArrayIntrinsic(compilation, row.Id, false)) && (!nonnegative || CheckCountInvariant(row.Id, false)),
                    () => CheckListStateIntrinsic(compilation, true) || CheckListStateIntrinsic(compilation, true, referenceValues: true) ||
                        CheckListStateIntrinsic(compilation, true, sourceParameters: true) || CheckListStateIntrinsic(compilation, true, referenceValues: true, sourceParameters: true) ||
                        empty && CheckListStateIntrinsic(compilation, true, opaqueValues: true) || incoming && CheckListStateIntrinsic(compilation, true, incoming: true) ||
                        opaqueStorage && (CheckOpaqueListStorageIntrinsic(compilation, true) || CheckOpaqueListStorageIntrinsic(compilation, true, direct: true)) ||
                        arrays && CheckInterfaceArrayIntrinsic(compilation, row.Id, true) || nonnegative && CheckCountInvariant(row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=concrete-count-nonnegative", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "empty, singleton and 65 entries; native lowering exercised by NativeSpecifiedCallTests.CountGettersProveNonnegativeResults", () => CheckCountInvariant(row.Id, false), () => CheckCountInvariant(row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=scalar-array-backed-interface", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, row.Id.Contains("IList", StringComparison.Ordinal) ? "compiled CFG/heap versus runtime direct and aliased get/set, compound updates and pre/post increments; RHS mutation and checked overflow; null/empty/singleton/extreme elements; negative, first/last, Count and extreme indexes" : "compiled CFG/heap versus runtime direct and aliased Count via ICollection and IList; null, empty, singleton and populated arrays", () => CheckInterfaceArrayIntrinsic(compilation, row.Id, false), () => CheckInterfaceArrayIntrinsic(compilation, row.Id, true));
            }

            var instance = row.Id switch
            {
                "M:System.String.Equals(System.String,System.String)" => false,
                "M:System.String.Equals(System.String)" => true,
                _ => (bool?)null
            };
            return instance.HasValue ? ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "equal, unequal, empty, null receiver/argument, embedded null, ordinal Unicode; compiled lowering and IR evaluation versus runtime", () => CheckEqualityIntrinsic(compilation, instance.Value, false), () => CheckEqualityIntrinsic(compilation, instance.Value, true)) : new ApiTestEvidence.Row(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "", "missing", "No behavioral probe registered for this intrinsic.");
        }).ToArray();
        ApiTestEvidence.Write(root, "intrinsic-test-evidence", rows);
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Where(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status != "passed")).Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Api + ": " + row.Detail)), Is.Empty, "Every inventoried intrinsic needs passing behavioral evidence; see intrinsic-test-evidence.md.");
    }

    private static bool CheckReferenceIdentityIntrinsic(CSharpCompilation references, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static bool Check(object a, object b) => object.ReferenceEquals(objB: b, objA: a); }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause((IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!);
        if (!lowered.Classification.IsExact)
        { return false; }
        object first = new object(), second = new object();
        foreach (var (left, right) in new (object? Left, object? Right)[] { (null, null), (null, first), (first, null), (first, first), (first, second) })
        {
            var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
            {
                [context.Parameters[0].Current] = left == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, left),
                [context.Parameters[1].Current] = right == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, right)
            };
            var interpreter = new IrInterpreter(factory);
            if (interpreter.Evaluate(lowered.SafeCondition, inputs).Value?.Boolean != true ||
                interpreter.Evaluate(lowered.Value, inputs).Value?.Boolean != (object.ReferenceEquals(left, right) ^ wrongClaim))
            { return false; }
        }
        return true;
    }

    private static bool CheckEqualityIntrinsic(CSharpCompilation referenceCompilation, bool instance, bool wrongClaim)
    {
        foreach (var (left, right) in EqualityEdges)
        {
            var expectedSafe = !instance || left != null;
            var expectedValue = expectedSafe && (instance ? left!.Equals(right, StringComparison.Ordinal) : string.Equals(left, right, StringComparison.Ordinal));
            var leftLiteral = System.Text.Json.JsonSerializer.Serialize(left);
            var rightLiteral = System.Text.Json.JsonSerializer.Serialize(right);
            var expression = instance ? "((string?)" + leftLiteral + ").Equals((string?)" + rightLiteral + ")" : "string.Equals((string?)" + leftLiteral + ", (string?)" + rightLiteral + ")";
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\npublic static class Probe { public static bool Check() => " + expression + "; }");
            var compilation = referenceCompilation.AddSyntaxTrees(tree);
            if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)))
            {
                return false;
            }

            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var target = model.GetDeclaredSymbol(syntax)!;
            var operation = model.GetOperation(syntax.ExpressionBody!.Expression) as IInvocationOperation;
            if (operation == null)
            {
                return false;
            }

            var factory = new IrFactory(IrExecutionSemantics.Total);
            var lowered = new RoslynTotalExpressionLowerer(new TotalLoweringContext(factory, target)).LowerClause(operation);
            if (!lowered.Classification.IsExact)
            {
                return false;
            }

            var interpreter = new IrInterpreter(factory);
            var safe = interpreter.Evaluate(lowered.SafeCondition).Value;
            if (safe == null || safe.Boolean != expectedSafe)
            {
                return false;
            }

            if (expectedSafe)
            {
                var value = interpreter.Evaluate(lowered.Value).Value;
                if (value == null || value.Boolean != (expectedValue ^ wrongClaim))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
