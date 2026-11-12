using System.Reflection;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Extensions.Abstractions.Tests;

/// <summary>
/// Extension authors compile against the SDK and its model types only. These tests keep the app's wire
/// contract (OctaGrid.Protocol) and other dependencies from leaking back into that surface.
/// </summary>
public sealed class SdkDependencyTests
{
    private static IReadOnlyList<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

    [Fact]
    public void The_SDK_does_not_reference_the_app_protocol()
    {
        Assert.DoesNotContain("OctaGrid.Protocol", References(typeof(IConnector).Assembly));
    }

    [Fact]
    public void The_SDK_references_only_its_models_and_the_logging_abstractions()
    {
        var own = References(typeof(IConnector).Assembly)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal) && name != "netstandard")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Microsoft.Extensions.Logging.Abstractions", "OctaGrid.Extensions.Models"], own);
    }

    [Fact]
    public void The_model_types_depend_on_nothing_but_the_framework()
    {
        var other = References(typeof(ConnectorDescriptor).Assembly)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal) && name != "netstandard");

        Assert.Empty(other);
    }
}
