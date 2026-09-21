using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="RefCodeAllocator.Renumber"/> (PPDO-88) — the codes siblings take after a delete.
/// </summary>
public sealed class RefCodeAllocatorRenumberTests
{
    private const string Parent = "1000-000-1-01-010-001";

    [Fact]
    public void Renumber_MiddleSiblingDeleted_ShiftsLaterSiblingsDownByOne()
    {
        IReadOnlyDictionary<string, string> moves =
            RefCodeAllocator.Renumber(Parent, [$"{Parent}-001", $"{Parent}-003", $"{Parent}-004"]);

        Assert.Equal(2, moves.Count);
        Assert.Equal($"{Parent}-002", moves[$"{Parent}-003"]);
        Assert.Equal($"{Parent}-003", moves[$"{Parent}-004"]);
    }

    [Fact]
    public void Renumber_LastSiblingDeleted_MovesNothing()
    {
        IReadOnlyDictionary<string, string> moves =
            RefCodeAllocator.Renumber(Parent, [$"{Parent}-001", $"{Parent}-002"]);

        Assert.Empty(moves);
    }

    [Fact]
    public void Renumber_GapsAlreadyPresent_CollapseIntoOneSequence()
    {
        IReadOnlyDictionary<string, string> moves =
            RefCodeAllocator.Renumber(Parent, [$"{Parent}-002", $"{Parent}-005", $"{Parent}-009"]);

        Assert.Equal($"{Parent}-001", moves[$"{Parent}-002"]);
        Assert.Equal($"{Parent}-002", moves[$"{Parent}-005"]);
        Assert.Equal($"{Parent}-003", moves[$"{Parent}-009"]);
    }

    [Fact]
    public void Renumber_SiblingsOutOfOrder_OrdersBySequenceNotInputOrder()
    {
        IReadOnlyDictionary<string, string> moves =
            RefCodeAllocator.Renumber(Parent, [$"{Parent}-003", $"{Parent}-001"]);

        Assert.Single(moves);
        Assert.Equal($"{Parent}-002", moves[$"{Parent}-003"]);
    }

    [Fact]
    public void Renumber_UnparseableSibling_KeepsItsCodeAndIsNotCounted()
    {
        IReadOnlyDictionary<string, string> moves =
            RefCodeAllocator.Renumber(Parent, [$"{Parent}-001", $"{Parent}-X1", $"{Parent}-003"]);

        Assert.Single(moves);
        Assert.Equal($"{Parent}-002", moves[$"{Parent}-003"]);
        Assert.DoesNotContain($"{Parent}-X1", moves.Keys);
    }

    [Fact]
    public void Renumber_PastNine_KeepsThreeDigitPadding()
    {
        List<string> siblings = Enumerable.Range(1, 9).Select(n => $"{Parent}-{n:D3}").ToList();
        siblings.Add($"{Parent}-011");

        IReadOnlyDictionary<string, string> moves = RefCodeAllocator.Renumber(Parent, siblings);

        Assert.Equal($"{Parent}-010", moves[$"{Parent}-011"]);
    }

    [Fact]
    public void Renumber_NoSiblings_MovesNothing()
        => Assert.Empty(RefCodeAllocator.Renumber(Parent, []));
}
