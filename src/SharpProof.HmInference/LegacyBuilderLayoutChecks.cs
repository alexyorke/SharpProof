using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class LegacyBuilderLayoutChecks
{
    private const string Support = BuilderLayoutChecks.Support;

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        LanguageVersion[] versions = [LanguageVersion.CSharp8, LanguageVersion.CSharp9];
        (string Name, string Body, string Signature)[] tests =
        [
            ("Callback", "static void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async value=>{await new LayoutSignal();return value;});}", "void M<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged"),
            ("ExpressionCallback", "static void M(Action<Func<LayoutTask<int>,LayoutTask<int>>> sink,string ignored){sink(async value=>await value);}", "void M<T1, T2>(global::System.Action<global::System.Func<global::LayoutTask<T1>, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged"),
            ("ParenthesizedCallback", "static void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async (value)=>{await new LayoutSignal();return value;});}", "void M<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged"),
            ("AnonymousCallback", "static void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async delegate(int value){await new LayoutSignal();return value;});}", "void M<T1>(global::System.Action<global::System.Func<int, global::LayoutTask<int>>> sink, T1 ignored)"),
            ("OmittedAnonymousParameters", "static void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async delegate{await new LayoutSignal();return 23;});}", "void M<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<int>>> sink, T2 ignored)"),
            ("MixedCallbacks", "static void M(Action<Func<int,System.Threading.Tasks.Task<int>>> first,Action<Func<long,LayoutTask<long>>> second,string ignored){first(async value=>{await System.Threading.Tasks.Task.Yield();return value;});second(async value=>{await new LayoutSignal();return value;});}", "void M<T1, T2, T3>(global::System.Action<global::System.Func<T1, global::System.Threading.Tasks.Task<T1>>> first, global::System.Action<global::System.Func<T2, global::LayoutTask<T2>>> second, T3 ignored) where T2 : unmanaged"),
            ("NestedLocal", "static void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async value=>{async LayoutTask<int> Local(int item){await new LayoutSignal();return item;}await new LayoutSignal();_=Local(0);return value;});}", "void M<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged"),
            ("VoidExpressionCallback", "static void M(Action<Func<System.Threading.Tasks.Task>> first,Action<Func<int,LayoutTask<int>>> second,string ignored){first(async ()=>await System.Threading.Tasks.Task.Yield());second(async value=>{await new LayoutSignal();return value;});}", "void M<T1, T2>(global::System.Action<global::System.Func<global::System.Threading.Tasks.Task>> first, global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> second, T2 ignored) where T1 : unmanaged"),
            ("HiddenLines", "\n#line hidden\nstatic void M(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async value=>{await new LayoutSignal();return value;});}", "void M<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged"),
            ("RepeatedLocalNames", "static LayoutTask<int> M(int value,long second,string ignored){{async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}_=Local(second);}{async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}return Local(value);}}", "global::LayoutTask<T1> M<T1, T2, T3>(T1 value, T2 second, T3 ignored) where T1 : unmanaged where T2 : unmanaged"),
            ("GenericOwner", "}class GenericOwner<TOuter>{static LayoutTask<int> M(int value,string ignored){async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}return Local(value);}}class Tail{", "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("GenericLocal", "static LayoutTask<int> M(int value,string ignored){async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}return Local(value);}", "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("GenericLocalExpression", "static LayoutTask<int> M(int value,string ignored){async LayoutTask<T> Local<T>(T item)=>await new LayoutTask<T>(item);return Local(value);}", "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged")
        ];
        var failures = new List<string>();
        foreach (var version in versions)
        {
            foreach (var test in tests)
            {
                var options = new CSharpParseOptions(version);
                var tree = CSharpSyntaxTree.ParseText("using System;class Cases{" + test.Body + "}", options, path: "Input.cs", cancellationToken: cancellationToken);
                var support = CSharpSyntaxTree.ParseText(Support, options, path: "Support.cs", cancellationToken: cancellationToken);
                var other = CSharpSyntaxTree.ParseText("class Unrelated {}", options, path: "Other.cs", cancellationToken: cancellationToken);
                var compilation = Program.Compile([tree, support, other]).WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, generalDiagnosticOption: ReportDiagnostic.Error));
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Legacy layout fixture: " + version + " " + test.Name + ": " + string.Join("; ", emitted.Diagnostics)); }
                var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
                if (result.Status != "Suggestion" || result.Signature != test.Signature)
                { failures.Add(version + " " + test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
                else
                { Program.Log.Info("PASS LegacyLayout" + version + test.Name); }
            }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var legacy = Program.Compile([CSharpSyntaxTree.ParseText(Support.Replace("#nullable enable", "", StringComparison.Ordinal).Replace("default!", "default(T)", StringComparison.Ordinal) + "class LegacyCases{" + tests[0].Body + "}", new CSharpParseOptions(LanguageVersion.CSharp7_3), cancellationToken: cancellationToken)])
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var legacyImage = new MemoryStream();
        if (!legacy.Emit(legacyImage, cancellationToken: cancellationToken).Success
            || Program.Results(legacy, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status == "Suggestion")
        { throw new InvalidOperationException("C# 7.3 must retain its nongeneric unmanaged state-machine domain."); }
        Program.Log.Info("PASS LegacyLayoutCSharp73PreservesConstructedUnmanagedLanguageBoundary");
        return versions.Length * tests.Length + 1 + await BuilderLayoutRuntimeChecks.Run(LanguageVersion.CSharp8, cancellationToken).ConfigureAwait(false);
    }
}
