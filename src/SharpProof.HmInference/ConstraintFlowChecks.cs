using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class ConstraintFlowChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("StructCall", "static int M(int x)=>Id(x); static T Id<T>(T x) where T:struct=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : struct"),
            ("UnmanagedCall", "static int M(int x)=>Id(x); static T Id<T>(T x) where T:unmanaged=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : unmanaged"),
            ("NullableClassCall", "static string M(string x)=>Id(x); static T Id<T>(T x) where T:class?=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : class?"),
            ("NewCall", "static object M(object x)=>Id(x); static T Id<T>(T x) where T:new()=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : new()"),
            ("InterfaceCall", "static int M(int x)=>Id(x); static T Id<T>(T x) where T:System.IComparable<T> =>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : global::System.IComparable<T1>"),
            ("BaseCall", "static System.Exception M(System.Exception x)=>Id(x); static T Id<T>(T x) where T:System.Exception=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : global::System.Exception"),
            ("CombinedCall", "static object M(object x)=>Id(x); static T Id<T>(T x) where T:class,new()=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : class, new()"),
            ("BaseBeforeInterface", "static B M(B x)=>First(Second(x)); static T First<T>(T x) where T:B=>x; static T Second<T>(T x) where T:System.ICloneable=>x; class B:System.ICloneable { public object Clone()=>this; }", "Suggestion", "T1 M<T1>(T1 x) where T1 : global::Cases.B, global::System.ICloneable"),
            ("DependentCall", "static object M(object x,object y)=>Id(x,y); static T Id<T,U>(T x,U y) where T:U=>x;", "Suggestion", "T1 M<T1, T2>(T1 x, T2 y) where T1 : T2"),
            ("DuplicateConstraint", "static object M(object x)=>Id(Id(x)); static T Id<T>(T x) where T:class=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : class"),
            ("DictionaryConstraint", "static System.Collections.Generic.Dictionary<string,int> M(System.Collections.Generic.Dictionary<string,int> x)=>x;", "Suggestion", "global::System.Collections.Generic.Dictionary<T1, T2> M<T1, T2>(global::System.Collections.Generic.Dictionary<T1, T2> x) where T1 : notnull"),
            ("ContainerConstraint", "static Box<object> M(Box<object> x)=>x; } class Box<T> where T:class,new() {", "Suggestion", "global::Box<T1> M<T1>(global::Box<T1> x) where T1 : class, new()"),
            ("TryCatchReturns", "static int M(int x,int y) { try { return x; } catch { return y; } }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("TryCatchLocal", "static int M(int x) { try { return x; } catch(System.Exception ex) { var saved=ex; throw saved; } }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("TryFilter", "static int M(int x,bool filter) { try { return x; } catch(System.Exception) when(filter) { return x; } }", "Suggestion", "T1 M<T1>(T1 x, bool filter)"),
            ("TryRethrow", "static int M(int x) { try { return x; } catch { throw; } }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("TryFinallyConstraint", "static int M(int x,bool flag) { try { return x; } finally { flag=!flag; } }", "Suggestion", "T1 M<T1>(T1 x, bool flag)"),
            ("TryFinallyThrow", "static int M(int x) { try { return x; } finally { throw new System.Exception(); } }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("TryLoopExit", "static int M(int x,bool flag) { while(flag) { try { break; } finally { flag=false; } } return x; }", "Suggestion", "T1 M<T1>(T1 x, bool flag)"),
            ("TryFallthrough", "static int M(int x,bool flag) { try { flag=!flag; } catch { flag=false; } return x; }", "Suggestion", "T1 M<T1>(T1 x, bool flag)"),
            ("UsingExpression", "static int M(System.IO.MemoryStream stream,int x) { using(stream) { return x; } }", "Suggestion", "T1 M<T1>(global::System.IO.MemoryStream stream, T1 x)"),
            ("UsingDeclaration", "static int M(System.IO.MemoryStream stream,int x) { using var resource=stream; return x; }", "Suggestion", "T1 M<T1>(global::System.IO.MemoryStream stream, T1 x)"),
            ("UsingTypedConversion", "static int M(System.IO.MemoryStream stream,int x) { using(System.IDisposable resource=stream) { return x; } }", "Suggestion", "T1 M<T1>(global::System.IO.MemoryStream stream, T1 x)"),
            ("AwaitUsing", "static async Task<int> M(System.IO.MemoryStream stream,int x) { await using(stream) { await Task.Yield(); return x; } }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.IO.MemoryStream stream, T1 x)"),
            ("AwaitUsingDeclaration", "static async Task<int> M(System.IO.MemoryStream stream,int x) { await using var resource=stream; return x; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.IO.MemoryStream stream, T1 x)"),
            ("MonitorLock", "static int M(object gate,int x) { lock(gate) { return x; } }", "Suggestion", "T1 M<T1>(object gate, T1 x)"),
            ("SpecialLock", "static int M(System.Threading.Lock gate,int x) { lock(gate) { return x; } }", "Suggestion", "T1 M<T1>(global::System.Threading.Lock gate, T1 x)")
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
