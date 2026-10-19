using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class ExitChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("ConditionalBreak", "static int M(int count,int x,int y) { for(var i=0;i<count;i++) { if(i>0) break; x=y; } return x; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("ConditionalContinue", "static int M(int count,int x,int y) { for(var i=0;i<count;i++) { if(i==0) continue; x=y; } return x; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("NestedLoops", "static int M(int x,int y) { for(var i=0;i<2;i++) { for(var j=0;j<2;j++) { x=y; break; } } return x; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("SwitchReturn", "static int M(int tag,int x,int y) { switch(tag) { case 0: return x; default: return y; } }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchBreak", "static int M(int tag,int x,int y) { switch(tag) { case 0: x=y; break; default: break; } return x; }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchNoDefault", "static int M(int tag,int x,int y) { switch(tag) { case 0: return x; } return y; }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchLabels", "static int M(int tag,int x,int y) { switch(tag) { case 0: case 1: return x; default: return y; } }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchGuard", "static int M(int tag,bool b,int x,int y) { switch(tag) { case 0 when b: return x; default: return y; } }", "Suggestion", "T1 M<T1>(int tag, bool b, T1 x, T1 y)"),
            ("SwitchContinue", "static int M(int count,int x,int y) { for(var i=0;i<count;i++) { switch(i) { case 0: continue; default: break; } x=y; } return x; }", "Suggestion", "T1 M<T1>(int count, T1 x, T1 y)"),
            ("SwitchBreakInLoop", "static int M(int x,int y) { for(var i=0;i<1;i++) { switch(i) { default: break; } x=y; } return x; }", "Suggestion", "T1 M<T1>(T1 x, T1 y)"),
            ("SwitchLoopBreak", "static int M(int tag,int x,int y) { switch(tag) { case 0: while(true) { x=y; break; } return x; default: return y; } }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchSharedScope", "static int M(int tag,int x,int y) { switch(tag) { case 0: var a=x; return a; default: var b=y; return b; } }", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("SwitchTypePattern", "static int M(object x) { switch(x) { case int value: return value; default: return 0; } }", "Skipped", null),
            ("GotoLabel", "static int M(int x) { goto Done; Done: return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ForeachDeconstruct", "static int M((int,bool)[] xs,int seed) { foreach(var (value,flag) in xs) seed=value; return seed; }", "Suggestion", "T1 M<T1, T2>((T1, T2)[] xs, T1 seed)"),
            ("ForeachDeconstructNested", "static string M((int,(bool,string))[] xs,string seed) { foreach(var (_,(_,text)) in xs) seed=text; return seed; }", "Suggestion", "T3 M<T1, T2, T3>((T1, (T2, T3))[] xs, T3 seed)"),
            ("ForeachDeconstructTyped", "static int M((int,bool)[] xs,int seed) { foreach((int value,bool flag) in xs) seed=value; return seed; }", "Concrete", null),
            ("ForeachCustomDeconstruct", "static int M(System.Collections.Generic.KeyValuePair<int,bool>[] xs,int seed) { foreach(var (value,flag) in xs) seed=value; return seed; }", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2>[] xs, T1 seed)"),
            ("CheckedStatement", "static int M(int x,bool ignored) { checked { x++; } return x; }", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("UncheckedStatement", "static int M(int x,bool ignored) { unchecked { x++; } return x; }", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("ThrowGuard", "static int M(bool fail,int x) { if(fail) throw new System.InvalidOperationException(); return x; }", "Suggestion", "T1 M<T1>(bool fail, T1 x)"),
            ("ThrowOnly", "static int M(bool ignored) { throw new System.InvalidOperationException(); }", "Suggestion", "int M<T1>(T1 ignored)"),
            ("ThrowExpression", "static int M(bool fail,int x) => fail ? throw new System.InvalidOperationException() : x;", "Suggestion", "T1 M<T1>(bool fail, T1 x)"),
            ("SwitchThrow", "static int M(int tag,int x) { switch(tag) { case 0: return x; default: throw new System.InvalidOperationException(); } }", "Suggestion", "T1 M<T1>(int tag, T1 x)"),
            ("TryFinally", "static int M(int x) { try { return x; } finally {} }", "Suggestion", "T1 M<T1>(T1 x)")
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

        foreach (var test in tests.Where(t => t.Name is "ConditionalBreak" or "ConditionalContinue" or "SwitchReturn" or "SwitchContinue" or "SwitchBreakInLoop" or "ForeachDeconstruct" or "ThrowGuard" or "CheckedStatement" or "UncheckedStatement"))
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
            var types = test.Name == "ForeachDeconstruct" ? new[]
            {
                typeof(string),
                typeof(bool)
            }

            : [typeof(string)];
            var generic = reflected.MakeGenericMethod(types);
            object?[] arguments = test.Name switch
            {
                "ForeachDeconstruct" => [new[]
                {
                    ("first", false),
                    ("last", true)
                }, "seed"],
                "ThrowGuard" => [false, "last"],
                "SwitchReturn" => [0, "first", "last"],
                "SwitchBreakInLoop" => ["first", "last"],
                "CheckedStatement" or "UncheckedStatement" => [int.MaxValue, "ignored"],
                _ => [2, "first", "last"]
            };
            if (test.Name == "CheckedStatement")
            {
                try
                {
                    generic.Invoke(null, arguments);
                    throw new InvalidOperationException("Checked overflow was lost.");
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is OverflowException)
                {
                }

                passed++;
                Program.Log.Info("PASS CheckedStatementRuntime");
                continue;
            }

            if (test.Name == "UncheckedStatement")
            {
                if (!Equals(generic.Invoke(null, arguments), int.MinValue))
                {
                    throw new InvalidOperationException("Unchecked wrapping changed.");
                }

                passed++;
                Program.Log.Info("PASS UncheckedStatementRuntime");
                continue;
            }

            var expected = test.Name == "SwitchReturn" ? "first" : "last";
            if (!Equals(generic.Invoke(null, arguments), expected))
            {
                throw new InvalidOperationException($"Runtime result changed for {test.Name}.");
            }

            if (test.Name == "ThrowGuard")
            {
                try
                {
                    generic.Invoke(null, [true, "ignored"]);
                    throw new InvalidOperationException("Throw guard failed.");
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
                {
                }
            }

            passed++;
            Program.Log.Info($"PASS {test.Name}Runtime");
        }

        return passed;
    }
}
