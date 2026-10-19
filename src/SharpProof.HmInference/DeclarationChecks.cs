using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using SharpProof.HmInference.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class DeclarationChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("PrimaryConstructorRead", "class Holder(int stored){int M(bool ignored)=>stored;}", "Suggestion", "int M<T1>(T1 ignored)"),
            ("PrimaryConstructorStructRead", "struct Holder(int stored){int M(bool ignored)=>stored;}", "Suggestion", "int M<T1>(T1 ignored)"),
            ("PrimaryConstructorStore", "class Holder(int stored){int M(int value,bool ignored){stored=value;return stored;}}", "Suggestion", "int M<T1>(int value, T1 ignored)"),
            ("PrimaryConstructorCompoundStore", "class Holder(int stored){int M(int value,bool ignored){stored+=value;return stored;}}", "Suggestion", "int M<T1>(int value, T1 ignored)"),
            ("PrimaryConstructorGenericCapture", "class Holder<T>(T stored){T M(bool ignored)=>stored;}", "Suggestion", "T M<T1>(T1 ignored)"),
            ("PrimaryConstructorAsyncCapture", "class Holder(System.Threading.Tasks.Task<int> stored){async System.Threading.Tasks.Task<int> M(bool ignored)=>await stored;}", "Suggestion", "global::System.Threading.Tasks.Task<int> M<T1>(T1 ignored)"),
            ("PrimaryConstructorLambdaCapture", "class Holder(int stored){System.Func<int> M(bool ignored)=>()=>stored;}", "Suggestion", "global::System.Func<int> M<T1>(T1 ignored)"),
            ("InstanceRoot", "int M(int value) => value;", "Suggestion", "T1 M<T1>(T1 value)"),
            ("ExistingGenericRoot", "static int M<T>(T ignored,int value) => value;", "Suggestion", "T1 M<T, T1>(T ignored, T1 value)"),
            ("ExistingConstraints", "static int M<T>(T ignored,int value) where T:class => value;", "Suggestion", "T1 M<T, T1>(T ignored, T1 value) where T : class"),
            ("ReservedTypeName", "static int M<T1>(T1 ignored,int value) => value;", "Suggestion", "T2 M<T1, T2>(T1 ignored, T2 value)"),
            ("ReservedLocalName", "static int M(int value) { var T1=value; return T1; }", "Suggestion", "T2 M<T2>(T2 value)"),
            ("RefRoot", "static int M(ref int value) => value;", "Suggestion", "T1 M<T1>(ref T1 value)"),
            ("OutRoot", "static int M(int value,out int copy) { copy=value; return copy; }", "Suggestion", "T1 M<T1>(T1 value, out T1 copy)"),
            ("InRoot", "static int M(in int value) => value;", "Suggestion", "T1 M<T1>(in T1 value)"),
            ("ParamsRoot", "static int M(params int[] values) => values[0];", "Suggestion", "T1 M<T1>(params T1[] values)"),
            ("OptionalRoot", "static int M(int value,bool ignored=false) => value;", "Suggestion", "T1 M<T1>(T1 value, bool ignored = false)"),
            ("AttributedRoot", "[System.Obsolete] static int M(int value) => value;", "Suggestion", "T1 M<T1>(T1 value)"),
            ("AttributedParameter", "static int M([System.Diagnostics.CodeAnalysis.DisallowNull] int value) => value;", "Suggestion", "T1 M<T1>([System.Diagnostics.CodeAnalysis.DisallowNull] T1 value)"),
            ("InstanceField", "int field=1; int M(bool ignored) => field;", "Suggestion", "int M<T1>(T1 ignored)"),
            ("InstanceProperty", "int Value => 1; int M(bool ignored) => Value;", "Suggestion", "int M<T1>(T1 ignored)"),
            ("ExplicitThis", "int field=1; int M(bool ignored) => this.field;", "Suggestion", "int M<T1>(T1 ignored)"),
            ("ImplicitInstanceCall", "int Id(int value) => value; int M(int value,bool ignored) => Id(value);", "Suggestion", "int M<T1>(int value, T1 ignored)"),
            ("StaticField", "static int field=1; static int M(bool ignored) => field;", "Suggestion", "int M<T1>(T1 ignored)"),
            ("GenericExplicitCallerRejected", "static int M<T>(int value) => value; static int Use() => M<string>(1);", "Rejected", null)
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

        return passed + await PrimaryConstructorRuntime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> PrimaryConstructorRuntime(CancellationToken cancellationToken)
    {
        const string source = """
            public class PrimaryHolder(int stored) {
                public int Read(bool ignored)=>stored;
                public int Store(int replacement,bool ignored){stored=replacement;return stored;}
            }
            public class PrimaryGenericHolder<T>(T stored) {
                public T Read(bool ignored)=>stored;
                public T Store(T replacement,bool ignored){stored=replacement;return stored;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PrimaryConstructorRuntime", "PrimaryConstructorRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("PrimaryConstructors.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 4; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var assembly in new[] { before, after })
        {
            var generic = ReferenceEquals(assembly, after);
            var holder = assembly.GetType("PrimaryHolder")!;
            var instance = Activator.CreateInstance(holder, 1)!;
            var store = holder.GetMethod("Store")!;
            var read = holder.GetMethod("Read")!;
            if (generic)
            { store = store.MakeGenericMethod(typeof(string)); read = read.MakeGenericMethod(typeof(Guid)); }
            if (store.Invoke(instance, generic ? new object[] { 42, "ignored" } : new object[] { 42, false }) is not 42
                || read.Invoke(instance, generic ? new object[] { Guid.NewGuid() } : new object[] { false }) is not 42)
            { throw new InvalidOperationException("Primary constructor capture lost mutation."); }
            var sentinel = new Uri("https://example.invalid/primary-constructor");
            holder = assembly.GetType("PrimaryGenericHolder`1")!.MakeGenericType(typeof(Uri));
            instance = Activator.CreateInstance(holder, new Uri("https://example.invalid/original"))!;
            store = holder.GetMethod("Store")!;
            read = holder.GetMethod("Read")!;
            if (generic)
            { store = store.MakeGenericMethod(typeof(string)); read = read.MakeGenericMethod(typeof(Guid)); }
            if (!ReferenceEquals(store.Invoke(instance, generic ? new object[] { sentinel, "ignored" } : new object[] { sentinel, false }), sentinel)
                || !ReferenceEquals(read.Invoke(instance, generic ? new object[] { Guid.NewGuid() } : new object[] { false }), sentinel))
            { throw new InvalidOperationException("Primary generic constructor capture lost reference identity."); }
        }
        Program.Log.Info("PASS PrimaryConstructorCodeFixMutationAndReferenceIdentity");
        return 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
