using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class BuilderLayoutRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalBuilderLayoutRunner", "NewBuilderLayoutRunner"];

    internal static async Task<int> Run(LanguageVersion languageVersion, CancellationToken cancellationToken)
    {
        var support = BuilderLayoutChecks.Support;
        var first = support.IndexOf("public struct LayoutBuilder<T>", StringComparison.Ordinal);
        var last = support.IndexOf("public readonly struct LayoutSignal", StringComparison.Ordinal);
        var unused = support[first..last].Replace("LayoutBuilder", "UnusedLayoutBuilder", StringComparison.Ordinal)
            .Replace("where S:unmanaged,IAsyncStateMachine", "where S:IAsyncStateMachine", StringComparison.Ordinal)
            .Replace("where A:INotifyCompletion where S:IAsyncStateMachine", "where A:INotifyCompletion where S:unmanaged,IAsyncStateMachine", StringComparison.Ordinal);
        var legacyTask = "";
        if (languageVersion < LanguageVersion.CSharp10)
        {
            unused = unused.Replace("LayoutTask<T>", "UnusedLayoutTask<T>", StringComparison.Ordinal);
            legacyTask = "[AsyncMethodBuilder(typeof(UnusedLayoutBuilder<>))] public readonly struct UnusedLayoutTask<T>{private readonly T value;public UnusedLayoutTask(T value){this.value=value;}public LayoutAwaiter<T> GetAwaiter(){LayoutTrace.Value+=\"awaiter;\";return new LayoutAwaiter<T>(value);}}";
        }
        var source = "using System.Linq;\n" + support + unused + legacyTask + """
            public static class BuilderLayoutProbe{
            #pragma warning disable CS0162, CS8321 // Deliberately unreachable calls and an unused forwarding function.
                public static int UnusedCaptured(int value,string ignored){async LayoutTask<int> Unused(){await new LayoutSignal();return value;}return value;}
                public static LayoutTask<int> DeadInstantiation(int value,string ignored){static async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}if(false)_=Local("dead");static LayoutTask<string> Unused()=>Local("unused");_=nameof(Unused);return Local(value);}
                public static int CapturedForward(int value,string ignored){static async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}void Forward(){_=Local(value);}try{return value;}finally{Forward();}}
            #pragma warning restore CS0162, CS8321
                public static LayoutTask<int> LocalCall(int value,string ignored){static LayoutTask<U> Forward<U>(U item)=>Local(item);static async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}return Forward(value);}
                public static LayoutTask<LayoutPayload<int,string>> LocalComposite(LayoutPayload<int,string> value,string ignored){static async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}return Local(value);}
                public static LayoutTask<int?> LocalNullable(int? value,string ignored){static async LayoutTask<T?> Local<T>(T? item) where T:struct{await new LayoutSignal();return item;}return Local(value);}
                public static void LocalGroup(Action<Func<int,LayoutTask<int>>> sink,string ignored){static async LayoutTask<T> Local<T>(T item){await new LayoutSignal();return item;}sink(Local);}
                public static void Contextual(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(static async value=>{await new LayoutSignal();return value;});}
                public static void Callback(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(static async value=>{await new LayoutSignal();return value;});}
                public static void Parenthesized(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(static async (value)=>{await new LayoutSignal();return value;});}
                public static void Anonymous(Action<Func<int,LayoutTask<int>>> sink,string ignored){sink(async delegate{await new LayoutSignal();return 23;});}
                public static void Multiple(Action<Func<int,LayoutTask<int>>> first,Action<Func<long,LayoutTask<long>>> second,string ignored){first(static async value=>{await new LayoutSignal();return value;});second(static async value=>{await new LayoutSignal();return value;});}
                public static async LayoutTask<int> Value(int value,string ignored){await new LayoutSignal();return value;}
                public static async LayoutTask<int> Local(int value,string ignored){var saved=value;await new LayoutSignal();return saved;}
                public static async LayoutTask<int> Phantom(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();return value.Value;}
                public static async LayoutTask<int?> Nullable(int? value,string ignored){await new LayoutSignal();return value;}
                public static async LayoutTask<int> Awaiter(LayoutEmptyAwaitable<int> value,string ignored){await value;return 0;}
                [AsyncMethodBuilder(typeof(UnusedLayoutBuilder<>))] public static async LayoutTask<int> Free(int value,string ignored){await new LayoutCriticalSignal();return value;}
            }
            public static class CallbackLayout{
                public static bool Managed(Delegate callback){
                    var attribute=callback.Method.GetCustomAttributesData().Single(item=>item.AttributeType.FullName=="System.Runtime.CompilerServices.AsyncStateMachineAttribute");
                    var state=(Type)attribute.ConstructorArguments[0].Value!;
                    if(state.IsGenericTypeDefinition)state=state.MakeGenericType(callback.Method.DeclaringType!.GetGenericArguments().Concat(callback.Method.GetGenericArguments()).ToArray());
                    return (bool)typeof(RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!.MakeGenericMethod(state).Invoke(null,null)!;
                }
            }
            public static class OriginalBuilderLayoutRunner{
                public static bool Run(){
                    LayoutTrace.Value="";
                    Func<int,LayoutTask<int>>? contextual=null;BuilderLayoutProbe.Contextual(value=>contextual=value,"tag");
                    if(contextual is null||CallbackLayout.Managed(contextual)||contextual(23).GetAwaiter().GetResult()!=23)return false;
                    Func<int,LayoutTask<int>>? callback=null;BuilderLayoutProbe.Callback((Func<int,LayoutTask<int>> value)=>callback=value,"tag");
                    if(callback is null||CallbackLayout.Managed(callback)||callback(23).GetAwaiter().GetResult()!=23)return false;
                    Func<int,LayoutTask<int>>? parenthesized=null;BuilderLayoutProbe.Parenthesized((Func<int,LayoutTask<int>> value)=>parenthesized=value,"tag");
                    if(parenthesized is null||CallbackLayout.Managed(parenthesized)||parenthesized(23).GetAwaiter().GetResult()!=23)return false;
                    Func<int,LayoutTask<int>>? anonymous=null;BuilderLayoutProbe.Anonymous((Func<int,LayoutTask<int>> value)=>anonymous=value,"tag");
                    if(anonymous is null||CallbackLayout.Managed(anonymous)||anonymous(23).GetAwaiter().GetResult()!=23)return false;
                    Func<int,LayoutTask<int>>? first=null;Func<long,LayoutTask<long>>? second=null;
                    BuilderLayoutProbe.Multiple((Func<int,LayoutTask<int>> value)=>first=value,(Func<long,LayoutTask<long>> value)=>second=value,"tag");
                    if(first is null||second is null||CallbackLayout.Managed(first)||CallbackLayout.Managed(second)||first(23).GetAwaiter().GetResult()!=23||second(23L).GetAwaiter().GetResult()!=23L)return false;
                    Func<int,LayoutTask<int>>? group=null;BuilderLayoutProbe.LocalGroup((Func<int,LayoutTask<int>> value)=>group=value,"tag");
                    if(group is null||CallbackLayout.Managed(group)||group(23).GetAwaiter().GetResult()!=23)return false;
                    if(BuilderLayoutProbe.UnusedCaptured(23,"tag")!=23
                        ||BuilderLayoutProbe.DeadInstantiation(23,"tag").GetAwaiter().GetResult()!=23
                        ||BuilderLayoutProbe.CapturedForward(23,"tag")!=23
                        ||BuilderLayoutProbe.LocalCall(23,"tag").GetAwaiter().GetResult()!=23
                        ||BuilderLayoutProbe.LocalComposite(new LayoutPayload<int,string>{Value=23},"tag").GetAwaiter().GetResult().Value!=23
                        ||BuilderLayoutProbe.LocalNullable((int?)23,"tag").GetAwaiter().GetResult()!=23
                        ||BuilderLayoutProbe.LocalNullable((int?)null,"tag").GetAwaiter().GetResult()!=null)return false;
                    return BuilderLayoutProbe.Value(23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Local(23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Phantom(new LayoutPayload<int,string>{Value=23},"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((int?)23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((int?)null,"tag").GetAwaiter().GetResult()is null
                        &&BuilderLayoutProbe.Awaiter(default(LayoutEmptyAwaitable<int>),"tag").GetAwaiter().GetResult()==0
                        &&BuilderLayoutProbe.Free(23,"tag").GetAwaiter().GetResult()==23;
                }
            }
            """;
        if (languageVersion < LanguageVersion.CSharp9)
        { source = source.Replace("static async value=>", "async value=>", StringComparison.Ordinal).Replace("static async (value)=>", "async (value)=>", StringComparison.Ordinal); }
        if (languageVersion < LanguageVersion.CSharp10)
        { source = source.Replace("[AsyncMethodBuilder(typeof(UnusedLayoutBuilder<>))] public static async LayoutTask<int> Free", "public static async UnusedLayoutTask<int> Free", StringComparison.Ordinal); }
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "BuilderLayoutRuntime", "BuilderLayoutRuntime", LanguageNames.CSharp, parseOptions: new CSharpParseOptions(languageVersion),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, optimizationLevel: OptimizationLevel.Release), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("BuilderLayoutRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalBuilderLayoutRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original builder layout behavior failed."); }
        var expectedTrace = (string)before.GetType("LayoutTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 20; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Builder layout analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "BuilderLayoutProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 18)
        { throw new InvalidOperationException("Expected eighteen builder layout fixes; got " + fixes); }
        document = document.Project.AddDocument("NewBuilderLayoutDomains.cs", """
            #nullable enable
            using System;
            public static class NewBuilderLayoutRunner{
                public static bool Run(){
                    LayoutTrace.Value="";var tag=new object();var payload=new string('x',3);
                    Func<int,LayoutTask<int>>? contextual=null;BuilderLayoutProbe.Contextual(value=>contextual=value,tag);
                    if(contextual is null||CallbackLayout.Managed(contextual)||contextual(23).GetAwaiter().GetResult()!=23)return false;
                    Func<byte,LayoutTask<byte>>? callback=null;BuilderLayoutProbe.Callback((Func<byte,LayoutTask<byte>> value)=>callback=value,tag);
                    if(callback is null||CallbackLayout.Managed(callback)||callback(23).GetAwaiter().GetResult()!=23)return false;
                    Func<ushort,LayoutTask<ushort>>? parenthesized=null;BuilderLayoutProbe.Parenthesized((Func<ushort,LayoutTask<ushort>> value)=>parenthesized=value,tag);
                    if(parenthesized is null||CallbackLayout.Managed(parenthesized)||parenthesized(23).GetAwaiter().GetResult()!=23)return false;
                    Func<string,LayoutTask<int>>? anonymous=null;BuilderLayoutProbe.Anonymous((Func<string,LayoutTask<int>> value)=>anonymous=value,tag);
                    if(anonymous is null||CallbackLayout.Managed(anonymous)||anonymous(payload).GetAwaiter().GetResult()!=23)return false;
                    Func<byte,LayoutTask<byte>>? first=null;Func<decimal,LayoutTask<decimal>>? second=null;
                    BuilderLayoutProbe.Multiple((Func<byte,LayoutTask<byte>> value)=>first=value,(Func<decimal,LayoutTask<decimal>> value)=>second=value,tag);
                    if(first is null||second is null||CallbackLayout.Managed(first)||CallbackLayout.Managed(second)||first(23).GetAwaiter().GetResult()!=23||second(23m).GetAwaiter().GetResult()!=23m)return false;
                    Func<byte,LayoutTask<byte>>? group=null;BuilderLayoutProbe.LocalGroup((Func<byte,LayoutTask<byte>> value)=>group=value,tag);
                    if(group is null||CallbackLayout.Managed(group)||group(23).GetAwaiter().GetResult()!=23)return false;
                    if(BuilderLayoutProbe.UnusedCaptured(23,tag)!=23
                        ||BuilderLayoutProbe.DeadInstantiation((byte)23,tag).GetAwaiter().GetResult()!=23
                        ||BuilderLayoutProbe.CapturedForward((decimal)23,tag)!=23m
                        ||BuilderLayoutProbe.LocalCall((byte)23,tag).GetAwaiter().GetResult()!=23
                        ||BuilderLayoutProbe.LocalComposite(new LayoutPayload<byte,object[]>{Value=23},tag).GetAwaiter().GetResult().Value!=23
                        ||BuilderLayoutProbe.LocalNullable((decimal?)23m,tag).GetAwaiter().GetResult()!=23m
                        ||BuilderLayoutProbe.LocalNullable((decimal?)null,tag).GetAwaiter().GetResult()!=null)return false;
                    return BuilderLayoutProbe.Value((byte)23,tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Local((byte)23,tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Phantom(new LayoutPayload<byte,object[]>{Value=23},tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((decimal?)23m,tag).GetAwaiter().GetResult()==23m
                        &&BuilderLayoutProbe.Nullable((decimal?)null,tag).GetAwaiter().GetResult()is null
                        &&BuilderLayoutProbe.Awaiter(default(LayoutEmptyAwaitable<byte>),tag).GetAwaiter().GetResult()==0
                        &&object.ReferenceEquals(BuilderLayoutProbe.Free(payload,tag).GetAwaiter().GetResult(),payload);
                }
            }
            """);
        foreach (var sourceDocument in document.Project.Documents)
        {
            if ((await sourceDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString().Contains("__HmLayout_", StringComparison.Ordinal))
            { throw new InvalidOperationException("A private layout probe leaked into the code fix."); }
        }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("LayoutTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("BuilderLayout fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS BuilderLayoutCodeFixValuesNullablePhysicalPhantomAndUnusedDomains");
        (string Name, Type[] Arguments, bool Managed)[] states =
        [
            ("Value", [typeof(byte), typeof(object)], false),
            ("Phantom", [typeof(byte), typeof(object[]), typeof(object)], false),
            ("Free", [typeof(string), typeof(object)], true)
        ];
        foreach (var entry in states)
        {
            var method = after.GetType("BuilderLayoutProbe")!.GetMethod(entry.Name)!;
            var attribute = method.GetCustomAttributesData().Single(item =>
            { ArgumentNullException.ThrowIfNull(item); return item.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"; });
            var state = ((Type)attribute.ConstructorArguments[0].Value!).MakeGenericType(entry.Arguments);
            var check = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!.MakeGenericMethod(state);
            if (check.Invoke(null, null) is not bool managed || managed != entry.Managed)
            { throw new InvalidOperationException("State-machine physical layout mismatch: " + entry.Name); }
        }
        Program.Log.Info("PASS BuilderLayoutRuntimeFieldContracts");
        var invalid = document.Project.AddDocument("InvalidLayoutDomain.cs", "#nullable enable\npublic struct ManagedLayoutPayload{public string? Value;}public static class InvalidLayoutDomain{public static void Bad(){_=BuilderLayoutProbe.Value(\"managed\",0);_=BuilderLayoutProbe.Value(new ManagedLayoutPayload(),0);BuilderLayoutProbe.Callback((System.Func<string,LayoutTask<string>> callback)=>{},0);_=BuilderLayoutProbe.LocalCall(\"managed\",0);_=BuilderLayoutProbe.LocalComposite(new LayoutPayload<ManagedLayoutPayload,string>(),0);_=BuilderLayoutProbe.LocalNullable((ManagedLayoutPayload?)null,0);BuilderLayoutProbe.LocalGroup((System.Func<string,LayoutTask<string>> callback)=>{},0);}}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (rejected.GetDiagnostics(cancellationToken).Count(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS8377"; }) != 7)
        { throw new InvalidOperationException("Layout constraints must reject managed reference and struct payloads."); }
        Program.Log.Info("PASS BuilderLayoutRejectsManagedPayloadDomains");
        return fixes + 3;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Builder layout runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
