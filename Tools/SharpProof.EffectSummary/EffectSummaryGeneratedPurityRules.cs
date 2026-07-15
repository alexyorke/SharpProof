internal static class EffectSummaryGeneratedPurityRules
{
    private static readonly GeneratedImpureRule[] GeneratedImpureRules =
    [
        new(["System.Guid.NewGuid()", "System.Decimal.ToInt32(decimal)"], [], ["throw"]),
        new([], ["System.Char.ConvertToUtf32(", "System.Char.ConvertFromUtf32("], ["throw"]),
        new(["System.TimeZoneInfo.FindSystemTimeZoneById(string)"], ["System.IO.Path.GetFullPath("], ["throw"]),
        new(["System.IO.FileSystemInfo.get_Extension()"], [], ["impure_callee"]),
        new([], ["System.Console.Beep", "System.Array.BinarySearch(", "System.String.Format("], ["impure_callee"]),
        new([], ["System.Console.Read", "System.Console.Write"], ["catalog_hit"]),
        new([], ["System.Console.get_"], ["global_state_read"]),
        new(
            [
                "System.Diagnostics.Stopwatch.GetTimestamp()",
                "System.Diagnostics.Stopwatch.get_ElapsedTicks()",
                "System.Diagnostics.Stopwatch.Start()",
                "System.Environment.get_StackTrace()"
            ],
            [],
            ["impure_callee"]),
        new([], ["System.Diagnostics.Process.Start("], ["global_state_write"]),
        new(
            [],
            [
                "System.Diagnostics.Process.GetCurrentProcess(",
                "System.Diagnostics.Process.GetProcesses",
                "System.Diagnostics.Process.get_"
            ],
            ["global_state_read"]),
        new(
            [],
            [
                "System.Text.StringBuilder.Append(",
                "System.Text.StringBuilder.AppendLine(",
                "System.Text.StringBuilder.Clear(",
                "System.Text.StringBuilder.Insert(",
                "System.Text.StringBuilder.Remove(",
                "System.Text.StringBuilder.Replace("
            ],
            ["catalog_hit"]),
        new([], ["System.Threading.Tasks.Task.Run("], ["caller_visible_memory_write"]),
        new(
            [
                "System.AppContext.get_TargetFrameworkName()",
                "System.Environment.set_CurrentDirectory(string)",
                "System.IO.Directory.SetCurrentDirectory(string)",
                "System.Threading.Thread.get_CurrentThread()"
            ],
            [
                "System.Activator.CreateInstance",
                "System.Activator.CreateInstanceFrom",
                "System.IO.Path.GetTempPath",
                "System.Threading.Tasks.Task.Delay("
            ],
            ["global_state_write"]),
        new(
            [
                "System.AppDomain.get_BaseDirectory()",
                "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
                "System.Configuration.ConfigurationManager.get_AppSettings()",
                "System.Configuration.ConfigurationManager.get_ConnectionStrings()"
            ],
            [],
            ["global_state_read"]),
        new(
            [
                "System.Environment.get_TickCount()",
                "System.Environment.get_TickCount64()",
                "System.Environment.get_CurrentManagedThreadId()",
                "System.Environment.get_ExitCode()"
            ],
            [],
            ["metadata_only_or_external"]),
        new(["System.Environment.Exit(int)"], [], ["unknown_callee"]),
        new(
            ["System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)"],
            ["System.Array.ConvertAll("],
            ["caller_visible_memory_write"]),
        new([], [], ["global_state_read", "impure_callee"], IsGeneratedArrayComparerSort),
        new(["System.Security.Claims.ClaimsPrincipal.IsInRole(string)"], [], ["global_state_read"])
    ];

    private static readonly GeneratedPureRule[] GeneratedPureRules =
    [
        new(
            "none",
            [
                "System.Diagnostics.StackFrame.GetMethod()",
                "System.Object.GetType()",
                "System.HashCode.ToHashCode()",
                "System.Index.get_End()",
                "System.Index.get_Start()",
                "System.Uri.IsWellFormedUriString(string, System.UriKind)",
                "System.Uri.UnescapeDataString(string)",
                "System.Decimal.Negate(decimal)",
                "System.Decimal.op_UnaryNegation(decimal)",
                "System.Decimal.Compare(decimal, decimal)",
                "System.Decimal.ToDouble(decimal)",
                "System.Buffers.ReadOnlySequence`1.Slice(long)",
                "System.Collections.Generic.SortedList`2.IndexOfKey(!0)"
            ],
            []),
        new(
            "internal_only",
            [
                "System.Diagnostics.Debug.Assert(bool)",
                "System.ComponentModel.BrowsableAttribute..ctor(bool)",
                "System.ComponentModel.DescriptionAttribute..ctor(string)",
                "System.ComponentModel.DataAnnotations.EmailAddressAttribute..ctor()",
                "System.Diagnostics.ConditionalAttribute..ctor(string)",
                "System.Uri.EscapeDataString(string)",
                "System.String.Clone()",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(int, int)",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(!!0)",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(string)",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(string)",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()",
                "System.Collections.Immutable.ISecurePooledObjectUser.get_PoolUserId()"
            ],
            [
                "System.String.Contains(",
                "System.String..ctor(char",
                "System.String.Split(",
                "System.String.CompareTo(",
                "System.String.Join(char, string[]",
                "System.String.Join(string, string[]",
                "System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>",
                "System.String.IndexOf(char",
                "System.IO.Path.GetExtension(",
                "System.IO.Path.HasExtension(",
                "System.IO.Path.GetFileName(",
                "System.IO.Path.GetFileNameWithoutExtension(",
                "System.IO.Path.GetDirectoryName(",
                "System.IO.Path.ChangeExtension(",
                "System.Linq.Expressions.Expression.Parameter(",
                "System.Linq.Expressions.Expression.Constant(",
                "System.Linq.Expressions.Expression.Lambda(",
                "System.Linq.Expressions.Expression.Call(",
                "System.Linq.Expressions.Expression.Equal(",
                "System.Linq.Expressions.Expression.NotEqual(",
                "System.Linq.Expressions.Expression.Add(",
                "System.Linq.Expressions.Expression.AddChecked(",
                "System.Linq.Expressions.Expression.Subtract(",
                "System.Linq.Expressions.Expression.SubtractChecked(",
                "System.Linq.Expressions.Expression.Multiply(",
                "System.Linq.Expressions.Expression.MultiplyChecked(",
                "System.Linq.Expressions.Expression.Divide(",
                "System.Linq.Expressions.Expression.Modulo(",
                "System.Linq.Expressions.Expression.AndAlso(",
                "System.Linq.Expressions.Expression.OrElse(",
                "System.Linq.Expressions.Expression.GreaterThan(",
                "System.Linq.Expressions.Expression.GreaterThanOrEqual(",
                "System.Linq.Expressions.Expression.LessThan(",
                "System.Linq.Expressions.Expression.LessThanOrEqual(",
                "System.Collections.Immutable.ImmutableArray.Create",
                "System.Collections.Immutable.ImmutableArray.CreateRange",
                "System.Collections.Immutable.ImmutableArray.ToImmutableArray",
                "System.Collections.Immutable.ImmutableArray`1.Slice(",
                "System.Collections.Immutable.ImmutableArray`1.AddRange(",
                "System.Collections.Immutable.ImmutableArray`1.InsertRange(",
                "System.Collections.Immutable.ImmutableArray`1.RemoveRange(",
                "System.Guid.ToByteArray(",
                "System.Guid.ToString(",
                "System.Net.IPAddress.get_",
                "System.Collections.Immutable.ImmutableHashSet`1+Enumerator.",
                "System.Collections.Immutable.SortedInt32KeyNode`1+Enumerator.",
                "System.Collections.Immutable.AllocFreeConcurrentStack`1.",
                "System.Collections.Immutable.SecureObjectPool",
                "System.Collections.Immutable.SecurePooledObject`1."
            ],
            IsImmutableHashSetEnumeratorMethod),
        new(
            "none",
            [
                "System.Array.get_Length()",
                "System.IO.DirectoryInfo.get_Parent()",
                "System.IO.FileInfo.get_DirectoryName()",
                "System.Environment.get_Is64BitOperatingSystem()",
                "System.Environment.get_Is64BitProcess()",
                "System.Environment.get_NewLine()",
                "System.Environment.get_HasShutdownStarted()",
                "System.Boolean.CompareTo(bool)",
                "System.Char.GetNumericValue(char)",
                "System.Char.ToLowerInvariant(char)",
                "System.Char.ToUpperInvariant(char)",
                "System.Collections.Generic.Queue`1.ToArray()",
                "System.Collections.Immutable.ImmutableQueue`1.Clear()",
                "System.Collections.Immutable.ImmutableStack`1.Clear()",
                "System.Collections.Immutable.ImmutableStack`1.get_IsEmpty()"
            ],
            [
                "System.Numerics.BitOperations.",
                "System.BitConverter.To",
                "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(",
                "System.Array.Exists(",
                "System.Array.FindIndex(",
                "System.Array.TrueForAll(",
                "System.Array.IndexOf(",
                "System.Collections.Generic.List`1.Exists(",
                "System.Collections.Generic.List`1.FindIndex(",
                "System.Collections.Generic.List`1.TrueForAll(",
                "System.ArgumentException.ThrowIfNullOrEmpty(",
                "System.ArgumentException.ThrowIfNullOrWhiteSpace(",
                "System.ArgumentNullException.ThrowIfNull(",
                "System.ArgumentOutOfRangeException.ThrowIf",
                "System.Diagnostics.Contracts.Contract.Requires(",
                "System.Diagnostics.Contracts.Contract.Ensures(",
                "System.ArgumentException..ctor(",
                "System.ArgumentNullException..ctor(",
                "System.BadImageFormatException..ctor(",
                "System.DivideByZeroException..ctor(",
                "System.IO.EndOfStreamException..ctor(",
                "System.FlagsAttribute..ctor(",
                "System.FormatException..ctor(",
                "System.Index..ctor(",
                "System.InvalidOperationException..ctor(",
                "System.IO.FileNotFoundException..ctor(",
                "System.ComponentModel.AddingNewEventArgs..ctor(",
                "System.ComponentModel.DataAnnotations.ValidationResult..ctor(",
                "System.NotImplementedException..ctor(",
                "System.NotSupportedException..ctor(",
                "System.ObjectDisposedException..ctor(",
                "System.ObsoleteAttribute..ctor(",
                "System.OverflowException..ctor(",
                "System.PlatformNotSupportedException..ctor(",
                "System.Range..ctor(",
                "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute..ctor(",
                "System.Runtime.CompilerServices.MethodImplAttribute..ctor(",
                "System.SerializableAttribute..ctor(",
                "System.UIntPtr..ctor(",
                "System.Type.get_",
                "System.RuntimeType.get_",
                "System.Reflection.MemberInfo.get_",
                "System.RuntimeTypeHandle.",
                "System.Runtime.CompilerServices.TypeHandle.",
                "System.Collections.Immutable.ImmutableList.Create",
                "System.Collections.Immutable.ImmutableList`1.Add(",
                "System.Collections.Immutable.ImmutableList`1.AddRange(",
                "System.Collections.Immutable.ImmutableList`1.Insert(",
                "System.Collections.Immutable.ImmutableList`1.InsertRange(",
                "System.Collections.Immutable.ImmutableList`1.Remove(",
                "System.Collections.Immutable.ImmutableList`1.RemoveAt(",
                "System.Collections.Immutable.ImmutableList`1.RemoveRange(",
                "System.Collections.Immutable.ImmutableList`1.Replace(",
                "System.Collections.Immutable.ImmutableList`1.SetItem(",
                "System.Collections.Immutable.ImmutableHashSet.Create",
                "System.Collections.Immutable.ImmutableDictionary.Create",
                "System.Collections.Immutable.ImmutableHashSet`1.get_Count(",
                "System.Collections.Immutable.ImmutableHashSet`1.get_IsEmpty(",
                "System.Collections.Immutable.ImmutableHashSet`1.get_KeyComparer(",
                "System.Collections.Immutable.ImmutableStack`1.Push(",
                "System.Char.Is",
                "System.Globalization.CompareInfo.Compare("
            ])
    ];

    internal static bool TryGetKnownGeneratedPureVisibility(string symbol, out string effectVisibilityClassification)
    {
        effectVisibilityClassification = "none";
        foreach (var rule in GeneratedPureRules)
        {
            if (!rule.Matches(symbol)) continue;

            effectVisibilityClassification = rule.Visibility;
            return true;
        }

        return false;
    }

    internal static bool TryGetKnownGeneratedImpureCategories(string symbol, out string[] categories)
    {
        categories = new[] { "impure_callee" };

        foreach (var rule in GeneratedImpureRules)
        {
            if (!rule.Matches(symbol)) continue;

            categories = [.. rule.Categories];
            return true;
        }

        return false;
    }

    private sealed record GeneratedImpureRule(
        string[] ExactSymbols,
        string[] SymbolPrefixes,
        string[] Categories,
        Func<string, bool>? Predicate = null)
    {
        internal bool Matches(string symbol)
        {
            return ExactSymbols.Contains(symbol, StringComparer.Ordinal) ||
                   SymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal)) ||
                   Predicate?.Invoke(symbol) == true;
        }
    }

    private sealed record GeneratedPureRule(
        string Visibility,
        string[] ExactSymbols,
        string[] SymbolPrefixes,
        Func<string, bool>? Predicate = null)
    {
        internal bool Matches(string symbol)
        {
            return ExactSymbols.Contains(symbol, StringComparer.Ordinal) ||
                   SymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal)) ||
                   Predicate?.Invoke(symbol) == true;
        }
    }

    private static bool IsImmutableHashSetEnumeratorMethod(string symbol)
    {
        return symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1", StringComparison.Ordinal) &&
               symbol.Contains("GetEnumerator()", StringComparison.Ordinal);
    }

    internal static bool IsGeneratedArrayComparerSort(string symbol)
    {
        return symbol.StartsWith("System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal);
    }
}
