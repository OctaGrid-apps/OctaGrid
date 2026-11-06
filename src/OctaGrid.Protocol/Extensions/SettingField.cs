namespace OctaGrid.Protocol.Extensions;

public enum SettingFieldType
{
    Text,
    Url,
    Number,
    Boolean,

    /// <summary>One value out of <see cref="SettingField.Choices"/>.</summary>
    Choice,
}

/// <summary>
/// Describes one setting of a connector or one parameter of a custom command. The client renders the
/// "add printer" form from these descriptions, so a new connector needs no client code.
/// </summary>
/// <param name="Key">Name under which the value is passed to the connector.</param>
/// <param name="Label">Text shown next to the input.</param>
/// <param name="Type">Kind of input.</param>
/// <param name="Required">Whether the user must provide a value.</param>
/// <param name="Secret">Whether the value is a credential: masked in the UI and stored in the credential store.</param>
/// <param name="DefaultValue">Value pre-filled in the form.</param>
/// <param name="Help">Short hint shown under the input.</param>
/// <param name="Choices">Allowed values when <paramref name="Type"/> is <see cref="SettingFieldType.Choice"/>.</param>
public sealed record SettingField(
    string Key,
    string Label,
    SettingFieldType Type = SettingFieldType.Text,
    bool Required = true,
    bool Secret = false,
    string? DefaultValue = null,
    string? Help = null,
    IReadOnlyList<string>? Choices = null);
