using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintersService> _logger;

    private readonly ISecretProtector _protector;

    public PrintersService(OctaGridContext context, WorkspaceAccess access, ISecretProtector protector, ILogger<PrintersService>? logger = null)
    {
        _context = context;
        _access = access;
        _protector = protector;
        _logger = logger ?? NullLogger<PrintersService>.Instance;
    }

    public async Task<PrinterResponse> Create(Guid userId, Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.ManagePrinters, cancellationToken);

        var validator = new CreatePrinterRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        var agent = await ResolveAgentAsync(workspaceId, request.AgentId, cancellationToken);

        var printer = Printer.Create(workspaceId, request.Name, request.ConnectorId, request.Settings, request.Enabled, agent);
        WriteSecrets(printer, PrinterSecrets.None.With(request.Secrets));

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

    /// <summary>
    /// The printers an agent has to connect to (its enabled ones), with their settings and decrypted secrets.
    /// This is what is pushed to the agent; it never reads the database itself. The backend does this by itself,
    /// so no user is checked: never return the result to a client.
    /// </summary>
    public async Task<List<PrinterProfile>> GetPrinterProfiles(Guid agentId, CancellationToken cancellationToken)
    {
        var printers = await _context.Printers
            .AsNoTracking()
            .Where(p => p.AgentId == agentId && p.Enabled)
            .Select(p => new { p.Id, p.ConnectorId, p.Settings, p.ProtectedSecrets })
            .ToListAsync(cancellationToken);

        // The connector receives plain settings and secrets together, by the keys it declared.
        return printers
            .Select(p =>
            {
                var settings = new Dictionary<string, string>(p.Settings);
                foreach (var (key, value) in ReadSecrets(p.Id, p.ProtectedSecrets).Values)
                {
                    settings[key] = value;
                }

                return new PrinterProfile(p.Id, p.ConnectorId, settings);
            })
            .ToList();
    }

    /// <summary>
    /// The settings to test a connection with while a saved printer is being edited. A client cannot read saved
    /// secrets back, so a secret that is left out of <paramref name="entered"/> is filled in from what is saved.
    /// A secret that is present with an empty value is one the user is removing: it stays out.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> AddSavedSecrets(
        Guid userId, Guid printerId, IReadOnlyDictionary<string, string> entered, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == printerId)
            .Select(p => new { p.WorkspaceId, p.ProtectedSecrets })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, Permission.ManagePrinters, cancellationToken);

        return ReadSecrets(printerId, printer.ProtectedSecrets).With(entered).Values;
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

        var validator = new UpdatePrinterRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        // Drops the saved secrets when the connector changes: they belong to the connector they were entered for.
        printer.Reconfigure(request.Name!, request.ConnectorId!, request.Settings!, request.Enabled);
        if (request.Secrets is { Count: > 0 })
        {
            // Encrypting again changes the stored text even for equal values, so only do it when something changed.
            var saved = ReadSecrets(printer.Id, printer.ProtectedSecrets);
            var secrets = saved.With(request.Secrets);
            if (!secrets.SameAs(saved))
            {
                WriteSecrets(printer, secrets);
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

        var validator = new UpdatePrinterIdentityRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        printer.Rename(request.Name!);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer identity {PrinterId} to '{PrinterName}' in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.WorkspaceId, userId);


        return MapToResponse(printer);
    }

    /// <summary>
    /// The agent a new printer gets: the one asked for, which must be in the printer's workspace, or else the
    /// workspace's only agent. With several agents and none asked for, the printer starts without one.
    /// </summary>
    // The printer checks that the agent is in its workspace when it is assigned.
    private async Task<Agent?> ResolveAgentAsync(Guid workspaceId, Guid? requested, CancellationToken cancellationToken)
    {
        if (requested is { } agentId)
        {
            return await _context.Agents
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Agent), agentId);
        }

        var agents = await _context.Agents
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            .OrderBy(a => a.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return agents.Count == 1 ? agents[0] : null;
    }

    /// <summary>Encrypts the secrets, key names included, into the printer row.</summary>
    private void WriteSecrets(Printer printer, PrinterSecrets secrets) =>
        printer.StoreSecrets(secrets.IsEmpty
            ? null
            : _protector.Protect(JsonSerializer.Serialize(secrets.Values), printer.Id.ToString("N")));

    /// <summary>
    /// Decrypts a printer's secrets. When they cannot be read (the key was lost, or the database came from
    /// another machine) the printer is treated as having none, so it can still be opened and the secrets
    /// entered again; the connection then fails with the printer's own "not authorised" answer.
    /// </summary>
    private PrinterSecrets ReadSecrets(Guid printerId, string? protectedSecrets)
    {
        if (string.IsNullOrEmpty(protectedSecrets))
        {
            return PrinterSecrets.None;
        }

        try
        {
            var json = _protector.Unprotect(protectedSecrets, printerId.ToString("N"));
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values is null ? PrinterSecrets.None : new PrinterSecrets(values);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // The exception carries no secret material, only why decryption failed.
            _logger.LogWarning(ex, "The saved secrets of printer {PrinterId} cannot be read; they have to be entered again", printerId);
            return PrinterSecrets.None;
        }
    }

    /// <summary>
    /// The names of the secrets that have a saved value, never the values. They are read from the encrypted
    /// text, so secrets that can no longer be decrypted are reported as not saved.
    /// </summary>
    private IReadOnlyList<string> SecretKeys(Guid printerId, string? protectedSecrets) =>
        ReadSecrets(printerId, protectedSecrets).Keys;

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
