using System.Globalization;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>The orders the printer's files can be listed in.</summary>
public enum FileSortOrder
{
    /// <summary>Most recently added first, as the printer's own screen lists them.</summary>
    NewestFirst,
    OldestFirst,
    NameAscending,
    NameDescending,

    /// <summary>Largest first, to find what to delete when the card is full.</summary>
    LargestFirst,
    SmallestFirst,
}

/// <summary>A sort order as offered in the sort menu.</summary>
public sealed record FileSortOption(FileSortOrder Order, string Label)
{
    /// <summary>Every order, in the order the menu offers them.</summary>
    public static IReadOnlyList<FileSortOption> All { get; } =
    [
        new(FileSortOrder.NewestFirst, "Newest first"),
        new(FileSortOrder.OldestFirst, "Oldest first"),
        new(FileSortOrder.NameAscending, "Name (A to Z)"),
        new(FileSortOrder.NameDescending, "Name (Z to A)"),
        new(FileSortOrder.LargestFirst, "Largest first"),
        new(FileSortOrder.SmallestFirst, "Smallest first"),
    ];

    public override string ToString() => Label;

    /// <summary>
    /// Sorts files by <paramref name="order"/>. Ties, and files whose date the printer does not report, fall back to
    /// the name, compared as people read it ("plate_2" before "plate_10").
    /// </summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> files, Func<T, PrintMetadata> metadata, FileSortOrder order)
    {
        var byName = Comparer<string>.Create((a, b) =>
            string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

        return order switch
        {
            // Files without a date go last in both directions: they are neither new nor old.
            FileSortOrder.NewestFirst => files
                .OrderBy(f => metadata(f).ModifiedAt is null)
                .ThenByDescending(f => metadata(f).ModifiedAt)
                .ThenBy(f => metadata(f).FileName, byName),
            FileSortOrder.OldestFirst => files
                .OrderBy(f => metadata(f).ModifiedAt is null)
                .ThenBy(f => metadata(f).ModifiedAt)
                .ThenBy(f => metadata(f).FileName, byName),
            FileSortOrder.NameDescending => files.OrderByDescending(f => metadata(f).FileName, byName),
            FileSortOrder.LargestFirst => files
                .OrderByDescending(f => metadata(f).FileSize)
                .ThenBy(f => metadata(f).FileName, byName),
            FileSortOrder.SmallestFirst => files
                .OrderBy(f => metadata(f).FileSize)
                .ThenBy(f => metadata(f).FileName, byName),
            _ => files.OrderBy(f => metadata(f).FileName, byName),
        };
    }
}
