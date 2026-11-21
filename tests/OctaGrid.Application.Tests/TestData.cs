using Microsoft.EntityFrameworkCore;
using OctaGrid.Data;
using OctaGrid.Data.Models;

namespace OctaGrid.Application.Tests;

/// <summary>Builds entities through their factories, with the ids a test wants.</summary>
internal static class TestData
{
    /// <summary>A workspace owned by <paramref name="ownerId"/>, with its default dashboard.</summary>
    public static Workspace Workspace(Guid id, string name, Guid ownerId)
    {
        var workspace = Data.Models.Workspace.Create(name, ownerId, DateTimeOffset.UtcNow);
        workspace.Id = id;
        return workspace;
    }

    /// <summary>Adds a member to a saved workspace.</summary>
    public static void AddMember(OctaGridContext context, Guid workspaceId, Guid userId, Role role)
    {
        context.Workspaces
            .Include(w => w.Members)
            .Single(w => w.Id == workspaceId)
            .AddMember(userId, role, DateTimeOffset.UtcNow);
        context.SaveChanges();
    }

    /// <summary>
    /// Deletes a saved workspace's dashboards behind the aggregate's back, leaving it as a database from before every
    /// workspace had one.
    /// </summary>
    public static void RemoveDashboards(OctaGridContext context, Guid workspaceId) =>
        context.Dashboards.Where(d => d.WorkspaceId == workspaceId).ExecuteDelete();

    public static Printer Printer(Guid id, Guid workspaceId, string name, string connectorId = "x", bool enabled = true)
    {
        var printer = Data.Models.Printer.Create(workspaceId, name, connectorId, new Dictionary<string, string>(), enabled);
        printer.Id = id;
        return printer;
    }
}
