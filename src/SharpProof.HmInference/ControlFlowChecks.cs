using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class ControlFlowChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("ForwardGoto", "static int M(int x) { goto Done; Done:return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ConditionalGoto", "static int M(bool choose,int x,int y) { if(choose) goto Other; return x; Other:return y; }", "Suggestion", "T1 M<T1>(bool choose, T1 x, T1 y)"),
            ("BackwardGoto", "static int M(int count,int x) { Again:if(count-->0) goto Again; return x; }", "Suggestion", "T1 M<T1>(int count, T1 x)"),
            ("GotoOutOfBlock", "static int M(int x) { {goto Done;} Done:return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("GotoFinally", "static int M(int x,bool flag) { try {goto Done;} finally {flag=!flag;} Done:return x; }", "Suggestion", "T1 M<T1>(T1 x, bool flag)"),
            ("GotoUsing", "static int M(System.IO.MemoryStream stream,int x) { using(stream) {goto Done;} Done:return x; }", "Suggestion", "T2 M<T1, T2>(T1 stream, T2 x) where T1 : global::System.IDisposable"),
            ("GotoCase", "static int M(int tag,int x,int y) { switch(tag) {case 0:goto case 1;case 1:return x;default:return y;} }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("GotoDefault", "static int M(int tag,int x) { switch(tag) {case 0:goto default;default:return x;} }", "Suggestion", "T1 M<T1>(int tag, T1 x)"),
            ("GotoNullCase", "static int M(string? tag,int x,int y) { switch(tag) {case \"\":goto case null;case null:return x;default:return y;} }", "Suggestion", "T1 M<T1>(string? tag, T1 x, T1 y)"),
            ("GotoReservedName", "static int M(int x) {goto T1;T1:return x;}", "Suggestion", "T2 M<T2>(T2 x)"),
            ("UnreachableConstraints", "static int M(int x,bool ignored) {return x; x++;}", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("UnreachableGotoConstraints", "static int M(int x,bool ignored) {goto Done; x++; Done:return x;}", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("UnreachableLocal", "static int M(int x) {goto Done; int unused=1; Done:return x;}", "Suggestion", "T1 M<T1>(T1 x)"),
            ("UninitializedLocal", "static int M(int x,bool ignored) {int value;value=x;return value;}", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("UninitializedWidening", "static long M(int x,bool ignored) {long value;value=x;return value;}", "Suggestion", "long M<T1>(int x, T1 ignored)"),
            ("TypedLocalWidening", "static long M(int x,bool ignored) {long value=x;return value;}", "Suggestion", "long M<T1>(int x, T1 ignored)"),
            ("TypedLocalBoxing", "static object M(int x,bool ignored) {object value=x;return value;}", "Suggestion", "object M<T1>(int x, T1 ignored)"),
            ("TypedNullableLocal", "static string? M(string x,bool ignored) {string? value=x;return value;}", "Suggestion", "string? M<T1>(string x, T1 ignored)"),
            ("TypedDelegateLocal", "static System.Func<int> M(int x,bool ignored) {System.Func<int> read=()=>x;return read;}", "Suggestion", "global::System.Func<int> M<T1>(int x, T1 ignored)"),
            ("TypedAsyncLocal", "static async Task<int> M(Task<int> task,bool ignored) {int value=await task;return value;}", "Suggestion", "global::System.Threading.Tasks.Task<int> M<T1>(global::System.Threading.Tasks.Task<int> task, T1 ignored)"),
            ("ConstantCaseTarget", "static int M(int tag,int x) {const int Route=1;switch(tag) {case 0:goto case Route;case Route:return x;default:return x;} }", "Suggestion", "T1 M<T1>(int tag, T1 x)"),
            ("ConstantNarrowing", "static byte M(int ignored) {const byte value=1;return value;}", "Suggestion", "byte M<T1>(T1 ignored)"),
            ("ConstantNameof", "static string M(int x) {const string name=nameof(x);return name;}", "Suggestion", "string M<T1>(T1 x)"),
            ("InfiniteWhileReturn", "static int M(int x) {while(true) return x;}", "Suggestion", "T1 M<T1>(T1 x)"),
            ("InfiniteForReturn", "static int M(int x) {for(;;) return x;}", "Suggestion", "T1 M<T1>(T1 x)"),
            ("InfiniteConditionalReturn", "static int M(bool stop,int x) {while(true) {if(stop) return x;} }", "Suggestion", "T1 M<T1>(bool stop, T1 x)"),
            ("NonReturningLoop", "static int M(int ignored) {for(;;) {} }", "Suggestion", "int M<T1>(T1 ignored)"),
            ("DoReturn", "static int M(int x) {do {return x;} while(false);}", "Suggestion", "T1 M<T1>(T1 x)"),
            ("NestedInfiniteLambda", "static System.Func<int> M(int x)=>()=>{for(;;) return x;};", "Suggestion", "global::System.Func<T1> M<T1>(T1 x)"),
            ("GotoIterator", "static System.Collections.Generic.IEnumerable<int> M(int x) {goto Done;Done:yield return x;}", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 x)"),
            ("GotoAsync", "static async Task<int> M(int x) {await Task.Yield();goto Done;Done:return x;}", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x)"),
            ("ImplicitBooleanCondition", "static int M(Flag flag,int x) {if(flag) return x;return x;} class Flag { public static implicit operator bool(Flag value)=>true; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x)"),
            ("TrueOperatorCondition", "static int M(Flag flag,int x) {while(flag) return x;return x;} class Flag { public static bool operator true(Flag value)=>true;public static bool operator false(Flag value)=>false; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x)"),
            ("ImplicitBooleanTernary", "static int M(Flag flag,int x,int y)=>flag?x:y; class Flag { public static implicit operator bool(Flag value)=>true; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x, T1 y)"),
            ("ImplicitBooleanFor", "static int M(Flag flag,int x) {for(;flag;) return x;return x;} class Flag { public static implicit operator bool(Flag value)=>true; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x)"),
            ("ImplicitBooleanDo", "static int M(Flag flag,int x,int y) {do{x=y;}while(flag);return x;} class Flag { public static implicit operator bool(Flag value)=>false; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x, T1 y)"),
            ("ImplicitBooleanSwitchGuard", "static int M(int tag,Flag flag,int x) {switch(tag) {case 0 when flag:return x;default:return x;} } class Flag { public static implicit operator bool(Flag value)=>true; }", "Suggestion", "T1 M<T1>(int tag, global::Cases.Flag flag, T1 x)"),
            ("ImplicitBooleanFilter", "static int M(Flag flag,int x) {try{throw new System.Exception();}catch(System.Exception)when(flag){return x;} } class Flag { public static implicit operator bool(Flag value)=>true; }", "Suggestion", "T1 M<T1>(global::Cases.Flag flag, T1 x)"),
            ("IndentedInstanceDeclaration", "\n    int M(int x)=>x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("LeadingDeclarationComment", "\n    /* declaration */ int M(int x)=>x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ParameterNullableDirectives", "static string? M(\n#nullable disable\nstring value,\n#nullable enable\nbool ignored)=>value;", "Suggestion", "T1? M<T1, T2>(T1 value, T2 ignored) where T1 : class"),
            ("VarargRoot", "static int M(int x,__arglist)=>x;", "Concrete", null),
            ("GotoDefiniteAssignment", "static int M(int x,bool ignored) {int value;goto Assign;Assign:value=x;return value;}", "Suggestion", "int M<T1>(int x, T1 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException($"Invalid fixture {test.Name}: {errors[0]}"); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Analyzer failure: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length;
    }
}
