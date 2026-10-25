using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static class RecursiveGroupChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        const string source = """
            public static class RecursivePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
                public static int ExistingCaller() => First(7, true);
            }
            """;
        var originalTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = Program.Compile([originalTree]);
        var root = (CompilationUnitSyntax)originalTree.GetRoot(cancellationToken);
        var first = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ => method.Identifier.ValueText == "First");
        var analysis = Inference.AnalyzeRecursiveGroup(first,
            compilation.GetSemanticModel(originalTree), cancellationToken)
            ?? throw new InvalidOperationException("Expected a valid recursive group proposal.");
        var signatures = analysis.Results.Values.Select(result /* SPINT001: all values valid - Group results are nonnull records. */ => result.Signature).ToArray();
        if (signatures.Length != 2
            || !signatures.Contains("T1 First<T1>(T1 value, bool again)")
            || !signatures.Contains("T1 Second<T1>(T1 value, bool again)"))
        { throw new InvalidOperationException("Unexpected recursive group signatures."); }
        var pairInfo = analysis.Results[first].RecursiveGroup;
        if (pairInfo is null || pairInfo.Tree != originalTree || pairInfo.AnchorStart != first.SpanStart
            || pairInfo.Signatures.Count != 2
            || analysis.Results.Values.Any(result /* SPINT001: all values valid - Group results are nonnull records. */ =>
                result.RecursiveGroup is null
                || result.Message != "Recursive group validated; apply all member signatures together."))
        { throw new InvalidOperationException("Validated group metadata is missing or incomplete."); }
        var pairNotices = Program.RecursiveGroupNotices(Program.Results(compilation, cancellationToken));
        if (pairNotices.Length != 1
            || pairNotices[0] != "Recursive group (2 methods): apply all member signatures together: T1 First<T1>(T1 value, bool again); T1 Second<T1>(T1 value, bool again)")
        { throw new InvalidOperationException("CLI group notice must list the atomic pair exactly once."); }
        Program.Log.Info("PASS RecursiveGroupMetadataNotice");

        var candidateRoot = analysis.RewrittenRoots[originalTree];
        var owner = candidateRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var stringCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => Second(\"text\", true);")!;
        var testedRoot = candidateRoot.ReplaceNode(owner, owner.AddMembers(stringCaller));
        var testedTree = originalTree.WithRootAndOptions(testedRoot, originalTree.Options);
        var testedCompilation = compilation.ReplaceSyntaxTree(originalTree, testedTree);
        var diagnostics = testedCompilation.GetDiagnostics(cancellationToken)
            .Where(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length != 0)
        { throw new InvalidOperationException("Applied group rejected: " + string.Join("\n", diagnostics.Select(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.ToString()))); }

        using var image = new MemoryStream();
        var emitted = testedCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Applied group failed emission: " + string.Join("\n", emitted.Diagnostics.Select(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.ToString()))); }
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        var pair = assembly.GetType("RecursivePair")!;
        if (!Equals(pair.GetMethod("ExistingCaller")!.Invoke(null, null), 7)
            || !Equals(pair.GetMethod("NewCaller")!.Invoke(null, null), "text"))
        { throw new InvalidOperationException("Applied group failed int/string runtime controls."); }
        Program.Log.Info("PASS RecursiveGroupAtomicRuntime: int=7 string=text");

        const string tripleSource = """
            public static class TriplePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? Third(value, false) : value;
                public static int Third(int value, bool again) => again ? First(value, false) : value;
                public static int ExistingCaller() => Third(9, true);
            }
            """;
        var tripleTree = CSharpSyntaxTree.ParseText(tripleSource, cancellationToken: cancellationToken);
        var tripleCompilation = Program.Compile([tripleTree]);
        var tripleFirst = tripleTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var triple = Inference.AnalyzeRecursiveGroup(tripleFirst,
            tripleCompilation.GetSemanticModel(tripleTree), cancellationToken)
            ?? throw new InvalidOperationException("Expected a three-method recursive group.");
        if (triple.Results.Count != 3 || triple.Results.Values.Any(result /* SPINT001: all values valid - Group results are nonnull records. */ => result.Status != "Suggestion"))
        { throw new InvalidOperationException("Three-method SCC did not generalize together."); }
        var tripleRoot = triple.RewrittenRoots[tripleTree];
        var tripleOwner = tripleRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var tripleNewCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => First(\"three\", true);")!;
        var tripleTestRoot = tripleRoot.ReplaceNode(tripleOwner, tripleOwner.AddMembers(tripleNewCaller));
        var tripleTestTree = tripleTree.WithRootAndOptions(tripleTestRoot, tripleTree.Options);
        var tripleTestCompilation = tripleCompilation.ReplaceSyntaxTree(tripleTree, tripleTestTree);
        using var tripleImage = new MemoryStream();
        var tripleEmit = tripleTestCompilation.Emit(tripleImage, cancellationToken: cancellationToken);
        if (!tripleEmit.Success)
        {
            throw new InvalidOperationException("Three-method SCC failed emission: "
            + string.Join("\n", tripleEmit.Diagnostics.Select(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.ToString())));
        }
        var tripleAssembly = System.Reflection.Assembly.Load(tripleImage.ToArray());
        var tripleType = tripleAssembly.GetType("TriplePair")!;
        if (!Equals(tripleType.GetMethod("ExistingCaller")!.Invoke(null, null), 9)
            || !Equals(tripleType.GetMethod("NewCaller")!.Invoke(null, null), "three"))
        { throw new InvalidOperationException("Three-method SCC failed int/string execution."); }
        Program.Log.Info("PASS RecursiveTripleAtomicRuntime: int=9 string=three");
        const string qualifiedSource = """
            using PairAlias = global::QualifiedCycle;
            public static class QualifiedCycle
            {
                public static int First(int value, bool again) => again ? QualifiedCycle.Second(value, false) : value;
                public static int Second(int value, bool again) => again ? global::QualifiedCycle.Third(value, false) : value;
                public static int Third(int value, bool again) => again ? PairAlias.First(value, false) : value;
                public static int ExistingCaller() => QualifiedCycle.First(11, true);
            }
            """;
        var qualifiedTree = CSharpSyntaxTree.ParseText(qualifiedSource, cancellationToken: cancellationToken);
        var qualifiedCompilation = Program.Compile([qualifiedTree]);
        if (qualifiedCompilation.GetDiagnostics(cancellationToken)
            .Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
                d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Qualified-call control must compile natively."); }
        var qualifiedRoot = (CompilationUnitSyntax)qualifiedTree.GetRoot(cancellationToken);
        var qualifiedFirst = qualifiedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ =>
                method.Identifier.ValueText == "First");
        var qualified = Inference.AnalyzeRecursiveGroup(qualifiedFirst,
            qualifiedCompilation.GetSemanticModel(qualifiedTree), cancellationToken)
            ?? throw new InvalidOperationException("Expected qualified three-member recursive group.");
        if (qualified.Results.Count != 3 || qualified.Results.Values.Any(result /* SPINT001: all values valid - Group results are nonnull records. */ =>
            result.Status != "Suggestion"))
        { throw new InvalidOperationException("Qualified SCC did not generalize atomically."); }
        var qualifiedChangedRoot = qualified.RewrittenRoots[qualifiedTree];
        var qualifiedOwner = qualifiedChangedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var qualifiedCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => PairAlias.Third(\"qualified\", true);")!;
        var qualifiedTestRoot = qualifiedChangedRoot.ReplaceNode(qualifiedOwner,
            qualifiedOwner.AddMembers(qualifiedCaller));
        var qualifiedTestTree = qualifiedTree.WithRootAndOptions(qualifiedTestRoot, qualifiedTree.Options);
        var qualifiedTestCompilation = qualifiedCompilation.ReplaceSyntaxTree(qualifiedTree, qualifiedTestTree);
        using var qualifiedImage = new MemoryStream();
        var qualifiedEmit = qualifiedTestCompilation.Emit(qualifiedImage, cancellationToken: cancellationToken);
        if (!qualifiedEmit.Success)
        {
            throw new InvalidOperationException("Qualified SCC failed emission: "
            + string.Join("\n", qualifiedEmit.Diagnostics.Select(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.ToString())));
        }
        var qualifiedType = System.Reflection.Assembly.Load(qualifiedImage.ToArray()).GetType("QualifiedCycle")!;
        if (!Equals(qualifiedType.GetMethod("ExistingCaller")!.Invoke(null, null), 11)
            || !Equals(qualifiedType.GetMethod("NewCaller")!.Invoke(null, null), "qualified"))
        { throw new InvalidOperationException("Qualified SCC failed int/string runtime controls."); }
        Program.Log.Info("PASS RecursiveQualifiedAtomicRuntime: int=11 string=qualified");

        const string constructedOwnerSource = """
            public class ConstructedBox<T>
            {
                public static int First(int value, bool again) => again ? ConstructedBox<int>.Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            }
            """;
        var constructedTree = CSharpSyntaxTree.ParseText(constructedOwnerSource, cancellationToken: cancellationToken);
        var constructedCompilation = Program.Compile([constructedTree]);
        if (constructedCompilation.GetDiagnostics(cancellationToken)
            .Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
                d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Constructed-owner control must compile natively."); }
        var constructedMethods = constructedTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var constructedModel = constructedCompilation.GetSemanticModel(constructedTree);
        var declaredFirst = constructedModel.GetDeclaredSymbol(constructedMethods[0], cancellationToken)!;
        var declaredSecond = constructedModel.GetDeclaredSymbol(constructedMethods[1], cancellationToken)!;
        var constructedCall = constructedMethods[0].DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Single();
        var constructedTarget = constructedModel.GetSymbolInfo(constructedCall, cancellationToken).Symbol as IMethodSymbol;
        if (constructedTarget is null
            || !SymbolEqualityComparer.Default.Equals(constructedTarget.OriginalDefinition, declaredSecond.OriginalDefinition)
            || SymbolEqualityComparer.Default.Equals(constructedTarget.ContainingType, declaredFirst.ContainingType))
        { throw new InvalidOperationException("Constructed-owner fixture does not exercise OriginalDefinition aliasing."); }
        if (constructedMethods.Any(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ =>
            Inference.AnalyzeRecursiveGroup(method, constructedModel, cancellationToken) is not null))
        { throw new InvalidOperationException("Constructed C<int> peer entered C<T> monomorphic SCC."); }
        Program.Log.Info("PASS RecursiveConstructedOwnerRejected");
        const string polymorphicRecursion = """
            public static class RecursivePair
            {
                public static object First(object value, bool again) => again ? Second(new[] { value }, false) : value;
                public static object Second(object[] values, bool again) => again ? First(values, false) : values[0];
            }
            """;
        var rejectedTree = CSharpSyntaxTree.ParseText(polymorphicRecursion,
            cancellationToken: cancellationToken);
        var rejectedCompilation = Program.Compile([rejectedTree]);
        if (rejectedCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Polymorphic recursion control must be native C# valid."); }
        var rejectedFirst = rejectedTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(rejectedFirst,
            rejectedCompilation.GetSemanticModel(rejectedTree), cancellationToken) is not null)
        { throw new InvalidOperationException("Polymorphic recursion passed a monomorphic group."); }
        Program.Log.Info("PASS RecursiveGroupPolymorphicRecursionRejected");
        const string unsupportedHelper = """
            public static class HelperCycle
            {
                public static int First(int value, bool again) => again ? Second(value, false) : System.Math.Abs(value);
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            }
            """;
        var helperTree = CSharpSyntaxTree.ParseText(unsupportedHelper, cancellationToken: cancellationToken);
        var helperCompilation = Program.Compile([helperTree]);
        if (helperCompilation.GetDiagnostics(cancellationToken).Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Unsupported helper control must be native C# valid."); }
        var helperFirst = helperTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(helperFirst,
            helperCompilation.GetSemanticModel(helperTree), cancellationToken) is not null)
        { throw new InvalidOperationException("External helper entered the core-only group path."); }
        Program.Log.Info("PASS RecursiveGroupCoreBoundary");
        const string suppressedWarning = """
            #pragma warning disable CS0618
            public static class WarningCycle
            {
                [System.Obsolete("old")] public static int Old(int value) => value;
                public static int First(int value, bool again) => again ? Second(value, false) : Old(value);
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            }
            #pragma warning restore CS0618
            """;
        var warningTree = CSharpSyntaxTree.ParseText(suppressedWarning, cancellationToken: cancellationToken);
        var warningCompilation = Program.Compile([warningTree]).WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error));
        if (warningCompilation.GetDiagnostics(cancellationToken).Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Suppressed warning control must compile under warning promotion: " + string.Join("; ", warningCompilation.GetDiagnostics(cancellationToken).Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        var unsuppressedTree = CSharpSyntaxTree.ParseText(
            suppressedWarning.Replace("#pragma warning disable CS0618", string.Empty, StringComparison.Ordinal),
            cancellationToken: cancellationToken);
        if (!Program.Compile([unsuppressedTree]).WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error))
            .GetDiagnostics(cancellationToken).Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "CS0618" && d.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Warning promotion control did not trigger CS0618."); }
        var warningFirst = warningTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "First"; });
        if (Inference.AnalyzeRecursiveGroup(warningFirst,
            warningCompilation.GetSemanticModel(warningTree), cancellationToken) is not null)
        { throw new InvalidOperationException("Warning suppression widened SCC core admission."); }
        Program.Log.Info("PASS RecursiveGroupWarningSuppressionBoundary");

        const string unrelatedWarning = """
            public static class WarningPair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            }
            public sealed class UnrelatedWarning { private int unused; }
            """;
        var unrelatedTree = CSharpSyntaxTree.ParseText(unrelatedWarning, cancellationToken: cancellationToken);
        var unrelatedCompilation = Program.Compile([unrelatedTree]);
        if (!unrelatedCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Id == "CS0169" && d.Severity == DiagnosticSeverity.Warning))
        { throw new InvalidOperationException("Unrelated warning control must produce CS0169."); }
        var unrelatedFirst = unrelatedTree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(unrelatedFirst,
            unrelatedCompilation.GetSemanticModel(unrelatedTree), cancellationToken) is null)
        { throw new InvalidOperationException("Existing unrelated warning blocked a valid SCC."); }
        Program.Log.Info("PASS RecursiveGroupExistingWarningPreserved");

        var promotedCompilation = unrelatedCompilation.WithOptions(new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, generalDiagnosticOption: ReportDiagnostic.Error));
        if (!promotedCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Id == "CS0169" && d.Severity == DiagnosticSeverity.Error)
            || Inference.AnalyzeRecursiveGroup(unrelatedFirst,
                promotedCompilation.GetSemanticModel(unrelatedTree), cancellationToken) is not null)
        { throw new InvalidOperationException("Promoted CS0169 must block the SCC."); }
        Program.Log.Info("PASS RecursiveGroupPromotedWarningRejected");

        var styleOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            { ["CS8019"] = ReportDiagnostic.Error });
        const string existingStyleError = """
            using System;
            public static class ExistingStylePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            }
            """;
        var existingStyleTree = CSharpSyntaxTree.ParseText(existingStyleError,
            cancellationToken: cancellationToken);
        var existingStyleCompilation = Program.Compile([existingStyleTree]).WithOptions(styleOptions);
        if (!existingStyleCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Id == "CS8019" && d.Severity == DiagnosticSeverity.Error
                && d.DefaultSeverity == DiagnosticSeverity.Hidden))
        { throw new InvalidOperationException("Existing style-error control must promote CS8019."); }
        var existingStyleFirst = existingStyleTree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(existingStyleFirst,
            existingStyleCompilation.GetSemanticModel(existingStyleTree), cancellationToken) is null)
        { throw new InvalidOperationException("Existing promoted style error blocked a valid SCC."); }
        Program.Log.Info("PASS RecursiveGroupExistingStyleErrorPreserved");

        const string newStyleError = """
            using System;
            public static class NewStylePair
            {
                public static DateTime First(DateTime value, bool again) => again ? Second(value, false) : value;
                public static DateTime Second(DateTime value, bool again) => again ? First(value, false) : value;
            }
            """;
        var newStyleTree = CSharpSyntaxTree.ParseText(newStyleError, cancellationToken: cancellationToken);
        var newStyleCompilation = Program.Compile([newStyleTree]);
        var newStyleFirst = newStyleTree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(newStyleFirst,
            newStyleCompilation.GetSemanticModel(newStyleTree), cancellationToken) is null)
        { throw new InvalidOperationException("New style-error control lacks a valid unpromoted group."); }
        var newStylePromoted = newStyleCompilation.WithOptions(styleOptions);
        if (newStylePromoted.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Id == "CS8019" && d.Severity == DiagnosticSeverity.Error)
            || Inference.AnalyzeRecursiveGroup(newStyleFirst,
                newStylePromoted.GetSemanticModel(newStyleTree), cancellationToken) is not null)
        { throw new InvalidOperationException("Newly introduced promoted CS8019 must block the SCC."); }
        Program.Log.Info("PASS RecursiveGroupNewStyleErrorRejected");

        const string nameofGroupSource = """
            public static class NameofPair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
                public static string NameInGroupTree(int value) => nameof(value);
            }
            """;
        const string nameofOtherSource = """
            public static class NameofOther
            {
                public static string NameInOtherTree(int value) => nameof(value);
            }
            """;
        var nameofGroupTree = CSharpSyntaxTree.ParseText(nameofGroupSource, cancellationToken: cancellationToken);
        var nameofOtherTree = CSharpSyntaxTree.ParseText(nameofOtherSource, cancellationToken: cancellationToken);
        var nameofCompilation = Program.Compile([nameofGroupTree, nameofOtherTree]);
        if (nameofCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Unrelated nameof control must compile natively."); }
        foreach (var tree in new[] { nameofGroupTree, nameofOtherTree })
        {
            var invocation = tree.GetRoot(cancellationToken).DescendantNodes()
                .OfType<InvocationExpressionSyntax>().Single(call /* SPINT001: all values valid - Syntax descendants are nonnull invocations. */ =>
                    call.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
            if (nameofCompilation.GetSemanticModel(tree).GetOperation(invocation, cancellationToken)
                is not Microsoft.CodeAnalysis.Operations.INameOfOperation)
            { throw new InvalidOperationException("nameof control must bind as INameOfOperation."); }
        }
        var nameofFirst = nameofGroupTree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First();
        var nameofGroup = Inference.AnalyzeRecursiveGroup(nameofFirst,
            nameofCompilation.GetSemanticModel(nameofGroupTree), cancellationToken)
            ?? throw new InvalidOperationException("Unrelated nameof blocked a valid SCC.");
        var nameofChangedTree = nameofGroupTree.WithRootAndOptions(
            nameofGroup.RewrittenRoots[nameofGroupTree], nameofGroupTree.Options);
        var nameofChangedCompilation = nameofCompilation.ReplaceSyntaxTree(nameofGroupTree, nameofChangedTree);
        using var nameofImage = new MemoryStream();
        if (!nameofChangedCompilation.Emit(nameofImage, cancellationToken: cancellationToken).Success)
        { throw new InvalidOperationException("Group with unrelated nameof failed emission."); }
        var nameofAssembly = System.Reflection.Assembly.Load(nameofImage.ToArray());
        if (!Equals(nameofAssembly.GetType("NameofPair")!.GetMethod("NameInGroupTree")!.Invoke(null, [3]), "value")
            || !Equals(nameofAssembly.GetType("NameofOther")!.GetMethod("NameInOtherTree")!.Invoke(null, [5]), "value"))
        { throw new InvalidOperationException("Unrelated nameof changed after group rewrite."); }
        Program.Log.Info("PASS RecursiveGroupUnrelatedNameofPreserved");

        const string dynamicCallerSource = """
            public static class DynamicPair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
                public static dynamic DynamicCaller(dynamic value) => First(value, true);
            }
            """;
        var dynamicTree = CSharpSyntaxTree.ParseText(dynamicCallerSource, cancellationToken: cancellationToken);
        var dynamicCompilation = Program.Compile([dynamicTree]);
        if (dynamicCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ =>
            d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Dynamic-call control must compile natively."); }
        var dynamicMethods = dynamicTree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().ToArray();
        var dynamicCall = dynamicMethods.Single(method /* SPINT001: all values valid - Syntax descendants are nonnull methods. */ =>
                method.Identifier.ValueText == "DynamicCaller")
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var dynamicModel = dynamicCompilation.GetSemanticModel(dynamicTree);
        if (dynamicModel.GetOperation(dynamicCall, cancellationToken)
            is not Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation)
        { throw new InvalidOperationException("Dynamic-call control must bind dynamically."); }
        if (Inference.AnalyzeRecursiveGroup(dynamicMethods[0], dynamicModel, cancellationToken) is not null)
        { throw new InvalidOperationException("Dynamic call to group member bypassed binding validation."); }
        Program.Log.Info("PASS RecursiveGroupDynamicCallRejected");

        const string instanceCycle = """
            public class InstancePair
            {
                public int First(int value, bool again) => again ? Second(value, false) : value;
                public int Second(int value, bool again) => again ? First(value, false) : value;
            }
            """;
        var instanceTree = CSharpSyntaxTree.ParseText(instanceCycle, cancellationToken: cancellationToken);
        var instanceCompilation = Program.Compile([instanceTree]);
        var instanceFirst = instanceTree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        if (Inference.AnalyzeRecursiveGroup(instanceFirst,
            instanceCompilation.GetSemanticModel(instanceTree), cancellationToken) is not null)
        { throw new InvalidOperationException("Unsupported instance group was generalized."); }
        Program.Log.Info("PASS RecursiveGroupUnsupportedRoot");
        return 15;
    }
}
