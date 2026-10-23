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
            ("TypeOfDiscard", "static int M(int value){_=typeof(int);return value;}", "Suggestion", "T1 M<T1>(T1 value)"),
            ("TypeOfTuple", "static (System.Type kind,int value) M(int value)=>(typeof(int?),value);", "Suggestion", "(global::System.Type kind, T1 value) M<T1>(T1 value)"),
            ("TypeOfOpenGeneric", "static (System.Type kind,int value) M(int value)=>(typeof(System.Collections.Generic.Dictionary<,>),value);", "Suggestion", "(global::System.Type kind, T1 value) M<T1>(T1 value)"),
            ("TypeOfExistingGeneric", "static (System.Type kind,int value) M<T>(int value)=>(typeof(T),value);", "Suggestion", "(global::System.Type kind, T1 value) M<T, T1>(T1 value)"),
            ("TypeOfVoid", "static (System.Type kind,int value) M(int value)=>(typeof(void),value);", "Suggestion", "(global::System.Type kind, T1 value) M<T1>(T1 value)"),
            ("TypeOfPointer", "unsafe static (System.Type kind,int value) M(int value)=>(typeof(int*),value);", "Suggestion", "(global::System.Type kind, T1 value) M<T1>(T1 value)"),
            ("TypeOfLambda", "static System.Func<(System.Type kind,int value)> M(int value)=>()=> (typeof(int),value);", "Suggestion", "global::System.Func<(global::System.Type kind, T1 value)> M<T1>(T1 value)"),
            ("TypeOfAsync", "static async System.Threading.Tasks.Task<(System.Type kind,int value)> M(System.Threading.Tasks.Task<int> value)=>(typeof(int),await value);", "Suggestion", "global::System.Threading.Tasks.Task<(global::System.Type kind, T1 value)> M<T1>(global::System.Threading.Tasks.Task<T1> value)"),
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
            if (test.Name == "TypeOfPointer")
            { compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true)); }
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

        return passed + await PrimaryConstructorRuntime(cancellationToken).ConfigureAwait(false) + await TypeOfRuntime(cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> TypeOfRuntime(CancellationToken cancellationToken)
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            public static class TypeTrace { public static int Initializers; }
            public sealed class TypeToken { static TypeToken(){TypeTrace.Initializers++;} }
            public static class TypeProbe {
                public static (Type kind,int value) Concrete(int value)=>(typeof(int?),value);
                public static (Type kind,int value) Token(int value)=>(typeof(TypeToken),value);
                public static (Type kind,int value) Open(int value)=>(typeof(Dictionary<,>),value);
                public static (Type kind,int value) Existing<T>(int value)=>(typeof(T),value);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "TypeOfRuntime", "TypeOfRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("TypeOf.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(original.WithAssemblyName("TypeOfOriginal"), cancellationToken);
        var after = Load(changed.WithAssemblyName("TypeOfChanged"), cancellationToken);
        foreach (var assembly in new[] { before, after })
        {
            var generalized = ReferenceEquals(assembly, after);
            foreach (var name in new[] { "Concrete", "Token", "Open", "Existing" })
            {
                var method = assembly.GetType("TypeProbe")!.GetMethod(name)!;
                var expectedType = name switch
                {
                    "Concrete" => typeof(int?),
                    "Token" => assembly.GetType("TypeToken")!,
                    "Open" => typeof(Dictionary<,>),
                    _ => typeof(Guid)
                };
                if (generalized ? method.GetGenericArguments().Length != (name == "Existing" ? 2 : 1)
                    : method.GetGenericArguments().Length != (name == "Existing" ? 1 : 0))
                { throw new InvalidOperationException("TypeOf Fix All missed " + name); }
                foreach (var domain in generalized ? new[] { typeof(int), typeof(string), typeof(int?) } : new[] { typeof(int) })
                {
                    ArgumentNullException.ThrowIfNull(domain);
                    var instantiated = name == "Existing" ? method.MakeGenericMethod(generalized ? new[] { typeof(Guid), domain } : new[] { typeof(Guid) })
                        : generalized ? method.MakeGenericMethod(domain) : method;
                    object?[] values = domain == typeof(int?) ? [37, null]
                        : [domain == typeof(string) ? new string('x', 3) : 37];
                    foreach (var value in values)
                    {
                        var result = (System.Runtime.CompilerServices.ITuple)instantiated.Invoke(null, new[] { value })!;
                        if (!Equals(result[0], expectedType) || !Equals(result[1], value)
                            || domain == typeof(string) && !ReferenceEquals(result[1], value))
                        { throw new InvalidOperationException("TypeOf Fix All changed type or value identity: " + name); }
                    }
                }
            }
            if (assembly.GetType("TypeTrace")!.GetField("Initializers")!.GetValue(null) is not 0)
            { throw new InvalidOperationException("TypeOf unexpectedly initialized its operand type."); }
        }
        Program.Log.Info("PASS TypeOfCodeFixTypeIdentityAndInitialization");
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
