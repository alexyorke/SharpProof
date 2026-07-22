using NUnit.Framework;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class SymbolicComplexityTests {
    private sealed record ComplexityCase(
        string Source,
        string Marker,
        SymbolicComplexityKind Kind,
        string? Text = null,
        SymbolicComplexityUnknownReason[]? Unknowns = null,
        string? Driver = null,
        string? Callee = null,
        SymbolicComplexityUnknownReason? CalleeUnknown = null,
        bool UseLineTarget = false,
        bool ExactUnknowns = false,
        int? UnknownDriverCount = null,
        int? NamedCalleeCount = null);
    private static IEnumerable<TestCaseData> ComplexityCases() {
        yield return Case("StraightLineMethod_IsConstant",
            """public static class C { public static int Work(int n) { var value=n+1; return value; } }""",
            "return value;", SymbolicComplexityKind.Constant, "O(1)");
        yield return Case("SingleForLoop_ProducesLinearComplexity",
            """public static class C { public static int Work(int n) { var sum=0; for(var i=0;i<n;i++){sum+=i;} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Linear, "O(n)", driver: "ForLoop");
        yield return Case("ForLoopWithConstantFirstIncrement_ProducesLinearComplexity",
            """public static class C { public static int Work(int n) { var sum=0; for(var i=0;i<n;i=1+i){sum+=i;} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Linear, "O(n)");
        yield return Case("SequentialLinearLoops_UseAsymptoticMax",
            """public static class C { public static int Work(int n) { var sum=0; for(var i=0;i<n;i++){sum+=i;} for(var j=0;j<n;j++){sum+=j;} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Linear, "O(n)");
        yield return Case("NestedForLoopsOverDistinctBounds_ProduceProduct",
            """public static class C { public static int Work(int n,int m) { var sum=0; for(var i=0;i<n;i++){for(var j=0;j<m;j++){sum+=i+j;}} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Product, "O(n * m)");
        yield return Case("FieldControlledLoopWithCall_RemainsConservativelyUnknown",
            """public sealed class C { private int _index; private void Reset()=>_index=0; public int Work(int count) { for(_index=0;_index<count;_index++){Reset();} return _index; } }""",
            "return _index;", SymbolicComplexityKind.Unknown,
            unknowns: [SymbolicComplexityUnknownReason.UnsupportedLoopShape]);
        yield return Case("NestedForLoopsOverSameBound_ProduceQuadratic",
            """public static class C { public static int Work(int n) { var sum=0; for(var i=0;i<n;i++){for(var j=0;j<n;j++){sum+=i+j;}} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Quadratic, "O(n^2)");
        yield return Case("BranchesUseWorstCaseMaximum",
            """public static class C { public static int Work(bool flag,int n) { var sum=0; if(flag){for(var i=0;i<n;i++){sum+=i;}}else{sum=1;} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Linear, "O(n)");
        yield return Case("ForeachOverString_IsLinearInLength",
            """public static class C { public static int CountLetters(string text) { var count=0; foreach(var ch in text){count+=ch;} return count; } }""",
            "return count;", SymbolicComplexityKind.Linear, "O(text.Length)");
        yield return Case("ForeachOverCollectionOutsideAnyNameTable_IsLinearInCount",
            """using System.Collections.Generic; public static class C { public static int SumAll(HashSet<int> values) { var sum=0; foreach(var value in values){sum+=value;} return sum; } }""",
            "return sum;", SymbolicComplexityKind.Linear, "O(values.Length)");
        yield return Case("MonotoneWhileLoop_IsLinear",
            """public static class C { public static int Work(int n) { var i=0; while(i<n){i++;} return i; } }""",
            "return i;", SymbolicComplexityKind.Linear, "O(n)");
        yield return Case("MonotoneDoLoop_IsLinear",
            """public static class C { public static int Work(int n) { var i=0; do{i++;}while(i<n); return i; } }""",
            "return i;", SymbolicComplexityKind.Linear, "O(n)", driver: "DoLoop");
        yield return Case("UnsupportedWhileLoop_IsUnknown",
            """public static class C { public static int Step(int value)=>value+1; public static int Work(int n) { var i=0; while(i<n){i=Step(i);} return i; } }""",
            "return i;", SymbolicComplexityKind.Unknown,
            unknowns: [SymbolicComplexityUnknownReason.UnsupportedWhileLoop]);
        yield return Case("KnownSourceCallee_ComposesIntoSurroundingLoop",
            """public static class C { public static void Helper(int m){for(var j=0;j<m;j++) { }} public static void Caller(int n,int m){for(var i=0;i<n;i++){Helper(m);}} }""",
            "Helper(m);", SymbolicComplexityKind.Product, "O(n * m)", callee: "Helper");
        yield return Case("OpenVirtualSourceCallee_IsConservativeUnknown",
            """public class Worker { public virtual void Work(int n) { } } public sealed class LinearWorker:Worker { public override void Work(int n){for(var i=0;i<n;i++) { }} } public static class C { public static void Caller(Worker worker,int n){worker.Work(n);} }""",
            "worker.Work(n);", SymbolicComplexityKind.Unknown,
            unknowns: [SymbolicComplexityUnknownReason.DynamicDispatch], calleeUnknown: SymbolicComplexityUnknownReason.DynamicDispatch);
        yield return Case("SealedReceiverSourceOverride_ComposesImplementationComplexity",
            """public abstract class Worker { public abstract void Work(int n); } public sealed class LinearWorker:Worker { public override void Work(int n){for(var i=0;i<n;i++) { }} } public static class C { public static void Caller(LinearWorker worker,int n){worker.Work(n);} }""",
            "worker.Work(n);", SymbolicComplexityKind.Linear, "O(n)", callee: "LinearWorker.Work");
        yield return Case("ExternalUnknownCallee_ProducesUnknown",
            """using System; public static class C { public static int Work(int n){_=Environment.GetEnvironmentVariable("PATH"); return n;} }""",
            "Environment.GetEnvironmentVariable", SymbolicComplexityKind.Unknown,
            unknowns: [SymbolicComplexityUnknownReason.ExternalCallee, SymbolicComplexityUnknownReason.UnknownCallee]);
        yield return Case("SelfRecursiveMethod_IsRecursiveUnknown",
            """public static class C { public static int Work(int n){if(n<=0){return 0;} return Work(n-1);} }""",
            "return Work", SymbolicComplexityKind.RecursiveUnknown, "O(RecursiveUnknown)");
        yield return Case("MutualRecursion_IsRecursiveUnknown",
            """public static class C { public static int First(int n)=>n<=0?0:Second(n-1); public static int Second(int n)=>n<=0?0:First(n-1); }""",
            "Second(n-1)", SymbolicComplexityKind.RecursiveUnknown);
        yield return Case("LineTarget_ResolvesContainingMethod",
            """public static class C { public static int Work(int n){var sum=0; for(var i=0;i<n;i++){sum+=i;} return sum;} }""",
            "sum+=i;", SymbolicComplexityKind.Linear, "O(n)", useLineTarget: true);
        yield return Case("NodeTarget_ResolvesContainingLocalFunction",
            """public static class C { public static int Work(int n){int Local(int m){for(var i=0;i<m;i++) { } return m;} return Local(n);} }""",
            "int Local", SymbolicComplexityKind.Linear, "O(m)");
        yield return Case("NodeTarget_ResolvesPropertyGetter",
            """public sealed class C { public int Count { get { var sum=0; for(var i=0;i<10;i++)sum+=i; return sum; } } }""",
            "int Count", SymbolicComplexityKind.Constant);
        yield return Case("NodeTarget_ResolvesIndexerGetter",
            """public sealed class C { public int this[int n] { get { var sum=0; for(var i=0;i<n;i++)sum+=i; return sum; } } }""",
            "this[int n]", SymbolicComplexityKind.Linear);
        yield return Case("UnsupportedForLoop_AggregatesPreLoopEvidenceOnce",
            """public static class C { private static int Seed(int value)=>value; private static bool KeepGoing(int value)=>value>=0; private static int Step(int value)=>value-1; public static int Work(int n){var result=0; for(var i=Seed(n);KeepGoing(i);i=Step(i)){result+=i;} return result;} }""",
            "return result;", SymbolicComplexityKind.Unknown,
            unknowns: [SymbolicComplexityUnknownReason.UnsupportedLoopShape], callee: "Seed",
            exactUnknowns: true, unknownDriverCount: 1, namedCalleeCount: 1);
    }
    [TestCaseSource(nameof(ComplexityCases))]
    public void ComplexityMatrix(object value) {
        var testCase = (ComplexityCase)value;
        var result = QueryComplexityAtMarker(testCase.Source, testCase.Marker, testCase.UseLineTarget);
        Assert.That(result.Complexity.Kind, Is.EqualTo(testCase.Kind));
        if (testCase.Text != null) Assert.That(result.Complexity.Text, Is.EqualTo(testCase.Text));
        if (testCase.Unknowns is { Length: > 0 }) {
            if (testCase.ExactUnknowns) Assert.That(result.UnknownReasons, Is.EqualTo(testCase.Unknowns));
            else Assert.That(result.UnknownReasons.Any(testCase.Unknowns.Contains), Is.True);
        }
        if (testCase.Driver != null) Assert.That(result.Drivers.Any(driver => driver.Kind == testCase.Driver), Is.True);
        if (testCase.Callee != null) Assert.That(result.CalleeSummaries.Any(summary
            => summary.MethodDisplayName.Contains(testCase.Callee, StringComparison.Ordinal)), Is.True);
        if (testCase.CalleeUnknown is { } reason) Assert.That(result.CalleeSummaries.Any(summary
            => summary.UnknownReason == reason), Is.True);
        if (testCase.UnknownDriverCount is { } driverCount) Assert.That(result.Drivers.Count(driver
            => driver.Kind == "Unknown"), Is.EqualTo(driverCount));
        if (testCase.NamedCalleeCount is { } calleeCount) Assert.That(result.CalleeSummaries.Count(summary
            => summary.MethodDisplayName.Contains(testCase.Callee!, StringComparison.Ordinal)), Is.EqualTo(calleeCount));
    }
    private static TestCaseData Case(
        string name, string source, string marker, SymbolicComplexityKind kind, string? text = null,
        SymbolicComplexityUnknownReason[]? unknowns = null, string? driver = null, string? callee = null,
        SymbolicComplexityUnknownReason? calleeUnknown = null,
        bool useLineTarget = false, bool exactUnknowns = false, int? unknownDriverCount = null,
        int? namedCalleeCount = null) => new TestCaseData(new ComplexityCase(
        source, marker, kind, text, unknowns, driver, callee, calleeUnknown,
        useLineTarget, exactUnknowns, unknownDriverCount, namedCalleeCount)).SetName(name);
    private static SymbolicComplexityResult QueryComplexityAtMarker(string source, string marker, bool useLineTarget = false) {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");
        var target = useLineTarget ? SharpProofTargetFactory.LineNumber(GetLineNumber(source,
            position)) : SharpProofTargetFactory.AtPosition(position);
        var (tree, compilation) = SymbolicSourceCompilation.Create(source, "SymbolicComplexityTests.cs",
            SymbolicSourceCompilationKind.Query, null, default);
        return QueryComplexity(SymbolicSourceInput.FromSyntaxTree(tree, compilation), target);
    }
    private static int GetLineNumber(string source, int position) =>
        source.Take(position).Count(static character => character == '\n') + 1;
    [Test]
    public void QueryComplexity_AllLinesTarget_ThrowsNotSupportedException() {
        var (tree, compilation) = SymbolicSourceCompilation.Create(
            "class C { }", "SymbolicComplexityTests.cs", SymbolicSourceCompilationKind.Query, null, default);
        var ex = Assert.Throws<NotSupportedException>(() => QueryComplexity(
            SymbolicSourceInput.FromSyntaxTree(tree, compilation), SharpProofTargetFactory.AllLines()));
        Assert.That(ex!.Message, Is.EqualTo("Complexity queries support point, position, or line targets only."));
    }
    private static SymbolicComplexityResult QueryComplexity(SymbolicSourceInput source, SharpProofTarget target) =>
        SymbolicMethodLikeQueryDispatcher.Execute(
            source,
            target,
            "Complexity queries support point, position, or line targets only.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true),
            static (resolved, compilation, token) => new SymbolicComplexityAnalysisSession(compilation, token).Analyze(resolved),
            default);
}
