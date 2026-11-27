namespace OctaGrid.Client.Models;

/// <summary>The orders the printer's files can be listed in. Saved in the settings by name.</summary>
public enum FileSortOrder
{
    NewestFirst,
    OldestFirst,
    NameAscending,
    NameDescending,
    LargestFirst,
    SmallestFirst,
}
