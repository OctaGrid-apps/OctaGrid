using OctaGrid.Client.Models;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

public class PrinterFormTests
{
    private static readonly ConnectorDescriptor Bambu = new(
        "octagrid.bambu-mqtt", "Bambu Lab (LAN mode)", "1.0.0", "OctaGrid", ConnectorCapabilities.None,
        [
            new SettingField("url", "Printer address", SettingFieldType.Url),
            new SettingField("apiKey", "Access code", Secret: true),
            new SettingField("serialNumber", "Serial number", Required: false),
        ],
        []);

    private static readonly ConnectorDescriptor Moonraker = new(
        "octagrid.moonraker", "Moonraker (Klipper)", "1.0.0", "OctaGrid", ConnectorCapabilities.None,
        [
            new SettingField("url", "Printer address", SettingFieldType.Url),
            new SettingField("apiKey", "API key", Required: false, Secret: true),
        ],
        []);

    private static readonly ConnectorDescriptor Rich = new(
        "acme.rich", "Rich", "1.0.0", "Acme", ConnectorCapabilities.None,
        [
            new SettingField("port", "Port", SettingFieldType.Number),
            new SettingField("tls", "Use TLS", SettingFieldType.Boolean, Required: false),
            new SettingField("mode", "Mode", SettingFieldType.Choice, DefaultValue: "fast", Choices: ["fast", "slow"]),
        ],
        []);

    private static PrinterViewModel Form(string connectorId = "octagrid.bambu-mqtt", Printer? saved = null, params ConnectorDescriptor[] connectors)
    {
        var printer = saved ?? new Printer { Name = "My printer", ConnectorId = connectorId };
        printer.ConnectorId = connectorId;
        var form = new PrinterViewModel(printer);
        form.SetConnectors(connectors.Length == 0 ? [Bambu, Moonraker] : connectors);
        return form;
    }

    private static SettingFieldViewModel Field(PrinterViewModel form, string key) => form.Fields.Single(f => f.Key == key);

    [Fact]
    public void Builds_one_field_per_declared_setting_in_order()
    {
        var form = Form();

        Assert.Equal(["url", "apiKey", "serialNumber"], form.Fields.Select(f => f.Key));
        Assert.Equal("Printer address", Field(form, "url").Field.Label);
        Assert.Equal("Serial number (optional)", Field(form, "serialNumber").Label);
        Assert.True(Field(form, "apiKey").IsSecretInput);
        Assert.True(Field(form, "url").IsTextInput);
    }

    [Fact]
    public void Fields_start_from_the_saved_values_and_defaults()
    {
        var saved = new Printer
        {
            Name = "P",
            Settings = new Dictionary<string, string> { ["url"] = "10.0.0.2" },
            Secrets = new Dictionary<string, string> { ["apiKey"] = "999" },
        };

        var form = Form("octagrid.bambu-mqtt", saved);

        Assert.Equal("10.0.0.2", Field(form, "url").Value);
        Assert.Equal("999", Field(form, "apiKey").Value);
        Assert.Equal(string.Empty, Field(form, "serialNumber").Value);
    }

    [Fact]
    public void Defaults_are_applied_and_booleans_start_false()
    {
        var form = Form("acme.rich", connectors: Rich);

        Assert.Equal("fast", Field(form, "mode").Value);
        Assert.Equal("false", Field(form, "tls").Value);
        Assert.True(Field(form, "tls").IsToggle);
        Assert.True(Field(form, "mode").IsChoice);
        Assert.Equal(["fast", "slow"], Field(form, "mode").Choices);
    }

    [Fact]
    public void Switching_connector_rebuilds_the_fields_and_keeps_shared_values()
    {
        var form = Form();
        Field(form, "url").Value = "192.168.1.9";
        Field(form, "apiKey").Value = "123456";

        form.ConnectorId = "octagrid.moonraker";

        Assert.Equal(["url", "apiKey"], form.Fields.Select(f => f.Key));
        Assert.Equal("192.168.1.9", Field(form, "url").Value);
        Assert.Equal("123456", Field(form, "apiKey").Value);
    }

    [Fact]
    public void ToPrinter_splits_settings_and_secrets_and_leaves_out_empty_values()
    {
        var form = Form();
        Field(form, "url").Value = "  192.168.1.9  ";
        Field(form, "apiKey").Value = "123456";

        var printer = form.ToPrinter();

        Assert.Equal("octagrid.bambu-mqtt", printer.ConnectorId);
        Assert.Equal("My printer", printer.Name);
        Assert.Equal(["url"], printer.Settings.Keys);
        Assert.Equal("192.168.1.9", printer.Settings["url"]);
        Assert.Equal(["apiKey"], printer.Secrets.Keys);
        Assert.Equal("123456", printer.Secrets["apiKey"]);
    }

    [Fact]
    public void An_unavailable_connector_keeps_the_saved_settings()
    {
        var saved = new Printer
        {
            Name = "P",
            Settings = new Dictionary<string, string> { ["url"] = "10.0.0.2" },
            Secrets = new Dictionary<string, string> { ["apiKey"] = "999" },
        };

        var form = Form("octagrid.moonraker", saved, connectors: Bambu);
        var printer = form.ToPrinter();

        Assert.Empty(form.Fields);
        Assert.Null(form.Connector);
        Assert.Equal("10.0.0.2", printer.Settings["url"]);
        Assert.Equal("999", printer.Secrets["apiKey"]);
    }

    [Fact]
    public void Validation_flags_missing_required_fields_but_not_optional_ones()
    {
        var form = Form();

        var valid = form.ValidateAll();

        Assert.False(valid);
        Assert.True(Field(form, "url").HasError);
        Assert.True(Field(form, "apiKey").HasError);
        Assert.False(Field(form, "serialNumber").HasError);
    }

    [Fact]
    public void Validation_passes_when_required_fields_are_filled()
    {
        var form = Form();
        Field(form, "url").Value = "192.168.1.9";
        Field(form, "apiKey").Value = "123456";

        Assert.True(form.ValidateAll());
    }

    [Fact]
    public void Editing_a_field_clears_its_message()
    {
        var form = Form();
        form.ValidateAll();

        Field(form, "url").Value = "192.168.1.9";

        Assert.False(Field(form, "url").HasError);
    }

    [Fact]
    public void Numbers_and_choices_are_checked()
    {
        var form = Form("acme.rich", connectors: Rich);
        Field(form, "port").Value = "abc";

        Assert.False(form.ValidateAll());
        Assert.Equal("Enter a number.", Field(form, "port").Error);

        Field(form, "port").Value = "8080";
        Field(form, "mode").Value = "medium";
        Assert.False(form.ValidateAll());
        Assert.False(Field(form, "port").HasError);
        Assert.True(Field(form, "mode").HasError);

        Field(form, "mode").Value = "slow";
        Assert.True(form.ValidateAll());
    }

    [Fact]
    public void The_toggle_bridges_to_the_text_value()
    {
        var form = Form("acme.rich", connectors: Rich);

        Field(form, "tls").BoolValue = true;

        Assert.Equal("true", Field(form, "tls").Value);
        Assert.True(Field(form, "tls").BoolValue);
    }

    [Fact]
    public void Testing_the_connection_needs_a_connector_and_the_required_fields()
    {
        var form = Form();
        Assert.False(form.CanTestConnection());

        Field(form, "url").Value = "192.168.1.9";
        Assert.False(form.CanTestConnection());

        Field(form, "apiKey").Value = "123456";
        Assert.True(form.CanTestConnection());
    }

    [Fact]
    public void Collected_settings_include_secrets_and_skip_empty_values()
    {
        var form = Form();
        Field(form, "url").Value = "192.168.1.9";
        Field(form, "apiKey").Value = "123456";

        var settings = form.CollectSettings();

        Assert.Equal(2, settings.Count);
        Assert.Equal("123456", settings["apiKey"]);
    }

    [Fact]
    public void Editing_a_field_raises_the_fields_changed_event()
    {
        var form = Form();
        var raised = 0;
        form.FieldsChanged += (_, _) => raised++;

        Field(form, "url").Value = "x";

        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_list_shows_the_connector_name_and_its_address_setting()
    {
        var response = new PrinterResponse(
            Guid.NewGuid(), "P", "octagrid.bambu-mqtt",
            new Dictionary<string, string> { ["url"] = "192.168.1.9", ["serialNumber"] = "01P" },
            new Dictionary<string, string> { ["apiKey"] = "123456" },
            "Connected", true, 3);

        var item = response.ToPrinterListItem([Bambu]);

        Assert.Equal("Bambu Lab (LAN mode)", item.ConnectorName);
        Assert.Equal("192.168.1.9", item.Address);
        Assert.Equal(3, item.Version);
    }

    [Fact]
    public void The_list_falls_back_to_the_id_and_a_url_setting_when_the_connector_is_unavailable()
    {
        var response = new PrinterResponse(
            Guid.NewGuid(), "P", "acme.gone",
            new Dictionary<string, string> { ["url"] = "10.0.0.1" },
            new Dictionary<string, string>(),
            "Unknown", true, 1);

        var item = response.ToPrinterListItem([Bambu]);

        Assert.Equal("acme.gone", item.ConnectorName);
        Assert.Equal("10.0.0.1", item.Address);
    }

    [Fact]
    public void Address_uses_the_first_non_secret_url_setting()
    {
        var connector = new ConnectorDescriptor(
            "acme.x", "X", "1.0.0", "Acme", ConnectorCapabilities.None,
            [new SettingField("token", "Token", Secret: true), new SettingField("host", "Host", SettingFieldType.Url)], []);

        var address = PrinterDisplay.Address(connector, new Dictionary<string, string> { ["host"] = "h.local", ["token"] = "t" });

        Assert.Equal("h.local", address);
    }
}
