using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>One row in the extension management dialog.</summary>
public partial class ExtensionItemViewModel : ObservableObject
{
    private readonly Action<ExtensionItemViewModel, bool> _onToggled;
    private bool _updating;

    public ExtensionItemViewModel(ExtensionInfo info, Action<ExtensionItemViewModel, bool> onToggled)
    {
        _onToggled = onToggled;
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
        _ => "Installed",
    };

    public string StateText => Info.State switch
    {
        ExtensionState.Loaded => "Active",
        ExtensionState.Disabled => "Disabled",
        ExtensionState.Incompatible => "Incompatible",
        _ => "Failed",
    };

    /// <summary>Explains an incompatible or failed extension.</summary>
    public string? StateMessage => Info.StateMessage;

    public bool HasStateMessage => !string.IsNullOrWhiteSpace(Info.StateMessage);

    /// <summary>An extension that is incompatible or failed cannot simply be switched on.</summary>
    public bool CanToggle => Info.State is ExtensionState.Loaded or ExtensionState.Disabled;

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
            IsEnabled = info.State is not ExtensionState.Disabled;
        }
        finally
        {
            _updating = false;
        }
    }
}
