using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicComplexityTests {
    [Test]
    public void StraightLineMethod_IsConstant() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var value = n + 1;
                                      return value;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return value;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Constant));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(1)"));
    }

    [Test]
    public void SingleForLoop_ProducesLinearComplexity() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      {
                                          sum += i;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
        Assert.That(result.Drivers.Any(driver => driver.Kind == "ForLoop"), Is.True);
    }

    [Test]
    public void ForLoopWithConstantFirstIncrement_ProducesLinearComplexity() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i = 1 + i)
                                      {
                                          sum += i;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
    }

    [Test]
    public void SequentialLinearLoops_UseAsymptoticMax() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      {
                                          sum += i;
                                      }

                                      for (var j = 0; j < n; j++)
                                      {
                                          sum += j;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
    }

    [Test]
    public void NestedForLoopsOverDistinctBounds_ProduceProduct() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n, int m)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      {
                                          for (var j = 0; j < m; j++)
                                          {
                                              sum += i + j;
                                          }
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Product));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n * m)"));
    }

    [Test]
    public void FieldControlledLoopWithCall_RemainsConservativelyUnknown() {
        const string source = """
                              public sealed class C
                              {
                                  private int _index;

                                  private void Reset() => _index = 0;

                                  public int Work(int count)
                                  {
                                      for (_index = 0; _index < count; _index++)
                                      {
                                          Reset();
                                      }

                                      return _index;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return _index;");

        Assert.That(result.Complexity.IsUnknown, Is.True);
        Assert.That(
            result.UnknownReasons,
            Does.Contain(SymbolicComplexityUnknownReason.UnsupportedLoopShape));
    }

    [Test]
    public void NestedForLoopsOverSameBound_ProduceQuadratic() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      {
                                          for (var j = 0; j < n; j++)
                                          {
                                              sum += i + j;
                                          }
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Quadratic));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n^2)"));
    }

    [Test]
    public void BranchesUseWorstCaseMaximum() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(bool flag, int n)
                                  {
                                      var sum = 0;
                                      if (flag)
                                      {
                                          for (var i = 0; i < n; i++)
                                          {
                                              sum += i;
                                          }
                                      }
                                      else
                                      {
                                          sum = 1;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
    }

    [Test]
    public void ForeachOverString_IsLinearInLength() {
        const string source = """
                              public static class C
                              {
                                  public static int CountLetters(string text)
                                  {
                                      var count = 0;
                                      foreach (var ch in text)
                                      {
                                          count += ch;
                                      }

                                      return count;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return count;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(text.Length)"));
    }

    [Test]
    public void ForeachOverCollectionOutsideAnyNameTable_IsLinearInCount() {
        // HashSet is deliberately not one of the types the cost model used to name
        // explicitly. It is recognised because it exposes an instance int Count, so
        // any collection of that shape now gets a bound instead of falling to unknown.
        const string source = """
                              using System.Collections.Generic;

                              public static class C
                              {
                                  public static int SumAll(HashSet<int> values)
                                  {
                                      var sum = 0;
                                      foreach (var value in values)
                                      {
                                          sum += value;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return sum;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        // The size variable is spelled ".Length" for every sized receiver reached through
        // this path, including the Count-bearing ones such as List and Dictionary.
        Assert.That(result.Complexity.Text, Is.EqualTo("O(values.Length)"));
    }

    [Test]
    public void MonotoneWhileLoop_IsLinear() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var i = 0;
                                      while (i < n)
                                      {
                                          i++;
                                      }

                                      return i;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return i;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
    }

    [Test]
    public void MonotoneDoLoop_IsLinear() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var i = 0;
                                      do
                                      {
                                          i++;
                                      }
                                      while (i < n);

                                      return i;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return i;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
        Assert.That(result.Drivers.Any(driver => driver.Kind == "DoLoop"), Is.True);
    }

    [Test]
    public void UnsupportedWhileLoop_IsUnknown() {
        const string source = """
                              public static class C
                              {
                                  public static int Step(int value) => value + 1;

                                  public static int Work(int n)
                                  {
                                      var i = 0;
                                      while (i < n)
                                      {
                                          i = Step(i);
                                      }

                                      return i;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return i;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Unknown));
        Assert.That(result.UnknownReasons, Does.Contain(SymbolicComplexityUnknownReason.UnsupportedWhileLoop));
    }

    [Test]
    public void KnownSourceCallee_ComposesIntoSurroundingLoop() {
        const string source = """
                              public static class C
                              {
                                  public static void Helper(int m)
                                  {
                                      for (var j = 0; j < m; j++)
                                      {
                                      }
                                  }

                                  public static void Caller(int n, int m)
                                  {
                                      for (var i = 0; i < n; i++)
                                      {
                                          Helper(m);
                                      }
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "Helper(m);");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Product));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n * m)"));
        Assert.That(
            result.CalleeSummaries.Any(summary =>
                summary.MethodDisplayName.Contains("Helper", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void OpenVirtualSourceCallee_IsConservativeUnknown() {
        const string source = """
                              public class Worker
                              {
                                  public virtual void Work(int n)
                                  {
                                  }
                              }

                              public sealed class LinearWorker : Worker
                              {
                                  public override void Work(int n)
                                  {
                                      for (var i = 0; i < n; i++)
                                      {
                                      }
                                  }
                              }

                              public static class C
                              {
                                  public static void Caller(Worker worker, int n)
                                  {
                                      worker.Work(n);
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "worker.Work(n);");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Unknown));
        Assert.That(result.UnknownReasons, Does.Contain(SymbolicComplexityUnknownReason.DynamicDispatch));
        Assert.That(result.CalleeSummaries,
            Has.Some.Matches<SymbolicComplexityCalleeInfo>(summary =>
                summary.UnknownReason == SymbolicComplexityUnknownReason.DynamicDispatch));
    }

    [Test]
    public void SealedReceiverSourceOverride_ComposesImplementationComplexity() {
        const string source = """
                              public abstract class Worker
                              {
                                  public abstract void Work(int n);
                              }

                              public sealed class LinearWorker : Worker
                              {
                                  public override void Work(int n)
                                  {
                                      for (var i = 0; i < n; i++)
                                      {
                                      }
                                  }
                              }

                              public static class C
                              {
                                  public static void Caller(LinearWorker worker, int n)
                                  {
                                      worker.Work(n);
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "worker.Work(n);");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
        Assert.That(result.CalleeSummaries,
            Has.Some.Matches<SymbolicComplexityCalleeInfo>(summary =>
                summary.MethodDisplayName.Contains("LinearWorker.Work", StringComparison.Ordinal)));
    }

    [Test]
    public void ExternalUnknownCallee_ProducesUnknown() {
        const string source = """
                              using System;

                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      _ = Environment.GetEnvironmentVariable("PATH");
                                      return n;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "Environment.GetEnvironmentVariable");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Unknown));
        Assert.That(
            result.UnknownReasons,
            Has.Some.Matches<SymbolicComplexityUnknownReason>(reason =>
                reason == SymbolicComplexityUnknownReason.ExternalCallee ||
                reason == SymbolicComplexityUnknownReason.UnknownCallee));
    }

    [Test]
    public void SelfRecursiveMethod_IsRecursiveUnknown() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      if (n <= 0)
                                      {
                                          return 0;
                                      }

                                      return Work(n - 1);
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return Work");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.RecursiveUnknown));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(RecursiveUnknown)"));
    }

    [Test]
    public void MutualRecursion_IsRecursiveUnknown() {
        const string source = """
                              public static class C
                              {
                                  public static int First(int n)
                                  {
                                      return n <= 0 ? 0 : Second(n - 1);
                                  }

                                  public static int Second(int n)
                                  {
                                      return n <= 0 ? 0 : First(n - 1);
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "Second(n - 1)");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.RecursiveUnknown));
    }

    [Test]
    public void LineTarget_ResolvesContainingMethod() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      {
                                          sum += i;
                                      }

                                      return sum;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "sum += i;", true);

        Assert.That(result.MethodName, Is.EqualTo("Work"));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(n)"));
    }

    [Test]
    public void NodeTarget_ResolvesContainingLocalFunction() {
        const string source = """
                              public static class C
                              {
                                  public static int Work(int n)
                                  {
                                      int Local(int m)
                                      {
                                          for (var i = 0; i < m; i++)
                                          {
                                          }

                                          return m;
                                      }

                                      return Local(n);
                                  }
                              }
                              """;

        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            source,
            "SymbolicComplexityTests.cs",
            "SymbolicComplexityTests.cs",
            "SharpProof.Test.SymbolicComplexity",
            null,
            default);
        var root = syntaxTree.GetRoot();
        var localFunction = root.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Single(node => node.Identifier.ValueText == "Local");

        var result = new SymbolicQueryExecutor().QueryComplexity(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTargetFactory.AtPosition(localFunction.SpanStart)));

        Assert.That(result.MethodName, Is.EqualTo("Local"));
        Assert.That(result.Complexity.Text, Is.EqualTo("O(m)"));
    }

    [Test]
    public void NodeTarget_ResolvesPropertyGetter() {
        const string source = """
                              public sealed class C
                              {
                                  public int Count
                                  {
                                      get
                                      {
                                          var sum = 0;
                                          for (var i = 0; i < 10; i++) sum += i;
                                          return sum;
                                      }
                                  }
                              }
                              """;

        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            source,
            "SymbolicComplexityTests.cs",
            "SymbolicComplexityTests.cs",
            "SharpProof.Test.SymbolicComplexity",
            null,
            default);
        var property = syntaxTree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();

        var result = new SymbolicQueryExecutor().QueryComplexity(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTargetFactory.AtPosition(property.SpanStart)));

        Assert.That(result.MethodName, Is.EqualTo("get_Count"));
        Assert.That(result.DeclarationKind, Is.EqualTo("property_getter"));
        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Constant));
    }

    [Test]
    public void NodeTarget_ResolvesIndexerGetter() {
        const string source = """
                              public sealed class C
                              {
                                  public int this[int n]
                                  {
                                      get
                                      {
                                          var sum = 0;
                                          for (var i = 0; i < n; i++) sum += i;
                                          return sum;
                                      }
                                  }
                              }
                              """;

        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            source,
            "SymbolicComplexityTests.cs",
            "SymbolicComplexityTests.cs",
            "SharpProof.Test.SymbolicComplexity",
            null,
            default);
        var indexer = syntaxTree.GetRoot().DescendantNodes().OfType<IndexerDeclarationSyntax>().Single();

        var result = new SymbolicQueryExecutor().QueryComplexity(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTargetFactory.AtPosition(indexer.SpanStart)));

        Assert.That(result.MethodName, Is.EqualTo("get_Item"));
        Assert.That(result.DeclarationKind, Is.EqualTo("indexer_getter"));
        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Linear));
    }

    [Test]
    public void UnsupportedForLoop_AggregatesPreLoopEvidenceOnce() {
        const string source = """
                              public static class C
                              {
                                  private static int Seed(int value) => value;
                                  private static bool KeepGoing(int value) => value >= 0;
                                  private static int Step(int value) => value - 1;

                                  public static int Work(int n)
                                  {
                                      var result = 0;
                                      for (var i = Seed(n); KeepGoing(i); i = Step(i))
                                      {
                                          result += i;
                                      }

                                      return result;
                                  }
                              }
                              """;

        var result = QueryComplexityAtMarker(source, "return result;");

        Assert.That(result.Complexity.Kind, Is.EqualTo(SymbolicComplexityKind.Unknown));
        Assert.That(result.UnknownReasons,
            Is.EqualTo(new[] { SymbolicComplexityUnknownReason.UnsupportedLoopShape }));
        Assert.That(result.Drivers.Count(driver => driver.Kind == "Unknown"), Is.EqualTo(1));
        Assert.That(
            result.CalleeSummaries.Count(summary =>
                summary.MethodDisplayName.Contains("Seed", StringComparison.Ordinal)),
            Is.EqualTo(1));
    }

    private static SymbolicComplexityResult QueryComplexityAtMarker(
        string source,
        string marker,
        bool useLineTarget = false) {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");

        var target = useLineTarget
            ? SharpProofTargetFactory.LineNumber(GetLineNumber(source, position))
            : SharpProofTargetFactory.AtPosition(position);
        var (tree, compilation) = SymbolicSourceCompilation.Create(
            source, "SymbolicComplexityTests.cs", SymbolicSourceCompilationKind.Query, null, default);
        return new SymbolicQueryExecutor().QueryComplexity(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(tree, compilation),
                target));
    }

    private static int GetLineNumber(string source, int position) {
        var line = 1;
        for (var index = 0; index < position; index++)
            if (source[index] == '\n')
                line++;

        return line;
    }

    [Test]
    public void QueryComplexity_AllLinesTarget_ThrowsNotSupportedException() {
        var (tree, compilation) = SymbolicSourceCompilation.Create(
            "class C { }", "SymbolicComplexityTests.cs", SymbolicSourceCompilationKind.Query, null, default);
        var ex = Assert.Throws<NotSupportedException>(() => new SymbolicQueryExecutor().QueryComplexity(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(tree, compilation),
                SharpProofTargetFactory.AllLines())));

        Assert.That(ex!.Message, Is.EqualTo("Complexity queries support point, position, or line targets only."));
    }
}
