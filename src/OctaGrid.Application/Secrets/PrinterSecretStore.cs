using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application.Secrets;

/// <summary>
/// Encrypts and decrypts the secrets kept on a printer row. It checks no permission: callers authorize the user
/// first, and never hand decrypted values back to a client.
/// </summary>
public sealed class PrinterSecretStore
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrinterSecretStore> _logger;
    private readonly ISecretProtector _protector;

    public PrinterSecretStore(OctaGridContext context, ISecretProtector protector, ILogger<PrinterSecretStore>? logger = null)
    {
        _context = context;
        _protector = protector;
        _logger = logger ?? NullLogger<PrinterSecretStore>.Instance;
    }

    /// <summary>Encrypts the secrets, key names included, into the printer row.</summary>
    public void Write(Printer printer, PrinterSecrets secrets) =>
        printer.StoreSecrets(secrets.IsEmpty
            ? null
            : _protector.Protect(JsonSerializer.Serialize(secrets.Values), printer.Id.ToString("N")));

    /// <summary>
    /// Decrypts a printer's secrets. When they cannot be read (the key was lost, or the database came from
    /// another machine) the printer is treated as having none, so it can still be opened and the secrets
    /// entered again; the connection then fails with the printer's own "not authorised" answer.
    /// </summary>
    public PrinterSecrets Read(Guid printerId, string? protectedSecrets)
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

    /// <summary>The saved secrets of a printer.</summary>
    /// <exception cref="NotFoundException">There is no such printer.</exception>
    public async Task<PrinterSecrets> ReadSavedAsync(Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == printerId)
            .Select(p => new { p.ProtectedSecrets })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        return Read(printerId, printer.ProtectedSecrets);
    }
}
