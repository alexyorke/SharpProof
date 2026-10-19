using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;
using System.Linq.Expressions;
using System.Reflection;

namespace SharpProof.HmInference;

internal static class ExpressionTreeChecks
{
    private const string Support = """
        #nullable enable
        using System;
        using System.Linq.Expressions;
        public delegate R TreeMap<A,R>(A value);
        public class TreeBox<T>{public T Value=default!;}
        public static class TreeApi {
            public static T Run<T>(Expression<Func<T>> expression)=>expression.Compile()();
            public static int Fixed(Expression<Func<int>> expression)=>expression.Compile()();
            public static void Observe<T>(T value){}
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("QuotedCapture", "static Expression<Func<int>> M(int value)=>()=>value;", "global::System.Linq.Expressions.Expression<global::System.Func<T1>> M<T1>(T1 value)"),
            ("QuotedUnusedInput", "static Expression<Func<int,int>> M(int value)=>item=>value;", "global::System.Linq.Expressions.Expression<global::System.Func<int, T1>> M<T1>(T1 value)"),
            ("QuotedIdentity", "static Expression<Func<int,int>> M(bool ignored)=>item=>item;", "global::System.Linq.Expressions.Expression<global::System.Func<int, int>> M<T1>(T1 ignored)"),
            ("QuotedCompile", "static int M(Expression<Func<int>> expression)=>expression.Compile()();", "T1 M<T1>(global::System.Linq.Expressions.Expression<global::System.Func<T1>> expression)"),
            ("QuotedApply", "static int M(Expression<Func<int,int>> expression,int value)=>expression.Compile()(value);", "T2 M<T1, T2>(global::System.Linq.Expressions.Expression<global::System.Func<T1, T2>> expression, T1 value)"),
            ("QuotedCustomDelegate", "static Expression<TreeMap<int,string>> M(string value)=>item=>value;", "global::System.Linq.Expressions.Expression<global::TreeMap<int, T1>> M<T1>(T1 value)"),
            ("QuotedGenericCall", "static int M(int value)=>TreeApi.Run(()=>value);", "T1 M<T1>(T1 value)"),
            ("QuotedFixedCall", "static int M(int value,bool ignored)=>TreeApi.Fixed(()=>value);", "int M<T1>(int value, T1 ignored)"),
            ("QuotedMember", "static Expression<Func<int>> M(TreeBox<int> box)=>()=>box.Value;", "global::System.Linq.Expressions.Expression<global::System.Func<T1>> M<T1>(global::TreeBox<T1> box)"),
            ("QuotedNullable", "static Expression<Func<string?>> M(string? value)=>()=>value;", "global::System.Linq.Expressions.Expression<global::System.Func<T1?>> M<T1>(T1? value) where T1 : class"),
            ("QuotedNested", "static Expression<Func<Expression<Func<int>>>> M(int value)=>()=>()=>value;", "global::System.Linq.Expressions.Expression<global::System.Func<global::System.Linq.Expressions.Expression<global::System.Func<T1>>>> M<T1>(T1 value)"),
            ("QuotedVoid", "static Expression<Action<int>> M(int value)=>item=>TreeApi.Observe(value);", "global::System.Linq.Expressions.Expression<global::System.Action<int>> M<T1>(T1 value)"),
            ("QuotedBaseExpression", "static Expression M(int value,bool ignored)=>(int item)=>value;", "global::System.Linq.Expressions.Expression M<T1>(int value, T1 ignored)"),
            ("QuotedBaseLambda", "static LambdaExpression M(int value,bool ignored)=>(int item)=>value;", "global::System.Linq.Expressions.LambdaExpression M<T1>(int value, T1 ignored)"),
            ("NaturalBaseDelegate", "static Delegate M(int value,bool ignored)=>(int item)=>value;", "global::System.Delegate M<T1>(int value, T1 ignored)")
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
            public static class QuoteProbe {
                public static Expression<Func<int>> Capture(int value)=>()=>value;
                public static Expression<Func<int>> Member(TreeBox<int> box)=>()=>box.Value;
                public static int Apply(Expression<Func<int,int>> expression,int value)=>expression.Compile()(value);
                public static int Execute(int value)=>TreeApi.Run(()=>value);
                public static Expression<Func<Expression<Func<int>>>> Nested(int value)=>()=>()=>value;
                public static Expression<Func<string?>> Nullable(string? value)=>()=>value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ExpressionRuntime", "ExpressionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Expressions.cs", Support + source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 6; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Expression analyzer crashed."); }
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "QuoteProbe" }; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var name in new[] { "Capture", "Member", "Nested", "Nullable" })
        {
            var type = name == "Nullable" ? typeof(string) : typeof(int);
            var original = before.GetType("QuoteProbe")!.GetMethod(name)!;
            var changed = after.GetType("QuoteProbe")!.GetMethod(name)!.MakeGenericMethod(type);
            object? input = name == "Nullable" ? "text" : 42;
            object? leftInput = input;
            object? rightInput = input;
            if (name == "Member")
            {
                leftInput = Activator.CreateInstance(before.GetType("TreeBox`1")!.MakeGenericType(type))!;
                rightInput = Activator.CreateInstance(after.GetType("TreeBox`1")!.MakeGenericType(type))!;
                leftInput.GetType().GetField("Value")!.SetValue(leftInput, input);
                rightInput.GetType().GetField("Value")!.SetValue(rightInput, input);
            }
            var left = (LambdaExpression)original.Invoke(null, new[] { leftInput })!;
            var right = (LambdaExpression)changed.Invoke(null, new[] { rightInput })!;
            if (TreeShape.Of(left) != TreeShape.Of(right))
            { throw new InvalidOperationException("Quoted tree shape changed: " + name); }
            object? Evaluate(LambdaExpression expression)
            {
                ArgumentNullException.ThrowIfNull(expression);
                var value = expression.Compile().DynamicInvoke();
                return value is LambdaExpression nested ? nested.Compile().DynamicInvoke() : value;
            }
            if (!Equals(Evaluate(left), Evaluate(right)) || !Equals(Evaluate(right), input))
            { throw new InvalidOperationException("Quoted result changed: " + name); }
            if (name == "Member")
            {
                var replacement = 73;
                rightInput!.GetType().GetField("Value")!.SetValue(rightInput, replacement);
                if (!Equals(Evaluate(right), replacement))
                { throw new InvalidOperationException("Quoted field capture became a snapshot."); }
            }
        }
        var probe = after.GetType("QuoteProbe")!;
        var sentinel = new Uri("https://example.invalid/expression");
        var capture = (Expression<Func<Uri>>)probe.GetMethod("Capture")!.MakeGenericMethod(typeof(Uri)).Invoke(null, new object[] { sentinel })!;
        if (!ReferenceEquals(capture.Compile()(), sentinel))
        { throw new InvalidOperationException("Quoted capture lost the new result domain."); }
        Expression<Func<Uri, string>> map = (Uri /* SPINT001: all values valid - Null maps to empty text; every relative or absolute URI supports ToString. */ value) => value == null ? string.Empty : value.ToString();
        var applied = probe.GetMethod("Apply")!.MakeGenericMethod(typeof(Uri), typeof(string)).Invoke(null, new object[] { map, sentinel });
        if (!Equals(applied, sentinel.AbsoluteUri))
        { throw new InvalidOperationException("Compiled quoted delegate lost distinct input/output types."); }
        var executed = probe.GetMethod("Execute")!.MakeGenericMethod(typeof(Uri)).Invoke(null, new object[] { sentinel });
        if (!ReferenceEquals(executed, sentinel))
        { throw new InvalidOperationException("Generic quote argument lost its captured value."); }
        var nullable = (Expression<Func<Uri?>>)probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Uri)).Invoke(null, new object?[] { null })!;
        if (nullable.Compile()() is not null)
        { throw new InvalidOperationException("Quoted nullable result was lost."); }
        Program.Log.Info("PASS ExpressionTreeCodeFixShapeCapturesCompileAndNewDomains");
        return 1;
    }

    private sealed class TreeShape : ExpressionVisitor
    {
        private readonly List<string> nodes = [];
        public static string Of(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            var visitor = new TreeShape();
            visitor.Visit(expression);
            return string.Join("|", visitor.nodes);
        }
        public override Expression? Visit(Expression? node)
        {
            if (node is null)
            { return null; }
            nodes.Add(node.NodeType + ":" + TypeName(node.Type));
            return base.Visit(node);
        }
        protected override Expression VisitMember(MemberExpression node)
        {
            ArgumentNullException.ThrowIfNull(node);
            nodes.Add("member:" + TypeName(node.Member.DeclaringType!) + ":" + node.Member.Name);
            return base.VisitMember(node);
        }
        private static string TypeName(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.Name.StartsWith("<>", StringComparison.Ordinal) ? "closure" : type.FullName!;
        }
    }
    private static Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return Assembly.Load(image.ToArray());
    }
}
