using System.Collections.Immutable;
using System.Globalization;

namespace SharpProof.Fuzz;

// Each shape is one bit of the coverage masks. The first four must keep proving.
// The written and allocating shapes need not: a local written in an enclosing
// loop and an allocation in an uncharged size are where the analysis can claim
// less than the runtime spends, so they are generated to be Unknown or correct.
internal enum ResourceBoundShape
{
    NestedLoops,
    PhasedLoop,
    CreditLoop,
    ChargedAllocation,
    WrittenCounter,
    WrittenCredit,
    WrittenPhase,
    AllocatingLoopBound,
    AllocatingLoopStart,
    AllocatingDimension
}

internal sealed record ResourceBoundCase(int Ordinal, ResourceBoundShape Shape, string Member, int ElementWidth)
{
    internal const int ShapeCount = 10;
    internal const string Parameters = "int n, int m, long w, int[] values, string text";
    public string Member { get; init; } = Member ?? throw new System.ArgumentNullException(nameof(Member));
    internal string Name => "Target" + Ordinal.ToString(CultureInfo.InvariantCulture);
}

// Every statement stays inside the analysis's closed proof rules, except the one
// shape a hazardous case exists to test. A wider grammar would only make the
// method Unknown, and an Unknown method has no bound to compare with the runtime.
internal sealed class ResourceBoundProgramGenerator
{
    private const int MaximumDepth = 3;
    private readonly Random random;
    private readonly string suffix;
    private readonly List<string> counters = [];
    private int locals;
    private int elementWidth = 1;
    private bool usesSink;

    private ResourceBoundProgramGenerator(int seed, int ordinal)
    {
        random = new Random(seed);
        suffix = ordinal.ToString(CultureInfo.InvariantCulture);
    }

    internal static ResourceBoundCase Generate(int seed, int ordinal)
    {
        RepositoryParameterGuard.RequireInitialized(ordinal < 0, nameof(ordinal), "Value must be initialized.");
        var shape = (ResourceBoundShape)(ordinal % ResourceBoundCase.ShapeCount);
        var generator = new ResourceBoundProgramGenerator(unchecked(seed + ordinal * 397), ordinal);
        var body = generator.Block(0, depth => generator.Core(shape, depth));
        var sink = generator.usesSink
            ? $" private static void Sink{generator.suffix}(int[] data, int size) {{ for (int i = 0; i < data.Length; i++) {{ var cell = new int[2]; }} for (int i = 0; i < size; i++) {{ }} }}"
            : "";
        return new(ordinal, shape, $"public static void Target{generator.suffix}({ResourceBoundCase.Parameters}) {{ {body} }}{sink}", generator.elementWidth);
    }

    private string Core(ResourceBoundShape shape, int depth)
    {
        return shape switch
        {
            ResourceBoundShape.NestedLoops => Loop(depth, inner => Loop(inner, random.Next(2) == 0 ? null : _ => Allocation())),
            ResourceBoundShape.PhasedLoop => Phased(depth),
            ResourceBoundShape.CreditLoop => Credit(depth),
            ResourceBoundShape.ChargedAllocation => Charged(depth),
            ResourceBoundShape.WrittenCounter => WrittenCounter(depth),
            ResourceBoundShape.WrittenCredit => WrittenCredit(depth),
            ResourceBoundShape.WrittenPhase => WrittenPhase(depth),
            ResourceBoundShape.AllocatingLoopBound => AllocatingLoopBound(depth),
            ResourceBoundShape.AllocatingLoopStart => AllocatingLoopStart(depth),
            ResourceBoundShape.AllocatingDimension => AllocatingDimension(depth),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private string Block(int depth, Func<int, string>? core)
    {
        var statements = new List<string>();
        for (var count = random.Next(3); count > 0; count--)
        { statements.Add(Statement(depth)); }
        if (core is not null)
        {
            var site = depth < 2 && random.Next(3) == 0 ? Loop(depth, core) : core(depth);
            statements.Insert(random.Next(statements.Count + 1), site);
        }
        return string.Join(" ", statements);
    }

    private string Statement(int depth)
    {
        return (depth >= MaximumDepth ? random.Next(3, 6) : random.Next(6)) switch
        {
            0 or 1 => Loop(depth, null),
            2 => $"if ({Condition()}) {{ {Block(depth + 1, null)} }} else {{ {Block(depth + 1, null)} }}",
            3 or 4 => Allocation(),
            _ => Scalar()
        };
    }

    private string Loop(int depth, Func<int, string>? core)
    {
        var counter = Local();
        switch (random.Next(9))
        {
            case 0:
                return $"for (int {counter} = {Literal(0, 2)}; {counter} < {Size()}; {counter}++) {{ {Body(counter, depth, core)} }}";
            case 1:
                return $"for (int {counter} = {Literal(0, 2)}; {counter} < {Literal(1, 9)}; {counter} += {Literal(2, 3)}) {{ {Body(counter, depth, core)} }}";
            case 2:
                return $"for (long {counter} = {Literal(0, 2)}; {counter} < {LongSize()}; {counter}++) {{ {Block(depth + 1, core)} }}";
            case 3:
                return $"for (long {counter} = 1; {counter} < {Size()}; {counter} *= {Literal(2, 3)}) {{ {Block(depth + 1, core)} }}";
            case 4:
                return $"for (int {counter} = {Size()}; {counter} > {Literal(0, 2)}; {counter} -= {Literal(1, 3)}) {{ {Body(counter, depth, core)} }}";
            case 5:
                return $"for (int {counter} = {Size()}; {counter} > 0; {counter} /= {Literal(2, 3)}) {{ {Body(counter, depth, core)} }}";
            case 6:
                return $"int {counter} = {Literal(0, 2)}; while ({counter} < {Size()}) {{ {Body(counter, depth, core)} {counter}++; }}";
            case 7:
                return $"int {counter} = {Size()}; while ({counter} > {Literal(0, 2)}) {{ {Body(counter, depth, core)} {counter}--; }}";
            default:
                // The write is sound here because the declaration above runs
                // again on each enclosing iteration.
                return $"int {counter} = {Size()}; while ({counter} > 0) {{ {Body(counter, depth, core)} {counter} -= {Literal(1, 2)}; }} {counter} = {Restored()};";
        }
    }

    private string Body(string counter, int depth, Func<int, string>? core)
    {
        counter = RepositoryParameterGuard.NotNull(counter, nameof(counter));
        counters.Add(counter);
        var body = Block(depth + 1, core);
        counters.RemoveAt(counters.Count - 1);
        return body;
    }

    private string Phased(int depth)
    {
        var left = Local();
        var right = Local();
        return $"int {left} = {Size()}, {right} = {Size()}; while ({left} > 0 || {right} > 0) {{ if ({left} > 0) {{ {Block(depth + 1, null)} {left}{PhaseStep()}; }} else {{ {Block(depth + 1, null)} {right}{PhaseStep()}; }} }}";
    }

    private string Credit(int depth)
    {
        var credit = Local();
        var index = Local();
        var (declaration, deposit) = random.Next(2) == 0
            ? ($"int {credit} = 0;", $"{credit}++;")
            : ($"long {credit} = {Literal(0, 3)};", $"{credit} += {Literal(2, 3)};");
        var bound = Size();
        var prefix = Body(index, depth, null);
        var drain = $"while ({credit} > 0) {{ {Block(depth + 1, null)} {credit}--; }}";
        return $"{declaration} for (int {index} = 0; {index} < {bound}; {index}++) {{ {deposit} {prefix} {(random.Next(3) == 0 ? $"if ({Condition()}) {{ {drain} }}" : drain)} }}";
    }

    private string Charged(int depth)
    {
        var local = Local();
        return random.Next(3) switch
        {
            0 => $"int {local} = new {ElementType()}[{Size()}].Length; while ({local} > 0) {{ {Block(depth + 1, null)} {local}--; }}",
            1 => $"int {local} = new {ElementType()}[{Literal(0, 9)}].Length; {Allocation()} while ({local} > {Literal(0, 2)}) {{ {local} -= {Literal(1, 2)}; }}",
            _ => Sink(Size(), Size())
        };
    }

    private string WrittenCounter(int depth)
    {
        var counter = Local();
        var loop = Local();
        var outer = Local();
        var start = Literal(1, 3);
        return random.Next(4) switch
        {
            0 => $"int {counter} = {start}; for (int {loop} = 0; {loop} < {Size()}; {loop}++) {{ {Block(depth + 1, null)} while ({counter} > 0) {{ {Block(depth + 2, null)} {counter}--; }} {counter} = {Restored()}; }}",
            1 => $"int {counter} = 0; for (int {loop} = 0; {loop} < {Size()}; {loop}++) {{ while ({counter} < {Literal(2, 5)}) {{ {counter}++; }} {counter} = -{Parameter()}; }}",
            2 => $"int {counter} = {start}; int {loop} = {Size()}; while ({loop} > 0) {{ while ({counter} > 0) {{ {counter}--; }} {counter} = {Restored()}; {loop}--; }}",
            _ => $"int {counter} = {start}; for (int {outer} = 0; {outer} < {Size()}; {outer}++) {{ for (int {loop} = 0; {loop} < {Literal(1, 3)}; {loop}++) {{ while ({counter} > 0) {{ {counter}--; }} }} {counter} = {Restored()}; }}"
        };
    }

    private string WrittenCredit(int depth)
    {
        var credit = Local();
        var index = Local();
        var outer = Local();
        var inner = Local();
        return random.Next(3) switch
        {
            0 => $"int {credit} = 0; for (int {outer} = 0; {outer} < {Size()}; {outer}++) {{ for (int {index} = 0; {index} < {Size()}; {index}++) {{ {credit}++; while ({credit} > 0) {{ {credit}--; }} }} {credit} = {Restored()}; }}",
            1 => $"int {inner} = 1; int {credit} = 0; for (int {index} = 0; {index} < {Size()}; {index}++) {{ {credit}++; while ({inner} > 0) {{ {inner}--; }} {inner} = {Restored()}; while ({credit} > 0) {{ {credit}--; }} }}",
            _ => $"int {inner} = 1; int {credit} = 0; for (int {index} = 0; {index} < {Size()}; {index}++) {{ {credit}++; while ({credit} > 0) {{ {Block(depth + 2, null)} while ({inner} > 0) {{ {inner}--; }} {inner} = {Restored()}; {credit}--; }} }}"
        };
    }

    private string WrittenPhase(int depth)
    {
        var left = Local();
        var right = Local();
        var loop = Local();
        var inner = Local();
        return random.Next(2) == 0
            ? $"int {left} = 1, {right} = 1; for (int {loop} = 0; {loop} < {Size()}; {loop}++) {{ while ({left} > 0 || {right} > 0) {{ if ({left} > 0) {{ {left}--; }} else {{ {right}--; }} }} {left} = {Restored()}; {right} = {Restored()}; }}"
            : $"int {inner} = 1; int {left} = {Size()}, {right} = {Size()}; while ({left} > 0 || {right} > 0) {{ if ({left} > 0) {{ {Block(depth + 2, null)} while ({inner} > 0) {{ {inner}--; }} {inner} = {Restored()}; {left}--; }} else {{ {right}--; }} }}";
    }

    private string AllocatingLoopBound(int depth)
    {
        var counter = Local();
        var bound = AllocatingSize();
        return random.Next(3) switch
        {
            0 => $"for (int {counter} = 0; {counter} < {bound}; {counter}++) {{ {Body(counter, depth, null)} }}",
            1 => $"for (long {counter} = 0; {counter} < {bound}; {counter}++) {{ {Block(depth + 1, null)} }}",
            _ => $"int {counter} = 0; while ({counter} < {bound}) {{ {Body(counter, depth, null)} {counter}++; }}"
        };
    }

    private string AllocatingLoopStart(int depth)
    {
        var counter = Local();
        return $"for (int {counter} = {AllocatingSize()}; {counter} > {Literal(0, 1)}; {counter}--) {{ {Body(counter, depth, null)} }}";
    }

    private string AllocatingDimension(int depth)
    {
        var local = Local();
        return random.Next(3) switch
        {
            0 => $"var {local} = new {ElementType()}[{AllocatingSize()}];",
            1 => Sink(AllocatingSize(), Size()),
            _ => $"int {local} = new {ElementType()}[{AllocatingSize()}].Length; while ({local} > 0) {{ {Block(depth + 1, null)} {local}--; }}"
        };
    }

    private string Sink(string length, string size)
    {
        length = RepositoryParameterGuard.NotNull(length, nameof(length));
        size = RepositoryParameterGuard.NotNull(size, nameof(size));
        usesSink = true;
        elementWidth = Math.Max(elementWidth, 4);
        return $"Sink{suffix}(new int[{length}], {size});";
    }

    private string Allocation()
    {
        return $"var {Local()} = new {ElementType()}[{Size()}];";
    }

    private string Scalar()
    {
        var local = Local();
        return $"int {local} = {Size()}; {local} = {local} + {Literal(1, 3)};";
    }

    private string Condition()
    {
        return random.Next(4) switch
        {
            0 => "n > m",
            1 => "m > " + Literal(0, 4),
            2 => "values.Length > " + Literal(0, 2),
            _ => "text.Length == " + Literal(0, 2)
        };
    }

    private string Size()
    {
        return random.Next(counters.Count == 0 ? 7 : 9) switch
        {
            0 or 1 or 2 => Literal(0, 6),
            3 => "n",
            4 => "m",
            5 => "values.Length",
            6 => "text.Length",
            _ => counters[random.Next(counters.Count)]
        };
    }

    private string LongSize()
    {
        return random.Next(3) == 0 ? "w" : Size();
    }

    private string AllocatingSize()
    {
        return random.Next(4) switch
        {
            0 or 1 => $"new {ElementType()}[{Literal(1, 9)}].Length",
            2 => $"new {ElementType()}[{Size()}].Length",
            _ => $"new string('x', {Literal(1, 9)}).Length"
        };
    }

    private string Restored()
    {
        return random.Next(6) switch
        {
            0 or 1 => "m",
            2 => "n",
            3 => "m + 1",
            4 => "n + m",
            _ => "values.Length"
        };
    }

    private string Parameter()
    {
        return random.Next(2) == 0 ? "m" : "n";
    }

    private string PhaseStep()
    {
        return random.Next(3) switch
        {
            0 => "--",
            1 => " -= " + Literal(2, 3),
            _ => " /= " + Literal(2, 3)
        };
    }

    private string ElementType()
    {
        var (name, width) = random.Next(8) switch
        {
            0 => ("byte", 1),
            1 => ("bool", 1),
            2 => ("short", 2),
            3 => ("char", 2),
            4 => ("int", 4),
            5 => ("float", 4),
            6 => ("long", 8),
            _ => ("double", 8)
        };
        elementWidth = Math.Max(elementWidth, width);
        return name;
    }

    private string Local()
    {
        return "v" + (locals++).ToString(CultureInfo.InvariantCulture);
    }

    private string Literal(int minimum, int maximum)
    {
        return random.Next(minimum, maximum + 1).ToString(CultureInfo.InvariantCulture);
    }
}
