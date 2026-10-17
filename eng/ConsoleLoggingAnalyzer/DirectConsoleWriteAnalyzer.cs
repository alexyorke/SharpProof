using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.ConsoleLoggingAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectConsoleWriteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SPLOG001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Write terminal output through a logger",
        "Use a logger instead of calling '{0}' directly",
        "Logging",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Repository code must route console lines through a logger.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("WriteLine" or "WriteLineAsync"))
        {
            return;
        }

        var console = context.Compilation.GetTypeByMetadataName("System.Console");
        if (console is null)
        {
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, console) &&
            !IsConsoleWriter(invocation.Instance, console))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool IsConsoleWriter(IOperation? receiver, INamedTypeSymbol console)
    {
        if (console is null)
        {
            throw new System.ArgumentNullException(nameof(console));
        }
        while (receiver is IConversionOperation conversion)
        {
            receiver = conversion.Operand;
        }

        return receiver is IPropertyReferenceOperation property &&
            (property.Property.Name is "Out" or "Error") &&
            SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, console);
    }
}
