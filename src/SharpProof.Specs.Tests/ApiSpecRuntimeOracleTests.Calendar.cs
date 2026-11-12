using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private const string CalendarInputs = "minimum, maximum and interior values of each kind; results under the invariant culture and under a culture that counts every DateTimeFormat, Calendar, NumberFormat, CompareInfo and TextInfo read";

    // Every calendar and clock row: the getters, then the six comparison
    // operators of each compared type, as DefaultApiSpecCatalog declares them.
    private static void AddCalendarWitnesses(ImmutableDictionary<string, RowWitness>.Builder witnesses)
    {
        ArgumentNullException.ThrowIfNull(witnesses);
        ImmutableArray<DateTime> dates = [DateTime.MinValue, DateTime.MaxValue,
            new(2024, 2, 29, 23, 59, 59, 999, DateTimeKind.Utc), new(1999, 12, 31, 0, 0, 0, DateTimeKind.Local),
            new(1970, 1, 1, 12, 30, 15, DateTimeKind.Unspecified)];
        ImmutableArray<DateOnly> days = [DateOnly.MinValue, DateOnly.MaxValue, new(2024, 2, 29), new(1999, 12, 31)];
        ImmutableArray<TimeOnly> times = [TimeOnly.MinValue, TimeOnly.MaxValue, new(12, 30, 15, 250), new(0, 0, 1)];
        ImmutableArray<TimeSpan> spans = [TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.Zero, TimeSpan.FromTicks(-123_456_789_012L),
            new(3, 4, 5, 6, 7)];
        ImmutableArray<DateTimeOffset> offsets = [DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            new(2024, 2, 29, 1, 2, 3, TimeSpan.FromHours(-14)), new(1999, 12, 31, 23, 59, 59, TimeSpan.FromHours(14))];
        foreach (var name in (ImmutableArray<string>)["Ticks", "Year", "Month", "Day", "DayOfYear", "Hour", "Minute", "Second", "Millisecond"])
        { AddCalendarGetter<DateTime, long>(witnesses, "DateTime", name, dates); }
        AddCalendarGetter<DateTime, DateTimeKind>(witnesses, "DateTime", "Kind", dates);
        AddCalendarGetter<DateTime, DayOfWeek>(witnesses, "DateTime", "DayOfWeek", dates);
        AddCalendarGetter<DateTime, DateTime>(witnesses, "DateTime", "Date", dates);
        AddCalendarGetter<DateTime, TimeSpan>(witnesses, "DateTime", "TimeOfDay", dates);
        foreach (var name in (ImmutableArray<string>)["Year", "Month", "Day", "DayOfYear", "DayNumber"])
        { AddCalendarGetter<DateOnly, long>(witnesses, "DateOnly", name, days); }
        AddCalendarGetter<DateOnly, DayOfWeek>(witnesses, "DateOnly", "DayOfWeek", days);
        foreach (var name in (ImmutableArray<string>)["Ticks", "Hour", "Minute", "Second", "Millisecond"])
        { AddCalendarGetter<TimeOnly, long>(witnesses, "TimeOnly", name, times); }
        foreach (var name in (ImmutableArray<string>)["Ticks", "Days", "Hours", "Minutes", "Seconds", "Milliseconds"])
        { AddCalendarGetter<TimeSpan, long>(witnesses, "TimeSpan", name, spans); }
        AddCalendarGetter<DateTimeOffset, DateTime>(witnesses, "DateTimeOffset", "UtcDateTime", offsets);
        AddCalendarGetter<DateTimeOffset, long>(witnesses, "DateTimeOffset", "UtcTicks", offsets);
        AddCalendarComparisons(witnesses, "DateTime", dates);
        AddCalendarComparisons(witnesses, "DateOnly", days);
        AddCalendarComparisons(witnesses, "TimeOnly", times);
        AddCalendarComparisons(witnesses, "TimeSpan", spans);
    }

    // A getter of an int result is read through a long-typed delegate, which
    // widens it without boxing.
    private static void AddCalendarGetter<TValue, TResult>(ImmutableDictionary<string, RowWitness>.Builder witnesses,
        string type, string member, ImmutableArray<TValue> samples)
    {
        ArgumentNullException.ThrowIfNull(witnesses);
        var value = Expression.Parameter(typeof(TValue), "value");
        var read = Expression.Lambda<Func<TValue, TResult>>(
            Expression.Convert(Expression.Property(value, member), typeof(TResult)), value).Compile();
        witnesses.Add(ApiSpecTable.CalendarWitness(type, member), CalendarRow(samples.Select(sample => (Func<TResult>)(() => read(sample)))
            .ToImmutableArray()));
    }

    private static void AddCalendarComparisons<TValue>(ImmutableDictionary<string, RowWitness>.Builder witnesses,
        string type, ImmutableArray<TValue> samples)
    {
        ArgumentNullException.ThrowIfNull(witnesses);
        ImmutableArray<(string Name, ExpressionType Kind)> operators = [("op_Equality", ExpressionType.Equal),
            ("op_Inequality", ExpressionType.NotEqual), ("op_LessThan", ExpressionType.LessThan),
            ("op_LessThanOrEqual", ExpressionType.LessThanOrEqual), ("op_GreaterThan", ExpressionType.GreaterThan),
            ("op_GreaterThanOrEqual", ExpressionType.GreaterThanOrEqual)];
        foreach (var (name, kind) in operators)
        {
            var left = Expression.Parameter(typeof(TValue), "left");
            var right = Expression.Parameter(typeof(TValue), "right");
            var compare = Expression.Lambda<Func<TValue, TValue, bool>>(Expression.MakeBinary(kind, left, right), left, right).Compile();
            var calls = samples.SelectMany(first => samples.Select(second => (Func<bool>)(() => compare(first, second)))).ToImmutableArray();
            witnesses.Add(ApiSpecTable.CalendarWitness(type, name), CalendarRow(calls));
        }
    }

    private static RowWitness CalendarRow<TResult>(ImmutableArray<Func<TResult>> calls)
    {
        var edges = calls.Select(call => RuntimeEdge.For(() => CalendarSink<TResult>.Value = call())).ToImmutableArray();
        var throws = new Lazy<ThrowObservation>(() => ObserveThrows(edges));
        return Row(
            effects: Effect(CalendarInputs, () => ObserveCalendarEffects(calls), SpecEffect.WritesAmbientState),
            allocation: Allocation(CalendarInputs, edges, SpecAllocationBehavior.MayAllocate),
            throws: Throws(CalendarInputs, throws, DoesNotThrowMutation),
            termination: Termination(CalendarInputs, throws, SpecTerminationBehavior.Unknown));
    }

    // The same values come back under a culture that records every
    // culture-specific read, and none of those reads happens.
    private static SpecEffect ObserveCalendarEffects<TResult>(ImmutableArray<Func<TResult>> calls)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var trap = new CalendarCultureTrap();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = calls.Select(static call => call()).ToImmutableArray();
            CultureInfo.CurrentCulture = trap;
            CultureInfo.CurrentUICulture = trap;
            var trapped = calls.Select(static call => call()).ToImmutableArray();
            return trap.Reads == 0 && invariant.SequenceEqual(trapped) ? SpecEffect.None : SpecEffect.Unknown;
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static class CalendarSink<T>
    {
        internal static T? Value;
    }

    private sealed class CalendarCultureTrap() : CultureInfo("fr-FR", useUserOverride: false)
    {
        internal int Reads { get; private set; }

        public override Calendar Calendar
        {
            get
            {
                Reads++;
                return base.Calendar;
            }
        }

        public override DateTimeFormatInfo DateTimeFormat
        {
            get
            {
                Reads++;
                return base.DateTimeFormat;
            }
            set => base.DateTimeFormat = value;
        }

        public override NumberFormatInfo NumberFormat
        {
            get
            {
                Reads++;
                return base.NumberFormat;
            }
            set => base.NumberFormat = value;
        }

        public override CompareInfo CompareInfo
        {
            get
            {
                Reads++;
                return base.CompareInfo;
            }
        }

        public override TextInfo TextInfo
        {
            get
            {
                Reads++;
                return base.TextInfo;
            }
        }
    }
}
