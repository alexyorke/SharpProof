using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class DeclarationChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests = [("InstanceRoot", "int M(int value) => value;", "Suggestion", "T1 M<T1>(T1 value)"), ("ExistingGenericRoot", "static int M<T>(T ignored,int value) => value;", "Suggestion", "T1 M<T, T1>(T ignored, T1 value)"), ("ExistingConstraints", "static int M<T>(T ignored,int value) where T:class => value;", "Suggestion", "T1 M<T, T1>(T ignored, T1 value) where T : class"), ("ReservedTypeName", "static int M<T1>(T1 ignored,int value) => value;", "Suggestion", "T2 M<T1, T2>(T1 ignored, T2 value)"), ("ReservedLocalName", "static int M(int value) { var T1=value; return T1; }", "Suggestion", "T2 M<T2>(T2 value)"), ("RefRoot", "static int M(ref int value) => value;", "Suggestion", "T1 M<T1>(ref T1 value)"), ("OutRoot", "static int M(int value,out int copy) { copy=value; return copy; }", "Suggestion", "T1 M<T1>(T1 value, out T1 copy)"), ("InRoot", "static int M(in int value) => value;", "Suggestion", "T1 M<T1>(in T1 value)"), ("ParamsRoot", "static int M(params int[] values) => values[0];", "Suggestion", "T1 M<T1>(params T1[] values)"), ("OptionalRoot", "static int M(int value,bool ignored=false) => value;", "Suggestion", "T1 M<T1>(T1 value, bool ignored = false)"), ("AttributedRoot", "[System.Obsolete] static int M(int value) => value;", "Suggestion", "T1 M<T1>(T1 value)"), ("AttributedParameter", "static int M([System.Diagnostics.CodeAnalysis.DisallowNull] int value) => value;", "Suggestion", "T1 M<T1>([System.Diagnostics.CodeAnalysis.DisallowNull] T1 value)"), ("InstanceField", "int field=1; int M(bool ignored) => field;", "Suggestion", "int M<T1>(T1 ignored)"), ("InstanceProperty", "int Value => 1; int M(bool ignored) => Value;", "Suggestion", "int M<T1>(T1 ignored)"), ("ExplicitThis", "int field=1; int M(bool ignored) => this.field;", "Suggestion", "int M<T1>(T1 ignored)"), ("ImplicitInstanceCall", "int Id(int value) => value; int M(int value,bool ignored) => Id(value);", "Suggestion", "int M<T1>(int value, T1 ignored)"), ("StaticField", "static int field=1; static int M(bool ignored) => field;", "Suggestion", "int M<T1>(T1 ignored)"), ("GenericExplicitCallerRejected", "static int M<T>(int value) => value; static int Use() => M<string>(1);", "Rejected", null)];
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

        return passed;
    }
}
