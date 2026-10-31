namespace OctaGrid.Protocol.Printers;

public sealed record PrintMetadata(string FileName, int FileSize, string? MaterialType, double? NozzleDiameter, double? EstimatedTimeSeconds);
