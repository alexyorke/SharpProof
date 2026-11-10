using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static readonly string IntrinsicEvidenceTest = typeof(DotnetApiInventoryTests).FullName + "." + nameof(PinnedFrameworkApiSupportEvidenceIsRecorded);
    private static readonly ImmutableArray<(string? Left, string? Right)> EqualityEdges = [("same", "same"), ("left", "right"), ("", ""), (null, null), (null, "value"), ("value", null), ("a\0b", "a\0b"), ("\u00e9", "e")];
    private static void WriteIntrinsicTestEvidence(string root, CSharpCompilation compilation, ImmutableArray<DotnetApiInventory.Row> inventory)
    {
        global::System.ArgumentNullException.ThrowIfNull(root);
        global::System.ArgumentNullException.ThrowIfNull(compilation);
        if (inventory.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(inventory)); }
        var rows = inventory.Where(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Evidence.Contains("intrinsic=", StringComparison.Ordinal))).Select(row =>
        {
            if (row.Evidence.Contains("intrinsic=guarded-exception-message-construction", StringComparison.Ordinal))
            {
                var resource = row.Evidence.Contains("intrinsic=guarded-exception-resource-synchronization", StringComparison.Ordinal);
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "null, empty and explicit messages; reverse named arguments; allocation retained; compiled CLR/IR normal-path agreement and deliberately wrong result; guarded resource-lock shape where applicable; native callback effects, message preconditions and runtime lock/culture oracles in ExceptionConstruction and ExceptionResourceLock tests",
                    () => CheckExceptionMessageIntrinsic(compilation, row.Id, false) && (!resource || CheckExceptionMessageIntrinsic(compilation, row.Id, false, resourceOnly: true)),
                    () => CheckExceptionMessageIntrinsic(compilation, row.Id, true) || resource && CheckExceptionMessageIntrinsic(compilation, row.Id, true, resourceOnly: true));
            }

            if (row.Evidence.Contains("intrinsic=guarded-exception-resource-synchronization", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "exact default/parameter-name/null-message overload; explicit allocation and conditional resource-lock IR retained; CLR/IR normal-path agreement and wrong-result control; pinned-runtime IL hashes, actual lock identity and culture callback lock ownership checked by ExceptionResourceLockOracleTests; native resource synchronization witnesses and unsupported module/prefix controls",
                    () => CheckExceptionMessageIntrinsic(compilation, row.Id, false, resourceOnly: true),
                    () => CheckExceptionMessageIntrinsic(compilation, row.Id, true, resourceOnly: true));
            }

            if (row.Evidence.Contains("intrinsic=typed-sz-array-clear-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "all valid ranges of a four-cell incoming array, aliases and unchanged cells; whole-array overload; CLR/IR equality and wrong-result control; native symbolic-range and effect controls in ArrayClear tests",
                    () => CheckArrayClearIntrinsic(compilation, row.Id, false), () => CheckArrayClearIntrinsic(compilation, row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=checked-integral-to-int64", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "signed/unsigned 8/16/32/64-bit limits, UTF-16 code units and Boolean 0/1; checked UInt64 overflow even in unchecked context; exact signed Int64 width; actual matching runtime overload and readiness observations; native arithmetic proof/refutation with uncertain initialization effects and unsupported logical clauses",
                    () => CheckIntegralToInt64Intrinsic(compilation, row.Id, false), () => CheckIntegralToInt64Intrinsic(compilation, row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=object-reference-identity", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "nulls, aliases and distinct references; named arguments; identity rather than virtual equality; native proof/refutation controls in ReferenceEqualsApi tests",
                    () => CheckReferenceIdentityIntrinsic(compilation, false), () => CheckReferenceIdentityIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=constrained-integer-comparable", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "constrained source helpers with nine exact integer/Char receiver types; all ordered boundary pairs versus actual CLR interface dispatch and a wrong-result control; native generic methods, local functions, preconditions and callback/domain exclusions",
                    () => CheckConstrainedComparableIntrinsic(compilation, false), () => CheckConstrainedComparableIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=default-comparer-cached-value", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "non-null cached normal result and repeated getter identity; compiled IR versus CLR and wrong-result control; native primitive, generic helper and runtime type identity controls; initialization effects remain opaque",
                    () => CheckDefaultComparerValueIntrinsic(compilation, false), () => CheckDefaultComparerValueIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=integer-comparison", StringComparison.Ordinal) || row.Evidence.Contains("intrinsic=default-integer-comparer-only", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "all ordered pairs of minimum, negative one, zero, one and maximum; signed/unsigned 8/16/32/64 bit and UTF-16 code units; exact promoted differences; named argument reordering; compiled IR versus runtime", () => CheckIntegerComparisonIntrinsic(compilation, row.Id, false), () => CheckIntegerComparisonIntrinsic(compilation, row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=fresh-default-integer-key-dictionary-state", StringComparison.Ordinal))
            {
                var genericKeys = row.Evidence.Contains("intrinsic=fresh-default-dictionary-construction-state", StringComparison.Ordinal);
                var constructorOrigin = row.Evidence.Contains("intrinsic=constructor-origin-dictionary-state", StringComparison.Ordinal);
                var opaqueStorage = row.Evidence.Contains("intrinsic=opaque-dictionary-value-storage", StringComparison.Ordinal);
                var capacity = row.Evidence.Contains("intrinsic=capacity-dictionary-construction-state", StringComparison.Ordinal);
                var opaqueState = row.Id is "M:System.Collections.Generic.Dictionary`2.#ctor" or "M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32)" or
                    "M:System.Collections.Generic.Dictionary`2.ContainsKey(`0)" or "P:System.Collections.Generic.Dictionary`2.Count";
                var scope = "compiled Int32-key dictionary state with scalar/reference values, aliases, identity/null, Count and membership; nine integer key boundaries and CLR/IR replay checked by DictionaryStateOracleTests" +
                    (opaqueState ? "; empty generic-value construction/Count/membership versus decimal runtime without value interpretation" : "") +
                    (genericKeys ? "; generic-key constructor/Count normal state versus string runtime, with initialization effects retained" : "") +
                    (constructorOrigin ? "; immutable generic source-helper Add/get/set/ContainsKey/Count share constructor-origin storage versus runtime; default-comparer heap guard retained after publication" : "") +
                    (opaqueStorage ? "; generic opaque Add/get/set copy distinct atoms and decimal CLR bits through direct and source-helper storage" : "") +
                    (capacity ? "; capacity constructor state checked with scalar, opaque and generic keys, source-helper aliases, normal-path uncertainty and wrong-result controls" : "");
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, scope,
                    () => (!capacity || CheckDictionaryStateIntrinsic(compilation, false, capacity: true) &&
                        CheckDictionaryStateIntrinsic(compilation, false, opaqueValues: true, capacity: true) &&
                        CheckDictionaryStateIntrinsic(compilation, false, genericKeys: true, capacity: true) &&
                        CheckDictionaryStateIntrinsic(compilation, false, sourceParameters: true, capacity: true)) &&
                        CheckDictionaryStateIntrinsic(compilation, false) && CheckDictionaryStateIntrinsic(compilation, false, referenceValues: true) &&
                        (!opaqueState || CheckDictionaryStateIntrinsic(compilation, false, opaqueValues: true)) &&
                        (!genericKeys || CheckDictionaryStateIntrinsic(compilation, false, genericKeys: true)) &&
                        (!constructorOrigin || CheckDictionaryStateIntrinsic(compilation, false, sourceParameters: true) &&
                            CheckDictionaryStateIntrinsic(compilation, false, referenceValues: true, sourceParameters: true)) &&
                        (!opaqueStorage || CheckOpaqueDictionaryStorageIntrinsic(compilation, false) && CheckOpaqueDictionaryStorageIntrinsic(compilation, false, direct: true)),
                    () => capacity && CheckDictionaryStateIntrinsic(compilation, true, capacity: true) ||
                        CheckDictionaryStateIntrinsic(compilation, true) || CheckDictionaryStateIntrinsic(compilation, true, referenceValues: true) ||
                        opaqueState && CheckDictionaryStateIntrinsic(compilation, true, opaqueValues: true) ||
                        genericKeys && CheckDictionaryStateIntrinsic(compilation, true, genericKeys: true) ||
                        constructorOrigin && (CheckDictionaryStateIntrinsic(compilation, true, sourceParameters: true) ||
                            CheckDictionaryStateIntrinsic(compilation, true, referenceValues: true, sourceParameters: true)) ||
                        opaqueStorage && (CheckOpaqueDictionaryStorageIntrinsic(compilation, true) || CheckOpaqueDictionaryStorageIntrinsic(compilation, true, direct: true)));
            }

            if (row.Evidence.Contains("intrinsic=list-insertion-range-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "compiled concrete and exact interface List Insert/GetRange normal state versus CLR; reverse shift, independent range storage, exact declared interface reference identity and API-specific opaque generic atoms versus decimal CLR bits; growth/bounds uncertainty retained; native proof and wrong-claim controls in NativeSpecifiedCallTests.ListRangesPreserveSharedContentsAndDistinctCopies",
                    () => CheckListRangeIntrinsic(compilation, row.Id, false) && CheckListRangeIntrinsic(compilation, row.Id, false, referenceValues: true) &&
                        CheckOpaqueListStorageIntrinsic(compilation, false, rangeMethod: row.Id),
                    () => CheckListRangeIntrinsic(compilation, row.Id, true) || CheckListRangeIntrinsic(compilation, row.Id, true, referenceValues: true) ||
                        CheckOpaqueListStorageIntrinsic(compilation, true, rangeMethod: row.Id));
            }
            if (row.Evidence.Contains("intrinsic=concrete-stack-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "compiled CLR/IR default/capacity construction, incoming and aliased LIFO Count/Push/Pop; generic opaque decimal payload identity and source helper; normal growth choices retain approximation",
                    () => CheckStackStateIntrinsic(compilation, false) && CheckStackStateIntrinsic(compilation, false, incoming: true) && CheckStackStateIntrinsic(compilation, false, opaque: true),
                    () => CheckStackStateIntrinsic(compilation, true) || CheckStackStateIntrinsic(compilation, true, incoming: true) || CheckStackStateIntrinsic(compilation, true, opaque: true));
            }
            if (row.Evidence.Contains("intrinsic=concrete-queue-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest, "default/capacity constructors; FIFO shared aliases and null/reference values; compiled IR versus CLR; native incoming, Old, scalar boundaries, opaque generic values, unknown writer and effect controls in QueueState tests", () => CheckQueueStateIntrinsic(compilation, false), () => CheckQueueStateIntrinsic(compilation, true));
            }
            if (row.Evidence.Contains("intrinsic=default-primitive-hash-set-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "default/capacity construction; primitive-key Add/Contains/Remove return values and aliased Count; CLR/IR oracle with deliberately wrong result; native boundary keys and immutable generic helper; initialization and unknown-writer fallback retained",
                    () => CheckHashSetStateIntrinsic(compilation, false), () => CheckHashSetStateIntrinsic(compilation, true));
            }

            if (row.Evidence.Contains("intrinsic=fresh-list-state", StringComparison.Ordinal))
            {
                var arrays = row.Evidence.Contains("intrinsic=scalar-array-backed-interface", StringComparison.Ordinal);
                var nonnegative = row.Evidence.Contains("intrinsic=concrete-count-nonnegative", StringComparison.Ordinal);
                var empty = row.Id is "M:System.Collections.Generic.List`1.#ctor" or "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.ICollection`1.Count";
                var incoming = row.Evidence.Contains("intrinsic=concrete-list-state", StringComparison.Ordinal);
                var opaqueStorage = row.Evidence.Contains("intrinsic=opaque-list-value-storage", StringComparison.Ordinal);
                var hashSetInterface = row.Evidence.Contains("intrinsic=default-hash-set-interface-state", StringComparison.Ordinal);
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "compiled owned List construction, Count, Add and scalar/reference content state through concrete/interface aliases versus runtime; incoming concrete List Count, set, Add and get versus runtime where declared; optional initialization/growth and bounds faults retained; immutable generic IList/ICollection source parameters share caller scalar/reference storage versus runtime; native entry-cell proof/refutation controls; opaque generic value storage and source-helper get/set compared with decimal CLR bits where declared; existing array and nonnegative probes retained; exact default HashSet ICollection probes run where declared",
                    () => (!hashSetInterface || CheckHashSetStateIntrinsic(compilation, false, throughInterface: true)) &&
                        CheckListStateIntrinsic(compilation, false) && CheckListStateIntrinsic(compilation, false, referenceValues: true) &&
                        CheckListStateIntrinsic(compilation, false, sourceParameters: true) && CheckListStateIntrinsic(compilation, false, referenceValues: true, sourceParameters: true) &&
                        (!empty || CheckListStateIntrinsic(compilation, false, opaqueValues: true)) &&
                        (!incoming || CheckListStateIntrinsic(compilation, false, incoming: true)) &&
                        (!opaqueStorage || CheckOpaqueListStorageIntrinsic(compilation, false) && CheckOpaqueListStorageIntrinsic(compilation, false, direct: true)) &&
                        (!arrays || CheckInterfaceArrayIntrinsic(compilation, row.Id, false)) && (!nonnegative || CheckCountInvariant(row.Id, false)),
                    () => hashSetInterface && CheckHashSetStateIntrinsic(compilation, true, throughInterface: true) ||
                        CheckListStateIntrinsic(compilation, true) || CheckListStateIntrinsic(compilation, true, referenceValues: true) ||
                        CheckListStateIntrinsic(compilation, true, sourceParameters: true) || CheckListStateIntrinsic(compilation, true, referenceValues: true, sourceParameters: true) ||
                        empty && CheckListStateIntrinsic(compilation, true, opaqueValues: true) || incoming && CheckListStateIntrinsic(compilation, true, incoming: true) ||
                        opaqueStorage && (CheckOpaqueListStorageIntrinsic(compilation, true) || CheckOpaqueListStorageIntrinsic(compilation, true, direct: true)) ||
                        arrays && CheckInterfaceArrayIntrinsic(compilation, row.Id, true) || nonnegative && CheckCountInvariant(row.Id, true));
            }

            if (row.Evidence.Contains("intrinsic=default-hash-set-interface-state", StringComparison.Ordinal))
            {
                return ApiTestEvidence.Run(row.Id, "IntrinsicSemantics", IntrinsicEvidenceTest,
                    "exact default HashSet receiver through ICollection Add/Contains/Remove/Count versus CLR with duplicate keys, aliases and Boolean keys; native primitive boundaries, generic helpers, reimplemented interfaces and unknown-writer controls in HashSetInterfaces tests",
                    () => CheckHashSetStateIntrinsic(compilation, false, throughInterface: true),
                    () => CheckHashSetStateIntrinsic(compilation, true, throughInterface: true));
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
        }).ToImmutableArray();
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
        foreach (var (left, right) in ImmutableArray.Create<(object? Left, object? Right)>((null, null), (null, first), (first, null), (first, first), (first, second)))
        {
            var inputBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
            inputBuilder[context.Parameters[0].Current] = left == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, left);
            inputBuilder[context.Parameters[1].Current] = right == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, right);
            var inputs = inputBuilder.ToImmutable();
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
