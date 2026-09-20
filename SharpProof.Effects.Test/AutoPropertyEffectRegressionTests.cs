namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class AutoPropertyEffectRegressionTests
{
    [Test]
    public void AutoPropertyAccessorsModelBackingFieldEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Dto
            {
                public int Age { get; set; }
                public int ReadOnly { get; }
                public int Initialized { get; init; }
                public static int Count { get; set; }
            }

            public readonly struct Point
            {
                public int X { get; }
                public Point(int x) => X = x;
            }

            public static class Sample
            {
                public static int Read(Dto value) => value.Age;
                public static void Write(Dto value, int age) => value.Age = age;
                public static int ReadStruct(Point value) => value.X;
                public static int ReadStatic() => Dto.Count;
                public static Dto Create() => new() { Age = 1 };
                public static Dto CreateInitialized() => new() { Initialized = 1 };
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var dto = EffectTestHost.RequireType(compilation, "Dto");
        var ageGetter = dto.GetMembers("Age").OfType<IPropertySymbol>().Single().GetMethod!;
        var ageSetter = dto.GetMembers("Age").OfType<IPropertySymbol>().Single().SetMethod!;
        var countGetter = dto.GetMembers("Count").OfType<IPropertySymbol>().Single().GetMethod!;

        var read = session.Analyze(EffectTestHost.SampleMethod(compilation, "Read"));
        var write = session.Analyze(EffectTestHost.SampleMethod(compilation, "Write"));
        var readStruct = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "ReadStruct"));
        var readStatic = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "ReadStatic"));
        var create = session.Analyze(EffectTestHost.SampleMethod(compilation, "Create"));
        var createInitialized = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "CreateInitialized"));
        var getter = session.Analyze(ageGetter);
        var setter = session.Analyze(ageSetter);
        var staticGetter = session.Analyze(countGetter);
        var pointGetter = EffectTestHost.RequireType(compilation, "Point")
            .GetMembers("X").OfType<IPropertySymbol>().Single().GetMethod!;
        var pointGetterResult = session.Analyze(pointGetter);

        using (Assert.EnterMultipleScope())
        {
            AssertComplete(getter, "Dto.Age.get");
            Assert.That(getter.Summary.Reads.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Receiver }));
            Assert.That(getter.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));

            AssertComplete(setter, "Dto.Age.set");
            Assert.That(setter.Summary.Writes.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Receiver }));

            AssertComplete(staticGetter, "Dto.Count.get");
            Assert.That(staticGetter.Summary.Reads.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Static() }));

            AssertComplete(pointGetterResult, "Point.X.get");
            Assert.That(pointGetterResult.Summary.Reads.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Receiver }));

            AssertComplete(read, "Read");
            Assert.That(read.Summary.Reads.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Parameter(0) }));
            Assert.That(read.Summary.Writes.IsEmpty, Is.True);

            AssertComplete(write, "Write");
            Assert.That(write.Summary.Writes.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Parameter(0) }));
            Assert.That(write.Summary.Reads.IsEmpty, Is.True);

            AssertComplete(readStruct, "ReadStruct");
            Assert.That(readStruct.Summary.Reads.IsEmpty, Is.True);

            AssertComplete(readStatic, "ReadStatic");
            Assert.That(readStatic.Summary.Reads.Regions, Is.EquivalentTo(
                new[] { EffectRegionId.Static() }));

            AssertComplete(create, "Create");
            Assert.That(create.Summary.Writes.IsUnknown, Is.False);
            Assert.That(create.Summary.Writes.Regions, Has.All.Property(
                nameof(EffectRegionId.Kind)).EqualTo(EffectRegionKind.Fresh));

            AssertComplete(createInitialized, "CreateInitialized");
            Assert.That(createInitialized.Summary.Writes.IsUnknown, Is.False);
            Assert.That(createInitialized.Summary.Writes.Regions, Has.All.Property(
                nameof(EffectRegionId.Kind)).EqualTo(EffectRegionKind.Fresh));
        }
    }

    private static void AssertComplete(EffectMethodResult result, string methodName)
    {
        Assert.That(
            result.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete),
            methodName);
        Assert.That(result.Projection.IsComplete, Is.True, methodName);
    }
}
