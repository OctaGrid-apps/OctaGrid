using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>One row in the extension management dialog.</summary>
public partial class ExtensionItemViewModel : ObservableObject
{
    private readonly Action<ExtensionItemViewModel, bool> _onToggled;
    private readonly Action<ExtensionItemViewModel> _onReview;
    private readonly Action<ExtensionItemViewModel> _onRemove;
    private bool _updating;

    public ExtensionItemViewModel(
        ExtensionInfo info,
        Action<ExtensionItemViewModel, bool> onToggled,
        Action<ExtensionItemViewModel> onReview,
        Action<ExtensionItemViewModel> onRemove)
    {
        _onToggled = onToggled;
        _onReview = onReview;
        _onRemove = onRemove;
        Info = info;
        Apply(info);
    }

    public ExtensionInfo Info { get; private set; }

    public string Id => Info.Id;

    public string Name => Info.Name;

    public string Details => $"{Info.Publisher} · v{Info.Version} · {SourceText}";

    public string SourceText => Info.Source switch
    {
        ExtensionSource.Bundled => "Built in",
        ExtensionSource.Store => "Microsoft Store",
        ExtensionSource.Development => "Developer folder",
        _ => "Installed",
    };

    public string StateText => Info.State switch
    {
        ExtensionState.Loaded => "Active",
        ExtensionState.Disabled => "Disabled",
        ExtensionState.Incompatible => "Incompatible",
        ExtensionState.NeedsApproval => "Needs approval",
        _ => "Failed",
    };

    /// <summary>The extension's web page, or null when it has none or the address is not a web address.</summary>
    public Uri? ProjectUri =>
        Uri.TryCreate(Info.ProjectUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    public bool HasProjectUri => ProjectUri is not null;

    /// <summary>Explains an incompatible or failed extension.</summary>
    public string? StateMessage => Info.StateMessage;

    public bool HasStateMessage => !string.IsNullOrWhiteSpace(Info.StateMessage);

    /// <summary>Tells the user what turning the extension off would stop.</summary>
    public string? UsageText => Info is { State: ExtensionState.Loaded, ActivePrinters: > 0 }
        ? Info.ActivePrinters == 1
            ? "1 printer is connected through this. Turning it off stops that printer."
            : $"{Info.ActivePrinters} printers are connected through this. Turning it off stops them."
        : null;

    public bool HasUsage => UsageText is not null;

    /// <summary>An extension that is incompatible or failed cannot simply be switched on.</summary>
    public bool CanToggle => Info.State is ExtensionState.Loaded or ExtensionState.Disabled;

    /// <summary>An installed extension that waits for the user to trust it before any of its code runs.</summary>
    public bool NeedsApproval => Info.State == ExtensionState.NeedsApproval;

    /// <summary>Installed extensions can be removed; built-in ones can only be turned off.</summary>
    public bool CanUninstall => Info.CanUninstall;

    [RelayCommand]
    private void Review() => _onReview(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);

    public string CapabilitiesText
    {
        get
        {
            var parts = new List<string>();
            if (Info.Capabilities.HasFlag(ConnectorCapabilities.PrintControl)) parts.Add("Print control");
            if (Info.Capabilities.HasFlag(ConnectorCapabilities.Files)) parts.Add("Files");
            if (Info.Capabilities.HasFlag(ConnectorCapabilities.Thumbnails)) parts.Add("Thumbnails");
            if (Info.Capabilities.HasFlag(ConnectorCapabilities.CustomCommands)) parts.Add("Custom commands");
            if (Info.Capabilities.HasFlag(ConnectorCapabilities.Discovery)) parts.Add("Discovery");
            return parts.Count == 0 ? "Connector" : "Connector · " + string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>Takes over the latest state from the agent without raising a toggle.</summary>
    public void Update(ExtensionInfo info)
    {
        Info = info;
        Apply(info);
        OnPropertyChanged(string.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_updating)
        {
            _onToggled(this, value);
        }
    }

    private void Apply(ExtensionInfo info)
    {
        _updating = true;
        try
        {
            IsEnabled = info.State is ExtensionState.Loaded;
        }
        finally
        {
            _updating = false;
        }
    }
}
