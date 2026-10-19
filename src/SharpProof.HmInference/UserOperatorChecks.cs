using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class UserOperatorChecks
{
    private const string Support = """
        #nullable enable
        using System;using System.Numerics;
        public static class OperatorTrace {public static string Log="";}
        public readonly record struct OperatorBox<T>(T Value) {
            public static OperatorBox<T> operator +(OperatorBox<T> left,OperatorBox<T> right){OperatorTrace.Log+="add;";return left;}
            public static OperatorBox<T> operator checked +(OperatorBox<T> left,OperatorBox<T> right){OperatorTrace.Log+="checked;";return right;}
            public static OperatorBox<T> operator +(OperatorBox<T> left,int right){OperatorTrace.Log+="offset;";return left;}
            public static OperatorBox<T> operator -(OperatorBox<T> value){OperatorTrace.Log+="negate;";return value;}
            public static OperatorBox<T> operator ++(OperatorBox<T> value){OperatorTrace.Log+="increment;";return value;}
            public static implicit operator T(OperatorBox<T> value){OperatorTrace.Log+="out;";return value.Value;}
            public static implicit operator OperatorBox<T>(T value){OperatorTrace.Log+="in;";return new(value);}
        }
        public readonly record struct OperatorOffsetBox<T>(T Value) {public static OperatorOffsetBox<T> operator +(OperatorOffsetBox<T> left,int right)=>left;}
        public readonly record struct OperatorLiftBox<T>(T Value) {public static OperatorLiftBox<T> operator +(OperatorLiftBox<T> left,OperatorLiftBox<T> right)=>left;}
        public readonly struct OperatorFixed {
            public static OperatorFixed operator +(OperatorFixed left,OperatorFixed right)=>left;
        }
        public static class OperatorApi {public static int Consume(int value)=>value;}
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("UserBinary", "static OperatorBox<int> M(OperatorBox<int> left,OperatorBox<int> right)=>left+right;", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> left, global::OperatorBox<T1> right)"),
            ("UserCheckedBinary", "static OperatorBox<int> M(OperatorBox<int> left,OperatorBox<int> right)=>checked(left+right);", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> left, global::OperatorBox<T1> right)"),
            ("UserUnary", "static OperatorBox<int> M(OperatorBox<int> value)=>-value;", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> value)"),
            ("UserIncrement", "static OperatorBox<int> M(OperatorBox<int> value)=>++value;", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> value)"),
            ("UserPostIncrement", "static OperatorBox<int> M(OperatorBox<int> value)=>value++;", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> value)"),
            ("UserCompound", "static OperatorBox<int> M(OperatorBox<int> value,OperatorBox<int> other){value+=other;return value;}", "Suggestion", "global::OperatorBox<T1> M<T1>(global::OperatorBox<T1> value, global::OperatorBox<T1> other)"),
            ("UserLiftedBinary", "static OperatorBox<int>? M(OperatorBox<int>? left,OperatorBox<int>? right)=>left+right;", "Suggestion", "global::OperatorBox<T1>? M<T1>(global::OperatorBox<T1>? left, global::OperatorBox<T1>? right)"),
            ("UserMixedLiftedBinary", "static OperatorLiftBox<int>? M(OperatorLiftBox<int>? left,OperatorLiftBox<int> right)=>left+right;", "Suggestion", "global::OperatorLiftBox<T1>? M<T1>(global::OperatorLiftBox<T1>? left, global::OperatorLiftBox<T1> right)"),
            ("UserNullLiftedBinary", "static OperatorLiftBox<int>? M(OperatorLiftBox<int> right)=>null+right;", "Suggestion", "global::OperatorLiftBox<T1>? M<T1>(global::OperatorLiftBox<T1> right)"),
            ("UserLiftedCallerInference", "static OperatorBox<int>? M(OperatorBox<int>? left,OperatorBox<int>? right)=>left+right;static OperatorBox<int>? Caller(OperatorBox<int> left,OperatorBox<int> right)=>M(left,right);", "Rejected", null),
            ("UserLiftedUnary", "static OperatorBox<int>? M(OperatorBox<int>? value)=>-value;", "Suggestion", "global::OperatorBox<T1>? M<T1>(global::OperatorBox<T1>? value)"),
            ("UserLiftedCompound", "static OperatorBox<int>? M(OperatorBox<int>? value,OperatorBox<int>? other){value+=other;return value;}", "Suggestion", "global::OperatorBox<T1>? M<T1>(global::OperatorBox<T1>? value, global::OperatorBox<T1>? other)"),
            ("UserNumericOperand", "static OperatorOffsetBox<int> M(OperatorOffsetBox<int> value,byte offset)=>value+offset;", "Suggestion", "global::OperatorOffsetBox<T1> M<T1>(global::OperatorOffsetBox<T1> value, byte offset)"),
            ("UserImplicitOut", "static int M(OperatorBox<int> value)=>value;", "Suggestion", "T1 M<T1>(global::OperatorBox<T1> value)"),
            ("UserImplicitIn", "static OperatorBox<int> M(int value)=>value;", "Suggestion", "global::OperatorBox<T1> M<T1>(T1 value)"),
            ("UserConversionArgument", "static int M(OperatorBox<int> value,bool ignored)=>OperatorApi.Consume(value);", "Suggestion", "int M<T1>(global::OperatorBox<int> value, T1 ignored)"),
            ("UserExplicitOut", "static int M(OperatorBox<int> value,bool ignored)=>(int)value;", "Suggestion", "int M<T1>(global::OperatorBox<int> value, T1 ignored)"),
            ("UserExplicitExistingType", "static T M<T>(OperatorBox<T> value,bool ignored)=>(T)value;", "Suggestion", "T M<T, T1>(global::OperatorBox<T> value, T1 ignored)"),
            ("UserLiftedConversion", "static int? M(OperatorBox<int>? value)=>value;", "Suggestion", "T1? M<T1>(global::OperatorBox<T1>? value) where T1 : struct"),
            ("UserAsyncBinary", "static async System.Threading.Tasks.Task<OperatorBox<int>> M(System.Threading.Tasks.Task<OperatorBox<int>> pending,OperatorBox<int> right)=>(await pending)+right;", "Suggestion", "global::System.Threading.Tasks.Task<global::OperatorBox<T1>> M<T1>(global::System.Threading.Tasks.Task<global::OperatorBox<T1>> pending, global::OperatorBox<T1> right)"),
            ("UserAsyncConversion", "static async System.Threading.Tasks.Task<int> M(System.Threading.Tasks.Task<OperatorBox<int>> pending)=>await pending;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<global::OperatorBox<T1>> pending)"),
            ("UserAsyncLiftedConversion", "static async System.Threading.Tasks.ValueTask<int?> M(System.Threading.Tasks.Task<OperatorBox<int>?> pending)=>await pending;", "Suggestion", "global::System.Threading.Tasks.ValueTask<T1?> M<T1>(global::System.Threading.Tasks.Task<global::OperatorBox<T1>?> pending) where T1 : struct"),
            ("UserNullableReferenceConversion", "static string? M(OperatorBox<string?> value)=>value;", "Suggestion", "T1? M<T1>(global::OperatorBox<T1?> value) where T1 : class"),
            ("UserAsyncNullableReferenceConversion", "static async System.Threading.Tasks.Task<string?> M(System.Threading.Tasks.Task<OperatorBox<string?>> pending)=>await pending;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::System.Threading.Tasks.Task<global::OperatorBox<T1?>> pending) where T1 : class"),
            ("UserFixedOperator", "static OperatorFixed M(OperatorFixed left,OperatorFixed right,bool ignored)=>left+right;", "Suggestion", "global::OperatorFixed M<T1>(global::OperatorFixed left, global::OperatorFixed right, T1 ignored)"),
            ("UserGenericMath", "static T M<T>(T left,T right,bool ignored) where T:IAdditionOperators<T,T,T> =>left+right;", "Suggestion", "T M<T, T1>(T left, T right, T1 ignored) where T : IAdditionOperators<T, T, T>")
        ];
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            public static class OperatorProbe {
                public static OperatorBox<int> Add(OperatorBox<int> left,OperatorBox<int> right)=>left+right;
                public static OperatorBox<int> Checked(OperatorBox<int> left,OperatorBox<int> right)=>checked(left+right);
                public static OperatorBox<int> Negate(OperatorBox<int> value)=>-value;
                public static OperatorBox<int> Increment(OperatorBox<int> value)=>++value;
                public static OperatorBox<int> Compound(OperatorBox<int> value,OperatorBox<int> other){value+=other;return value;}
                public static async System.Threading.Tasks.ValueTask<int?> AwaitUnwrap(System.Threading.Tasks.Task<OperatorBox<int>?> pending)=>await pending;
                public static int Unwrap(OperatorBox<int> value)=>value;
                public static OperatorBox<int> Wrap(int value)=>value;
                public static OperatorBox<int>? Lifted(OperatorBox<int>? left,OperatorBox<int>? right)=>left+right;
            }
            public static class OriginalOperatorRunner {
                public static string Run(){
                    OperatorTrace.Log="";var left=new OperatorBox<int>(1);var right=new OperatorBox<int>(2);
                    if(OperatorProbe.Add(left,right).Value!=1||OperatorProbe.Checked(left,right).Value!=2||OperatorProbe.Negate(left).Value!=1||OperatorProbe.Increment(left).Value!=1||OperatorProbe.Compound(left,right).Value!=1)return "bad";
                    OperatorBox<int>? optionalLeft=left,optionalRight=right;
                    if(OperatorProbe.Unwrap(left)!=1||OperatorProbe.Wrap(2).Value!=2||OperatorProbe.Lifted(optionalLeft,optionalRight)?.Value!=1||OperatorProbe.Lifted(null,optionalRight)!=null)return "bad";
                    return OperatorTrace.Log;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "OperatorRuntime", "OperatorRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Operators.cs", Support + source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var originalTrace = before.GetType("OriginalOperatorRunner")!.GetMethod("Run")!.Invoke(null, null);
        if (Equals(originalTrace, "bad"))
        { throw new InvalidOperationException("Original operator behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 9; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Operator analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "OperatorProbe" }; }) ?? throw new InvalidOperationException("Missing operator fix at round " + round + ": " + string.Join<MethodResult>("; ", Program.Results((CSharpCompilation)compilation, cancellationToken)));
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewOperatorDomains.cs", """
            using System;
            public static class NewOperatorRunner {
                public static bool Run(){
                    var first=new Uri("https://example.invalid/first");var last=new Uri("https://example.invalid/last");
                    var left=new OperatorBox<Uri>(first);var right=new OperatorBox<Uri>(last);OperatorTrace.Log="";
                    if(!ReferenceEquals(OperatorProbe.Add(left,right).Value,first)||!ReferenceEquals(OperatorProbe.Checked(left,right).Value,last)||!ReferenceEquals(OperatorProbe.Negate(left).Value,first)||!ReferenceEquals(OperatorProbe.Increment(left).Value,first)||!ReferenceEquals(OperatorProbe.Compound(left,right).Value,first))return false;
                    OperatorBox<Uri>? optionalLeft=left,optionalRight=right;
                    if(!ReferenceEquals(OperatorProbe.Unwrap(left),first)||!ReferenceEquals(OperatorProbe.Wrap(last).Value,last)||!ReferenceEquals(OperatorProbe.Lifted(optionalLeft,optionalRight)?.Value,first)||OperatorProbe.Lifted<Uri>(null,right)!=null)return false;
                    return OperatorTrace.Log=="add;checked;negate;increment;add;out;in;add;";
                }
                public static async System.Threading.Tasks.Task<bool> RunAsync(){
                    var pending=new System.Threading.Tasks.TaskCompletionSource<OperatorBox<long>?>();OperatorTrace.Log="";
                    var result=OperatorProbe.AwaitUnwrap(pending.Task);
                    if(result.IsCompleted||OperatorTrace.Log!="")return false;
                    pending.SetResult(new OperatorBox<long>(1234L));
                    if(await result!=1234L||OperatorTrace.Log!="out;")return false;
                    OperatorTrace.Log="";
                    var empty=await OperatorProbe.AwaitUnwrap(System.Threading.Tasks.Task.FromResult<OperatorBox<long>?>(null));
                    if(empty!=null||OperatorTrace.Log!="")return false;
                    try {await OperatorProbe.AwaitUnwrap(System.Threading.Tasks.Task.FromException<OperatorBox<long>?>(new InvalidOperationException("expected")));return false;}
                    catch(InvalidOperationException){}
                    var cancelled=new System.Threading.CancellationToken(true);
                    try {await OperatorProbe.AwaitUnwrap(System.Threading.Tasks.Task.FromCanceled<OperatorBox<long>?>(cancelled));return false;}
                    catch(OperationCanceledException exception){return exception.CancellationToken==cancelled&&OperatorTrace.Log=="";}
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var fixedTrace = after.GetType("OriginalOperatorRunner")!.GetMethod("Run")!.Invoke(null, null);
        if (!Equals(originalTrace, fixedTrace) || after.GetType("NewOperatorRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Operator/conversion fixes changed checked selection, lifting, effects or identity."); }
        var asynchronous = (Task<bool>)after.GetType("NewOperatorRunner")!.GetMethod("RunAsync")!.Invoke(null, null)!;
        if (!await asynchronous.ConfigureAwait(false))
        { throw new InvalidOperationException("Lifted async conversion changed suspension, null lifting or operator effects."); }
        Program.Log.Info("PASS OperatorCodeFixCheckedLiftedConversionsEffectsAndNewDomains");
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
