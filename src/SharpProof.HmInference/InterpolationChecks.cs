using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class InterpolationChecks
{
    private const string Support = """
        #nullable enable
        using System;using System.Collections.Generic;using System.Globalization;using System.Runtime.CompilerServices;using System.Threading.Tasks;
        public static class InterpolationTrace {public static string Log="";public static bool Continue=true;public static object? LastContext;public static T Touch<T>(T value){Log+="hole;";return value;}}
        [InterpolatedStringHandler]public struct ProbeHandler {
            public ProbeHandler(int literalLength,int formattedCount){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value,int alignment=0,string? format=null){}
        }
        [InterpolatedStringHandler]public struct ProbeSpecialHandler {
            public ProbeSpecialHandler(int literalLength,int formattedCount){}
            public void AppendLiteral(string text){}
            public void AppendFormatted(int value){}
            public void AppendFormatted<T>(T value){}
        }
        [InterpolatedStringHandler]public struct ProbeConstrainedHandler {
            public ProbeConstrainedHandler(int literalLength,int formattedCount){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value) where T:IFormattable{}
        }
        [InterpolatedStringHandler]public struct ProbeParamsHandler {
            public ProbeParamsHandler(int literalLength,int formattedCount){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(params T[] values) where T:IFormattable{}
        }
        [InterpolatedStringHandler]public struct ProbeForwardHandler {
            public ProbeForwardHandler(int literalLength,int formattedCount,bool enabled,out bool append){InterpolationTrace.Log+="ctor:"+enabled+";";append=enabled;}
            public bool AppendLiteral(string text){InterpolationTrace.Log+="literal:"+text+";";return InterpolationTrace.Continue;}
            public bool AppendFormatted<T>(T value,int alignment=0,string? format=null){InterpolationTrace.Log+="value:"+(value is IFormattable f?f.ToString(format,CultureInfo.InvariantCulture):value?.ToString())+":"+alignment+";";return InterpolationTrace.Continue;}
        }
        [InterpolatedStringHandler]public struct ProbeGenericHandler<T> {
            public ProbeGenericHandler(int literalLength,int formattedCount){}
            public ProbeGenericHandler(int literalLength,int formattedCount,ProbeOwner<T> owner){}
            public void AppendLiteral(string text){}
            public void AppendFormatted(T value){}
        }
        [InterpolatedStringHandler]public struct ProbePackedHandler {
            public ProbePackedHandler(int literalLength,int formattedCount,params object?[] contexts){InterpolationTrace.LastContext=contexts[0];}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        [InterpolatedStringHandler]public struct ProbeSpanPackedHandler {
            public ProbeSpanPackedHandler(int literalLength,int formattedCount,params ReadOnlySpan<object?> contexts){InterpolationTrace.Log+="span-ctor;";InterpolationTrace.LastContext=contexts[0];}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        [InterpolatedStringHandler]public struct ProbeListPackedHandler {
            public ProbeListPackedHandler(int literalLength,int formattedCount,params List<object?> contexts){InterpolationTrace.Log+="list-ctor;";InterpolationTrace.LastContext=contexts[0];}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        [CollectionBuilder(typeof(ProbeContextPackBuilder),nameof(ProbeContextPackBuilder.Create))]
        public readonly struct ProbeContextPack<T>:IEnumerable<T> {
            private readonly T[] values;
            public ProbeContextPack(T[] values){this.values=values;}
            public T First=>values[0];
            public IEnumerator<T> GetEnumerator()=>((IEnumerable<T>)values).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        public static class ProbeContextPackBuilder {
            public static ProbeContextPack<T> Create<T>(ReadOnlySpan<T> values){InterpolationTrace.Log+="pack-builder;";return new(values.ToArray());}
        }
        [InterpolatedStringHandler]public struct ProbeBuilderPackedHandler<T> {
            public ProbeBuilderPackedHandler(int literalLength,int formattedCount,params ProbeContextPack<T> contexts){InterpolationTrace.Log+="pack-ctor;";InterpolationTrace.LastContext=contexts.First;}
            public void AppendLiteral(string text){}
            public void AppendFormatted<TValue>(TValue value){}
        }
        public sealed class ProbeOverloadedContexts:IEnumerable<object?> {
            private readonly List<object?> values=[];
            public ProbeOverloadedContexts(){InterpolationTrace.Log+="collection;";}
            public object? First=>values[0];
            public void Add(int value){InterpolationTrace.Log+="add-int;";values.Add(value);}
            public void Add(object? value){InterpolationTrace.Log+="add-object;";values.Add(value);}
            public IEnumerator<object?> GetEnumerator()=>values.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [InterpolatedStringHandler]public struct ProbeOverloadedPackedHandler {
            public ProbeOverloadedPackedHandler(int literalLength,int formattedCount,params ProbeOverloadedContexts contexts){InterpolationTrace.Log+="overload-ctor;";InterpolationTrace.LastContext=contexts.First;}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        [InterpolatedStringHandler]public struct ProbeThisHandler {
            public ProbeThisHandler(int literalLength,int formattedCount,object owner){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        [InterpolatedStringHandler]public struct ProbeContextHandler {
            public ProbeContextHandler(int literalLength,int formattedCount,object context){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        public sealed class ProbeOwner<T> {
            public void Write([InterpolatedStringHandlerArgument("")]ProbeGenericHandler<T> handler){}
        }
        public static class InterpolationApi {
            public static void Take(ProbeHandler handler){}
            public static void Special(ProbeSpecialHandler handler){}
            public static void Constrained(ProbeConstrainedHandler handler){}
            public static void Params(ProbeParamsHandler handler){}
            public static string Forward(bool enabled,[InterpolatedStringHandlerArgument("enabled")]ProbeForwardHandler handler){InterpolationTrace.Log+="call;";return InterpolationTrace.Log;}
            public static void Generic<T>(ProbeOwner<T> owner,[InterpolatedStringHandlerArgument("owner")]ProbeGenericHandler<T> handler){}
            public static void Context<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbeContextHandler handler){}
            public static void Packed<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbePackedHandler handler){}
            public static void SpanPacked<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbeSpanPackedHandler handler){}
            public static void ListPacked<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbeListPackedHandler handler){}
            public static void BuilderPacked<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbeBuilderPackedHandler<T> handler){}
            public static void OverloadedPacked<T>(T context,[InterpolatedStringHandlerArgument("context")]ProbeOverloadedPackedHandler handler){}
            public static void NamedOverloadedPacked<T>(bool flag,T context,[InterpolatedStringHandlerArgument("context")]ProbeOverloadedPackedHandler handler){}
            public static FormattableString Capture(FormattableString value)=>value;
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("FormattableValue", "static FormattableString M(int value)=>$\"v={value,4:D2}\";", "global::System.FormattableString M<T1>(T1 value)"),
            ("FormattableNullableTarget", "static FormattableString? M(int value)=>$\"{value}\";", "global::System.FormattableString? M<T1>(T1 value)"),
            ("FormattableInterface", "static IFormattable M(int value)=>$\"v={value}\";", "global::System.IFormattable M<T1>(T1 value)"),
            ("FormattableNullable", "static FormattableString M(string? value)=>$\"{value}\";", "global::System.FormattableString M<T1>(T1? value) where T1 : class"),
            ("FormattableNullableValue", "static FormattableString M(int? value)=>$\"{value}\";", "global::System.FormattableString M<T1>(T1? value) where T1 : struct"),
            ("FormattableExistingNullable", "static FormattableString M<T>(T? value,bool ignored)=>$\"{value}\";", "global::System.FormattableString M<T, T1>(T? value, T1 ignored)"),
            ("FormattableLocal", "static FormattableString M(int value){FormattableString captured=$\"{value}\";return captured;}", "global::System.FormattableString M<T1>(T1 value)"),
            ("FormattableStore", "static FormattableString M(int value){FormattableString captured=null!;captured=$\"{value}\";return captured;}", "global::System.FormattableString M<T1>(T1 value)"),
            ("FormattableParentheses", "static FormattableString M(int value)=>($\"{value}\");", "global::System.FormattableString M<T1>(T1 value)"),
            ("FormattableArgument", "static FormattableString M(int value)=>InterpolationApi.Capture($\"{value}\");", "global::System.FormattableString M<T1>(T1 value)"),
            ("FormattableLambda", "static Func<FormattableString> M(int value)=>()=>$\"{value}\";", "global::System.Func<global::System.FormattableString> M<T1>(T1 value)"),
            ("FormattableAsync", "static async Task<FormattableString> M(Task<int> pending)=>$\"{await pending}\";", "global::System.Threading.Tasks.Task<global::System.FormattableString> M<T1>(global::System.Threading.Tasks.Task<T1> pending)"),
            ("HandlerGenericAppend", "static void M(int value)=>InterpolationApi.Take($\"v={value,4:D2}\");", "void M<T1>(T1 value)"),
            ("HandlerReturn", "static ProbeHandler M(int value)=>$\"{value}\";", "global::ProbeHandler M<T1>(T1 value)"),
            ("HandlerGenericReturn", "static ProbeGenericHandler<int> M(int value)=>$\"{value}\";", "global::ProbeGenericHandler<T1> M<T1>(T1 value)"),
            ("HandlerLocal", "static void M(int value){ProbeHandler handler=$\"{value}\";InterpolationApi.Take(handler);}", "void M<T1>(T1 value)"),
            ("HandlerSpecializedAppend", "static void M(int value,bool ignored)=>InterpolationApi.Special($\"{value}\");", "void M<T1>(int value, T1 ignored)"),
            ("HandlerAppendConstraint", "static void M(int value)=>InterpolationApi.Constrained($\"{value}\");", "void M<T1>(T1 value) where T1 : global::System.IFormattable"),
            ("HandlerParamsAppendConstraint", "static void M(int value)=>InterpolationApi.Params($\"{value}\");", "void M<T1>(T1 value) where T1 : global::System.IFormattable"),
            ("HandlerForwardedBoolean", "static string M(bool enabled,int value)=>InterpolationApi.Forward(enabled,$\"before{value}after\");", "string M<T1>(bool enabled, T1 value)"),
            ("HandlerForwardedOwner", "static void M(ProbeOwner<int> owner,int value)=>InterpolationApi.Generic(owner,$\"{value}\");", "void M<T1>(global::ProbeOwner<T1> owner, T1 value)"),
            ("HandlerForwardedReceiver", "static void M(ProbeOwner<int> owner,int value)=>owner.Write($\"{value}\");", "void M<T1>(global::ProbeOwner<T1> owner, T1 value)"),
            ("HandlerImplicitReceiver", "void M(int value)=>Write($\"{value}\");void Write([InterpolatedStringHandlerArgument(\"\")]ProbeThisHandler handler){}", "void M<T1>(T1 value)"),
            ("HandlerForwardedBoxing", "static void M(int context,int value)=>InterpolationApi.Context(context,$\"{value}\");", "void M<T1, T2>(T1 context, T2 value) where T1 : notnull"),
            ("HandlerConstructorParams", "static void M(int context,int value)=>InterpolationApi.Packed(context,$\"{value}\");", "void M<T1, T2>(T1 context, T2 value)"),
            ("HandlerConstructorSpanParams", "static void M(int context,int value)=>InterpolationApi.SpanPacked(context,$\"{value}\");", "void M<T1, T2>(T1 context, T2 value)"),
            ("HandlerConstructorListParams", "static void M(int context,int value)=>InterpolationApi.ListPacked(context,$\"{value}\");", "void M<T1, T2>(T1 context, T2 value)"),
            ("HandlerConstructorBuilderParams", "static void M(int context,int value)=>InterpolationApi.BuilderPacked(context,$\"{value}\");", "void M<T1, T2>(T1 context, T2 value)"),
            ("HandlerConstructorOverloadedAdd", "static void M(int context,int value)=>InterpolationApi.OverloadedPacked(context,$\"{value}\");", "void M<T1>(int context, T1 value)"),
            ("HandlerConstructorNamedArguments", "static void M(bool flag,int context,int value)=>InterpolationApi.NamedOverloadedPacked(context:context,flag:flag,handler:$\"{value}\");", "void M<T1>(bool flag, int context, T1 value)"),
            ("HandlerConcatenation", "static void M(int left,int right)=>InterpolationApi.Take($\"a{left}\"+$\"b{right}\");", "void M<T1, T2>(T1 left, T2 right)"),
            ("HandlerNested", "static void M(int value)=>InterpolationApi.Take($\"outer{InterpolationApi.Forward(true,$\"inner{value}\")}\");", "void M<T1>(T1 value)")
        ];
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        var protocolTree = CSharpSyntaxTree.ParseText(Support + "class ProtocolUse{static void M(int value)=>InterpolationApi.Take($\"v={value}\");}", cancellationToken: cancellationToken);
        var protocolCompilation = Program.Compile([protocolTree]);
        var protocolRoot = await protocolTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var literalMethod = protocolRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "AppendLiteral" && method.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax { Identifier.ValueText: "ProbeHandler" }; });
        var protocolResult = Inference.Analyze(literalMethod, protocolCompilation.GetSemanticModel(protocolTree), cancellationToken);
        if (protocolResult.Signature != "void AppendLiteral<T1>(T1 text)")
        { throw new InvalidOperationException("Interpolation protocol root: " + protocolResult); }
        Program.Log.Info("PASS InterpolationProtocolMethodGeneralization");
        return tests.Length + 1 + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            public static class InterpolationProbe {
                public static FormattableString Capture(int value)=>$"value={value,6:D4}";
                public static FormattableString CaptureNullable(string? value)=>$"value={value}";
                public static async Task<FormattableString> CaptureAsync(Task<int> pending)=>$"awaited={await pending}";
                public static void Packed(int context,int value)=>InterpolationApi.Packed(context,$"{value}");
                public static void SpanPacked(int context,int value)=>InterpolationApi.SpanPacked(InterpolationTrace.Touch(context),$"{InterpolationTrace.Touch(value)}");
                public static void ListPacked(int context,int value)=>InterpolationApi.ListPacked(InterpolationTrace.Touch(context),$"{InterpolationTrace.Touch(value)}");
                public static void BuilderPacked(int context,int value)=>InterpolationApi.BuilderPacked(InterpolationTrace.Touch(context),$"{InterpolationTrace.Touch(value)}");
                public static void OverloadedPacked(int context,int value)=>InterpolationApi.OverloadedPacked(InterpolationTrace.Touch(context),$"{InterpolationTrace.Touch(value)}");
                public static void NamedOverloadedPacked(bool flag,int context,int value)=>InterpolationApi.NamedOverloadedPacked(context:InterpolationTrace.Touch(context),flag:flag,handler:$"{InterpolationTrace.Touch(value)}");
                public static string Render(int value,bool enabled)=>InterpolationApi.Forward(enabled,$"before{InterpolationTrace.Touch(value),4:D2}after");
            }
            public static class OriginalInterpolationRunner {
                public static string Run(){
                    var captured=InterpolationProbe.Capture(7);
                    if(captured.Format!="value={0,6:D4}"||!Equals(captured.GetArgument(0),7)||captured.ToString(CultureInfo.InvariantCulture)!="value=  0007")return "bad";
                    if(InterpolationProbe.CaptureNullable((string?)null).GetArgument(0)!=null)return "bad";
                    InterpolationTrace.Continue=true;InterpolationTrace.Log="";var full=InterpolationProbe.Render(7,true);
                    InterpolationTrace.Log="";var disabled=InterpolationProbe.Render(7,false);
                    InterpolationTrace.Continue=false;InterpolationTrace.Log="";var stopped=InterpolationProbe.Render(7,true);
                    if(full!="ctor:True;literal:before;hole;value:07:4;literal:after;call;"||disabled!="ctor:False;call;"||stopped!="ctor:True;literal:before;call;")return "bad";
                    InterpolationProbe.Packed(17,7);if(!Equals(InterpolationTrace.LastContext,17))return "bad";
                    InterpolationTrace.Log="";InterpolationProbe.SpanPacked(19,7);if(!Equals(InterpolationTrace.LastContext,19)||InterpolationTrace.Log!="hole;span-ctor;hole;")return "bad";
                    InterpolationTrace.Log="";InterpolationProbe.ListPacked(23,7);if(!Equals(InterpolationTrace.LastContext,23)||InterpolationTrace.Log!="hole;list-ctor;hole;")return "bad";
                    InterpolationTrace.Log="";InterpolationProbe.BuilderPacked(29,7);if(!Equals(InterpolationTrace.LastContext,29)||InterpolationTrace.Log!="hole;pack-builder;pack-ctor;hole;")return "bad";
                    var built=InterpolationTrace.Log;InterpolationTrace.Log="";InterpolationProbe.OverloadedPacked(31,7);
                    if(!Equals(InterpolationTrace.LastContext,31)||InterpolationTrace.Log!="hole;collection;add-int;overload-ctor;hole;")return "bad";
                    var overloaded=InterpolationTrace.Log;InterpolationTrace.Log="";InterpolationProbe.NamedOverloadedPacked(false,37,7);
                    if(!Equals(InterpolationTrace.LastContext,37)||InterpolationTrace.Log!="hole;collection;add-int;overload-ctor;hole;")return "bad";
                    return full+disabled+stopped+built+overloaded+InterpolationTrace.Log;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "InterpolationRuntime", "InterpolationRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Interpolation.cs", Support + source);
        var original = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var originalTrace = original.GetType("OriginalInterpolationRunner")!.GetMethod("Run")!.Invoke(null, null);
        if (Equals(originalTrace, "bad"))
        { throw new InvalidOperationException("Original interpolation behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 10; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Interpolation analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "InterpolationProbe" }; }) ?? throw new InvalidOperationException("Missing interpolation fix: " + string.Join<MethodResult>("; ", Program.Results((CSharpCompilation)compilation, cancellationToken)));
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewInterpolationDomains.cs", """
            using System;using System.Threading.Tasks;
            public sealed class InterpolationToken {public override string ToString()=>"token";}
            public static class NewInterpolationRunner {
                public static async Task<bool> Run(){
                    var value=new InterpolationToken();
                    var captured=InterpolationProbe.Capture(value);
                    if(captured.Format!="value={0,6:D4}"||!ReferenceEquals(captured.GetArgument(0),value))return false;
                    if(InterpolationProbe.CaptureNullable<InterpolationToken>(null).GetArgument(0)!=null)return false;
                    var pending=new TaskCompletionSource<InterpolationToken>();var awaited=InterpolationProbe.CaptureAsync(pending.Task);
                    if(awaited.IsCompleted)return false;
                    pending.SetResult(value);var result=await awaited;
                    if(result.Format!="awaited={0}"||!ReferenceEquals(result.GetArgument(0),value))return false;
                    InterpolationTrace.Continue=true;InterpolationTrace.Log="";
                    var rendered=InterpolationProbe.Render(value,true);
                    if(rendered!="ctor:True;literal:before;hole;value:"+value+":4;literal:after;call;")return false;
                    InterpolationTrace.LastContext=null;InterpolationProbe.Packed(value,value);
                    if(!ReferenceEquals(InterpolationTrace.LastContext,value))return false;
                    InterpolationTrace.LastContext=null;InterpolationTrace.Log="";InterpolationProbe.SpanPacked(value,value);
                    if(!ReferenceEquals(InterpolationTrace.LastContext,value)||InterpolationTrace.Log!="hole;span-ctor;hole;")return false;
                    InterpolationTrace.LastContext=null;InterpolationTrace.Log="";InterpolationProbe.ListPacked(value,value);
                    if(!ReferenceEquals(InterpolationTrace.LastContext,value)||InterpolationTrace.Log!="hole;list-ctor;hole;")return false;
                    InterpolationTrace.LastContext=null;InterpolationTrace.Log="";InterpolationProbe.BuilderPacked(value,value);
                    if(!ReferenceEquals(InterpolationTrace.LastContext,value)||InterpolationTrace.Log!="hole;pack-builder;pack-ctor;hole;")return false;
                    InterpolationTrace.LastContext=null;InterpolationTrace.Log="";InterpolationProbe.OverloadedPacked(31,value);
                    if(!Equals(InterpolationTrace.LastContext,31)||InterpolationTrace.Log!="hole;collection;add-int;overload-ctor;hole;")return false;
                    InterpolationTrace.LastContext=null;InterpolationTrace.Log="";InterpolationProbe.NamedOverloadedPacked(false,37,value);
                    return Equals(InterpolationTrace.LastContext,37)&&InterpolationTrace.Log=="hole;collection;add-int;overload-ctor;hole;";
                }
            }
            """);
        var updated = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var updatedTrace = updated.GetType("OriginalInterpolationRunner")!.GetMethod("Run")!.Invoke(null, null);
        var newDomains = (Task<bool>)updated.GetType("NewInterpolationRunner")!.GetMethod("Run")!.Invoke(null, null)!;
        if (!Equals(originalTrace, updatedTrace) || !await newDomains.ConfigureAwait(false))
        { throw new InvalidOperationException("Interpolation fixes changed formatting, boxing identity, handler short circuiting or async capture."); }
        Program.Log.Info("PASS InterpolationCodeFixFormattingIdentityShortCircuitAndSuspension");
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
