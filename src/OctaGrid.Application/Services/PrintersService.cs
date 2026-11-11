using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Telemetry;
using OctaGrid.Application.Validation;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Services;

public sealed class PrintersService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintersService> _logger;
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();

    private readonly IMessagePublisher _publisher;
    private readonly IPrinterSecretStore _secrets;

    public PrintersService(OctaGridContext context, IMessagePublisher publisher, IPrinterSecretStore secrets, ILogger<PrintersService>? logger = null)
    {
        _context = context;
        _publisher = publisher;
        _secrets = secrets;
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
            // Set here, not on save, because the secrets are stored under the printer id first.
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Settings = new Dictionary<string, string>(request.Settings),
            ConnectorId = request.ConnectorId,
            SecretKeys = SecretKeys(secrets),
            Enabled = request.Enabled,
            WorkspaceId = workspaceId,
            Status = new PrinterStatus()
            {
                State = ConnectionState.Unknown
            }
        };

        if (secrets.Count > 0)
        {
            await _secrets.SetAsync(printer.Id, secrets, cancellationToken);
        }

        try
        {
            await _context.Printers.AddAsync(printer, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave secrets behind for a printer that was never created.
            await _secrets.SetAsync(printer.Id, NoSecrets, CancellationToken.None);
            throw;
        }

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
        await _secrets.SetAsync(printer.Id, NoSecrets, cancellationToken);

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
            .Select(p => new { p.Id, p.ConnectorId, p.Settings, p.SecretKeys })
            .ToListAsync(cancellationToken);

        // The connector receives plain settings and secrets together, by the keys it declared.
        var profiles = new List<PrinterProfile>(printers.Count);
        foreach (var p in printers)
        {
            var settings = new Dictionary<string, string>(p.Settings);
            if (p.SecretKeys.Count > 0)
            {
                foreach (var (key, value) in await _secrets.GetAsync(p.Id, cancellationToken))
                {
                    settings[key] = value;
                }
            }

            profiles.Add(new PrinterProfile(p.Id, p.ConnectorId, settings));
        }

        return profiles;
    }

    /// <summary>
    /// The settings to test a connection with while a saved printer is being edited: what the user entered,
    /// plus the printer's saved secrets for the keys the user left empty (a client cannot read those back).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> AddSavedSecrets(
        Guid userId, Guid printerId, IReadOnlyDictionary<string, string> entered, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == printerId)
            .Select(p => new { p.WorkspaceId, p.SecretKeys })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, printer.WorkspaceId, cancellationToken);

        if (printer.SecretKeys.Count == 0)
        {
            return entered;
        }

        var settings = new Dictionary<string, string>(entered);
        foreach (var (key, value) in await _secrets.GetAsync(printerId, cancellationToken))
        {
            if (!settings.TryGetValue(key, out var typed) || string.IsNullOrWhiteSpace(typed))
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
                new PrinterResponse(p.Id,
                    p.Name,
                    p.ConnectorId,
                    p.Settings,
                    p.SecretKeys,
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.Enabled,
                    p.Version))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);
        return response;
    }

    public async Task<List<PrinterResponse>> GetPrintersList(Guid userId, Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var response = await _context.Printers
            .AsNoTracking()
            .WhereIf(query.Enabled.HasValue, p => p.Enabled == query.Enabled!.Value)
            .Where(p => p.WorkspaceId == workspaceId)
            .Select(p =>
                new PrinterResponse(p.Id,
                    p.Name,
                    p.ConnectorId,
                    p.Settings,
                    p.SecretKeys,
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.Enabled,
                    p.Version))
            .ToListAsync(cancellationToken);

        return response;
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
        var saved = connectorChanged || printer.SecretKeys.Count == 0
            ? NoSecrets
            : await _secrets.GetAsync(printer.Id, cancellationToken);
        var secrets = ApplySecretChanges(saved, request.Secrets);
        if (connectorChanged || request.Secrets is { Count: > 0 })
        {
            await _secrets.SetAsync(printer.Id, secrets, cancellationToken);
        }

        printer.Name = request.Name!;
        printer.Settings = new Dictionary<string, string>(request.Settings!);
        printer.SecretKeys = SecretKeys(secrets);
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

    private static List<string> SecretKeys(Dictionary<string, string> secrets) =>
        secrets.Keys.Order(StringComparer.Ordinal).ToList();

    private static PrinterResponse MapToResponse(Printer printer) =>
        new(printer.Id, printer.Name, printer.ConnectorId, printer.Settings, printer.SecretKeys, printer.Status != null ? printer.Status.State.ToString() : nameof(ConnectionState.Unknown), printer.Enabled, printer.Version);
}
