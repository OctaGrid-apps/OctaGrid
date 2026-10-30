using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Telemetry;
using OctaGrid.Application.Validation;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Services;

public sealed class PrintersService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintersService> _logger;
    private readonly IMessagePublisher _publisher;

    public PrintersService(OctaGridContext context, IMessagePublisher publisher, ILogger<PrintersService>? logger = null)
    {
        _context = context;
        _publisher = publisher;
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

        var printer = new Printer
        {
            Name = request.Name,
            EndpointAddress = request.Url,
            Protocol = request.ConnectionProtocol.ParseOrThrow<PrinterProtocol>(),
            EncryptedApiKey = string.IsNullOrEmpty(request.ApiKey) ? null : request.ApiKey,
            Enabled = request.Enabled,
            WorkspaceId = workspaceId,
            Status = new PrinterStatus()
            {
                State = ConnectionState.Unknown
            }
        };

        await _context.Printers.AddAsync(printer, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created printer {PrinterId} ('{PrinterName}', {Protocol} @ {Endpoint}, Enabled={Enabled}) in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.Protocol, printer.EndpointAddress, printer.Enabled, workspaceId, userId);

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

    public async Task<List<DeviceProfile>> GetDeviceProfiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var printers = await _context.Printers
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.Enabled)
            .Select(p => new { p.Id, p.Protocol, p.EndpointAddress, p.EncryptedApiKey })
            .ToListAsync(cancellationToken);

        // ToDo: EncryptedApiKey is currently stored as-is (see Create/Update); decrypt here once it isn't.
        return printers
            .Select(p => new DeviceProfile(
                p.Id,
                p.Protocol.ToString(),
                p.EndpointAddress,
                string.IsNullOrEmpty(p.EncryptedApiKey)
                    ? []
                    : new Dictionary<string, string> { ["ApiKey"] = p.EncryptedApiKey }))
            .ToList();
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
                    p.EndpointAddress,
                    p.Protocol.ToString(),
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.EncryptedApiKey,
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
                    p.EndpointAddress,
                    p.Protocol.ToString(),
                    p.Status != null ? p.Status.State.ToString() : nameof(ConnectionState.Unknown),
                    p.EncryptedApiKey,
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

        printer.Name = request.Name!;
        printer.EndpointAddress = request.Url!;
        printer.EncryptedApiKey = string.IsNullOrEmpty(request.ApiKey) ? null : request.ApiKey;
        printer.Protocol = request.ConnectionProtocol!.ParseOrThrow<PrinterProtocol>();
        printer.Enabled = request.Enabled;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated printer {PrinterId} ('{PrinterName}', {Protocol} @ {Endpoint}, Enabled={Enabled}) in workspace {WorkspaceId} by user {UserId}",
            printer.Id, printer.Name, printer.Protocol, printer.EndpointAddress, printer.Enabled, printer.WorkspaceId, userId);

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

    private static PrinterResponse MapToResponse(Printer printer) =>
        new(printer.Id, printer.Name, printer.EndpointAddress, printer.Protocol.ToString(), printer.Status != null ? printer.Status.State.ToString() : nameof(ConnectionState.Unknown), printer.EncryptedApiKey, printer.Enabled, printer.Version);
}
