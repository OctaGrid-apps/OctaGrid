using System.Globalization;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>The orders the printer's files can be listed in.</summary>
public enum FileSortOrder
{
    NewestFirst,
    OldestFirst,
    NameAscending,
    NameDescending,
    LargestFirst,
    SmallestFirst,
}

/// <summary>A sort order and its label in the sort menu.</summary>
public sealed record FileSortOption(FileSortOrder Order, string Label)
{
    /// <summary>Every order, as the menu lists them.</summary>
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
    /// Sorts files by <paramref name="order"/>. Ties are ordered by name, with numbers compared
    /// by value ("plate_2" before "plate_10").
    /// </summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> files, Func<T, PrintMetadata> metadata, FileSortOrder order)
    {
        var byName = Comparer<string>.Create((a, b) =>
            string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

        return order switch
        {
            // Files without a date go last in both directions.
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
