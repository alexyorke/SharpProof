using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class ExactConcreteDispatchFlowTests
{
    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return Scenario("InterfaceMethodDispatch_AliasedExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.InterfaceMethodHierarchy, "Process(int value)",
            "IWorker worker = new ExactWorker();\nIWorker alias = worker;\nreturn alias.Compute(value);");
        yield return Scenario("VirtualMethodDispatch_CastExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(int value)",
            "Worker worker = (Worker)new ExactWorker();\nreturn worker.Compute(value);");
        yield return Scenario("VirtualMethodDispatch_SameConcreteConditionalMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(bool chooseLeft, int value)",
            "Worker worker = chooseLeft ? new ExactWorker() : new ExactWorker();\nreturn worker.Compute(value);");
        yield return Scenario("VirtualPropertyDispatch_SameConcreteConditionalMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy, "ReadValue(bool chooseLeft)",
            "BaseValue value = chooseLeft ? new ExactValue() : new ExactValue();\nreturn value.Value;");
        yield return Scenario("VirtualMethodDispatch_SameConcreteIfElseMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(bool chooseLeft, int value)", """
Worker worker;
if (chooseLeft)
{
    worker = new ExactWorker();
}
else
{
    worker = new ExactWorker();
}

return worker.Compute(value);
""");
        yield return Scenario("VirtualPropertyDispatch_SameConcreteIfElseMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy, "ReadValue(bool chooseLeft)", """
BaseValue value;
if (chooseLeft)
{
    value = new ExactValue();
}
else
{
    value = new ExactValue();
}

return value.Value;
""");
        yield return Scenario("VirtualMethodDispatch_SameConcreteCoalesceMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(int value)",
            "ExactWorker primary = new ExactWorker();\nExactWorker fallback = new ExactWorker();\nWorker worker = primary ?? fallback;\nreturn worker.Compute(value);");
        yield return Scenario("VirtualPropertyDispatch_SameConcreteCoalesceMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy, "ReadValue()",
            "ExactValue primary = new ExactValue();\nExactValue fallback = new ExactValue();\nBaseValue value = primary ?? fallback;\nreturn value.Value;");
        yield return Scenario("VirtualMethodDispatch_SameConcreteSwitchExpressionMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(bool chooseLeft, int value)", """
Worker worker = chooseLeft switch
{
    true => new ExactWorker(),
    false => new ExactWorker(),
};

return worker.Compute(value);
""");
        yield return Scenario("VirtualMethodDispatch_ContradictoryConditionImpureBranch_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(int discriminator, int value)", """
Worker worker = new ExactWorker();
if (discriminator == 0 && discriminator != 0)
{
    worker = new ImpureWorker();
}

return worker.Compute(value);
""");
        yield return Scenario("VirtualPropertyDispatch_ContradictoryConditionImpureBranch_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy, "ReadValue(int discriminator)", """
BaseValue value = new ExactValue();
if (discriminator == 0 && discriminator != 0)
{
    value = new ImpureValue();
}

return value.Value;
""");
        yield return Scenario("VirtualMethodDispatch_NullCoalescingAssignmentExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(int value)",
            "Worker worker = new ExactWorker();\nworker ??= new ExactWorker();\nreturn worker.Compute(value);");
        yield return Scenario(
            "VirtualMethodDispatch_NullInitializedNullCoalescingAssignmentExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy, "Process(int value)",
            "Worker worker = null;\nworker ??= new ExactWorker();\nreturn worker.Compute(value);");
        yield return Scenario(
            "VirtualPropertyDispatch_NullInitializedNullCoalescingAssignmentExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy, "ReadValue()",
            "BaseValue value = null;\nvalue ??= new ExactValue();\nreturn value.Value;");
    }

    private static TestCaseData Scenario(string name, string hierarchy, string signature, string body)
    {
        return new TestCaseData(hierarchy, signature, body).SetName(name);
    }

    [TestCaseSource(nameof(Scenarios))]
    public async Task ExactConcreteDispatchFlow_NoDiagnostic(string hierarchy, string signature, string body)
    {
        var test = ExactConcreteDispatchTestSources.CreateSource(hierarchy, signature, body);
        var (_, diagnostic) = await AnalyzerTestHost.AssertSingleSp0002Async(test);
        Assert.That(diagnostic.Properties["sharpproof.impurity.symbol"],
            Does.Contain("System.Console.WriteLine"));
    }
}
