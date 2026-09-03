using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Ir;

namespace SharpProof.Testing;

public enum DifferentialStatus
{
    Agreement,
    Abstained,
    Mismatch
}

public sealed record DifferentialResult(
    DifferentialStatus Status,
    IrEvaluationResult Interpreted,
    string Detail);

public sealed class IrCSharpDifferentialOracle(IrFactory factory)
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IrInterpreter _interpreter = new(factory);

    public DifferentialResult Compare(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue> variables)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(variables);

        var interpreted = _interpreter.Evaluate(term, variables);
        if (!TryCreateProgram(term, variables, out var program, out var orderedVariables, out var reason))
        {
            return new DifferentialResult(DifferentialStatus.Abstained, interpreted, reason);
        }

        var tree = CSharpSyntaxTree.ParseText(
            program,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "SharpProofOracle_" + term.Id.Value.ToString(CultureInfo.InvariantCulture),
            [tree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            var errors = string.Join(
                " | ",
                emit.Diagnostics
                    .Where(static value => value.Severity == DiagnosticSeverity.Error)
                    .OrderBy(static value => value.Location.SourceSpan.Start)
                    .Select(static value => value.Id + ": " + value.GetMessage(CultureInfo.InvariantCulture)));
            return new DifferentialResult(
                DifferentialStatus.Mismatch,
                interpreted,
                "Generated C# did not compile: " + errors);
        }

        var loadContext = new DifferentialOracleLoadContext();
        try
        {
            image.Position = 0;
            var assembly = loadContext.LoadFromStream(image);
            var method = assembly.GetType("SharpProofGeneratedOracle")!.GetMethod(
                "Evaluate",
                BindingFlags.Public | BindingFlags.Static)!;
            var runtimeValues = new Dictionary<IrValue, object?>(
                ReferenceEqualityComparer.Instance);
            var actual = method.Invoke(
                null,
                [.. orderedVariables.Select(
                    binding => ToRuntimeValue(variables[binding], runtimeValues))]);
            return CompareValue(interpreted, actual);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            return CompareException(interpreted, exception.InnerException);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private sealed class DifferentialOracleLoadContext() :
        AssemblyLoadContext(
            "SharpProofDifferentialOracle",
            isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return null;
        }
    }

    private bool TryCreateProgram(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue> values,
        out string program,
        out ImmutableArray<IrVarId> orderedVariables,
        out string reason)
    {
        var variables = new SortedDictionary<int, IrVarId>();
        var terms = new List<IrTerm>();
        if (!TryCollectTerms(
                term,
                variables,
                new HashSet<IrId>(),
                terms,
                out reason))
        {
            program = "";
            orderedVariables = [];
            return false;
        }

        orderedVariables = [.. variables.Values];
        foreach (var variable in orderedVariables)
        {
            if (!values.TryGetValue(variable, out var value))
            {
                program = "";
                reason = "A referenced variable has no concrete value.";
                return false;
            }

            var variableType = _factory.GetVariableInfo(variable).Type;
            if (value == null || value.Type != variableType)
            {
                program = "";
                reason = "A referenced variable has an invalid concrete value.";
                return false;
            }
        }

        if (!TryGetCSharpType(term.Type, out var returnType))
        {
            program = "";
            reason = "The result type is outside the executable oracle subset.";
            return false;
        }

        var source = new StringBuilder();
        source.AppendLine("#nullable enable");
        source.AppendLine("public static class SharpProofGeneratedOracle {");
        source.AppendLine("    private static long Value(long value) => value;");
        source.Append("    public static ");
        source.Append(returnType);
        source.Append(" Evaluate(");
        for (var index = 0; index < orderedVariables.Length; index++)
        {
            if (index != 0)
            {
                source.Append(", ");
            }

            var variable = orderedVariables[index];
            if (!TryGetCSharpType(
                    _factory.GetVariableInfo(variable).Type,
                    out var parameterType))
            {
                program = "";
                reason = "A variable type is outside the executable oracle subset.";
                return false;
            }
            source.Append(parameterType);
            source.Append(" v");
            source.Append(variable.Value.ToString(CultureInfo.InvariantCulture));
        }
        source.AppendLine(") {");
        foreach (var current in terms)
        {
            if (!TryAppendLazyDeclaration(source, current, out reason))
            {
                program = "";
                return false;
            }
        }
        source.Append("        return checked(");
        AppendLazyValue(source, term);
        source.AppendLine(");");
        source.AppendLine("    }");
        source.AppendLine("}");
        program = source.ToString();
        reason = "";
        return true;
    }

    private bool TryCollectTerms(
        IrTerm term,
        IDictionary<int, IrVarId> variables,
        ISet<IrId> visited,
        ICollection<IrTerm> terms,
        out string reason)
    {
        var pending = new Stack<(IrTerm Term, bool ChildrenReady)>();
        pending.Push((term, ChildrenReady: false));
        while (pending.Count != 0)
        {
            var (current, childrenReady) = pending.Pop();
            if (childrenReady)
            {
                terms.Add(current);
                continue;
            }

            if (!visited.Add(current.Id))
            {
                continue;
            }
            if (!TryGetCSharpType(current.Type, out _))
            {
                reason = "The result type is outside the executable oracle subset.";
                return false;
            }

            if (current is IrOpaqueTerm)
            {
                reason = "The term contains an opaque call.";
                return false;
            }
            if (current is not (IrBooleanTerm or IrIntegerTerm or IrStringTerm or
                IrNullTerm or IrVariableTerm or IrUnaryTerm or IrBinaryTerm or
                IrConditionalTerm or IrCastTerm or IrLengthTerm or
                IrSequenceAccessTerm))
            {
                reason = "The term kind is outside the executable oracle subset.";
                return false;
            }
            if (current is IrVariableTerm variable)
            {
                variables[variable.Variable.Value] = variable.Variable;
            }

            pending.Push((current, ChildrenReady: true));
            var children = IrTraversal.GetChildren(current);
            for (var index = children.Length - 1; index >= 0; index--)
            {
                pending.Push((children[index], ChildrenReady: false));
            }
        }

        reason = "";
        return true;
    }

    private bool TryAppendLazyDeclaration(
        StringBuilder builder,
        IrTerm term,
        out string reason)
    {
        if (!TryGetCSharpType(term.Type, out var type))
        {
            reason = "The result type is outside the executable oracle subset.";
            return false;
        }

        builder.Append("        var t");
        builder.Append(term.Id.Value.ToString(CultureInfo.InvariantCulture));
        builder.Append(" = new System.Lazy<");
        builder.Append(type);
        builder.Append(">(() => checked(");
        switch (term)
        {
            case IrBooleanTerm boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            case IrIntegerTerm integer:
                AppendInteger(builder, integer.Value);
                break;
            case IrStringTerm text:
                builder.Append(SymbolDisplay.FormatLiteral(
                    _factory.GetString(text.Value),
                    quote: true));
                break;
            case IrNullTerm:
                builder.Append("((");
                builder.Append(type);
                builder.Append(")null!)");
                break;
            case IrVariableTerm variable:
                builder.Append('v');
                builder.Append(variable.Variable.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case IrUnaryTerm unary:
                builder.Append('(');
                builder.Append(unary.Operator == IrUnaryOperator.Not ? '!' : '-');
                AppendLazyValue(builder, unary.Operand);
                builder.Append(')');
                break;
            case IrBinaryTerm binary:
                builder.Append('(');
                AppendLazyValue(builder, binary.Left);
                builder.Append(' ');
                builder.Append(BinaryToken(binary.Operator));
                builder.Append(' ');
                AppendLazyValue(builder, binary.Right);
                builder.Append(')');
                break;
            case IrConditionalTerm conditional:
                builder.Append('(');
                AppendLazyValue(builder, conditional.Condition);
                builder.Append(" ? ");
                AppendLazyValue(builder, conditional.WhenTrue);
                builder.Append(" : ");
                AppendLazyValue(builder, conditional.WhenFalse);
                builder.Append(')');
                break;
            case IrCastTerm cast:
                builder.Append("((");
                builder.Append(type);
                builder.Append(')');
                AppendLazyValue(builder, cast.Operand);
                builder.Append(')');
                break;
            case IrLengthTerm length:
                builder.Append('(');
                AppendLazyValue(builder, length.Value);
                builder.Append(").Length");
                break;
            case IrSequenceAccessTerm access:
                builder.Append('(');
                AppendLazyValue(builder, access.Sequence);
                builder.Append(")[");
                AppendLazyValue(builder, access.Index);
                builder.Append(']');
                break;
            case IrOpaqueTerm:
                reason = "The term contains an opaque call.";
                return false;
            default:
                reason = "The term kind is outside the executable oracle subset.";
                return false;
        }
        builder.AppendLine("));");
        reason = "";
        return true;
    }

    private static void AppendLazyValue(StringBuilder builder, IrTerm term)
    {
        builder.Append('t');
        builder.Append(term.Id.Value.ToString(CultureInfo.InvariantCulture));
        builder.Append(".Value");
    }

    private bool TryGetCSharpType(IrTypeId type, out string name)
    {
        return TryGetSupportedType(type, out name, out _);
    }

    private bool TryGetSupportedType(
        IrTypeId type,
        out string csharpName,
        out Type? runtimeType)
    {
        var info = _factory.GetTypeInfo(type);
        if (info.Kind == IrTypeKind.Sequence &&
            info.ElementType != null &&
            TryGetSupportedType(
                info.ElementType.Value,
                out var elementName,
                out var elementRuntimeType))
        {
            csharpName = elementName + "[]";
            runtimeType = elementRuntimeType!.MakeArrayType();
            return true;
        }

        (csharpName, runtimeType) = info.Kind switch
        {
            IrTypeKind.Boolean => ("bool", typeof(bool)),
            IrTypeKind.Integer => ("long", typeof(long)),
            IrTypeKind.String => ("string", typeof(string)),
            IrTypeKind.Reference when type == _factory.ObjectType =>
                ("object", typeof(object)),
            _ => ("", null)
        };
        return runtimeType != null;
    }

    private static string BinaryToken(IrBinaryOperator @operator)
    {
        return @operator switch
        {
            IrBinaryOperator.Add => "+",
            IrBinaryOperator.Subtract => "-",
            IrBinaryOperator.Multiply => "*",
            IrBinaryOperator.Divide => "/",
            IrBinaryOperator.Remainder => "%",
            IrBinaryOperator.AndAlso => "&&",
            IrBinaryOperator.OrElse => "||",
            IrBinaryOperator.Equal => "==",
            IrBinaryOperator.NotEqual => "!=",
            IrBinaryOperator.LessThan => "<",
            IrBinaryOperator.LessThanOrEqual => "<=",
            IrBinaryOperator.GreaterThan => ">",
            IrBinaryOperator.GreaterThanOrEqual => ">=",
            IrBinaryOperator.StringConcat => "+",
            _ => throw new ArgumentOutOfRangeException(nameof(@operator))
        };
    }

    private static void AppendInteger(StringBuilder builder, long value)
    {
        builder.Append("Value(");
        if (value == long.MinValue)
        {
            builder.Append("long.MinValue");
            builder.Append(')');
            return;
        }
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append('L');
        builder.Append(')');
    }

    private object? ToRuntimeValue(
        IrValue value,
        IDictionary<IrValue, object?> converted)
    {
        if (converted.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var runtimeValue = value.Kind switch
        {
            IrValueKind.Boolean => value.Boolean,
            IrValueKind.Integer => value.Integer,
            IrValueKind.String => value.String,
            IrValueKind.Null => null,
            IrValueKind.Reference => value.Reference,
            IrValueKind.Sequence => ToRuntimeArray(value, converted),
            _ => throw new InvalidOperationException(
                "The concrete value is outside the executable oracle subset.")
        };
        converted[value] = runtimeValue;
        return runtimeValue;
    }

    private Array ToRuntimeArray(
        IrValue value,
        IDictionary<IrValue, object?> converted)
    {
        var info = _factory.GetTypeInfo(value.Type);
        if (info.ElementType == null ||
            !TryGetRuntimeType(info.ElementType.Value, out var elementType))
        {
            throw new InvalidOperationException(
                "The sequence element type is outside the executable oracle subset.");
        }

        var result = Array.CreateInstance(elementType, value.Elements.Length);
        converted[value] = result;
        for (var index = 0; index < value.Elements.Length; index++)
        {
            result.SetValue(
                ToRuntimeValue(value.Elements[index], converted),
                index);
        }

        return result;
    }

    private bool TryGetRuntimeType(IrTypeId type, out Type runtimeType)
    {
        var supported = TryGetSupportedType(
            type,
            out _,
            out var supportedRuntimeType);
        runtimeType = supportedRuntimeType!;
        return supported;
    }

    private static DifferentialResult CompareValue(
        IrEvaluationResult interpreted,
        object? actual)
    {
        if (interpreted.Status != IrEvaluationStatus.Value)
        {
            return new DifferentialResult(
                DifferentialStatus.Mismatch,
                interpreted,
                "Compiled C# returned normally while the IR did not.");
        }

        var agrees = ValuesAgree(interpreted.Value!, actual);
        return new DifferentialResult(
            agrees ? DifferentialStatus.Agreement : DifferentialStatus.Mismatch,
            interpreted,
            agrees ? "" : "Compiled C# and the IR interpreter produced different values.");
    }

    private static bool ValuesAgree(IrValue interpreted, object? actual)
    {
        var comparedSequences = new Dictionary<IrValue, HashSet<Array>>(
            ReferenceEqualityComparer.Instance);
        return ValuesAgreeCore(interpreted, actual, comparedSequences);
    }

    private static bool ValuesAgreeCore(
        IrValue interpreted,
        object? actual,
        IDictionary<IrValue, HashSet<Array>> comparedSequences)
    {
        return interpreted.Kind switch
        {
            IrValueKind.Boolean =>
                actual is bool value && value == interpreted.Boolean,
            IrValueKind.Integer =>
                actual is long value && value == interpreted.Integer,
            IrValueKind.String => actual is string value &&
                                  string.Equals(
                                      value,
                                      interpreted.String,
                                      StringComparison.Ordinal),
            IrValueKind.Null => actual == null,
            IrValueKind.Reference =>
                ReferenceEquals(actual, interpreted.Reference),
            IrValueKind.Sequence =>
                SequenceAgrees(interpreted, actual, comparedSequences),
            _ => false
        };
    }

    private static bool SequenceAgrees(
        IrValue interpreted,
        object? actual,
        IDictionary<IrValue, HashSet<Array>> comparedSequences)
    {
        if (actual is not Array array ||
            array.Rank != 1 ||
            array.Length != interpreted.Elements.Length)
        {
            return false;
        }
        if (!comparedSequences.TryGetValue(interpreted, out var comparedArrays))
        {
            comparedArrays = new HashSet<Array>(ReferenceEqualityComparer.Instance);
            comparedSequences.Add(interpreted, comparedArrays);
        }
        if (!comparedArrays.Add(array))
        {
            return true;
        }
        for (var index = 0; index < array.Length; index++)
        {
            if (!ValuesAgreeCore(
                    interpreted.Elements[index],
                    array.GetValue(index),
                    comparedSequences))
            {
                return false;
            }
        }
        return true;
    }

    private static DifferentialResult CompareException(
        IrEvaluationResult interpreted,
        Exception actual)
    {
        var kind = IrExceptionKindFacts.FromException(actual);
        var agrees = interpreted.Status == IrEvaluationStatus.Exception &&
                     kind != null &&
                     interpreted.Exception!.Kind == kind.Value;
        return new DifferentialResult(
            agrees ? DifferentialStatus.Agreement : DifferentialStatus.Mismatch,
            interpreted,
            agrees
                ? ""
                : "Compiled C# threw " + actual.GetType().Name +
                  " while the IR reported " + interpreted.Status + ".");
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return [.. trustedAssemblies
            .Split(Path.PathSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }
}
