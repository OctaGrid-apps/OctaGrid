namespace OctaGrid.Client.Models;

public class Option<T>
{
    public Option(string displayText, T value)
    {
        DisplayText = displayText;
        Value = value;
    }

    public string DisplayText { get; set; } = "";
    public T Value { get; set; }
}
