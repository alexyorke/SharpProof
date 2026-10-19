using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class FlowChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("AssignParameter", "static int M(int x,int y) { x=y; return x; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("AssignLocal", "static int M(int x,int y) { var result=x; result=y; return result; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("AssignmentValue", "static int M(int x,int y) => x=y;", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("CompoundAssignment", "static int M(int x,int y,bool ignored) { x+=y; return x; }", "Suggestion", "int M<T1>(int x, int y, T1 ignored)"),
            ("Increment", "static int M(int x) => ++x;", "Concrete", null),
            ("Postfix", "static int M(int x) => x++;", "Concrete", null),
            ("ArrayWrite", "static int[] M(int[] xs,int i,int x) { xs[i]=x; return xs; }", "Suggestion", "T1[] M<T1>(T1[] xs, int i, T1 x)"),
            ("NestedBlocks", "static int M(int x,int y) { var result=x; { var inner=y; result=inner; } return result; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("IfFallthrough", "static int M(bool b,int x,int y) { if(b) x=y; return x; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("IfElseFallthrough", "static int M(bool b,int x,int y) { var result=x; if(b) result=y; else result=x; return result; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("While", "static int M(bool b,int x,int y) { while(b) { x=y; b=false; } return x; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("Do", "static int M(int x,int y) { do { x=y; } while(false); return x; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("For", "static int M(int count,int x,int y) { for(var i=0;i<count;i++) x=y; return x; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("ForInitializers", "static int M(int count,int x,int y) { var i=0; for(i=0;i<count;++i) { x=y; } return x; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("ForScope", "static int M(int x,int y) { for(var i=0;i<1;i++) { x=y; } for(var i=0;i<1;i++) { x=y; } return x; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("AssignmentWidening", "static long M(long x,int y,bool ignored) { x=y; return x; }", "Suggestion", "long M<T1>(long x, int y, T1 ignored)"),
            ("StringCompound", "static string M(string x,string y) { x+=y; return x; }", "Concrete", null),
            ("CustomIncrement", "static Counter M(Counter x) => ++x; struct Counter { public static Counter operator ++(Counter x) => x; }", "Skipped", null),
            ("ForeachArray", "static int M(int[] xs,int seed) { var result=seed; foreach(var x in xs) result=x; return result; }", "Suggestion", "T1 M<T1>(T1[] xs, T1 seed)"),
            ("ForeachMatrix", "static int M(int[,] xs,int seed) { var result=seed; foreach(var x in xs) result=x; return result; }", "Suggestion", "T1 M<T1>(T1[,] xs, T1 seed)"),
            ("ForeachExplicitConversion", "static long M(int[] xs,bool ignored) { var result=0L; foreach(long x in xs) result=x; return result; }", "Suggestion", "long M<T1>(int[] xs, T1 ignored)"),
            ("LoopReturn", "static int M(int count,int x,int y) { for(var i=0;i<count;i++) { if(i>0) return x; } return y; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("TupleReassign", "static int M((int,bool) pair,int x,bool b) { (x,b)=pair; return x; }", "Suggestion", "T1 M<T1, T2>((T1, T2) pair, T1 x, T2 b)"),
            ("TupleDiscardReassign", "static int M((int,bool) pair,int x) { (x,_)=pair; return x; }", "Suggestion", "T1 M<T1, T2>((T1, T2) pair, T1 x)"),
            ("VoidCall", "static int M(int x) { Consume(x); return x; } static void Consume<T>(T x) {}", "Suggestion", "T1 M<T1>(T1 x)"),
            ("EmptyStatements", "static int M(int x) { ; { ; } return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("BreakLoop", "static int M(int x) { while(true) { break; } return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ContinueLoop", "static int M(int x) { for(var i=0;i<1;i++) continue; return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ListEnumeration", "static int M(System.Collections.Generic.List<int> xs,int seed) { foreach(var x in xs) seed=x; return seed; }", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.List<T1> xs, T1 seed)"),
            ("InfiniteLoopReturn", "static int M(int x) { while(true) return x; }", "Skipped", null),
            ("PropertyWrite", "static int M(System.Text.StringBuilder builder,int x) { builder.Length=x; return x; }", "Concrete", null)
        ];
        var failures = new List<string>();
        var passed = 0;
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken: cancellationToken).Where(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Severity == DiagnosticSeverity.Error;
            }).ToArray();
            if (errors.Length != 0)
            {
                failures.Add($"Invalid test {test.Name}: {errors[0]}");
                continue;
            }

            var result = Program.Results(compilation, cancellationToken: cancellationToken).Single(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Method == "M";
            });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }

            var diagnostics = await (compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (diagnostics.Any(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Id == "AD0001";
            }) || diagnostics.Count(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Id == "HM0001";
            }) != Program.Results(compilation, cancellationToken: cancellationToken).Count(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Status == "Suggestion";
            }))
            {
                failures.Add($"Analyzer diagnostics disagree in {test.Name}.");
            }
            else
            {
                passed++;
                Program.Log.Info($"PASS {test.Name}");
            }
        }

        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join("\n", failures));
        }

        foreach (var test in tests.Where(t => t.Name is "AssignLocal" or "While" or "For" or "ForeachArray" or "ArrayWrite"))
        {
            var original = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var signature = Program.Results(original, cancellationToken: cancellationToken).Single(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Method == "M";
            }).Signature!;
            var tree = original.SyntaxTrees.Single();
            var root = (await (tree.GetRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false));
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            {
                global::System.ArgumentNullException.ThrowIfNull(m);
                return m.Identifier.ValueText == "M";
            });
            var replacement = SyntaxFactory.ParseMemberDeclaration("static " + signature + " " + method.Body)!;
            var changed = original.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(root.ReplaceNode(method, replacement), tree.Options));
            using var stream = new MemoryStream();
            var emitted = changed.Emit(stream, cancellationToken: cancellationToken);
            if (!emitted.Success)
            {
                throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
            }

            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var reflected = assembly.GetType("Cases")!.GetMethod("M", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var array = new[]
            {
                "first",
                "last"
            };
            object?[] arguments = test.Name switch
            {
                "While" => [true, "first", "last"],
                "For" => [2, "first", "last"],
                "ForeachArray" => [array, "seed"],
                "ArrayWrite" => [array, 0, "last"],
                _ => ["first", "last"]
            };
            var actual = reflected.MakeGenericMethod(typeof(string)).Invoke(null, arguments);
            if (test.Name == "ArrayWrite" ? !ReferenceEquals(actual, array) || array[0] != "last" : !Equals(actual, "last"))
            {
                throw new InvalidOperationException($"Runtime result changed for {test.Name}.");
            }

            passed++;
            Program.Log.Info($"PASS {test.Name}Runtime");
        }

        return passed;
    }
}
