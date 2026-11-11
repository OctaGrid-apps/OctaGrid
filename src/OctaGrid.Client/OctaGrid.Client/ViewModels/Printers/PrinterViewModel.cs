using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Models;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The printer being created or edited. The connector-specific inputs are not fixed properties:
/// they are <see cref="Fields"/>, built from the settings the selected connector declares.
/// </summary>
public partial class PrinterViewModel : ViewModelValidatorBase
{
    private readonly Printer _original;
    private IReadOnlyList<ConnectorDescriptor> _connectors = [];

    public PrinterViewModel(Printer printer)
    {
        _original = printer;
        Id = printer.Id;
        Name = printer.Name;
        IsEnabled = printer.Enabled;
        ConnectorId = printer.ConnectorId;
    }

    /// <summary>Raised when the user edits a field, so dependent commands can re-check.</summary>
    public event EventHandler? FieldsChanged;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(3, 100)]
    public partial string ConnectorId { get; set; }

    /// <summary>The inputs of the selected connector. Empty when the connector is not available.</summary>
    public ObservableCollection<SettingFieldViewModel> Fields { get; } = [];

    /// <summary>The selected connector's description, or null when it is not available.</summary>
    public ConnectorDescriptor? Connector =>
        _connectors.FirstOrDefault(c => string.Equals(c.Id, ConnectorId, StringComparison.OrdinalIgnoreCase));

    [ObservableProperty]
    public partial Guid? Id { get; private set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 100)]
    public partial string Name { get; set; }

    /// <summary>Tells the form which connectors exist and builds the fields of the selected one.</summary>
    public void SetConnectors(IReadOnlyList<ConnectorDescriptor> connectors)
    {
        _connectors = connectors;
        RebuildFields();
    }

    /// <summary>
    /// The saved printer whose secrets still apply to this form, or null: a new printer has none, and after
    /// switching to another connector the saved ones belong to the previous connector.
    /// </summary>
    public Guid? SavedPrinterId =>
        Id is { } id && id != Guid.Empty && string.Equals(ConnectorId, _original.ConnectorId, StringComparison.OrdinalIgnoreCase) ? id : null;

    /// <summary>True when the connection can be tested: a connector is chosen and its required fields are filled in.</summary>
    public bool CanTestConnection() =>
        Connector is not null
        && Fields.All(f => !f.Required || f.Field.Type == SettingFieldType.Boolean || f.HasSavedValue || !string.IsNullOrWhiteSpace(f.Value));

    /// <summary>
    /// All entered values, secrets included, by key; used to test the connection. A saved secret the user did
    /// not retype is not in here: the backend fills it in for <see cref="SavedPrinterId"/>. A saved secret the
    /// user is removing is in here with an empty value, which tells the backend not to fill it in.
    /// </summary>
    public Dictionary<string, string> CollectSettings()
    {
        var all = new Dictionary<string, string>();
        foreach (var field in Fields)
        {
            if (!string.IsNullOrWhiteSpace(field.Value))
            {
                all[field.Key] = field.Value.Trim();
            }
            else if (field.IsSavedValueRemoved)
            {
                all[field.Key] = string.Empty;
            }
        }

        return all;
    }

    /// <summary>Checks the name, the connector and every field, showing what is wrong.</summary>
    /// <returns>True when the printer can be saved.</returns>
    public bool ValidateAll()
    {
        Validate();
        var fieldsValid = true;
        foreach (var field in Fields)
        {
            fieldsValid &= field.Validate();
        }

        return !HasErrors && fieldsValid;
    }

    public Printer ToPrinter()
    {
        var settings = new Dictionary<string, string>();
        var secrets = new Dictionary<string, string>();

        if (Connector is null)
        {
            // The connector is not available, so its fields are unknown: keep what was saved. Saved secrets
            // stay as they are when none are sent.
            settings = new Dictionary<string, string>(_original.Settings);
        }
        else
        {
            foreach (var field in Fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Value))
                {
                    (field.IsSecret ? secrets : settings)[field.Key] = field.Value.Trim();
                }
                else if (field.IsSavedValueRemoved)
                {
                    // An empty value asks the backend to remove the saved secret; leaving the key out keeps it.
                    secrets[field.Key] = string.Empty;
                }
            }
        }

        return new Printer
        {
            Id = Id ?? Guid.Empty,
            Name = Name ?? "",
            ConnectorId = ConnectorId ?? "",
            Settings = settings,
            Secrets = secrets,
            Enabled = IsEnabled,
        };
    }

    partial void OnConnectorIdChanged(string value)
    {
        RebuildFields();
        OnPropertyChanged(nameof(Connector));
    }

    private void RebuildFields()
    {
        // Values already typed win over the saved ones, so switching connector keeps shared inputs
        // (such as the printer address) instead of asking for them again.
        var typed = Fields.ToDictionary(f => f.Key, f => f.Value);

        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }

        Fields.Clear();

        var connector = Connector;
        if (connector is null)
        {
            FieldsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Saved secrets belong to the saved connector; after switching to another one they no longer apply.
        var connectorIsSaved = string.Equals(connector.Id, _original.ConnectorId, StringComparison.OrdinalIgnoreCase);

        foreach (var setting in connector.Settings)
        {
            var value = typed.GetValueOrDefault(setting.Key)
                        ?? (setting.Secret ? string.Empty : null)
                        ?? _original.Settings.GetValueOrDefault(setting.Key)
                        ?? setting.DefaultValue
                        ?? (setting.Type == SettingFieldType.Boolean ? "false" : string.Empty);

            // A saved secret is never sent to the client, so its input starts empty and means "keep it".
            var hasSavedValue = setting.Secret && connectorIsSaved && _original.SavedSecretKeys.Contains(setting.Key);
            var field = new SettingFieldViewModel(setting, value, hasSavedValue);
            field.PropertyChanged += OnFieldPropertyChanged;
            Fields.Add(field);
        }

        FieldsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFieldPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingFieldViewModel.Value))
        {
            FieldsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
