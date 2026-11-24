using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class DuplicateCandidateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(.01)]
    [InlineData(.5)]
    [InlineData(.82)]
    [InlineData(1)]
    public void MatchesExhaustiveOracleIncludingRepeatedFingerprintsAndLanguagePartitions(double threshold)
    {
        var random = new Random(781);
        var methods = Enumerable.Range(0, 100).Select(i => Executable(i,
            Enumerable.Range(0, random.Next(1, 30)).Select(_ => "node" + random.Next(30)).ToArray(), i % 3 == 0 ? "java" : "csharp")).ToArray();
        var expected = methods.SelectMany((left, i) => methods.Skip(i + 1).Where(right => right.Language == left.Language)
            .Select(right => new DuplicateMatch(left, right, Similarity(left, right))))
            .Where(match => match.Similarity >= threshold).OrderBy(match => match.Left.Id, StringComparer.Ordinal)
            .ThenBy(match => match.Right.Id, StringComparer.Ordinal).ToArray();
        var actual = new DuplicateCandidates(methods, default).Matches(threshold)
            .OrderBy(match => match.Left.Id, StringComparer.Ordinal).ThenBy(match => match.Right.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SparseCandidatesAvoidDisjointComparisonsAndKeepInclusiveBoundary()
    {
        var methods = new[] { Executable(0, ["a", "b", "c"]), Executable(1, ["a", "b", "d"]), Executable(2, ["e", "f", "g"]) };
        var search = new DuplicateCandidates(methods, default);
        Assert.Equal(.5, Assert.Single(search.Matches(.5)).Similarity);
        Assert.Equal(1, search.Comparisons);
        Assert.Equal(3, new DuplicateCandidates(methods, default).Matches(0).Count());
    }

    [Fact]
    public void CancellationIsObservedEvenWithoutQualifyingCandidates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new DuplicateCandidates([Executable(0, ["a"])], cancellation.Token).Matches(.82).ToArray());
    }

    private static StructuralExecutable Executable(int index, string[] fingerprints, string language = "csharp") =>
        new()
        {
            Id = "executable" + index, TypeId = "Type" + index, Language = language, Location = new("source", 1, 10),
            Complexity = 1, Fingerprints = fingerprints, NodeCount = 30, Syntax = true, IsMethod = index % 2 == 0
        };

    private static double Similarity(StructuralExecutable left, StructuralExecutable right)
    {
        var first = left.Fingerprints.ToHashSet(StringComparer.Ordinal);
        var second = right.Fingerprints.ToHashSet(StringComparer.Ordinal);
        var shared = first.Count(second.Contains);
        return (double)shared / (first.Count + second.Count - shared);
    }
}
