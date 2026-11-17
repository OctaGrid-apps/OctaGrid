using System.Globalization;
using Avalonia.Data.Converters;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Client.Converters;

/// <summary>
/// For a row that carries its own list of granted permissions (a workspace in a list of workspaces): whether the
/// permission given as the parameter is among them. A list that is not known withholds nothing.
/// </summary>
internal sealed class HasPermissionConverter : IValueConverter
{
    public static readonly HasPermissionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is not Permission permission
        || WorkspacePermissions.Allows(value as IReadOnlyList<Permission>, permission);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
