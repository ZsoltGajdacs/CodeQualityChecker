using Cqc.Core;
using Cqc.Java;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class EvidenceTests
{
    [Theory]
    [InlineData(-1, 1, 0, 1)]
    [InlineData(2, 1, 0, 1)]
    [InlineData(0, 0, 2, 1)]
    public void InvalidCountersAreRejectedAtBoundary(int branches, int totalBranches, int lines, int totalLines)
    {
        var evidence = new CoverageEvidence("2", "source", "config", true, new(), [new("method", branches, totalBranches, lines, totalLines)]);
        Assert.Throws<ArgumentException>(() => EvidenceStore.Read(System.Text.Json.JsonSerializer.Serialize(evidence, Wire.Json)));
    }

    [Fact]
    public void DuplicateCoverageIdentitiesAreRejected()
    {
        var entry = new MethodCoverage("method", 1, 2, 2, 3);
        var evidence = new CoverageEvidence("2", "source", "config", true, new(), [entry, entry]);
        Assert.Throws<ArgumentException>(() => EvidenceStore.Read(System.Text.Json.JsonSerializer.Serialize(evidence, Wire.Json)));
    }

    [Fact]
    public void CoverageReadersMapOverloadsAndBranchlessMethods()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "<report><package><class name=\"Demo/Thing\"><method name=\"run\" desc=\"(ILjava/lang/String;)I\"><counter type=\"BRANCH\" missed=\"1\" covered=\"3\"/><counter type=\"LINE\" missed=\"0\" covered=\"4\"/></method><method name=\"run\" desc=\"()I\"><counter type=\"LINE\" missed=\"0\" covered=\"1\"/></method></class></package></report>");
            var facts = new[] { Fact("java:Demo.Thing.run(int,java.lang.String)"), Fact("java:Demo.Thing.run()") };
            var coverage = JavaCoverageReader.ReadJava(file, facts);
            Assert.Equal(2, coverage.Length);
            Assert.Equal(3, coverage[0].CoveredBranches);
            Assert.Equal(0, coverage[1].TotalBranches);
            Assert.Equal(1, coverage[1].CoveredLines);
        }
        finally { File.Delete(file); }
    }

    private static MethodFact Fact(string id) => new(id, "java:Demo.Thing", "java", new("Thing.java", 1, 5), 2, 4, false, false, false, 0, [], [], 20);

    [Fact]
    public void JavaGenericCoverageUsesErasedSignatureWithoutChangingFindingIdentity()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "<report><class name=\"Demo/Thing\"><method name=\"run\" desc=\"(Ljava/util/List;)V\"><counter type=\"LINE\" covered=\"1\" missed=\"0\"/></method></class></report>");
            var fact = Fact("java:Demo.Thing.run(java.util.List&lt;java.lang.String&gt;)") with { CoverageSignature = "java:Demo.Thing.run(java.util.List)" };
            var entry = Assert.Single(JavaCoverageReader.ReadJava(file, [fact]));
            Assert.Equal(fact.Id, entry.MethodId);
            Assert.Equal(1, entry.CoveredLines);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void OpenCoverUnionsComplementaryBranchesAndMapsGenericSourcePoints()
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        try
        {
            File.WriteAllText(first, OpenCoverReport(1, 0));
            File.WriteAllText(second, OpenCoverReport(0, 1));
            var method = new MethodFact("csharp:Demo.Example.Run(System.Collections.Generic.List<System.String>)", "csharp:Demo.Example", "csharp",
                new("Example.cs", 1, 5), 2, 3, false, false, false, 0, [], [], 20);
            var entry = Assert.Single(CSharpCoverageReader.ReadReports([first, second], [method]));
            Assert.Equal(method.Id, entry.MethodId);
            Assert.Equal(2, entry.TotalBranches);
            Assert.Equal(2, entry.CoveredBranches);
            Assert.Equal(1, entry.TotalLines);
            Assert.Equal(1, entry.CoveredLines);
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [Fact]
    public void OpenCoverMapsGenericArityEvenWhenOverloadsShareASourceLine()
    {
        using var fixture = FixtureWorkspace.Empty();
        var report = Path.Combine(fixture.Root, "opencover.xml");
        File.WriteAllText(report, """
            <CoverageSession><Modules><Module>
              <Files><File uid="1" fullPath="/scratch/Example.cs" /></Files>
              <Classes><Class><FullName>Demo.Example</FullName><Methods>
                <Method><Name>System.Int32 Demo.Example::Run()</Name><FileRef uid="1" />
                  <SequencePoints><SequencePoint sl="3" vc="1" /></SequencePoints></Method>
                <Method><Name>System.Int32 Demo.Example::Run&lt;T&gt;()</Name><FileRef uid="1" />
                  <SequencePoints><SequencePoint sl="3" vc="0" /></SequencePoints></Method>
              </Methods></Class></Classes>
            </Module></Modules></CoverageSession>
            """);
        var facts = new[] { "Demo.Example.Run()", "Demo.Example.Run``1()" }.Select(signature =>
            new MethodFact("csharp:Demo.csproj#" + signature, "csharp:Demo.csproj#Demo.Example", "csharp",
                new("Example.cs", 3, 3), 1, 1, false, false, false, 0, [], [], 10) { CoverageSignature = signature });

        var coverage = CSharpCoverageReader.ReadReport(report, facts);
        Assert.Equal(2, coverage.Length);
        Assert.Equal(1, Assert.Single(coverage, method => method.MethodId.EndsWith("#Demo.Example.Run()", StringComparison.Ordinal)).CoveredLines);
        Assert.Equal(0, Assert.Single(coverage, method => method.MethodId.EndsWith("#Demo.Example.Run``1()", StringComparison.Ordinal)).CoveredLines);
    }

    private static string OpenCoverReport(int left, int right) => $$"""
        <CoverageSession><Modules><Module><Files><File uid="1" fullPath="/scratch/Example.cs" /></Files><Classes><Class>
        <FullName>Demo.Example</FullName><Methods><Method><Name>System.Void Demo.Example::Run(System.Collections.Generic.List`1&lt;System.String&gt;)</Name><FileRef uid="1" />
        <SequencePoints><SequencePoint sl="3" vc="{{left + right}}" /></SequencePoints><BranchPoints>
        <BranchPoint offset="1" path="0" ordinal="0" vc="{{left}}" /><BranchPoint offset="1" path="1" ordinal="1" vc="{{right}}" />
        </BranchPoints></Method></Methods></Class></Classes></Module></Modules></CoverageSession>
        """;
}
