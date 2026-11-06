namespace OctaGrid.Connectors.BambuLab;

internal static class BambuTopics
{
    public static string Report(string serial) => $"device/{serial}/report";

    public static string Request(string serial) => $"device/{serial}/request";
}
