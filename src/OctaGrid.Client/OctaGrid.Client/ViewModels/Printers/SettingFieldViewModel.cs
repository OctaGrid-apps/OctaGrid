using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>One input in the Edit Printer form, built from a connector's declared setting.</summary>
public partial class SettingFieldViewModel : ObservableObject
{
    public SettingFieldViewModel(SettingField field, string initialValue, bool hasSavedValue = false)
    {
        Field = field;
        HasSavedValue = hasSavedValue;
        Value = initialValue;
    }

    public SettingField Field { get; }

    public string Key => Field.Key;

    public string Label => Field.Required ? Field.Label : $"{Field.Label} (optional)";

    public string? Help => Field.Help;

    public bool HasHelp => !string.IsNullOrWhiteSpace(Field.Help);

    public bool Required => Field.Required;

    public bool IsSecret => Field.Secret;

    /// <summary>
    /// True for a secret that already has a saved value. Saved secrets are never sent to the client, so the
    /// input is empty: leaving it empty keeps the saved value, typing replaces it.
    /// </summary>
    public bool HasSavedValue { get; }

    /// <summary>
    /// True when the saved value can be removed without replacing it: the secret is optional. A required
    /// secret can only be replaced.
    /// </summary>
    public bool CanRemoveSavedValue => HasSavedValue && !Field.Required;

    /// <summary>
    /// True when the user chose to remove the saved value. An empty input alone means "keep", so removing
    /// needs this explicit choice. Typing a value takes it back, because the new value replaces the saved one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecretPlaceholder))]
    [NotifyPropertyChangedFor(nameof(RemoveSavedValueLabel))]
    public partial bool IsSavedValueRemoved { get; set; }

    public string RemoveSavedValueLabel => IsSavedValueRemoved ? "Keep saved value" : "Remove saved value";

    /// <summary>Shown inside an empty secret input.</summary>
    public string? SecretPlaceholder => (HasSavedValue, IsSavedValueRemoved) switch
    {
        (false, _) => null,
        (true, true) => "Will be removed when you save.",
        (true, false) => "Saved. Type to replace.",
    };

    [RelayCommand]
    private void ToggleRemoveSavedValue()
    {
        if (CanRemoveSavedValue)
        {
            IsSavedValueRemoved = !IsSavedValueRemoved;
        }
    }

    public IReadOnlyList<string> Choices => Field.Choices ?? [];

    /// <summary>A normal text box (text, address, number).</summary>
    public bool IsTextInput => !Field.Secret && Field.Type is SettingFieldType.Text or SettingFieldType.Url or SettingFieldType.Number;

    /// <summary>A masked text box for credentials.</summary>
    public bool IsSecretInput => Field.Secret && Field.Type is not (SettingFieldType.Boolean or SettingFieldType.Choice);

    public bool IsToggle => Field.Type == SettingFieldType.Boolean;

    public bool IsChoice => Field.Type == SettingFieldType.Choice;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>The value as text. Booleans are <c>true</c> or <c>false</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoolValue))]
    public partial string Value { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>Bridges the text value to a toggle switch.</summary>
    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    /// <summary>Checks the value against the declared rules and shows the problem, if any.</summary>
    /// <returns>True when the value is acceptable.</returns>
    public bool Validate()
    {
        var empty = string.IsNullOrWhiteSpace(Value);
        if (Field.Required && empty && !HasSavedValue && Field.Type != SettingFieldType.Boolean)
        {
            Error = "This field is required.";
        }
        else if (!empty && Field.Type == SettingFieldType.Number
                 && !double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            Error = "Enter a number.";
        }
        else if (!empty && Field.Type == SettingFieldType.Choice && !Choices.Contains(Value))
        {
            Error = "Choose one of the options.";
        }
        else
        {
            Error = null;
        }

        return Error is null;
    }

    partial void OnValueChanged(string value)
    {
        // A typed value replaces the saved one, so there is nothing left to remove.
        if (!string.IsNullOrWhiteSpace(value))
        {
            IsSavedValueRemoved = false;
        }

        // Editing a field clears its message; it is checked again when the form is submitted.
        Error = null;
    }
}
