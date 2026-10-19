using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class TypeShapeChecks
{
    private const string Support = """
        #nullable enable
            using System;
            using System.Threading.Tasks;
            using System.Runtime.CompilerServices;
        public class ShapeBase<A,B> {public A ReadLeft()=>default!;public B ReadRight()=>default!;}
        public class ShapeOuter<A> {
            public class Inner<B>:ShapeBase<A,B> {
                public A Left=default!;public B Right=default!;
                public A GetLeft()=>Left;public B GetRight()=>Right;
                public void Deconstruct(out A left,out B right){left=Left;right=Right;}
            }
            public class Plain {public A Value=default!;public A Read()=>Value;}
            public class Middle<B> {public class Deep<C> {public (A,B,C) Read()=>default!;}}
            public delegate B Map<B>(A value);
        }
        public interface ShapeVariant<out A>{public interface Inner<out B>{}}
        public static class ShapeVariantApi{public static object Read(ShapeVariant<object>.Inner<object> value)=>value;}
        public class ShapeContainer {public class Item<T>{}}
        public class ShapeConstrained<A> where A:class {public class Inner<B> where B:A {}}
        public class ShapeAwait<T> {
            public Task<T> Pending=Task.FromResult(default(T)!);
            public Awaiter GetAwaiter()=>new(Pending.GetAwaiter());
            public struct Awaiter:ICriticalNotifyCompletion {
                private TaskAwaiter<T> inner;
                public Awaiter(TaskAwaiter<T> inner){this.inner=inner;}
                public bool IsCompleted=>inner.IsCompleted;
                public T GetResult()=>inner.GetResult();
                public void OnCompleted(Action continuation)=>inner.OnCompleted(continuation);
                public void UnsafeOnCompleted(Action continuation)=>inner.UnsafeOnCompleted(continuation);
            }
        }
        public static class ShapeIndexTrace {public static int Count;}
        public readonly struct ShapeIntegerKey {public static implicit operator int(ShapeIntegerKey value){ShapeIndexTrace.Count++;return 1;}}
        public readonly struct ShapeRangeKey {public static implicit operator Range(ShapeRangeKey value){ShapeIndexTrace.Count++;return 1..;}}
        namespace System {public class ShapeIndexOwner {public readonly struct Range {public static implicit operator int(Range value)=>1;}}}
        namespace System.Threading.Tasks {public class ShapeNamedTaskOwner {public class Task<T> {public TaskAwaiter<T> GetAwaiter()=>global::System.Threading.Tasks.Task.FromResult(default(T)!).GetAwaiter();}}}
        public ref struct ShapeToken {public int Number;}
        public ref struct ShapeSlot<T> where T:allows ref struct {public T Value;}
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("NestedAwaiterInference", "static async Task<int> M(ShapeAwait<int> value)=>await value;", "global::System.Threading.Tasks.Task<T1> M<T1>(global::ShapeAwait<T1> value)"),
            ("NestedOwnerVariance", "static object M(ShapeVariant<string>.Inner<string> value)=>ShapeVariantApi.Read(value);", "object M<T1, T2>(global::ShapeVariant<T1>.Inner<T2> value) where T1 : class where T2 : class"),
            ("NestedWellKnownTaskName", "static async Task<int> M(System.Threading.Tasks.ShapeNamedTaskOwner.Task<int> value)=>await value;", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.ShapeNamedTaskOwner.Task<T1> value)"),
            ("ByteArrayIndexConversion", "static int M(int[] values,byte index)=>values[index];", "T1 M<T1>(T1[] values, byte index)"),
            ("UserArrayIndexConversion", "static int M(int[] values,ShapeIntegerKey index)=>values[index];", "T1 M<T1>(T1[] values, global::ShapeIntegerKey index)"),
            ("UserArrayRangeConversion", "static int[] M(int[] values,ShapeRangeKey index)=>values[index];", "T1[] M<T1>(T1[] values, global::ShapeRangeKey index)"),
            ("NestedRangeNameIsNotIntrinsic", "static int M(int[] values,System.ShapeIndexOwner.Range index)=>values[index];", "T1 M<T1>(T1[] values, global::System.ShapeIndexOwner.Range index)"),
            ("NestedGenericIdentity","static ShapeOuter<int>.Inner<string> M(ShapeOuter<int>.Inner<string> value)=>value;","global::ShapeOuter<T1>.Inner<T2> M<T1, T2>(global::ShapeOuter<T1>.Inner<T2> value)"),
            ("NestedOuterArguments","static ShapeOuter<int>.Plain M(ShapeOuter<int>.Plain value)=>value;","global::ShapeOuter<T1>.Plain M<T1>(global::ShapeOuter<T1>.Plain value)"),
            ("NestedStaticOwner","static ShapeContainer.Item<int> M(ShapeContainer.Item<int> value)=>value;","global::ShapeContainer.Item<T1> M<T1>(global::ShapeContainer.Item<T1> value)"),
            ("DeepNestedIdentity","static ShapeOuter<int>.Middle<string>.Deep<bool> M(ShapeOuter<int>.Middle<string>.Deep<bool> value)=>value;","global::ShapeOuter<T1>.Middle<T2>.Deep<T3> M<T1, T2, T3>(global::ShapeOuter<T1>.Middle<T2>.Deep<T3> value)"),
            ("NestedOuterMethod","static int M(ShapeOuter<int>.Plain value)=>value.Read();","T1 M<T1>(global::ShapeOuter<T1>.Plain value)"),
            ("NestedInheritedMethod","static int M(ShapeOuter<int>.Inner<string> value)=>value.ReadLeft();","T1 M<T1, T2>(global::ShapeOuter<T1>.Inner<T2> value)"),
            ("NestedFields","static (int,string) M(ShapeOuter<int>.Inner<string> value)=>(value.Left,value.Right);","(T1, T2) M<T1, T2>(global::ShapeOuter<T1>.Inner<T2> value)"),
            ("NestedDelegate","static string M(ShapeOuter<int>.Map<string> map,int value)=>map(value);","T2 M<T1, T2>(global::ShapeOuter<T1>.Map<T2> map, T1 value)"),
            ("NestedDeconstruction","static int M(ShapeOuter<int>.Inner<string> value){var(left,right)=value;return left;}","T1 M<T1, T2>(global::ShapeOuter<T1>.Inner<T2> value)"),
            ("NestedOwnerConstraints","static ShapeConstrained<object>.Inner<string> M(ShapeConstrained<object>.Inner<string> value)=>value;","global::ShapeConstrained<T1>.Inner<T2> M<T1, T2>(global::ShapeConstrained<T1>.Inner<T2> value) where T1 : class where T2 : T1"),
            ("NestedNullableTupleNames","static ShapeOuter<(int first,bool flag)>.Inner<string?> M(ShapeOuter<(int first,bool flag)>.Inner<string?> value)=>value;","global::ShapeOuter<(T1 first, T2 flag)>.Inner<T3?> M<T1, T2, T3>(global::ShapeOuter<(T1 first, T2 flag)>.Inner<T3?> value) where T3 : class"),
            ("NestedRefLikeEnumeratorIdentity", "static Span<int>.Enumerator M(Span<int>.Enumerator value)=>value;", "global::System.Span<T1>.Enumerator M<T1>(global::System.Span<T1>.Enumerator value)"),
            ("NestedRefLikeEnumeratorReturn", "static Span<int>.Enumerator M(Span<int> value)=>value.GetEnumerator();", "global::System.Span<T1>.Enumerator M<T1>(global::System.Span<T1> value)"),
            ("SpanListPattern", "static int M(ReadOnlySpan<int> values,int fallback)=>values is [var first,..]?first:fallback;", "T1 M<T1>(global::System.ReadOnlySpan<T1> values, T1 fallback)"),
            ("SpanIdentity","static Span<int> M(Span<int> value)=>value;","global::System.Span<T1> M<T1>(global::System.Span<T1> value)"),
            ("ReadOnlySpanIndexer","static int M(ReadOnlySpan<int> values,int index)=>values[index];","T1 M<T1>(global::System.ReadOnlySpan<T1> values, int index)"),
            ("SpanStore","static void M(Span<int> values,int index,int replacement)=>values[index]=replacement;","void M<T1>(global::System.Span<T1> values, int index, T1 replacement)"),
            ("SpanSlice","static Span<int> M(Span<int> values,int start,int end)=>values[start..end];","global::System.Span<T1> M<T1>(global::System.Span<T1> values, int start, int end)"),
            ("SpanFromEnd","static int M(ReadOnlySpan<int> values,int fromEnd)=>values[^fromEnd];","T1 M<T1>(global::System.ReadOnlySpan<T1> values, int fromEnd)"),
            ("ArrayRange","static int[] M(int[] values,int start)=>values[start..];","T1[] M<T1>(T1[] values, int start)"),
            ("ArrayFromEnd","static int M(int[] values,int fromEnd)=>values[^fromEnd];","T1 M<T1>(T1[] values, int fromEnd)"),
            ("RefReturn","static ref int M(Span<int> values,int index)=>ref values[index];","ref T1 M<T1>(global::System.Span<T1> values, int index)"),
            ("ReadOnlyRefReturn","static ref readonly int M(ReadOnlySpan<int> values,int index)=>ref values[index];","ref readonly T1 M<T1>(global::System.ReadOnlySpan<T1> values, int index)"),
            ("RefForeachStore","static void M(Span<int> values,int replacement){foreach(ref var item in values)item=replacement;}","void M<T1>(global::System.Span<T1> values, T1 replacement)"),
            ("ReadOnlyRefForeach","static int M(ReadOnlySpan<int> values,int seed){foreach(ref readonly var item in values)seed=item;return seed;}","T1 M<T1>(global::System.ReadOnlySpan<T1> values, T1 seed)"),
            ("ArraySpanRefForeach","static void M(int[] values,int replacement){foreach(ref var item in values.AsSpan())item=replacement;}","void M<T1>(T1[] values, T1 replacement)"),
            ("RefLikeParameterPermission","static ShapeToken M(ShapeToken value)=>value;","T1 M<T1>(T1 value) where T1 : allows ref struct"),
            ("RefLikeTypeArgumentPermission","static ShapeSlot<ShapeToken> M(ShapeSlot<ShapeToken> value)=>value;","global::ShapeSlot<T1> M<T1>(global::ShapeSlot<T1> value) where T1 : allows ref struct")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "\nclass Cases {" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var legacy = Program.Compile([CSharpSyntaxTree.ParseText("ref struct Token{} class Legacy {static Token M(Token value,bool ignored)=>value;}", new CSharpParseOptions(LanguageVersion.CSharp12), cancellationToken: cancellationToken)]);
        var legacyResult = Program.Results(legacy, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
        if (legacyResult.Signature != "global::Token M<T1>(global::Token value, T1 ignored)")
        { throw new InvalidOperationException("LegacyRefLikePermission: " + legacyResult.Status + ": " + (legacyResult.Signature ?? legacyResult.Message)); }
        Program.Log.Info("PASS LegacyRefLikePermission");
        return tests.Length + 1 + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        var source = Support + """
            public static class TypeShapeProbe {
                public static async Task<int> AwaitNested(ShapeAwait<int> value)=>await value;
                public static ShapeOuter<int>.Inner<string> NestedIdentity(ShapeOuter<int>.Inner<string> value)=>value;
                public static int NestedRead(ShapeOuter<int>.Inner<string> value)=>value.GetLeft();
                public static int ListRead(System.Collections.Generic.List<int> values,int index)=>values[index];
                public static int ListStore(System.Collections.Generic.List<int> values,int index,int replacement){values[index]=replacement;return values[index];}
                public static int SpanRead(ReadOnlySpan<int> values,int index)=>values[index];
                public static void SpanWrite(Span<int> values,int index,int replacement)=>values[index]=replacement;
                public static Span<int> Slice(Span<int> values,int start,int end)=>values[start..end];
                public static ref int Borrow(Span<int> values,int index)=>ref values[index];
                public static ref readonly int BorrowReadOnly(ReadOnlySpan<int> values,int index)=>ref values[index];
                public static void RefFill(Span<int> values,int replacement){foreach(ref var item in values)item=replacement;}
                public static int RefRead(ReadOnlySpan<int> values,int seed){foreach(ref readonly var item in values)seed=item;return seed;}
                public static int[] ArrayTail(int[] values,int start)=>values[start..];
                public static int ArrayIndex(int[] values,int fromEnd)=>values[^fromEnd];
                public static int ConvertedIndex(int[] values,ShapeIntegerKey index)=>values[index];
                public static int[] ConvertedRange(int[] values,ShapeRangeKey index)=>values[index];
                public static ShapeToken TokenIdentity(ShapeToken value)=>value;
            }
            public static class OriginalShapeRunner {
                public static bool Run(){
                    if(TypeShapeProbe.AwaitNested(new ShapeAwait<int>{Pending=Task.FromResult(42)}).GetAwaiter().GetResult()!=42)return false;
                    var nested=new ShapeOuter<int>.Inner<string>{Left=42,Right="text"};
                    if(!ReferenceEquals(TypeShapeProbe.NestedIdentity(nested),nested)||TypeShapeProbe.NestedRead(nested)!=42)return false;
                    var list=new System.Collections.Generic.List<int>{1,2};if(TypeShapeProbe.ListRead(list,1)!=2||TypeShapeProbe.ListStore(list,0,42)!=42||list[0]!=42)return false;
                    int[] values=[1,2,3];Span<int> span=values;ReadOnlySpan<int> read=values;
                    if(TypeShapeProbe.SpanRead(read,1)!=2)return false;
                    TypeShapeProbe.SpanWrite(span,1,10);if(values[1]!=10)return false;
                    ref var alias=ref TypeShapeProbe.Borrow(span,0);alias=7;if(values[0]!=7)return false;
                    ref readonly var immutable=ref TypeShapeProbe.BorrowReadOnly(read,1);if(immutable!=10)return false;
                    var slice=TypeShapeProbe.Slice(span,1,3);if(slice.Length!=2||slice[0]!=10)return false;
                    slice[0]=11;if(values[1]!=11)return false;
                    TypeShapeProbe.RefFill(span,9);if(values[0]!=9||values[1]!=9||TypeShapeProbe.RefRead(read,-1)!=9)return false;
                    var copy=TypeShapeProbe.ArrayTail(values,1);copy[0]=100;if(values[1]!=9||TypeShapeProbe.ArrayIndex(values,1)!=9)return false;
                    ShapeIndexTrace.Count=0;if(TypeShapeProbe.ConvertedIndex(values,new ShapeIntegerKey())!=9||TypeShapeProbe.ConvertedRange(values,new ShapeRangeKey()).Length!=2||ShapeIndexTrace.Count!=2)return false;
                    return TypeShapeProbe.TokenIdentity(new ShapeToken{Number=123}).Number==123;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "TypeShapeRuntime", "TypeShapeRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("TypeShapeRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalShapeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original shape behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 20; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Type-shape analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "TypeShapeProbe" }; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 17)
        { throw new InvalidOperationException("Expected seventeen shape fixes; got " + fixes); }
        document = document.Project.AddDocument("NewShapeDomains.cs", """
            using System;
            using System.Threading.Tasks;
            using System.Runtime.CompilerServices;
            public static class NewShapeRunner {
                public static async Task<bool> RunAwait(){var sentinel=new Uri("https://example.invalid/nested-await");var completion=new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);var task=TypeShapeProbe.AwaitNested(new ShapeAwait<Uri>{Pending=completion.Task});if(task.IsCompleted)return false;completion.SetResult(sentinel);return ReferenceEquals(await task,sentinel);}
                public static bool Run(){
                    var id=Guid.NewGuid();var other=Guid.NewGuid();var sentinel=new Uri("https://example.invalid/shape");
                    var nested=new ShapeOuter<Guid>.Inner<Uri>{Left=id,Right=sentinel};
                    if(!ReferenceEquals(TypeShapeProbe.NestedIdentity(nested),nested)||TypeShapeProbe.NestedRead(nested)!=id)return false;
                    var list=new System.Collections.Generic.List<Uri>{sentinel};if(!ReferenceEquals(TypeShapeProbe.ListRead(list,0),sentinel)||!ReferenceEquals(TypeShapeProbe.ListStore(list,0,sentinel),sentinel))return false;
                    Guid[] values=[id,other];Span<Guid> span=values;ReadOnlySpan<Guid> read=values;
                    if(TypeShapeProbe.SpanRead(read,0)!=id)return false;
                    TypeShapeProbe.SpanWrite(span,1,id);if(values[1]!=id)return false;
                    ref var alias=ref TypeShapeProbe.Borrow(span,0);alias=other;if(values[0]!=other)return false;
                    ref readonly var immutable=ref TypeShapeProbe.BorrowReadOnly(read,0);if(immutable!=other)return false;
                    TypeShapeProbe.RefFill(span,id);if(TypeShapeProbe.RefRead(read,other)!=id)return false;
                    var slice=TypeShapeProbe.Slice(span,0,1);slice[0]=other;if(values[0]!=other)return false;
                    var copy=TypeShapeProbe.ArrayTail(values,0);copy[0]=id;if(values[0]!=other||TypeShapeProbe.ArrayIndex(values,1)!=id)return false;
                    ShapeIndexTrace.Count=0;if(TypeShapeProbe.ConvertedIndex(values,new ShapeIntegerKey())!=id||TypeShapeProbe.ConvertedRange(values,new ShapeRangeKey()).Length!=1||ShapeIndexTrace.Count!=2)return false;
                    var echoed=TypeShapeProbe.TokenIdentity(span);echoed[0]=id;if(values[0]!=id)return false;
                    return ReferenceEquals(TypeShapeProbe.TokenIdentity(sentinel),sentinel);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (after.GetType("OriginalShapeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true || after.GetType("NewShapeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Fixed nested/span/ref behavior failed."); }
        var nestedAwait = (Task<bool>)after.GetType("NewShapeRunner")!.GetMethod("RunAwait")!.Invoke(null, null)!;
        if (!await nestedAwait.ConfigureAwait(false))
        { throw new InvalidOperationException("Nested awaiter suspension lost values."); }
        Program.Log.Info("PASS TypeShapeCodeFixRuntime");
        Program.Log.Info("PASS RefAliasingSlicesAndGeneralizedRefStructDomains");
        return 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var problems = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
