using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class IndirectStorageChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class IndirectBox<T>{
                public T Value;public T Other;public int Gets;
                public IndirectBox(T value,T other){Value=value;Other=other;}
                public ref T Slot{get{Gets++;return ref Value;}}
            }
            public sealed class IndirectPair<T,U>{
                public T Value;public U Other;
                public IndirectPair(T value,U other){Value=value;Other=other;}
            }
            public sealed class IndirectSource<T>{
                [return:MaybeNull]public T Read(T value)=>default;
                [MaybeNull]public T Property=>default;
                [MaybeNull]public T Field=default!;
            }
            [return:MaybeNull]public delegate T IndirectReader<T>(T value);
            public static class IndirectProbe{
                [return:MaybeNull]public static T Maybe<T>(T value)=>default;
                public static T? Optional<T>(T value)=>default;
                public static int Field(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                public static int Chain(IndirectBox<int> box,int fill){ref var first=ref box.Value;ref var second=ref first;second=Maybe(fill);return box.Value;}
                public static int Element(int[] values,int fill){ref var alias=ref values[0];alias=Maybe(fill);return values[0];}
                public static int Property(IndirectBox<int> box,int fill){ref var alias=ref box.Slot;alias=Maybe(fill);return box.Value;}
                public static int Owner(IndirectBox<int> box,IndirectSource<int> source,int fill){ref var alias=ref box.Value;alias=source.Read(fill);return box.Value;}
                public static int Delegate(IndirectBox<int> box,IndirectReader<int> read,int fill){ref var alias=ref box.Value;alias=read(fill);return box.Value;}
                public static int SourceProperty(IndirectBox<int> box,IndirectSource<int> source){ref var alias=ref box.Value;alias=source.Property;return box.Value;}
                public static int SourceField(IndirectBox<int> box,IndirectSource<int> source){ref var alias=ref box.Value;alias=source.Field;return box.Value;}
                public static int OptionalStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Optional(fill);return box.Value;}
                public static int Copy(IndirectBox<int> box,int fill){var copy=box.Value;ref var alias=ref box.Value;alias=Maybe(fill);return copy;}
                public static int Sibling(IndirectPair<int,int> box,int fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Other;}
                public static int? Physical(IndirectBox<int?> box,int? fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                public static async Task<int> Async(Task<int> fill,IndirectBox<int> box){var value=await fill;ref var alias=ref box.Value;alias=Maybe(value);return box.Value;}
                public static int Typed(IndirectBox<int> box,int fill){ref int alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                [return:MaybeNull][return:NotNullIfNotNull(nameof(value))]
                public static T Guarded<T>(T value)=>value;
                public static int GuardedStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Guarded(fill);return box.Value;}
            }
            public static class OriginalIndirectRunner{
                public static async Task<bool> Run(CancellationToken token){
                    var field=new IndirectBox<int>(7,11);if(IndirectProbe.Field(field,9)!=0||field.Value!=0||field.Other!=11)return false;
                    var chain=new IndirectBox<int>(7,11);if(IndirectProbe.Chain(chain,9)!=0||chain.Value!=0)return false;
                    var values=new[]{7};if(IndirectProbe.Element(values,9)!=0||values[0]!=0)return false;
                    var property=new IndirectBox<int>(7,11);if(IndirectProbe.Property(property,9)!=0||property.Value!=0||property.Gets!=1)return false;
                    var source=new IndirectSource<int>();var owner=new IndirectBox<int>(7,11);
                    if(IndirectProbe.Owner(owner,source,9)!=0||owner.Value!=0)return false;
                    var delegated=new IndirectBox<int>(7,11);IndirectReader<int> reader=IndirectProbe.Maybe;
                    if(IndirectProbe.Delegate(delegated,reader,9)!=0||delegated.Value!=0)return false;
                    var sourceProperty=new IndirectBox<int>(7,11);if(IndirectProbe.SourceProperty(sourceProperty,source)!=0||sourceProperty.Value!=0)return false;
                    var sourceField=new IndirectBox<int>(7,11);if(IndirectProbe.SourceField(sourceField,source)!=0||sourceField.Value!=0)return false;
                    var optionalStore=new IndirectBox<int>(7,11);if(IndirectProbe.OptionalStore(optionalStore,9)!=0||optionalStore.Value!=0)return false;
                    var copy=new IndirectBox<int>(7,11);if(IndirectProbe.Copy(copy,9)!=7||copy.Value!=0)return false;
                    var sibling=new IndirectPair<int,int>(7,11);if(IndirectProbe.Sibling(sibling,9)!=11||sibling.Value!=0||sibling.Other!=11)return false;
                    var optional=new IndirectBox<int?>(7,11);if(IndirectProbe.Physical(optional,9)!=null||optional.Value!=null)return false;
                    var asyncBox=new IndirectBox<int>(7,11);var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=IndirectProbe.Async(gate.Task,asyncBox);if(pending.IsCompleted||asyncBox.Value!=7)return false;
                    gate.SetResult(9);if(await pending.WaitAsync(token)!=0||asyncBox.Value!=0)return false;
                    var fault=new InvalidOperationException("indirect fault");
                    try{await IndirectProbe.Async(Task.FromException<int>(fault),new IndirectBox<int>(7,11));return false;}
                    catch(InvalidOperationException caught){if(!ReferenceEquals(caught,fault))return false;}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    try{await IndirectProbe.Async(Task.FromCanceled<int>(canceled.Token),new IndirectBox<int>(7,11));return false;}
                    catch(OperationCanceledException caught){return caught.CancellationToken==canceled.Token;}
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NewIndirectRunner{
                public static async Task<bool> Run(CancellationToken token){
                    var value=new object();var other=new object();
                    var field=new IndirectBox<object?>(value,other);if(IndirectProbe.Field(field,value)!=null||field.Value!=null||!ReferenceEquals(field.Other,other))return false;
                    var chain=new IndirectBox<object?>(value,other);if(IndirectProbe.Chain(chain,value)!=null||chain.Value!=null)return false;
                    object?[] values={value};if(IndirectProbe.Element(values,value)!=null||values[0]!=null)return false;
                    var property=new IndirectBox<object?>(value,other);if(IndirectProbe.Property(property,value)!=null||property.Value!=null||property.Gets!=1)return false;
                    var source=new IndirectSource<object>();var owner=new IndirectBox<object?>(value,other);
                    if(IndirectProbe.Owner(owner,source,value)!=null||owner.Value!=null)return false;
                    var delegated=new IndirectBox<object?>(value,other);IndirectReader<object> reader=IndirectProbe.Maybe;
                    if(IndirectProbe.Delegate(delegated,reader,value)!=null||delegated.Value!=null)return false;
                    var sourceProperty=new IndirectBox<object?>(value,other);if(IndirectProbe.SourceProperty(sourceProperty,source)!=null||sourceProperty.Value!=null)return false;
                    var sourceField=new IndirectBox<object?>(value,other);if(IndirectProbe.SourceField(sourceField,source)!=null||sourceField.Value!=null)return false;
                    var optionalStore=new IndirectBox<object?>(value,other);if(IndirectProbe.OptionalStore(optionalStore,value)!=null||optionalStore.Value!=null)return false;
                    var copy=new IndirectBox<object?>(value,other);if(!ReferenceEquals(IndirectProbe.Copy(copy,value),value)||copy.Value!=null)return false;
                    var sibling=new IndirectPair<object?,Uri>(value,new Uri("https://example.invalid/sibling"));
                    if(!ReferenceEquals(IndirectProbe.Sibling(sibling,value),sibling.Other)||sibling.Value!=null)return false;
                    var optional=new IndirectBox<int?>(23,29);if(IndirectProbe.Physical(optional,19)!=null||optional.Value!=null)return false;
                    var asyncBox=new IndirectBox<object?>(value,other);var gate=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=IndirectProbe.Async(gate.Task,asyncBox);if(pending.IsCompleted||!ReferenceEquals(asyncBox.Value,value))return false;
                    gate.SetResult(value);if(await pending.WaitAsync(token)!=null||asyncBox.Value!=null)return false;
                    var fault=new InvalidOperationException("indirect fault");
                    try{await IndirectProbe.Async(Task.FromException<object>(fault),new IndirectBox<object?>(value,other));return false;}
                    catch(InvalidOperationException caught){if(!ReferenceEquals(caught,fault))return false;}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    try{await IndirectProbe.Async(Task.FromCanceled<object>(canceled.Token),new IndirectBox<object?>(value,other));return false;}
                    catch(OperationCanceledException caught){return caught.CancellationToken==canceled.Token;}
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "IndirectStorage", "IndirectStorage", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("IndirectStorage.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var originalErrors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (originalErrors.Length != 0)
        { throw new InvalidOperationException("Indirect fixture: " + originalErrors[0]); }
        var expected = new Dictionary<string, string>
        {
            ["Field"] = "T1? Field<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Chain"] = "T1? Chain<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Element"] = "T1? Element<T1>(T1?[] values, T1 fill)",
            ["Property"] = "T1? Property<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Owner"] = "T1? Owner<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source, T1 fill)",
            ["Delegate"] = "T1? Delegate<T1>(global::IndirectBox<T1?> box, global::IndirectReader<T1> read, T1 fill)",
            ["SourceProperty"] = "T1? SourceProperty<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source)",
            ["SourceField"] = "T1? SourceField<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source)",
            ["OptionalStore"] = "T1? OptionalStore<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Copy"] = "T1? Copy<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Sibling"] = "T2 Sibling<T1, T2>(global::IndirectPair<T1?, T2> box, T1 fill)",
            ["Async"] = "global::System.Threading.Tasks.Task<T1?> Async<T1>(global::System.Threading.Tasks.Task<T1> fill, global::IndirectBox<T1?> box)"
        };
        var results = Program.Results((CSharpCompilation)original, cancellationToken)
            .Where(result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return expected.ContainsKey(result.Method) || result.Method is "Typed" or "GuardedStore";
            })
            .ToDictionary(result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return result.Method;
            }, StringComparer.Ordinal);
        foreach (var (name, signature) in expected)
        {
            if (!results.TryGetValue(name, out var result) || result.Status != "Suggestion" || result.Signature != signature)
            { throw new InvalidOperationException("Indirect " + name + ": expected " + signature + "; got " + (result?.Status ?? "absent") + ": " + (result?.Signature ?? result?.Message)); }
            Program.Log.Info("PASS IndirectMaybe" + name);
        }
        if (results["Typed"].Signature?.Contains("?>", StringComparison.Ordinal) == true
            || results["GuardedStore"].Signature?.Contains("?>", StringComparison.Ordinal) == true)
        { throw new InvalidOperationException("Typed ref or NotNullIfNotNull storage widened."); }
        CheckMetadata(cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature",
            provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
            .OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var changedErrors = changed.GetDiagnostics(cancellationToken).Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (changedErrors.Length != 0)
        { throw new InvalidOperationException("Indirect Fix All: " + changedErrors[0]); }
        await Execute(original.WithAssemblyName("OriginalIndirect"), "OriginalIndirectRunner", cancellationToken).ConfigureAwait(false);
        await Execute(changed.WithAssemblyName("ChangedIndirect"), "OriginalIndirectRunner", cancellationToken).ConfigureAwait(false);
        var newDomain = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken));
        await Execute(newDomain.WithAssemblyName("NewIndirect"), "NewIndirectRunner", cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS IndirectMaybeOriginalAndNewDomainsEffectsFaultCancellation");
        return expected.Count + 5;
    }

    private static void CheckMetadata(CancellationToken cancellationToken)
    {
        var library = CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public sealed class ImportedSource<T>{[return:MaybeNull]public T Read(T value)=>default;}
            """, cancellationToken: cancellationToken);
        using var image = new MemoryStream();
        var emitted = Program.Compile([library]).WithAssemblyName("IndirectContracts").Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Indirect contract emission: " + string.Join("\n", emitted.Diagnostics)); }
        var site = CSharpSyntaxTree.ParseText("""
            #nullable enable
            public sealed class ImportedBox<T>{public T Value=default!;}
            public static class ImportedUse{
                public static int M(ImportedBox<int> box,ImportedSource<int> source,int fill){
                    ref var alias=ref box.Value;alias=source.Read(fill);return box.Value;
                }
            }
            """, cancellationToken: cancellationToken);
        var compilation = Program.Compile([site]).AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        var result = Program.Results(compilation, cancellationToken).Single(candidate =>
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return candidate.Method == "M";
        });
        const string expected = "T1? M<T1>(global::ImportedBox<T1?> box, global::ImportedSource<T1> source, T1 fill)";
        if (result.Status != "Suggestion" || result.Signature != expected)
        { throw new InvalidOperationException("Indirect imported contract: expected " + expected + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
        Program.Log.Info("PASS IndirectMaybeImportedOwnerContract");
    }

    private static async Task Execute(Compilation compilation, string runner, CancellationToken cancellationToken)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Indirect emission: " + string.Join("\n", emitted.Diagnostics)); }
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("IndirectStorage", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var pending = (Task<bool>)assembly.GetType(runner)!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Indirect runtime: " + runner); }
        }
        finally { context.Unload(); }
    }
}
