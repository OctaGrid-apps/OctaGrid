using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Services;

public sealed class PrintersService
{
    private static readonly CreatePrinterRequestValidator CreateValidator = new();
    private static readonly UpdatePrinterRequestValidator UpdateValidator = new();
    private static readonly UpdatePrinterIdentityRequestValidator UpdateIdentityValidator = new();

    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintersService> _logger;
    private readonly PrinterRouting _routing;
    private readonly PrinterSecretStore _secrets;

    public PrintersService(
        OctaGridContext context,
        WorkspaceAccess access,
        PrinterRouting routing,
        PrinterSecretStore secrets,
        ILogger<PrintersService>? logger = null)
    {
        _context = context;
        _access = access;
        _routing = routing;
        _secrets = secrets;
        _logger = logger ?? NullLogger<PrintersService>.Instance;
    }

    public async Task<PrinterResponse> Create(Guid userId, Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.ManagePrinters, cancellationToken);
        CreateValidator.ValidateOrThrow(request);

        var agent = await _routing.AgentForNewPrinter(workspaceId, request.AgentId, cancellationToken);

        var printer = Printer.Create(workspaceId, request.Name, request.ConnectorId, request.Settings, request.Enabled, agent);
        _secrets.Write(printer, PrinterSecrets.None.With(request.Secrets));

        // The secrets are part of the row, so they are saved or not saved together with the printer.
        await _context.Printers.AddAsync(printer, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created printer {PrinterId} ('{PrinterName}', {ConnectorId}, Enabled={Enabled}) for agent {AgentId} in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.ConnectorId, printer.Enabled, printer.AgentId, workspaceId, userId);

        return MapToResponse(printer);
    }

    public async Task Delete(Guid userId, Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, Permission.ManagePrinters, cancellationToken);

        printer.Remove();
        _context.Printers.Remove(printer);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted printer {PrinterId} ('{PrinterName}') from workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.WorkspaceId, userId);
    }

    public async Task<PrinterResponse> GetPrinter(Guid userId, Guid workspaceId, Guid printerId, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.View, cancellationToken);

        var response = await _context.Printers
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.Id == printerId)
            .Select(p =>
                new PrinterRow(p.Id,
                    p.Name,
                    p.ConnectorId,
                    p.Settings,
                    p.ProtectedSecrets,
                    p.Status != null ? p.Status.State : null,
                    p.Enabled,
                    p.Version,
                    p.AgentId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);
        return MapToResponse(response);
    }

    public async Task<List<PrinterResponse>> GetPrintersList(Guid userId, Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.View, cancellationToken);

        var response = await _context.Printers
            .AsNoTracking()
            .WhereIf(query.Enabled.HasValue, p => p.Enabled == query.Enabled!.Value)
            .Where(p => p.WorkspaceId == workspaceId)
            .Select(p =>
                new PrinterRow(p.Id,
                    p.Name,
                    p.ConnectorId,
                    p.Settings,
                    p.ProtectedSecrets,
                    p.Status != null ? p.Status.State : null,
                    p.Enabled,
                    p.Version,
                    p.AgentId))
            .ToListAsync(cancellationToken);

        return response.Select(MapToResponse).ToList();
    }

    public async Task SetEnabled(Guid userId, Guid printerId, bool state, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, Permission.ManagePrinters, cancellationToken);

        if (!printer.SetEnabled(state))
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Printer {PrinterId} ('{PrinterName}') enabled state set to {Enabled} in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, state, printer.WorkspaceId, userId);
    }

    public async Task<PrinterResponse> Update(Guid userId, Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, Permission.ManagePrinters, cancellationToken);
        UpdateValidator.ValidateOrThrow(request);

        // Drops the saved secrets when the connector changes: they belong to the connector they were entered for.
        printer.Reconfigure(request.Name!, request.ConnectorId!, request.Settings!, request.Enabled);
        if (request.Secrets is { Count: > 0 })
        {
            // Encrypting again changes the stored text even for equal values, so only do it when something changed.
            var saved = _secrets.Read(printer.Id, printer.ProtectedSecrets);
            var secrets = saved.With(request.Secrets);
            if (!secrets.SameAs(saved))
            {
                _secrets.Write(printer, secrets);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer {PrinterId} ('{PrinterName}', {ConnectorId}, Enabled={Enabled}) in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.ConnectorId, printer.Enabled, printer.WorkspaceId, userId);

        return MapToResponse(printer);
    }

    public async Task<PrinterResponse> UpdateIdentity(Guid userId, Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, Permission.ManagePrinters, cancellationToken);
        UpdateIdentityValidator.ValidateOrThrow(request);

        printer.Rename(request.Name!);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer identity {PrinterId} to '{PrinterName}' in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.WorkspaceId, userId);

        return MapToResponse(printer);
    }

    /// <summary>
    /// The names of the secrets that have a saved value, never the values. They are read from the encrypted
    /// text, so secrets that can no longer be decrypted are reported as not saved.
    /// </summary>
    private IReadOnlyList<string> SecretKeys(Guid printerId, string? protectedSecrets) =>
        _secrets.Read(printerId, protectedSecrets).Keys;

    private PrinterResponse MapToResponse(Printer printer) =>
        new(printer.Id, printer.Name, printer.ConnectorId, printer.Settings, SecretKeys(printer.Id, printer.ProtectedSecrets), printer.Enabled, printer.Version, printer.AgentId, printer.Status?.State);

    private PrinterResponse MapToResponse(PrinterRow row) =>
        new(row.Id, row.Name, row.ConnectorId, row.Settings, SecretKeys(row.Id, row.ProtectedSecrets), row.Enabled, row.Version, row.AgentId, row.State);

    /// <summary>What a read query selects; the secret names are worked out after it, by decrypting.</summary>
    private sealed record PrinterRow(
        Guid Id,
        string Name,
        string ConnectorId,
        Dictionary<string, string> Settings,
        string? ProtectedSecrets,
        UniversalPrinterState? State,
        bool Enabled,
        long Version,
        Guid? AgentId);
}
