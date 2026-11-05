using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using OctaGrid.Prototype;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation.Collections;

// Probe for ADR 0001, open question 2: can a packaged full-trust host discover third-party
// MSIX app extensions and load managed code from them in-process?
//
// Run it through its execution alias:   octagrid-probe

const string ContractName = "octagrid.connector";

var log = new StringBuilder();
void Log(string line)
{
    Console.WriteLine(line);
    log.AppendLine(line);
}

try
{
    Log($"Host package:      {Package.Current.Id.FullName}");
    Log($"Host API version:  {ExtensionApi.Current}");
    Log($"Contract name:     {ContractName}");
    Log("");

    var catalog = AppExtensionCatalog.Open(ContractName);
    var extensions = await catalog.FindAllAsync();
    Log($"Found {extensions.Count} extension(s)");

    foreach (var ext in extensions)
    {
        Log("");
        Log($"--- {ext.DisplayName} (id '{ext.Id}') ---");
        Log($"Package:           {ext.Package.Id.FullName}");
        Log($"Publisher:         {ext.Package.Id.Publisher}");
        Log($"Signature kind:    {ext.Package.SignatureKind}");
        Log($"Status OK:         {ext.Package.Status.VerifyIsOK()}");

        var properties = await ext.GetExtensionPropertiesAsync();
        Log("Properties:");
        Dump(properties, "  ", Log);

        var assemblyFile = Read(properties, "Assembly");
        var entryType = Read(properties, "EntryType");
        var apiVersion = Version.TryParse(Read(properties, "ApiVersion"), out var v) ? v : null;

        Log("");
        Log($"API compatibility: extension {apiVersion?.ToString() ?? "?"} vs host {ExtensionApi.Current} -> " +
            (apiVersion is not null && apiVersion.Major == ExtensionApi.Current.Major &&
             apiVersion.Minor <= ExtensionApi.Current.Minor ? "COMPATIBLE" : "INCOMPATIBLE"));

        // A) Package install location (C:\Program Files\WindowsApps\...)
        string? installDir = null;
        try { installDir = ext.Package.EffectiveLocation?.Path; }
        catch (Exception ex) { Log($"EffectiveLocation:   FAILED {ex.GetType().Name}: {ex.Message}"); }
        Log($"Install location:  {installDir ?? "(none)"}");
        if (installDir is not null && assemblyFile is not null && entryType is not null)
        {
            TryLoad("A) package install location", Path.Combine(installDir, assemblyFile), entryType, Log);
        }

        // D) Out of process: start the extension's own executable through its execution alias
        var alias = Read(properties, "Alias");
        if (alias is not null)
        {
            Log("");
            Log($"[D) out-of-process] starting alias {alias} --serve");
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(alias, "--serve")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;
                process.StandardInput.WriteLine("ping");
                process.StandardInput.Flush();
                var reply = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Log($"  REPLY:           {reply}");
                process.StandardInput.WriteLine("quit");
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Log($"  FAILED:          {ex.GetType().Name}: {ex.Message}");
            }
        }

        // C) Read the files, copy the package content into the host's own data folder, load the copy
        if (installDir is not null && assemblyFile is not null && entryType is not null)
        {
            try
            {
                var bytes = File.ReadAllBytes(Path.Combine(installDir, assemblyFile));
                Log($"[C) read] ReadAllBytes OK ({bytes.Length} bytes)");

                var target = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "extensions",
                    ext.Package.Id.FamilyName, ext.Package.Id.Version.Major + "." + ext.Package.Id.Version.Minor + "." +
                    ext.Package.Id.Version.Build + "." + ext.Package.Id.Version.Revision);
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.TopDirectoryOnly))
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                }
                Log($"[C) copy] copied package files to {target}");
                TryLoad("C) copy in host data folder", Path.Combine(target, assemblyFile), entryType, Log);
            }
            catch (Exception ex)
            {
                Log($"[C) read/copy] FAILED {ex.GetType().Name}: {ex.Message}");
            }
        }

        // B) Public folder, read through the extension broker
        try
        {
            var publicFolder = await ext.GetPublicFolderAsync();
            Log($"Public folder:     {publicFolder.Path}");
            if (assemblyFile is not null && entryType is not null)
            {
                TryLoad("B) public folder", Path.Combine(publicFolder.Path, assemblyFile), entryType, Log);
            }
        }
        catch (Exception ex)
        {
            Log($"Public folder:     FAILED {ex.GetType().Name}: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    Log($"PROBE FAILED: {ex}");
}

var logPath = Path.Combine(Path.GetTempPath(), "octagrid-probe.log");
try { File.WriteAllText(logPath, log.ToString()); Console.WriteLine($"\nLog written to {logPath}"); }
catch (Exception ex) { Console.WriteLine($"\nCould not write log: {ex.Message}"); }

static string? Read(IPropertySet set, string key) =>
    set.TryGetValue(key, out var value)
        ? value is IPropertySet nested
            ? nested.TryGetValue("#text", out var text) ? text?.ToString() : null
            : value?.ToString()
        : null;

static void Dump(IPropertySet set, string indent, Action<string> log)
{
    foreach (var pair in set)
    {
        if (pair.Value is IPropertySet nested)
        {
            log($"{indent}{pair.Key}:");
            Dump(nested, indent + "  ", log);
        }
        else
        {
            log($"{indent}{pair.Key} = {pair.Value}");
        }
    }
}

static void TryLoad(string label, string assemblyPath, string entryType, Action<string> log)
{
    log("");
    log($"[{label}] loading {assemblyPath}");
    try
    {
        log($"  file exists:     {File.Exists(assemblyPath)}");
        var context = new ExtensionLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(entryType, throwOnError: true)!;
        var connector = (IConnector)Activator.CreateInstance(type)!;
        log($"  LOADED:          {connector.Descriptor}");
        log($"  Ping():          {connector.Ping()}");
        log($"  SDK loaded from: {AssemblyLoadContext.GetLoadContext(typeof(IConnector).Assembly)?.Name} context (shared with the extension)");
    }
    catch (Exception ex)
    {
        log($"  FAILED:          {ex.GetType().Name}: {ex.Message}");
    }
}

/// <summary>
/// Per-extension load context. The SDK assembly always resolves from the host so that
/// <see cref="IConnector"/> is the same type on both sides.
/// </summary>
internal sealed class ExtensionLoadContext(string assemblyPath) : AssemblyLoadContext(assemblyPath, isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(IConnector).Assembly.GetName().Name)
        {
            return null; // fall back to the default context
        }

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
