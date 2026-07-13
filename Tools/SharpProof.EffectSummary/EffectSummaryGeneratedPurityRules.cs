internal static class EffectSummaryGeneratedPurityRules
{
    internal static bool TryGetKnownGeneratedPureVisibility(string symbol, out string effectVisibilityClassification)
    {
        effectVisibilityClassification = "none";

        if (symbol is
            "System.Diagnostics.StackFrame.GetMethod()" or
            "System.Object.GetType()" or
            "System.HashCode.ToHashCode()" or
            "System.Index.get_End()" or
            "System.Index.get_Start()" or
            "System.Uri.IsWellFormedUriString(string, System.UriKind)" or
            "System.Uri.UnescapeDataString(string)" or
            "System.Decimal.Negate(decimal)" or
            "System.Decimal.op_UnaryNegation(decimal)" or
            "System.Decimal.Compare(decimal, decimal)" or
            "System.Decimal.ToDouble(decimal)" or
            "System.Buffers.ReadOnlySequence`1.Slice(long)")
            return true;

        if (string.Equals(
                symbol,
                "System.Collections.Generic.SortedList`2.IndexOfKey(!0)",
                StringComparison.Ordinal))
            return true;

        if (symbol is
            "System.Diagnostics.Debug.Assert(bool)" or
            "System.ComponentModel.BrowsableAttribute..ctor(bool)" or
            "System.ComponentModel.DescriptionAttribute..ctor(string)" or
            "System.ComponentModel.DataAnnotations.EmailAddressAttribute..ctor()" or
            "System.Diagnostics.ConditionalAttribute..ctor(string)" or
            "System.Uri.EscapeDataString(string)")
        {
            effectVisibilityClassification = "internal_only";
            return true;
        }

        if (IsPureGeneratedStringMember(symbol) ||
            IsPureGeneratedPathHelper(symbol) ||
            IsPureGeneratedExpressionFactory(symbol) ||
            IsPureGeneratedInterpolatedStringHandlerMember(symbol) ||
            IsPureGeneratedImmutableArrayMember(symbol) ||
            IsPureGeneratedValueArrayProjection(symbol) ||
            IsPureGeneratedDeterministicValueFormatting(symbol) ||
            IsPureGeneratedDeterministicNumericHelper(symbol) ||
            IsPureGeneratedImmutableCollectionPoolInfrastructure(symbol) ||
            IsPureGeneratedStableNetworkValue(symbol) ||
            IsPureGeneratedArrayPredicate(symbol) ||
            IsPureGeneratedListPredicate(symbol) ||
            IsPureGeneratedArrayRead(symbol) ||
            IsPureGeneratedArgumentGuard(symbol) ||
            IsPureGeneratedContractGuard(symbol) ||
            IsPureGeneratedConstructor(symbol) ||
            IsPureGeneratedTypeMetadata(symbol) ||
            IsPureGeneratedImmutableMember(symbol) ||
            IsPureGeneratedFileSystemMetadataGetter(symbol) ||
            IsPureGeneratedEnvironmentStableGetter(symbol) ||
            IsPureGeneratedCharHelper(symbol) ||
            IsPureGeneratedQueueFreshArray(symbol) ||
            IsPureGeneratedCultureCompare(symbol))
        {
            if (IsPureGeneratedStringMember(symbol) ||
                IsPureGeneratedPathHelper(symbol) ||
                IsPureGeneratedExpressionFactory(symbol) ||
                IsPureGeneratedInterpolatedStringHandlerMember(symbol) ||
                IsPureGeneratedImmutableArrayMember(symbol) ||
                IsPureGeneratedValueArrayProjection(symbol) ||
                IsPureGeneratedDeterministicValueFormatting(symbol) ||
                IsPureGeneratedImmutableCollectionPoolInfrastructure(symbol) ||
                IsPureGeneratedStableNetworkValue(symbol))
                effectVisibilityClassification = "internal_only";

            return true;
        }

        return false;
    }

    internal static bool TryGetKnownGeneratedImpureCategories(string symbol, out string[] categories)
    {
        categories = new[] { "impure_callee" };

        if (symbol is
            "System.Guid.NewGuid()" or
            "System.Decimal.ToInt32(decimal)")
        {
            categories = new[] { "throw" };
            return true;
        }

        if (symbol.StartsWith("System.Char.ConvertToUtf32(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Char.ConvertFromUtf32(", StringComparison.Ordinal))
        {
            categories = new[] { "throw" };
            return true;
        }

        if (symbol.StartsWith("System.IO.Path.GetFullPath(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.TimeZoneInfo.FindSystemTimeZoneById(string)", StringComparison.Ordinal))
        {
            categories = new[] { "throw" };
            return true;
        }

        if (string.Equals(symbol, "System.IO.FileSystemInfo.get_Extension()", StringComparison.Ordinal))
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Console.Beep", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Array.BinarySearch(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.String.Format(", StringComparison.Ordinal))
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Console.Read", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Console.Write", StringComparison.Ordinal))
        {
            categories = new[] { "catalog_hit" };
            return true;
        }

        if (symbol.StartsWith("System.Console.get_", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol is
            "System.Diagnostics.Stopwatch.GetTimestamp()" or
            "System.Diagnostics.Stopwatch.get_ElapsedTicks()" or
            "System.Diagnostics.Stopwatch.Start()" or
            "System.Environment.get_StackTrace()")
        {
            categories = new[] { "impure_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Diagnostics.Process.Start(", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_write" };
            return true;
        }

        if (symbol.StartsWith("System.Diagnostics.Process.GetCurrentProcess(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Diagnostics.Process.GetProcesses", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Diagnostics.Process.get_", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol.StartsWith("System.Text.StringBuilder.Append(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.AppendLine(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Clear(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Insert(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Remove(", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Text.StringBuilder.Replace(", StringComparison.Ordinal))
        {
            categories = new[] { "catalog_hit" };
            return true;
        }

        if (symbol.StartsWith("System.Threading.Tasks.Task.Run(", StringComparison.Ordinal))
        {
            categories = new[] { "caller_visible_memory_write" };
            return true;
        }

        if (symbol.StartsWith("System.Activator.CreateInstance", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Activator.CreateInstanceFrom", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.AppContext.get_TargetFrameworkName()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Environment.set_CurrentDirectory(string)", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.IO.Directory.SetCurrentDirectory(string)", StringComparison.Ordinal) ||
            symbol.StartsWith("System.IO.Path.GetTempPath", StringComparison.Ordinal) ||
            symbol.StartsWith("System.Threading.Tasks.Task.Delay(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Threading.Thread.get_CurrentThread()", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_write" };
            return true;
        }

        if (string.Equals(symbol, "System.AppDomain.get_BaseDirectory()", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)",
                StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Configuration.ConfigurationManager.get_AppSettings()",
                StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Configuration.ConfigurationManager.get_ConnectionStrings()",
                StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        if (symbol is
            "System.Environment.get_TickCount()" or
            "System.Environment.get_TickCount64()" or
            "System.Environment.get_CurrentManagedThreadId()" or
            "System.Environment.get_ExitCode()")
        {
            categories = new[] { "metadata_only_or_external" };
            return true;
        }

        if (string.Equals(symbol, "System.Environment.Exit(int)", StringComparison.Ordinal))
        {
            categories = new[] { "unknown_callee" };
            return true;
        }

        if (symbol.StartsWith("System.Array.ConvertAll(", StringComparison.Ordinal) ||
            string.Equals(symbol, "System.Collections.Generic.List`1.ForEach(System.Action`1<!0>)",
                StringComparison.Ordinal))
        {
            categories = new[] { "caller_visible_memory_write" };
            return true;
        }

        if (IsGeneratedArrayComparerSort(symbol))
        {
            categories = new[] { "global_state_read", "impure_callee" };
            return true;
        }

        if (string.Equals(symbol, "System.Security.Claims.ClaimsPrincipal.IsInRole(string)", StringComparison.Ordinal))
        {
            categories = new[] { "global_state_read" };
            return true;
        }

        return false;
    }

    internal static bool IsPureGeneratedArrayRead(string symbol)
    {
        return symbol.StartsWith("System.Array.IndexOf(", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Array.get_Length()", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedArgumentGuard(string symbol)
    {
        return symbol.StartsWith("System.ArgumentException.ThrowIfNullOrEmpty(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentException.ThrowIfNullOrWhiteSpace(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentNullException.ThrowIfNull(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentOutOfRangeException.ThrowIf", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedContractGuard(string symbol)
    {
        return symbol.StartsWith("System.Diagnostics.Contracts.Contract.Requires(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Diagnostics.Contracts.Contract.Ensures(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedConstructor(string symbol)
    {
        return symbol.StartsWith("System.ArgumentException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ArgumentNullException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.BadImageFormatException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.DivideByZeroException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.EndOfStreamException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.FlagsAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.FormatException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Index..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.InvalidOperationException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.FileNotFoundException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ComponentModel.AddingNewEventArgs..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ComponentModel.DataAnnotations.ValidationResult..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.NotImplementedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.NotSupportedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ObjectDisposedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.ObsoleteAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.OverflowException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.PlatformNotSupportedException..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Range..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.CallerArgumentExpressionAttribute..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.MethodImplAttribute..ctor(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.SerializableAttribute..ctor(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.UIntPtr..ctor(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedTypeMetadata(string symbol)
    {
        return symbol.StartsWith("System.Type.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.RuntimeType.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Reflection.MemberInfo.get_", StringComparison.Ordinal) ||
               symbol.StartsWith("System.RuntimeTypeHandle.", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Runtime.CompilerServices.TypeHandle.", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedStringMember(string symbol)
    {
        return symbol.StartsWith("System.String.Contains(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String..ctor(char", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Split(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.CompareTo(", StringComparison.Ordinal) ||
               IsPureGeneratedStringJoin(symbol) ||
               string.Equals(symbol, "System.String.Clone()", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.IndexOf(char", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedStringJoin(string symbol)
    {
        return symbol.StartsWith("System.String.Join(char, string[]", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Join(string, string[]", StringComparison.Ordinal) ||
               symbol.StartsWith("System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>",
                   StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedPathHelper(string symbol)
    {
        return symbol.StartsWith("System.IO.Path.GetExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.HasExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetFileName(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetFileNameWithoutExtension(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.GetDirectoryName(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.IO.Path.ChangeExtension(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedExpressionFactory(string symbol)
    {
        return symbol.StartsWith("System.Linq.Expressions.Expression.Parameter(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Constant(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Lambda(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Call(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Equal(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.NotEqual(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Add(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.AddChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Subtract(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.SubtractChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Multiply(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.MultiplyChecked(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Divide(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.Modulo(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.AndAlso(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.OrElse(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.GreaterThan(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.GreaterThanOrEqual(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.LessThan(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Linq.Expressions.Expression.LessThanOrEqual(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedInterpolatedStringHandlerMember(string symbol)
    {
        return string.Equals(symbol, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(int, int)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(!!0)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(string)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(string)",
                   StringComparison.Ordinal) ||
               string.Equals(symbol,
                   "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()",
                   StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedImmutableMember(string symbol)
    {
        return symbol.StartsWith("System.Collections.Immutable.ImmutableList.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Add(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.AddRange(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Insert(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.InsertRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Remove(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.RemoveAt(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.RemoveRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.Replace(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableList`1.SetItem(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableDictionary.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_Count(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_IsEmpty(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1.get_KeyComparer(",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableQueue`1.Clear()",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableStack`1.Clear()",
                   StringComparison.Ordinal) ||
               string.Equals(symbol, "System.Collections.Immutable.ImmutableStack`1.get_IsEmpty()",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableStack`1.Push(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedImmutableArrayMember(string symbol)
    {
        return symbol.StartsWith("System.Collections.Immutable.ImmutableArray.Create", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray.CreateRange", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray.ToImmutableArray",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.Slice(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.AddRange(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.InsertRange(",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableArray`1.RemoveRange(",
                   StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedValueArrayProjection(string symbol)
    {
        return symbol.StartsWith("System.Guid.ToByteArray(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedDeterministicValueFormatting(string symbol)
    {
        return symbol.StartsWith("System.Guid.ToString(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedDeterministicNumericHelper(string symbol)
    {
        return symbol.StartsWith("System.Numerics.BitOperations.", StringComparison.Ordinal) ||
               symbol.StartsWith("System.BitConverter.To", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(",
                   StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedFileSystemMetadataGetter(string symbol)
    {
        return string.Equals(symbol, "System.IO.DirectoryInfo.get_Parent()", StringComparison.Ordinal) ||
               string.Equals(symbol, "System.IO.FileInfo.get_DirectoryName()", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedEnvironmentStableGetter(string symbol)
    {
        return symbol is
            "System.Environment.get_Is64BitOperatingSystem()" or
            "System.Environment.get_Is64BitProcess()" or
            "System.Environment.get_NewLine()" or
            "System.Environment.get_HasShutdownStarted()";
    }

    internal static bool IsPureGeneratedCharHelper(string symbol)
    {
        return symbol is
                   "System.Boolean.CompareTo(bool)" or
                   "System.Char.GetNumericValue(char)" or
                   "System.Char.ToLowerInvariant(char)" or
                   "System.Char.ToUpperInvariant(char)" ||
               symbol.StartsWith("System.Char.Is", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedQueueFreshArray(string symbol)
    {
        return string.Equals(symbol, "System.Collections.Generic.Queue`1.ToArray()", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedCultureCompare(string symbol)
    {
        return symbol.StartsWith("System.Globalization.CompareInfo.Compare(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedStableNetworkValue(string symbol)
    {
        return symbol.StartsWith("System.Net.IPAddress.get_", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedImmutableCollectionPoolInfrastructure(string symbol)
    {
        return string.Equals(
                   symbol,
                   "System.Collections.Immutable.ISecurePooledObjectUser.get_PoolUserId()",
                   StringComparison.Ordinal) ||
               (symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1", StringComparison.Ordinal) &&
                symbol.Contains("GetEnumerator()", StringComparison.Ordinal)) ||
               symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1+Enumerator.",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.SortedInt32KeyNode`1+Enumerator.",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.AllocFreeConcurrentStack`1.",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.SecureObjectPool", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Immutable.SecurePooledObject`1.", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedArrayPredicate(string symbol)
    {
        return symbol.StartsWith("System.Array.Exists(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.FindIndex(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.TrueForAll(", StringComparison.Ordinal);
    }

    internal static bool IsPureGeneratedListPredicate(string symbol)
    {
        return symbol.StartsWith("System.Collections.Generic.List`1.Exists(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Generic.List`1.FindIndex(", StringComparison.Ordinal) ||
               symbol.StartsWith("System.Collections.Generic.List`1.TrueForAll(", StringComparison.Ordinal);
    }

    internal static bool IsGeneratedArrayComparerSort(string symbol)
    {
        return symbol.StartsWith("System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal) ||
               symbol.StartsWith("System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)",
                   StringComparison.Ordinal);
    }
}
