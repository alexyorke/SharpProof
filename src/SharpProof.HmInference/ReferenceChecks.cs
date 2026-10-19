using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ReferenceChecks
{
    private const string Support = """
        #nullable enable
        using System;
        public delegate T RefRead<T>(ref T value);
        public delegate void RefWrite<T>(ref T value,T replacement);
        public delegate bool RefTry<T>(out T value);
        public delegate ref T RefBorrow<T>(ref T value);
        public delegate ref readonly T ReadonlyBorrow<T>(ref T value);
        public delegate T ReadonlyRead<T>(in T value);
        public delegate T ScopedRead<T>(scoped ref T value);
        public delegate int ArrayRead<T>(params T[] values);
        public class RefBox<T> {public ref T Borrow(ref T value)=>ref value;public ref readonly T BorrowReadonly(ref T value)=>ref value;}
        public class RefConstruct {public RefConstruct(ref int value){value++;}}
        public static class RefApi {
            public static T Read<T>(ref T value)=>value;
            public static T Readonly<T>(in T value)=>value;
            public static T ReadonlyRef<T>(ref readonly T value)=>value;
            public static long Widen(in long value)=>value;
            public static void Store<T>(ref T target,T value)=>target=value;
            public static void Copy<T>(T value,out T result)=>result=value;
            public static ref T Borrow<T>(ref T value)=>ref value;
            public static ref readonly T BorrowReadonly<T>(ref T value)=>ref value;
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("GenericRefCall", "static int M(int value)=>RefApi.Read(ref value);", "T1 M<T1>(T1 value)"),
            ("GenericRefStore", "static int M(ref int target,int value){RefApi.Store(ref target,value);return target;}", "T1 M<T1>(ref T1 target, T1 value)"),
            ("OutVar", "static int M(int value){RefApi.Copy(value,out var result);return result;}", "T1 M<T1>(T1 value)"),
            ("NamedOutVar", "static int M(int value){RefApi.Copy(result:out var result,value:value);return result;}", "T1 M<T1>(T1 value)"),
            ("OutDiscard", "static int M(int value){RefApi.Copy(value,out _);return value;}", "T1 M<T1>(T1 value)"),
            ("TypedOut", "static int M(int value,bool ignored){RefApi.Copy(value,out int result);return result;}", "int M<T1>(int value, T1 ignored)"),
            ("ExplicitIn", "static int M(int value)=>RefApi.Readonly(in value);", "T1 M<T1>(T1 value)"),
            ("ReadonlyRefParameter", "static int M(int value)=>RefApi.ReadonlyRef(in value);", "T1 M<T1>(T1 value)"),
            ("OmittedInConversion", "static long M(int value,bool ignored)=>RefApi.Widen(value);", "long M<T1>(int value, T1 ignored)"),
            ("RefReturnCall", "static ref int M(ref int value)=>ref RefApi.Borrow(ref value);", "ref T1 M<T1>(ref T1 value)"),
            ("RefResultStore", "static int M(ref int target,int value){RefApi.Borrow(ref target)=value;return target;}", "T1 M<T1>(ref T1 target, T1 value)"),
            ("RefLocalAlias", "static int M(ref int target,int value){ref var alias=ref RefApi.Borrow(ref target);alias=value;return target;}", "T1 M<T1>(ref T1 target, T1 value)"),
            ("RefDelegateInvoke", "static int M(RefRead<int> read,ref int value)=>read(ref value);", "T1 M<T1>(global::RefRead<T1> read, ref T1 value)"),
            ("OutDelegateInvoke", "static int M(RefTry<int> factory){factory(out var value);return value;}", "T1 M<T1>(global::RefTry<T1> factory)"),
            ("RefReturnDelegate", "static ref int M(RefBorrow<int> borrow,ref int value)=>ref borrow(ref value);", "ref T1 M<T1>(global::RefBorrow<T1> borrow, ref T1 value)"),
            ("ReadonlyDelegate", "static int M(ReadonlyRead<int> read,int value)=>read(in value);", "T1 M<T1>(global::ReadonlyRead<T1> read, T1 value)"),
            ("RefMethodGroup", "static RefBorrow<int> M(RefBox<int> box)=>box.Borrow;", "global::RefBorrow<T1> M<T1>(global::RefBox<T1> box)"),
            ("RefLambda", "static RefRead<int> M(bool ignored)=>(ref int value)=>value;", "global::RefRead<int> M<T1>(T1 ignored)"),
            ("ImplicitRefLambda", "static RefRead<int> M(int seed)=>(ref value)=>seed;", "global::RefRead<T1> M<T1>(T1 seed)"),
            ("RefAnonymousMethod", "static RefRead<int> M(bool ignored)=>delegate(ref int value){return value;};", "global::RefRead<int> M<T1>(T1 ignored)"),
            ("RefReturnLambda", "static RefBorrow<int> M(bool ignored)=>(ref int value)=>ref value;", "global::RefBorrow<int> M<T1>(T1 ignored)"),
            ("ScopedLambda", "static ScopedRead<int> M(bool ignored)=>(scoped ref int value)=>value;", "global::ScopedRead<int> M<T1>(T1 ignored)"),
            ("ParamsLambda", "static ArrayRead<int> M(bool ignored)=>(params int[] values)=>values.Length;", "global::ArrayRead<int> M<T1>(T1 ignored)"),
            ("AttributedLambda", "static Func<int,int> M(bool ignored)=>[Obsolete] (int value)=>value;", "global::System.Func<int, int> M<T1>(T1 ignored)"),
            ("ReadonlyRefReturn", "static ref readonly int M(ref int value)=>ref RefApi.BorrowReadonly(ref value);", "ref readonly T1 M<T1>(ref T1 value)"),
            ("ReadonlyRefDelegate", "static ref readonly int M(ReadonlyBorrow<int> read,ref int value)=>ref read(ref value);", "ref readonly T1 M<T1>(global::ReadonlyBorrow<T1> read, ref T1 value)"),
            ("ReadonlyMethodGroup", "static ReadonlyBorrow<int> M(RefBox<int> box)=>box.BorrowReadonly;", "global::ReadonlyBorrow<T1> M<T1>(global::RefBox<T1> box)"),
            ("OutLambda", "static RefTry<int> M(bool ignored)=>(out int value)=>{value=42;return true;};", "global::RefTry<int> M<T1>(T1 ignored)"),
            ("ImplicitOutLambda", "static RefTry<int> M(int seed)=>(out value)=>{value=seed;return true;};", "global::RefTry<T1> M<T1>(T1 seed)"),
            ("AttributedLambdaParameter", "static Func<string,string> M(bool ignored)=>([System.Diagnostics.CodeAnalysis.DisallowNull] string value)=>value;", "global::System.Func<string, string> M<T1>(T1 ignored)"),
            ("NullableOut", "static string? M(string? value){RefApi.Copy(value,out var result);return result;}", "T1? M<T1>(T1? value) where T1 : class"),
            ("NullableRef", "static string? M(ref string? value)=>RefApi.Read(ref value);", "T1? M<T1>(ref T1? value) where T1 : class"),
            ("RefConstructor", "static int M(int value,bool ignored){_=new RefConstruct(ref value);return value;}", "int M<T1>(int value, T1 ignored)")
        ];
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Source + "}", new CSharpParseOptions(test.Name.StartsWith("Implicit", StringComparison.Ordinal) ? LanguageVersion.Preview : LanguageVersion.Default), cancellationToken: cancellationToken)]);
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
        const string probes = """
            public static class RefProbe {
                public static int Read(int value)=>RefApi.Read(ref value);
                public static int Store(ref int target,int value){RefApi.Store(ref target,value);return target;}
                public static int Copy(int value){RefApi.Copy(value,out var result);return result;}
                public static int Alias(ref int target,int value){ref var alias=ref RefApi.Borrow(ref target);alias=value;return target;}
                public static int StoreResult(ref int target,int value){RefApi.Borrow(ref target)=value;return target;}
                public static ref int Borrow(ref int value)=>ref RefApi.Borrow(ref value);
                public static int Delegate(RefRead<int> read,ref int value)=>read(ref value);
                public static ref int DelegateBorrow(RefBorrow<int> read,ref int value)=>ref read(ref value);
                public static RefBorrow<int> Group(RefBox<int> box)=>box.Borrow;
                public static RefRead<int> Lambda(bool ignored)=>(ref int value)=>value;
            }
            public static class OriginalRefRunner {
                public static bool Run(){
                    int value=1;
                    if(RefProbe.Read(42)!=42||RefProbe.Copy(42)!=42||RefProbe.Store(ref value,2)!=2||value!=2)return false;
                    if(RefProbe.Alias(ref value,3)!=3||RefProbe.StoreResult(ref value,4)!=4||value!=4)return false;
                    ref var alias=ref RefProbe.Borrow(ref value);alias=5;if(value!=5)return false;
                    RefRead<int> read=(ref int slot)=>slot;
                    if(RefProbe.Delegate(read,ref value)!=5)return false;
                    RefBorrow<int> borrow=RefApi.Borrow;ref var second=ref RefProbe.DelegateBorrow(borrow,ref value);second=6;if(value!=6)return false;
                    var group=RefProbe.Group(new RefBox<int>());ref var third=ref group(ref value);third=7;
                    return value==7&&RefProbe.Lambda(false)(ref value)==7;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ReferenceRuntime", "ReferenceRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("References.cs", Support + probes);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalRefRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original ref behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 10; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Ref analyzer crashed."); }
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "RefProbe" }; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewRefDomains.cs", """
            #nullable enable
            using System;
            public static class NewRefRunner {
                public static bool Run(){
                    var sentinel=new Uri("https://example.invalid/ref");var other=new Uri("https://example.invalid/other");Uri value=sentinel;
                    if(!ReferenceEquals(RefProbe.Read(sentinel),sentinel)||!ReferenceEquals(RefProbe.Copy(sentinel),sentinel))return false;
                    if(!ReferenceEquals(RefProbe.Store(ref value,other),other)||!ReferenceEquals(value,other))return false;
                    RefProbe.Alias(ref value,sentinel);if(!ReferenceEquals(value,sentinel))return false;
                    RefProbe.StoreResult(ref value,other);ref var alias=ref RefProbe.Borrow(ref value);alias=sentinel;if(!ReferenceEquals(value,sentinel))return false;
                    RefRead<Uri> read=(ref Uri slot)=>slot;if(!ReferenceEquals(RefProbe.Delegate(read,ref value),sentinel))return false;
                    RefBorrow<Uri> borrow=RefApi.Borrow;ref var second=ref RefProbe.DelegateBorrow(borrow,ref value);second=other;
                    var group=RefProbe.Group(new RefBox<Uri>());ref var third=ref group(ref value);third=sentinel;
                    int number=42;return ReferenceEquals(value,sentinel)&&RefProbe.Lambda("ignored")(ref number)==42;
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (after.GetType("OriginalRefRunner")!.GetMethod("Run")!.Invoke(null, null) is not true || after.GetType("NewRefRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Applied ref code fixes changed aliases, mutations or reference identity."); }
        Program.Log.Info("PASS ReferenceCodeFixAliasingMutationAndNewDomains");
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
