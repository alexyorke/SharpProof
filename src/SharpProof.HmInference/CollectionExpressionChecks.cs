using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CollectionExpressionChecks
{
    private const string Support = """
        #nullable enable
        using System;using System.Collections;using System.Collections.Generic;using System.Runtime.CompilerServices;
        public static class CollectionApi {
            public static T First<T>(params List<T> values)=>values[0];
            public static T SpanFirst<T>(params ReadOnlySpan<T> values)=>values[0];
            public static int Count<T>(params List<T> values)=>values.Count;
        }
        public class OverloadedBag<T>:IEnumerable<T> {
            private readonly List<T> items=[];
            public void Add(T value)=>items.Add(value);
            public void Add(int value)=>items.Add((T)(object)value);
            public IEnumerator<T> GetEnumerator()=>items.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [CollectionBuilder(typeof(BagBuilder),nameof(BagBuilder.Create))]
        public class BuiltBag<T>:IEnumerable<T> {
            private readonly T[] items;public BuiltBag(T[] items){this.items=items;}
            public IEnumerator<T> GetEnumerator()=>((IEnumerable<T>)items).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [InterpolatedStringHandler]public struct CollectionValueHandler {
            public CollectionValueHandler(int literalLength,int formattedCount){}
            public void AppendLiteral(string text){}
            public void AppendFormatted<T>(T value){}
        }
        public static class CollectionTrace {public static string Log="";public static T Item<T>(T value){Log+="item;";return value;}}
        public class TraceEnumerable<T>(T value):IEnumerable<T> {
            public IEnumerator<T> GetEnumerator(){CollectionTrace.Log+="get;";try{CollectionTrace.Log+="next;";yield return value;}finally{CollectionTrace.Log+="dispose;";}}
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        public static class BagBuilder {public static BuiltBag<T> Create<T>(ReadOnlySpan<T> items)=>new(items.ToArray());}
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        var constrainedBuilder = Support.Replace("Create<T>(ReadOnlySpan<T> items)=>", "Create<T>(ReadOnlySpan<T> items) where T:struct=>", StringComparison.Ordinal);
        (string Name, string Source, string Signature)[] tests =
        [
            ("ParamsListExpanded", "static int M(int value)=>CollectionApi.First(value,value);", "T1 M<T1>(T1 value)"),
            ("ParamsSpanExpanded", "static int M(int value)=>CollectionApi.SpanFirst(value,value);", "T1 M<T1>(T1 value)"),
            ("ParamsCollectionEmpty", "static int M(bool ignored)=>CollectionApi.Count<int>();", "int M<T1>(T1 ignored)"),
            ("ParamsCollectionExplicit", "static int M(List<int> values)=>CollectionApi.First(values);", "T1 M<T1>(global::System.Collections.Generic.List<T1> values)"),
            ("CollectionArray", "static int[] M(int value)=>[value];", "T1[] M<T1>(T1 value)"),
            ("CollectionList", "static List<int> M(int value)=>[value,value];", "global::System.Collections.Generic.List<T1> M<T1>(T1 value)"),
            ("CollectionInterface", "static IEnumerable<int> M(int value)=>[value];", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 value)"),
            ("CollectionSpread", "static List<int> M(int[] values,int value)=>[..values,value];", "global::System.Collections.Generic.List<T1> M<T1>(T1[] values, T1 value)"),
            ("CollectionSpreadConversion", "static long[] M(int[] values,bool ignored)=>[..values];", "long[] M<T1>(int[] values, T1 ignored)"),
            ("CollectionNumericElement", "static long[] M(int value,bool ignored)=>[value];", "long[] M<T1>(int value, T1 ignored)"),
            ("CollectionBuilder", "static BuiltBag<int> M(int value)=>[value];", "global::BuiltBag<T1> M<T1>(T1 value)"),
            ("CollectionBuilderSpread", "static BuiltBag<int> M(int[] values)=>[..values];", "global::BuiltBag<T1> M<T1>(T1[] values)"),
            ("CollectionAddOverload", "static OverloadedBag<int> M(int value,bool ignored)=>[value];", "global::OverloadedBag<int> M<T1>(int value, T1 ignored)"),
            ("CollectionBoxing", "static List<object?> M(int value)=>[value];", "global::System.Collections.Generic.List<object?> M<T1>(T1 value)"),
            ("CollectionListNumericElement", "static List<long> M(int value,bool ignored)=>[value];", "global::System.Collections.Generic.List<long> M<T1>(int value, T1 ignored)"),
            ("CollectionFormattableElement", "static List<FormattableString> M(int value)=>[$\"{value}\"];", "global::System.Collections.Generic.List<global::System.FormattableString> M<T1>(T1 value)"),
            ("CollectionHandlerElement", "static List<CollectionValueHandler> M(int value)=>[$\"{value}\"];", "global::System.Collections.Generic.List<global::CollectionValueHandler> M<T1>(T1 value)"),
            ("CollectionNullable", "static List<string?> M(string? value)=>[value];", "global::System.Collections.Generic.List<T1?> M<T1>(T1? value) where T1 : class"),
            ("CollectionBuilderConstraint", "static BuiltBag<int> M(int value)=>[value];", "global::BuiltBag<T1> M<T1>(T1 value) where T1 : struct"),
            ("CollectionTypedLocal", "static int M(int value,bool ignored){List<int> values=[value];return values[0];}", "int M<T1>(int value, T1 ignored)")
        ];
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText((test.Name == "CollectionBuilderConstraint" ? constrainedBuilder : Support) + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            public static class CollectionProbe {
                public static List<int> Effects(TraceEnumerable<int> values,int last)=>[..values,CollectionTrace.Item(last)];
                public static int Expanded(int value)=>CollectionApi.First(value,value);
                public static int SpanExpanded(int value)=>CollectionApi.SpanFirst(value,value);
                public static int[] Array(int value)=>[value];
                public static List<int> Spread(int[] values,int last)=>[..values,last];
                public static BuiltBag<int> Build(int[] values)=>[..values];
            }
            public static class OriginalCollectionRunner {
                public static string Trace(){CollectionTrace.Log="";var values=CollectionProbe.Effects(new TraceEnumerable<int>(1),2);if(values.Count!=2||values[0]!=1||values[1]!=2)return "bad";return CollectionTrace.Log;}
                public static bool Run(){
                    if(CollectionProbe.Expanded(42)!=42||CollectionProbe.SpanExpanded(42)!=42||CollectionProbe.Array(42)[0]!=42)return false;
                    int[] input=[1,2];var spread=CollectionProbe.Spread(input,3);if(spread.Count!=3||spread[0]!=1||spread[2]!=3)return false;
                    input[0]=99;if(spread[0]!=1)return false;
                    using var e=CollectionProbe.Build([42]).GetEnumerator();return e.MoveNext()&&e.Current==42;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "CollectionRuntime", "CollectionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Collections.cs", Support + source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalCollectionRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original collection behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 6; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Collection analyzer crashed."); }
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "CollectionProbe" }; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewCollectionDomains.cs", """
            using System;
            public static class NewCollectionRunner {
                public static bool Run(){
                    var first=new Uri("https://example.invalid/first");var last=new Uri("https://example.invalid/last");
                    if(!ReferenceEquals(CollectionProbe.Expanded(first),first)||!ReferenceEquals(CollectionProbe.SpanExpanded(first),first)||!ReferenceEquals(CollectionProbe.Array(first)[0],first))return false;
                    Uri[] input=[first];var spread=CollectionProbe.Spread(input,last);if(!ReferenceEquals(spread[0],first)||!ReferenceEquals(spread[1],last))return false;
                    input[0]=last;if(!ReferenceEquals(spread[0],first))return false;
                    using var e=CollectionProbe.Build<Uri>([first]).GetEnumerator();return e.MoveNext()&&ReferenceEquals(e.Current,first);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var originalTrace = before.GetType("OriginalCollectionRunner")!.GetMethod("Trace")!.Invoke(null, null);
        var changedTrace = after.GetType("OriginalCollectionRunner")!.GetMethod("Trace")!.Invoke(null, null);
        if (Equals(originalTrace, "bad") || !Equals(originalTrace, changedTrace))
        { throw new InvalidOperationException("Spread enumeration/disposal/element evaluation order changed."); }
        if (after.GetType("OriginalCollectionRunner")!.GetMethod("Run")!.Invoke(null, null) is not true || after.GetType("NewCollectionRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Applied collection fixes changed values, copy behavior or reference identity."); }
        Program.Log.Info("PASS CollectionCodeFixParamsSpreadsBuildersAndNewDomains");
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
