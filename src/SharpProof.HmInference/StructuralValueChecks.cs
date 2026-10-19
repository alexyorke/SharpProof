using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class StructuralValueChecks
{
    private const string Support = """
        #nullable enable
        using System;
        public record ShapeRec<T>(T Value);
        public record ShapeDerived<T>(T Value,string Label):ShapeRec<T>(Value);
        public record struct ShapeStruct<T>(T Value);
        public record ShapeFixed(int Value);
        public record ShapeWide(long Value);
        public struct ShapeToken {public int Value;}
        public static class ShapeTrace{public static string Log="";public static T Rhs<T>(T value){Log+="rhs;";return value;}}
        public record ShapeEffect<T> {
            private T value;
            public T Value {get=>value;init{ShapeTrace.Log+="set;";this.value=value;}}
            public ShapeEffect(T value){this.value=value;}
            protected ShapeEffect(ShapeEffect<T> other){ShapeTrace.Log+="clone;";value=other.value;}
        }
        public static class ShapeId {public static T Id<T>(T value)=>value;}
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("AnonymousRead", "static int M(int value){var item=new{Value=value};return item.Value;}", "T1 M<T1>(T1 value)"),
            ("AnonymousInferredName", "static int M(int value){var item=new{value};return item.value;}", "T1 M<T1>(T1 value)"),
            ("AnonymousNested", "static int M(int value){var item=new{Inner=new{Value=value}};return item.Inner.Value;}", "T1 M<T1>(T1 value)"),
            ("AnonymousGenericCall", "static int M(int value)=>ShapeId.Id(new{Value=value}).Value;", "T1 M<T1>(T1 value)"),
            ("AnonymousPattern", "static int M(int value){var item=new{Value=value};return item is {Value:var found}?found:value;}", "T1 M<T1>(T1 value)"),
            ("AnonymousWith", "static int M(int value,int replacement){var item=new{Value=value};return (item with{Value=replacement}).Value;}", "T1 M<T1>(T1 value, T1 replacement)"),
            ("AnonymousOpaqueReturn", "static object M(int value,bool ignored)=>new{Value=value};", "object M<T1>(int value, T1 ignored)"),
            ("AnonymousNullable", "static string? M(string? value){var item=new{Value=value};return item.Value;}", "T1? M<T1>(T1? value) where T1 : class"),
            ("RecordWith", "static ShapeRec<int> M(ShapeRec<int> value,int replacement)=>value with{Value=replacement};", "global::ShapeRec<T1> M<T1>(global::ShapeRec<T1> value, T1 replacement)"),
            ("RecordStructWith", "static ShapeStruct<int> M(ShapeStruct<int> value,int replacement)=>value with{Value=replacement};", "global::ShapeStruct<T1> M<T1>(global::ShapeStruct<T1> value, T1 replacement)"),
            ("RecordDerivedWith", "static ShapeDerived<int> M(ShapeDerived<int> value,int replacement)=>value with{Value=replacement};", "global::ShapeDerived<T1> M<T1>(global::ShapeDerived<T1> value, T1 replacement)"),
            ("RecordEmptyWith", "static ShapeRec<int> M(ShapeRec<int> value)=>value with{};", "global::ShapeRec<T1> M<T1>(global::ShapeRec<T1> value)"),
            ("StructEmptyWith", "static ShapeToken M(ShapeToken value)=>value with{};", "T1 M<T1>(T1 value) where T1 : struct"),
            ("FixedRecordWith", "static ShapeFixed M(ShapeFixed value,int replacement,bool ignored)=>value with{Value=replacement};", "global::ShapeFixed M<T1>(global::ShapeFixed value, int replacement, T1 ignored)"),
            ("WithNumericConversion", "static ShapeWide M(ShapeWide value,int replacement,bool ignored)=>value with{Value=replacement};", "global::ShapeWide M<T1>(global::ShapeWide value, int replacement, T1 ignored)"),
            ("WithSetterEffect", "static ShapeEffect<int> M(ShapeEffect<int> value,int replacement)=>value with{Value=ShapeTrace.Rhs(replacement)};", "global::ShapeEffect<T1> M<T1>(global::ShapeEffect<T1> value, T1 replacement)")
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
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            public static class StructuralProbe {
                public static int Anonymous(int value){var item=new{Inner=new{Value=value}};return item.Inner.Value;}
                public static int ReplaceAnonymous(int value,int replacement){var item=new{Value=value};return (item with{Value=replacement}).Value;}
                public static ShapeRec<int> Clone(ShapeRec<int> value,int replacement)=>value with{Value=replacement};
                public static ShapeStruct<int> Copy(ShapeStruct<int> value,int replacement)=>value with{Value=replacement};
                public static ShapeEffect<int> Effects(ShapeEffect<int> value,int replacement)=>value with{Value=ShapeTrace.Rhs(replacement)};
            }
            public static class OriginalStructuralRunner {
                public static bool Run(){
                    if(StructuralProbe.Anonymous(42)!=42||StructuralProbe.ReplaceAnonymous(1,42)!=42)return false;
                    var original=new ShapeDerived<int>(1,"child");var clone=StructuralProbe.Clone(original,42);
                    if(ReferenceEquals(original,clone)||original.Value!=1||clone is not ShapeDerived<int>{Value:42,Label:"child"})return false;
                    var item=new ShapeStruct<int>(1);var copy=StructuralProbe.Copy(item,42);if(item.Value!=1||copy.Value!=42)return false;
                    var effect=new ShapeEffect<int>(1);ShapeTrace.Log="";var changed=StructuralProbe.Effects(effect,42);
                    return effect.Value==1&&changed.Value==42&&!ReferenceEquals(effect,changed)&&ShapeTrace.Log=="clone;rhs;set;";
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "StructuralValueRuntime", "StructuralValueRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("StructuralValues.cs", Support + source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalStructuralRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original structural behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 5; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Structural-value analyzer crashed."); }
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "StructuralProbe" }; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewStructuralDomains.cs", """
            using System;
            public static class NewStructuralRunner {
                public static bool Run(){
                    var first=new Uri("https://example.invalid/first");var second=new Uri("https://example.invalid/second");
                    if(!ReferenceEquals(StructuralProbe.Anonymous(first),first)||!ReferenceEquals(StructuralProbe.ReplaceAnonymous(first,second),second))return false;
                    var original=new ShapeDerived<Uri>(first,"child");var clone=StructuralProbe.Clone(original,second);
                    if(ReferenceEquals(original,clone)||!ReferenceEquals(original.Value,first)||clone is not ShapeDerived<Uri>{Label:"child"}||!ReferenceEquals(clone.Value,second))return false;
                    var item=new ShapeStruct<Uri>(first);var copy=StructuralProbe.Copy(item,second);if(!ReferenceEquals(item.Value,first)||!ReferenceEquals(copy.Value,second))return false;
                    var effect=new ShapeEffect<Uri>(first);ShapeTrace.Log="";var changed=StructuralProbe.Effects(effect,second);
                    return ReferenceEquals(effect.Value,first)&&ReferenceEquals(changed.Value,second)&&!ReferenceEquals(effect,changed)&&ShapeTrace.Log=="clone;rhs;set;";
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (after.GetType("OriginalStructuralRunner")!.GetMethod("Run")!.Invoke(null, null) is not true || after.GetType("NewStructuralRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Applied structural fixes changed cloning, initializer effects or identity."); }
        Program.Log.Info("PASS StructuralValueCodeFixCloneEffectsAndNewDomains");
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
