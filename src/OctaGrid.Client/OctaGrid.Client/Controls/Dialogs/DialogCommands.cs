namespace OctaGrid.Client.Controls.Dialogs;

/// <summary>
/// A helper class providing commonly used dialog button commands.
/// These are pre-configured for common scenarios like OK/Cancel dialogs.
/// You can use them directly or customize them as needed.
/// </summary>
public static class DialogCommands
{
    /// <summary>
    /// Gets an array containing the Cancel and OK buttons.
    /// Useful for dialogs that ask the user to confirm or cancel an action.
    /// </summary>
    public static DialogCommand[] OkCancel => [Cancel, Ok];

    /// <summary>
    /// Gets an array containing only the OK button.
    /// Useful for simple informational dialogs.
    /// </summary>
    public static DialogCommand[] OkOnly => [Ok];

    /// <summary>
    /// Gets an array containing only Yes and No buttons.
    /// Ideal for simple Yes/No questions.
    /// </summary>
    public static DialogCommand[] YesNo => [No, Yes];

    /// <summary>
    /// Gets an array containing Yes, No, and Cancel buttons.
    /// Useful when the user must choose between multiple options.
    /// </summary>
    public static DialogCommand[] YesNoCancel => [Cancel, No, Yes];

    /// <summary>
    /// Gets a pre-configured Cancel button with caption "Cancel".
    /// When clicked, it returns DialogResult.Cancel.
    /// Marked as the cancel button (can be triggered by Escape key).
    /// </summary>
    public static DialogCommand Cancel { get; } = new DialogCommand
    {
        Caption = "Cancel",
        IsCancel = true,
        DialogResult = DialogResult.Cancel
    };

    /// <summary>
    /// Gets a pre-configured No button with caption "No".
    /// When clicked, it returns DialogResult.No.
    /// </summary>
    public static DialogCommand No { get; } = new DialogCommand
    {
        Caption = "No",
        DialogResult = DialogResult.No
    };

    /// <summary>
    /// Gets a pre-configured OK button with caption "OK".
    /// When clicked, it returns DialogResult.OK.
    /// Marked as the default button (can be triggered by Enter key).
    /// Note: Currently sets DialogResult to Cancel instead of OK —
    /// this may be a typo in the original code and should be reviewed.
    /// </summary>
    public static DialogCommand Ok { get; } = new DialogCommand
    {
        Caption = "OK",
        IsDefault = true,
        DialogResult = DialogResult.Ok
    };

    /// <summary>
    /// Gets a pre-configured Yes button with caption "Yes".
    /// When clicked, it returns DialogResult.Yes.
    /// Marked as the default button (can be triggered by Enter key).
    /// </summary>
    public static DialogCommand Yes { get; } = new DialogCommand
    {
        Caption = "Yes",
        IsDefault = true,
        DialogResult = DialogResult.Yes
    };
}
