namespace OctaGrid.Prototype.Sample;

public sealed class SampleConnector : IConnector
{
    public ConnectorDescriptor Descriptor { get; } =
        new("sample.connector", "Sample connector (prototype)", "1.0.0");

    public string Ping() => $"pong from {typeof(SampleConnector).Assembly.Location}";
}

internal static class Program
{
    // Started by the host through the package's execution alias with --serve. Runs with the
    // extension package's own identity, so Windows allows it to execute from its own folder.
    private static void Main(string[] args)
    {
        if (!args.Contains("--serve"))
        {
            Console.WriteLine("OctaGrid sample connector package. Start it with --serve (the host does this).");
            return;
        }

        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            if (line == "quit") break;
            if (line == "ping")
            {
                Console.WriteLine($"pong from out-of-process connector, pid {Environment.ProcessId}, running {Environment.ProcessPath}");
            }
        }
    }
}
