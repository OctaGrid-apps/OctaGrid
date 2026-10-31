namespace OctaGrid.Client.Models;

public class AddPrinterListItem : IPrinterListItem
{
    public static readonly AddPrinterListItem Instance = new();

    private AddPrinterListItem()
    { }
}
