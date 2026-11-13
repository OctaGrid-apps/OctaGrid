using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Telemetry;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Services;

public sealed class PrintersService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintersService> _logger;
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();

    private readonly IMessagePublisher _publisher;
    private readonly ISecretProtector _protector;

    public PrintersService(OctaGridContext context, IMessagePublisher publisher, ISecretProtector protector, ILogger<PrintersService>? logger = null)
    {
        _context = context;
        _publisher = publisher;
        _protector = protector;
        _logger = logger ?? NullLogger<PrintersService>.Instance;
    }

    public async Task<PrinterResponse> Create(Guid userId, Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var validator = new CreatePrinterRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        var secrets = ApplySecretChanges(NoSecrets, request.Secrets);

        var printer = new Printer
        {
            // Set here, not on save, because the secrets are encrypted for this printer id.
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Settings = new Dictionary<string, string>(request.Settings),
            ConnectorId = request.ConnectorId,
            Enabled = request.Enabled,
            WorkspaceId = workspaceId,
            Status = new PrinterStatus()
            {
                State = ConnectionState.Unknown
            }
        };
        WriteSecrets(printer, secrets);

        // The secrets are part of the row, so they are saved or not saved together with the printer.
        await _context.Printers.AddAsync(printer, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created printer {PrinterId} ('{PrinterName}', {ConnectorId}, Enabled={Enabled}) in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.ConnectorId, printer.Enabled, workspaceId, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Printer, printer.Id, printer.Version, ChangeKind.Upserted, printer.WorkspaceId));

        return MapToResponse(printer);
    }

    public async Task Delete(Guid userId, Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        _context.Printers.Remove(printer);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted printer {PrinterId} ('{PrinterName}') from workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.WorkspaceId, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Printer, printer.Id, printer.Version, ChangeKind.Deleted, printer.WorkspaceId));
    }

    public async Task<List<PrinterProfile>> GetPrinterProfiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var printers = await _context.Printers
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.Enabled)
            .Select(p => new { p.Id, p.ConnectorId, p.Settings, p.ProtectedSecrets })
            .ToListAsync(cancellationToken);

        // The connector receives plain settings and secrets together, by the keys it declared.
        return printers
            .Select(p =>
            {
                var settings = new Dictionary<string, string>(p.Settings);
                foreach (var (key, value) in ReadSecrets(p.Id, p.ProtectedSecrets))
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

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        var settings = new Dictionary<string, string>();
        foreach (var (key, value) in ReadSecrets(printerId, printer.ProtectedSecrets))
        {
            if (!entered.ContainsKey(key))
            {
                settings[key] = value;
            }
        }

        foreach (var (key, value) in entered)
        {
            if (!string.IsNullOrEmpty(value))
            {
                settings[key] = value;
            }
        }

        return settings;
    }

    public async Task<PrinterResponse> GetPrinter(Guid userId, Guid workspaceId, Guid printerId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var response = await _context.Printers
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.Id == printerId)
            .Select(p =>
                new PrinterRow(p.Id,
                    p.Name,
                    p.ConnectorId,
                    p.Settings,
                    p.ProtectedSecrets,
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.Enabled,
                    p.Version))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);
        return MapToResponse(response);
    }

    public async Task<List<PrinterResponse>> GetPrintersList(Guid userId, Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

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
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.Enabled,
                    p.Version))
            .ToListAsync(cancellationToken);

        return response.Select(MapToResponse).ToList();
    }

    public async Task SetEnabled(Guid userId, Guid printerId, bool state, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        if (printer.Enabled == state)
        {
            return;
        }

        printer.Enabled = state;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Printer {PrinterId} ('{PrinterName}') enabled state set to {Enabled} in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, state, printer.WorkspaceId, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Printer, printer.Id, printer.Version, ChangeKind.Upserted, printer.WorkspaceId));
    }

    public async Task<PrinterResponse> Update(Guid userId, Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        var validator = new UpdatePrinterRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        // Saved secrets belong to the connector they were entered for; another connector starts without them.
        var connectorChanged = !string.Equals(printer.ConnectorId, request.ConnectorId, StringComparison.OrdinalIgnoreCase);
        var saved = connectorChanged ? NoSecrets : ReadSecrets(printer.Id, printer.ProtectedSecrets);
        if (connectorChanged || request.Secrets is { Count: > 0 })
        {
            // Encrypting again changes the stored text even for equal values, so only do it when something changed.
            WriteSecrets(printer, ApplySecretChanges(saved, request.Secrets));
        }

        printer.Name = request.Name!;
        printer.Settings = new Dictionary<string, string>(request.Settings!);
        printer.ConnectorId = request.ConnectorId!;
        printer.Enabled = request.Enabled;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer {PrinterId} ('{PrinterName}', {ConnectorId}, Enabled={Enabled}) in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.ConnectorId, printer.Enabled, printer.WorkspaceId, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Printer, printer.Id, printer.Version, ChangeKind.Upserted, printer.WorkspaceId));

        return MapToResponse(printer);
    }

    public async Task<PrinterResponse> UpdateIdentity(Guid userId, Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers.FindAsync([printerId], cancellationToken: cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        var validator = new UpdatePrinterIdentityRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        printer.Name = request.Name!;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer identity {PrinterId} to '{PrinterName}' in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.WorkspaceId, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Printer, printer.Id, printer.Version, ChangeKind.Upserted, printer.WorkspaceId));

        return MapToResponse(printer);
    }

    /// <summary>A key with a value is saved, a key with an empty value is removed, and a key that is left out is kept.</summary>
    private static Dictionary<string, string> ApplySecretChanges(
        IReadOnlyDictionary<string, string> saved, IReadOnlyDictionary<string, string>? changes)
    {
        var result = new Dictionary<string, string>(saved);
        foreach (var (key, value) in changes ?? NoSecrets)
        {
            if (string.IsNullOrEmpty(value))
            {
                result.Remove(key);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Encrypts the secrets, key names included, into the printer row.</summary>
    private void WriteSecrets(Printer printer, Dictionary<string, string> secrets)
    {
        printer.ProtectedSecrets = secrets.Count == 0
            ? null
            : _protector.Protect(JsonSerializer.Serialize(secrets), printer.Id.ToString("N"));
    }

    /// <summary>
    /// Decrypts a printer's secrets. When they cannot be read (the key was lost, or the database came from
    /// another machine) the printer is treated as having none, so it can still be opened and the secrets
    /// entered again; the connection then fails with the printer's own "not authorised" answer.
    /// </summary>
    private IReadOnlyDictionary<string, string> ReadSecrets(Guid printerId, string? protectedSecrets)
    {
        if (string.IsNullOrEmpty(protectedSecrets))
        {
            return NoSecrets;
        }

        try
        {
            var json = _protector.Unprotect(protectedSecrets, printerId.ToString("N"));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? NoSecrets;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // The exception carries no secret material, only why decryption failed.
            _logger.LogWarning(ex, "The saved secrets of printer {PrinterId} cannot be read; they have to be entered again", printerId);
            return NoSecrets;
        }
    }

    /// <summary>
    /// The names of the secrets that have a saved value, never the values. They are read from the encrypted
    /// text, so secrets that can no longer be decrypted are reported as not saved.
    /// </summary>
    private IReadOnlyList<string> SecretKeys(Guid printerId, string? protectedSecrets) =>
        ReadSecrets(printerId, protectedSecrets).Keys.Order(StringComparer.Ordinal).ToList();

    private PrinterResponse MapToResponse(Printer printer) =>
        new(printer.Id, printer.Name, printer.ConnectorId, printer.Settings, SecretKeys(printer.Id, printer.ProtectedSecrets), printer.Status != null ? printer.Status.State.ToString() : nameof(ConnectionState.Unknown), printer.Enabled, printer.Version);

    private PrinterResponse MapToResponse(PrinterRow row) =>
        new(row.Id, row.Name, row.ConnectorId, row.Settings, SecretKeys(row.Id, row.ProtectedSecrets), row.ConnectionState, row.Enabled, row.Version);

    /// <summary>What a read query selects; the secret names are worked out after it, by decrypting.</summary>
    private sealed record PrinterRow(
        Guid Id,
        string Name,
        string ConnectorId,
        Dictionary<string, string> Settings,
        string? ProtectedSecrets,
        string ConnectionState,
        bool Enabled,
        long Version);
}
