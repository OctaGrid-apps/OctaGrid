using OctaGrid.Application.Authorization;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Tests;

/// <summary>
/// What each role may do, written out. A change to a role's privileges has to be made here too, so it is never
/// made by accident.
/// </summary>
public sealed class RolePermissionPolicyTests
{
    private readonly RolePermissionPolicy _policy = new();

    public static TheoryData<Role, Permission[]> Roles => new()
    {
        { Role.Reader, [Permission.View] },
        { Role.Operator, [Permission.View, Permission.ControlPrint, Permission.ManageFiles] },
        {
            Role.Admin,
            [
                Permission.View, Permission.ControlPrint, Permission.ManageFiles, Permission.SendCommands,
                Permission.ManagePrinters, Permission.ManageDashboards, Permission.ManageAgents, Permission.ManageExtensions,
            ]
        },
        { Role.Owner, Enum.GetValues<Permission>() },
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public void A_role_has_exactly_its_permissions(Role role, Permission[] expected) =>
        Assert.Equal(expected.Order(), _policy.For(role).Order());

    [Fact]
    public void Every_role_is_listed_and_can_at_least_view()
    {
        foreach (var role in Enum.GetValues<Role>())
        {
            Assert.Contains(Permission.View, _policy.For(role));
        }
    }

    [Theory]
    [InlineData(Role.Reader, Role.Operator)]
    [InlineData(Role.Operator, Role.Admin)]
    [InlineData(Role.Admin, Role.Owner)]
    public void A_higher_role_can_do_everything_the_one_below_can(Role lower, Role higher) =>
        Assert.Subset(_policy.For(higher).ToHashSet(), _policy.For(lower).ToHashSet());

    [Fact]
    public void A_role_the_policy_does_not_know_may_do_nothing() =>
        Assert.Empty(_policy.For((Role)999));
}
