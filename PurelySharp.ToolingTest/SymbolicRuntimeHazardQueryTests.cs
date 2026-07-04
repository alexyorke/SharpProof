using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class SymbolicRuntimeHazardQueryTests
    {
        [Test]
        public void QuerySourceRuntimeHazardsLine_ReturnsProvenDirectThrow()
        {
            const string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "throw new InvalidOperationException();", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(hazard.Category, Is.EqualTo("direct_throw"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.direct-throw"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.direct-throw"));
            Assert.That(hazard.Proof.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
            Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
            Assert.That(hazard.Proof.UnknownReason, Is.EqualTo(SymbolicUnknownReason.None));
            Assert.That(hazard.InvariantInfo.MergedText, Is.EqualTo(hazard.MergedInvariantText));
            Assert.That(hazard.InvariantInfo.Facts, Is.EquivalentTo(hazard.SymbolicFacts));
            Assert.That(hazard.InvariantInfo.Proofs.Single().Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesThrowNullAsNullReferenceException()
        {
            const string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Exception? error = null;
        throw error;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "throw error;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_throw_null"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicRelationAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.throw-null.trigger"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.throw-null.trigger"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesLiteralThrowNullAsNullReferenceException()
        {
            const string source = @"
public class TestClass
{
    public void TestMethod()
    {
        throw null;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "throw null;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_throw_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesPathProvenThrowMaybeNullAsNullReferenceException()
        {
            const string source = @"
using System;

public class TestClass
{
    public void TestMethod(Exception? error)
    {
        if (error is null)
        {
            throw error;
        }
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "throw error;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_throw_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedDivideByZero()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return 10 / divisor;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.DivideByZeroException"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.divide-by-zero"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Has.Some.StartsWith("ir."));
            Assert.That(hazard.Proof.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
            Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
            Assert.That(hazard.Proof.Budget, Is.Not.Null);
            Assert.That(hazard.Proof.Budget!.MaxPathConditions, Is.EqualTo(SmtAnalysisOptions.Default.MaxPathConditions));
            Assert.That(hazard.InvariantInfo.Facts, Is.EquivalentTo(hazard.SymbolicFacts));
            Assert.That(hazard.PathConditions, Does.Contain("divisor == 0"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_RemainderDivisorUsesIrPrecondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value % 2 == 0)
        {
            return 10 / (value % 2);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return 10 / (value % 2);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.DivideByZeroException"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.divide-by-zero");
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownDivideByZeroCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        return 10 / divisor;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(source, "return 10 / divisor;", smtAnalysis);
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return 10 / divisor;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
            Assert.That(candidateResult.Hazards, Has.Count.EqualTo(2));
            Assert.That(
                candidateResult.Hazards.Any(hazard =>
                    hazard.Kind == SymbolicRuntimeHazardKind.DivideByZero &&
                    hazard.Status == SymbolicRuntimeHazardStatus.Unknown),
                Is.True);
            Assert.That(
                candidateResult.Hazards.Any(hazard =>
                    hazard.Kind == SymbolicRuntimeHazardKind.CheckedIntegralOverflow &&
                    hazard.Status == SymbolicRuntimeHazardStatus.Unreachable &&
                    string.Equals(hazard.ExceptionType, "System.OverflowException", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCompoundDivideByZero()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            value /= divisor;
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value /= divisor;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DivideByZero }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.DivideByZeroException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedCompoundModuloByNonZeroIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            value %= divisor;
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value %= divisor;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.DivideByZero }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.DivideByZeroException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_modulo_by_zero"));
        }

        [Test]
        public void QueryNodeRuntimeHazards_DefaultExcludesNestedCallableHazards()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        string? text = null;
        int Local() => text.Length;
        int divisor = 0;
        return value / divisor;
    }
}";

            var (method, semanticModel) = CreateMethodContext(source, "TestMethod");
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
                method,
                semanticModel,
                smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.OperationText, Is.EqualTo("value / divisor"));
            Assert.That(result.ScopeStart, Is.EqualTo(method.SpanStart));
            Assert.That(result.ScopeEnd, Is.EqualTo(method.Span.End));
        }

        [Test]
        public void QueryNodeRuntimeHazards_CanIncludeNestedCallableHazards()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        string? text = null;
        int Local() => text.Length;
        int divisor = 0;
        return value / divisor;
    }
}";

            var (method, semanticModel) = CreateMethodContext(source, "TestMethod");
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
                method,
                semanticModel,
                smtAnalysis,
                includeNestedCallables: true);

            Assert.That(result.Hazards.Select(hazard => hazard.Kind), Is.EquivalentTo(new[]
            {
                SymbolicRuntimeHazardKind.NullDereference,
                SymbolicRuntimeHazardKind.DivideByZero,
            }));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedNullDereference()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string? value)
    {
        if (value is null)
        {
            return value.Length;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return value.Length;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_MemberReceiverNullDereferenceUsesIrPrecondition()
        {
            const string source = @"
public sealed class Holder
{
    public string? Value { get; set; }
}

public class TestClass
{
    public int TestMethod(Holder holder)
    {
        if (holder.Value is null)
        {
            return holder.Value.Length;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return holder.Value.Length;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.null-dereference"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.null-dereference"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesWithExpressionNullReceiverDereference()
        {
            const string source = @"
public record Person(string Name);

public class TestClass
{
    public Person TestMethod(Person? person)
    {
        if (person is null)
        {
            return person with { Name = ""fallback"" };
        }

        return person;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return person with { Name = \"fallback\" };",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_with_null"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.WithExpression.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_PrunesWithExpressionNullReceiverAfterNonNullGuard()
        {
            const string source = @"
public record Person(string Name);

public class TestClass
{
    public Person TestMethod(Person? person)
    {
        if (person is not null)
        {
            return person with { Name = ""safe"" };
        }

        return new Person(""fallback"");
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return person with { Name = \"safe\" };",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_with_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDeconstructionNullReceiverDereference()
        {
            const string source = @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "(left, right) = pair;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_deconstruction_null"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.SimpleAssignmentExpression.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_PrunesDeconstructionNullReceiverAfterNonNullGuard()
        {
            const string source = @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is not null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "(left, right) = pair;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_deconstruction_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDeconstructionDeclarationNullReceiverDereference()
        {
            const string source = @"
public sealed class Pair
{
    public void Deconstruct(out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            var (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "var (left, right) = pair;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_deconstruction_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DoesNotTreatExtensionDeconstructionNullSourceAsImplicitDereference()
        {
            const string source = @"
public sealed class Pair
{
}

public static class PairExtensions
{
    public static void Deconstruct(this Pair pair, out int left, out int right)
    {
        left = 1;
        right = 2;
    }
}

public class TestClass
{
    public int TestMethod(Pair? pair)
    {
        if (pair is null)
        {
            int left;
            int right;
            (left, right) = pair;
            return left + right;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "(left, right) = pair;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesForeachNullSourceDereference()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "foreach (var value in values)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.ForEachStatement.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_PrunesForeachNullSourceAfterNonNullGuard()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is not null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "foreach (var value in values)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesForeachNullSourceAfterNonNullGuardAsUnreachableCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string[] values)
    {
        if (values is not null)
        {
            foreach (var value in values)
            {
                return value.Length;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "foreach (var value in values)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.ForEachStatement.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesAwaitNullDereference()
        {
            const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod()
    {
        Task<int> task = null!;
        return await task;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return await task;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_await_null"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.AwaitExpression.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_PrunesAwaitNullDereferenceAfterNonNullGuard()
        {
            const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod(Task<int> task)
    {
        if (task is not null)
        {
            return await task;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return await task;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesAwaitNullDereferenceAfterNonNullGuardAsUnreachableCandidate()
        {
            const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> TestMethod(Task<int> task)
    {
        if (task is not null)
        {
            return await task;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return await task;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullDereference));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_await_null"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.AwaitExpression.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesLockNullSourceArgumentNull()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "lock (gate)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentNull }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentNullException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_lock_null"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.LockStatement.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_PrunesLockNullSourceAfterNonNullGuard()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "lock (gate)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentNull }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ClassifiesLockNullSourceAfterNonNullGuardAsUnreachableCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object gate)
    {
        if (gate is not null)
        {
            lock (gate)
            {
                return 1;
            }
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "lock (gate)",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentNull }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentNullException"));
            Assert.That(hazard.NodeKind, Is.EqualTo(SyntaxKind.LockStatement.ToString()));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicMemberNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Missing;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_member_null_binding"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.dynamic-null-binding"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.dynamic-null-binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesCastedDynamicMemberNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null).Missing;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return ((dynamic)null).Missing;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_member_null_binding"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.dynamic-null-binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Missing();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesCastedDynamicInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null).Missing();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return ((dynamic)null).Missing();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicDirectInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesCastedDynamicDirectInvocationNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null)();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return ((dynamic)null)();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_invocation_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesDynamicIndexerNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value[0];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_index_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesCastedDynamicIndexerNullBinding()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        return ((dynamic)null)[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return ((dynamic)null)[0];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_dynamic_index_null_binding"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownDynamicNullBindingCandidate()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod(dynamic value)
    {
        return value.Missing;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return value.Missing;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return value.Missing;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazards_NonNullDynamicReceiverPrunesNullBindingCandidate()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = new object();
        return value.ToString();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.ToString();",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.DynamicNullBinding }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesNullableValueWithoutValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        int? value = default;
        return value.Value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return value.Value;", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.nullable-value.without-value"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_SuppressesNullableValueAfterCoalesceFallbackAssignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? left)
    {
        int? value = left ?? 5;
        return value.Value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazards_ProvesNullableValueAfterConditionalAccessNullReceiverAssignment()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        int? value = text?.Length;
        if (text is null)
        {
            return value.Value;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.nullable-value.without-value"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.nullable-value.without-value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesNullableExplicitCastWithoutValue()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (!value.HasValue)
        {
            return (int)value;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_nullable_value_without_value"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_GuardedNullableExplicitCastIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int? value)
    {
        if (value.HasValue)
        {
            return (int)value;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NullableValueWithoutValue }));

            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_nullable_value_without_value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesUnboxNullCast()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        object value = null;
        return (int)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.UnboxNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.NullReferenceException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_unbox_null"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownUnboxNullCastCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(object value)
    {
        return (int)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return (int)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.UnboxNull }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.UnboxNull));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidReferenceCast()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod()
    {
        object value = new object();
        return (string)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterNegativeTypeAndNullTests()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        if (value is not string && value is not null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.invalid-cast.mismatch"));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain("ir.runtime-hazard.invalid-cast.mismatch"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_SuppressesInvalidCastAfterAsCastNonNullGuard()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterAsCastNullAndSourceNonNull()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        var text = value as string;
        if (text == null && value != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_SuppressesInvalidCastAfterInlineAsAssignmentNonNullGuard()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidCastAfterInlineAsAssignmentNullAndSourceNonNull()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        string text;
        if ((text = value as string) == null && value != null)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownInvalidCastAfterNegativeTypeTest()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(object value)
    {
        if (value is not string)
        {
            return (string)value;
        }

        return string.Empty;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesInvalidUnboxCast()
        {
            const string source = @"
public class TestClass
{
    public long TestMethod()
    {
        object value = 1;
        return (long)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (long)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCast));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.InvalidCastException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_invalid_cast"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesCompatibleCast()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod()
    {
        object value = ""text"";
        return (string)value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return (string)value;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.InvalidCast }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesBuiltInIndexOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values.Length == 0)
        {
            return values[0];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.index.out-of-range");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesArrayGetValueIndexOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod(int[] values)
    {
        return values.GetValue(values.Length);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.GetValue(values.Length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_array_get_value_index_out_of_range"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.array-get-value.index-out-of-range");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedMultidimensionalArrayGetValueIndexOutOfRangeIsPruned()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod(int[,] values, int row, int column)
    {
        if (row >= 0 && row < values.GetLength(0) && column >= 0 && column < values.GetLength(1))
        {
            return values.GetValue(row, column);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.GetValue(row, column);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_array_get_value_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_AssignedModuloIndexUnderPositiveLengthGuardIsUnreachable()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values.Length > 0 && hash >= 0)
        {
            var index = hash % values.Length;
            return values[index];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values[index];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_AssignedAbsModuloIndexUnderPositiveLengthGuardIsUnreachable()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        if (values.Length > 0)
        {
            var index = Math.Abs(hash % values.Length);
            return values[index];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values[index];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DirectAbsModuloIndexIsUnreachable()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int[] values, int hash)
    {
        return values[Math.Abs(hash % values.Length)];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values[Math.Abs(hash % values.Length)];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesObjectErasedArrayCastAliasIndexOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new int[4];
        object boxed = values;
        var alias = (int[])boxed;
        return alias[4];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return alias[4];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesAssignedSpanSliceIndexOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(ReadOnlySpan<int> values)
    {
        var tail = values.Slice(values.Length);
        return tail[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return tail[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesBuiltInRangeOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value)
    {
        if (value.Length == 0)
        {
            return value[1..];
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return value[1..];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesStringSubstringArgumentOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value.Substring(value.Length + 1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Substring(value.Length + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_string_substring_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedStringSubstringArgumentOutOfRangeIsPruned()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value, int start)
    {
        if (start >= 0 && start <= value.Length)
        {
            return value.Substring(start);
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return value.Substring(start);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return value.Substring(start);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_string_substring_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesStringRemoveStartAtLengthArgumentOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value)
    {
        return value.Remove(value.Length);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Remove(value.Length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_string_remove_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_StringRemoveZeroCountAtLengthIsPruned()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value)
    {
        if (value.Length >= 0)
        {
            return value.Remove(value.Length, 0);
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.Remove(value.Length, 0);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_string_remove_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesSpanSliceArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        return values.Slice(values.Length + 1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.Slice(values.Length + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_slice_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_OverflowProneSpanSliceGuardRemainsUnknown()
        {
            const string source = @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length);
        }

        return values;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.Slice(start, length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_slice_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesSpanSliceUncheckedAddOverflowArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public ReadOnlySpan<int> TestMethod(ReadOnlySpan<int> values)
    {
        var start = int.MaxValue;
        var length = 1;
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.Slice(start, length);
        }

        return values;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.Slice(start, length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_slice_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesArrayAsSpanArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public Span<int> TestMethod(int[] values)
    {
        return values.AsSpan(values.Length + 1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.AsSpan(values.Length + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_memory_extensions_as_span_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_OverflowProneStringAsSpanGuardRemainsUnknown()
        {
            const string source = @"
using System;

public class TestClass
{
    public ReadOnlySpan<char> TestMethod(string value, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= value.Length)
        {
            return value.AsSpan(start, length);
        }

        return value.AsSpan();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.AsSpan(start, length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_memory_extensions_as_span_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesStringAsMemoryArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public ReadOnlyMemory<char> TestMethod(string value)
    {
        return value.AsMemory(value.Length + 1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value.AsMemory(value.Length + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_memory_extensions_as_memory_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_OverflowProneArrayAsMemoryGuardRemainsUnknown()
        {
            const string source = @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(int[] values, int start, int length)
    {
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.AsMemory(start, length);
        }

        return values.AsMemory();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.AsMemory(start, length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_memory_extensions_as_memory_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesArrayAsMemoryUncheckedAddOverflowArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(int[] values)
    {
        var start = int.MaxValue;
        var length = 1;
        if (start >= 0 && length >= 0 && start + length <= values.Length)
        {
            return values.AsMemory(start, length);
        }

        return values.AsMemory();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.AsMemory(start, length);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_memory_extensions_as_memory_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesMemorySliceNegativeLengthArgumentOutOfRange()
        {
            const string source = @"
using System;

public class TestClass
{
    public Memory<int> TestMethod(Memory<int> values)
    {
        return values.Slice(0, -1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values.Slice(0, -1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_slice_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownStringSubstringArgumentOutOfRangeCandidate()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value, int start)
    {
        return value.Substring(start);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return value.Substring(start);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return value.Substring(start);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_string_substring_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedRangeAccessArgumentOutOfRangeStillPruned()
        {
            const string source = @"
public class TestClass
{
    public string TestMethod(string value, int start, int end)
    {
        if (start >= 0 && start <= end && end <= value.Length)
        {
            return value[start..end];
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value[start..end];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_range_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesListIndexerArgumentOutOfRange()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count == 0)
        {
            return values[0];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesReadOnlyListIndexerArgumentOutOfRange()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return values[0];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[0];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedReadOnlyListIndexerArgumentOutOfRangeIsPruned()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(IReadOnlyList<int> values, int index)
    {
        if (index >= 0 && index < values.Count)
        {
            return values[index];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[index];", smtAnalysis);

            Assert.That(result.Hazards, Is.Empty);

            var allCandidates = QueryLine(
                source,
                "return values[index];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(allCandidates);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesListIndexFromEndArgumentOutOfRange()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count == 0)
        {
            return values[^1];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[^1];", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedListIndexFromEndArgumentOutOfRangeIsPruned()
        {
            const string source = @"
using System.Collections.Generic;

public class TestClass
{
    public int TestMethod(List<int> values)
    {
        if (values.Count > 0)
        {
            return values[^1];
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return values[^1];", smtAnalysis);

            Assert.That(result.Hazards, Is.Empty);

            var allCandidates = QueryLine(
                source,
                "return values[^1];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.ArgumentOutOfRange }));

            var hazard = AssertSingleHazard(allCandidates);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArgumentOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentOutOfRangeException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_count_index_out_of_range"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedIntegralOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return checked(value + 1);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked(value + 1);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo("ir.runtime-hazard.checked-integral.binary-overflow"));
            Assert.That(hazard.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
            Assert.That(hazard.Proof.Budget, Is.Not.Null);
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedIntegralOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return checked(value + 1);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return checked(value + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return checked(value + 1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedDivisionOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(value / -1);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value / -1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.checked-integral.signed-division-overflow");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedCheckedDivisionOverflowIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value > int.MinValue && divisor == -1)
        {
            return checked(value / divisor);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value / divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedUncheckedDivisionOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            return unchecked(value / divisor);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return unchecked(value / divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.checked-integral.signed-division-overflow");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedRemainderOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MinValue)
        {
            return checked(value % -1);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value % -1);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedUncheckedRemainderOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            return unchecked(value % divisor);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return unchecked(value % divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedLongRemainderOverflow()
        {
            const string source = @"
public class TestClass
{
    public long TestMethod(long value)
    {
        if (value == long.MinValue)
        {
            return checked(value % -1L);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value % -1L);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedCheckedRemainderOverflowIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value > int.MinValue && divisor == -1)
        {
            return checked(value % divisor);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value % divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedDivisionOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        return checked(value / divisor);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return checked(value / divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return checked(value / divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazards_UIntDivisionDoesNotCreateImpossibleCheckedOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public uint TestMethod(uint value, uint divisor)
    {
        return checked(value / divisor);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return checked(value / divisor);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundAssignmentOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            checked
            {
                value += 1;
            }
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value += 1;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundDivisionOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            checked
            {
                value /= divisor;
            }
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value /= divisor;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedCompoundRemainderOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (value == int.MinValue && divisor == -1)
        {
            checked
            {
                value %= divisor;
            }
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value %= divisor;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedCheckedCompoundAssignmentOverflowIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int delta)
    {
        if (value >= int.MinValue && delta >= 0 && value <= int.MaxValue - delta)
        {
            checked
            {
                value += delta;
            }
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "value += delta;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedCompoundAssignmentOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value, int delta)
    {
        checked
        {
            value += delta;
        }

        return value;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "value += delta;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "value += delta;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));

            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedNegativeArrayLength()
        {
            const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        if (length < 0)
        {
            return new int[length];
        }

        return new int[0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return new int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_negative_array_length"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.array.negative-length.aggregate");
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownNegativeArrayLengthCandidate()
        {
            const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        return new int[length];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return new int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return new int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedNegativeStackAllocLength()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        if (length < 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "Span<int> span = stackalloc int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeStackAllocLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_negative_stackalloc_length"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.stackalloc.negative-length.aggregate");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedStackAllocLengthIsPruned()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        if (length >= 0)
        {
            Span<int> span = stackalloc int[length];
            return span.Length;
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "Span<int> span = stackalloc int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeStackAllocLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_negative_stackalloc_length"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownNegativeStackAllocLengthCandidate()
        {
            const string source = @"
using System;

public class TestClass
{
    public int TestMethod(int length)
    {
        Span<int> span = stackalloc int[length];
        return span.Length;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "Span<int> span = stackalloc int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "Span<int> span = stackalloc int[length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NegativeStackAllocLength }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeStackAllocLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_negative_stackalloc_length"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_ArrayCreationNormalCompletionPrunesNegativeLengthBranch()
        {
            const string source = @"
public class TestClass
{
    public int[] TestMethod(int length)
    {
        var values = new int[length];
        if (length < 0)
        {
            return new int[length + 0];
        }

        return values;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return new int[length + 0];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
        }

        [Test]
        public void QuerySourceRuntimeHazards_MultidimensionalArrayNegativeLength_ProvesOverflow()
        {
            const string source = @"
public class TestClass
{
    public int[,] TestMethod()
    {
        var length = -1;
        return new int[1, length];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return new int[1, length];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.NegativeArrayLength }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.NegativeArrayLength));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_AssignedMultidimensionalArrayDimensionLengthProvesIndexOutOfRange()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int rows, int columns)
    {
        var values = new int[rows, columns];
        return values[rows, 0];
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return values[rows, 0];",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.IndexOutOfRange }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.IndexOutOfRange));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.IndexOutOfRangeException"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedBytePreIncrementOverflow()
        {
            const string source = @"
public class TestClass
{
    public byte TestMethod(byte value)
    {
        if (value == byte.MaxValue)
        {
            return checked(++value);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked(++value);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("++value"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.checked-integral.increment-overflow");
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedLongPostDecrementOverflow()
        {
            const string source = @"
public class TestClass
{
    public long TestMethod(long value)
    {
        if (value == long.MinValue)
        {
            return checked(value--);
        }

        return 0L;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked(value--);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_integral_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("value--"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.checked-integral.decrement-overflow");
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedPostIncrementOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public short TestMethod(short value)
    {
        return checked(value++);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return checked(value++);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return checked(value++);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.OperationText, Is.EqualTo("value++"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnreachableCheckedPreIncrementOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == int.MaxValue)
        {
            return 0;
        }

        if (value == int.MaxValue)
        {
            return checked(++value);
        }

        return 1;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return checked(++value);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return checked(++value);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.OperationText, Is.EqualTo("++value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedCheckedExplicitNumericConversionOverflow()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(long value)
    {
        if (value > int.MaxValue)
        {
            return checked((int)value);
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(source, "return checked((int)value);", smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.OverflowException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_checked_numeric_conversion_overflow"));
            Assert.That(hazard.OperationText, Is.EqualTo("(int)value"));
            AssertIrExceptionPrecondition(hazard, "ir.runtime-hazard.checked-conversion.overflow");
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownCheckedExplicitNumericConversionOverflowCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(long value)
    {
        return checked((int)value);
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return checked((int)value);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return checked((int)value);",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.CheckedIntegralOverflow }));
            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.OperationText, Is.EqualTo("(int)value"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesArrayCovarianceStoreMismatch()
        {
            const string source = @"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = 42;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "values[0] = 42;",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.ArrayTypeMismatch));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArrayTypeMismatchException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_array_type_mismatch"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesCompatibleArrayCovarianceStore()
        {
            const string source = @"
public class TestClass
{
    public void TestMethod()
    {
        object[] values = new string[1];
        values[0] = ""text"";
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "values[0] = \"text\";",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.ArrayTypeMismatch }));

            Assert.That(result.Hazards, Is.Empty);
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_ProvesGuardedSwitchExpressionNoMatch()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0,
            };
        }

        return 0;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value switch",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.SwitchExpressionNoMatch));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Proven));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.Runtime.CompilerServices.SwitchExpressionException"));
            Assert.That(hazard.Category, Is.EqualTo("definite_switch_expression_no_match"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsLine_GuardedSwitchExpressionNoMatchIsPruned()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value <= 0)
        {
            return value switch
            {
                < 0 => -1,
                0 => 0,
            };
        }

        return 1;
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var result = QueryLine(
                source,
                "return value switch",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.SwitchExpressionNoMatch));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unreachable));
            Assert.That(hazard.Category, Is.EqualTo("definite_switch_expression_no_match"));
        }

        [Test]
        public void QuerySourceRuntimeHazards_DefaultSuppressesUnknownSwitchExpressionNoMatchCandidate()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            < 0 => -1,
            0 => 0,
        };
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var defaultResult = QueryLine(
                source,
                "return value switch",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(kinds: new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));
            Assert.That(defaultResult.Hazards, Is.Empty);

            var candidateResult = QueryLine(
                source,
                "return value switch",
                smtAnalysis,
                new SymbolicRuntimeHazardQueryOptions(
                    includeUnprovenCandidates: true,
                    kinds: new[] { SymbolicRuntimeHazardKind.SwitchExpressionNoMatch }));

            var hazard = AssertSingleHazard(candidateResult);
            Assert.That(hazard.Kind, Is.EqualTo(SymbolicRuntimeHazardKind.SwitchExpressionNoMatch));
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.Category, Is.EqualTo("definite_switch_expression_no_match"));
        }

        [Test]
        public void QuerySourceRuntimeHazardsSpan_FiltersToRequestedSpan()
        {
            const string source = @"
using System;

public class TestClass
{
    public void TestMethod(bool flag)
    {
        if (flag)
        {
            throw new InvalidOperationException();
        }

        throw new ArgumentException();
    }
}";

            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
            var spanStart = FindPosition(source, "throw new ArgumentException();");
            var spanEnd = spanStart + "throw new ArgumentException();".Length;
            var result = new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazardsSpan(
                source,
                "Hazards.cs",
                spanStart,
                spanEnd,
                smtAnalysis);

            var hazard = AssertSingleHazard(result);
            Assert.That(hazard.OperationText, Is.EqualTo("throw new ArgumentException();"));
            Assert.That(hazard.ExceptionType, Is.EqualTo("System.ArgumentException"));
        }

        [Test]
        public async Task SymbolicCli_RuntimeHazardsJson_EmitsProvenHazard()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicRuntimeHazards-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "return 10 / divisor;").ToString(),
                    "--runtime-hazards",
                    "--json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
                var hazard = root.GetProperty("Hazards")[0];
                Assert.That(hazard.GetProperty("Kind").GetString(), Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero.ToString()));
                Assert.That(hazard.GetProperty("Status").GetString(), Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
                Assert.That(hazard.GetProperty("ExceptionType").GetString(), Is.EqualTo("System.DivideByZeroException"));
                var triggerPrecondition = hazard.GetProperty("TriggerPrecondition");
                Assert.That(triggerPrecondition.GetProperty("Kind").GetString(), Is.EqualTo("SymbolicExceptionPreconditionAtom"));
                Assert.That(triggerPrecondition.GetProperty("Provenance").GetString(), Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_RuntimeHazardsCompactJson_EmitsTriggerPrecondition()
        {
            const string source = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicRuntimeHazardsCompact-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "return 10 / divisor;").ToString(),
                    "--runtime-hazards",
                    "--compact-json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
                var hazard = root.GetProperty("hazards")[0];
                var triggerPrecondition = hazard.GetProperty("triggerPrecondition");
                Assert.That(triggerPrecondition.GetProperty("kind").GetString(), Is.EqualTo("SymbolicExceptionPreconditionAtom"));
                Assert.That(triggerPrecondition.GetProperty("provenance").GetString(), Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
                Assert.That(triggerPrecondition.GetProperty("confidence").GetString(), Is.EqualTo("Exact"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        [Test]
        public async Task SymbolicCli_RuntimeHazardsJson_EmitsDynamicNullBindingHazard()
        {
            const string source = @"
public class TestClass
{
    public object TestMethod()
    {
        dynamic value = null;
        return value.Missing;
    }
}";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicRuntimeDynamicHazards-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "return value.Missing;").ToString(),
                    "--runtime-hazards",
                    "--hazard-kind",
                    "DynamicNullBinding",
                    "--json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
                var hazard = root.GetProperty("Hazards")[0];
                Assert.That(hazard.GetProperty("Kind").GetString(), Is.EqualTo(SymbolicRuntimeHazardKind.DynamicNullBinding.ToString()));
                Assert.That(hazard.GetProperty("Status").GetString(), Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
                Assert.That(hazard.GetProperty("ExceptionType").GetString(), Is.EqualTo("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException"));
                Assert.That(hazard.GetProperty("Category").GetString(), Is.EqualTo("definite_dynamic_member_null_binding"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        private static SymbolicRuntimeHazardQueryResult QueryLine(
            string source,
            string marker,
            SmtAnalysisService smtAnalysis,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            return new SymbolicRuntimeHazardQueryService().QuerySourceRuntimeHazardsLine(
                source,
                "Hazards.cs",
                FindLine(source, marker),
                smtAnalysis,
                references: AnalyzerTestHost.GetTrustedPlatformReferences(),
                options: options);
        }

        private static SymbolicRuntimeHazard AssertSingleHazard(SymbolicRuntimeHazardQueryResult result)
        {
            Assert.That(result.Hazards, Has.Count.EqualTo(1));
            return result.Hazards.Single();
        }

        private static void AssertIrExceptionPrecondition(
            SymbolicRuntimeHazard hazard,
            string provenance)
        {
            Assert.That(hazard.TriggerPrecondition, Is.Not.Null);
            Assert.That(hazard.TriggerPrecondition!.Kind, Is.EqualTo("SymbolicExceptionPreconditionAtom"));
            Assert.That(hazard.TriggerPrecondition.Provenance, Is.EqualTo(provenance));
            Assert.That(hazard.SymbolicFacts.Select(static fact => fact.Provenance), Does.Contain(provenance));
        }

        private static (MethodDeclarationSyntax Method, SemanticModel SemanticModel) CreateMethodContext(
            string source,
            string methodName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: "NodeHazards.cs");
            var compilation = CSharpCompilation.Create(
                "NodeHazards",
                new[] { syntaxTree },
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var method = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(candidate => candidate.Identifier.ValueText == methodName);
            return (method, compilation.GetSemanticModel(syntaxTree));
        }

        private static int FindLine(string source, string text)
        {
            var position = FindPosition(source, text);
            var line = 1;
            for (var index = 0; index < position; index++)
            {
                if (source[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static int FindPosition(string source, string text)
        {
            var position = source.IndexOf(text, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Text not found: " + text);
            }

            return position;
        }

    }
}
