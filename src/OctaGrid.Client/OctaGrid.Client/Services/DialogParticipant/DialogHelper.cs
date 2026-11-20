using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls.Dialogs;

namespace OctaGrid.Client.Services;

/// <summary>
/// A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Dialog, file picker and notification helpers for a registered context, most likely a ViewModel.
    /// </summary>
    /// <param name="context">The context used to resolve the TopLevel, window or overlay</param>
    extension(IDialogParticipant? context)
    {
        /// <summary>
        /// Shows an open file dialog for a registered context, most likely a ViewModel
        /// </summary>
        /// <param name="title">The dialog title or a default is null</param>
        /// <param name="fileTypes">A list of <see cref="FilePickerFileType"/> to choose</param>
        /// <param name="selectMany">Is selecting many files allowed?</param>
        /// <returns>An array of file names</returns>
        /// <exception cref="ArgumentNullException">if context was null</exception>
        public async Task<IEnumerable<IStorageFile>?> OpenFileDialogAsync(
            string? title = null,
            FilePickerFileType[]? fileTypes = null,
            bool selectMany = false)
        {
            ArgumentNullException.ThrowIfNull(context);

            // lookup the TopLevel for the context. If no TopLevel was found, we throw an exception
            var topLevel = DialogManager.GetTopLevelForContext(context)
                           ?? throw new InvalidOperationException("No TopLevel was resolved for the given context.");

            // Open the file dialog
            var storageFiles = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions()
                {
                    AllowMultiple = selectMany,
                    FileTypeFilter = fileTypes,
                    Title = title ?? "Select any file(s)"
                });

            // return the result
            return storageFiles;
        }

        /// <summary>
        /// Closes a dialog window with the given result
        /// </summary>
        /// <param name="result">The result to return</param>
        /// <exception cref="InvalidOperationException">If the <see cref="TopLevel"/> is not a <see cref="Window"/></exception>
        public void ReturnResultFromDialogWindow(object? result)
        {
            ArgumentNullException.ThrowIfNull(context);

            var dialogWindow = DialogManager.GetTopLevelForContext(context) as Window
                               ?? throw new InvalidOperationException(
                                   "The method ReturnResultFromDialogWindow can only be used on a Window");

            dialogWindow.Close(result);
        }

        /// <summary>
        /// Shows a save file dialog for a registered context, most likely a ViewModel
        /// </summary>
        /// <param name="title">The dialog title or a default is null</param>
        /// <param name="fileTypes">A list of <see cref="FilePickerFileType"/> to choose</param>
        /// <param name="defaultExtension">The default extension to use</param>
        /// <param name="suggestedFileName">The suggested file name</param>
        /// <returns>The chosen file, or null if the dialog was canceled</returns>
        /// <exception cref="ArgumentNullException">if context was null</exception>
        public async Task<SaveFilePickerResult?> SafeFileDialogAsync(
            string? title = null,
            FilePickerFileType[]? fileTypes = null,
            string? defaultExtension = null,
            string? suggestedFileName = null)
        {
            ArgumentNullException.ThrowIfNull(context);

            // lookup the TopLevel for the context. If no TopLevel was found, we throw an exception
            var topLevel = DialogManager.GetTopLevelForContext(context)
                           ?? throw new InvalidOperationException("No TopLevel was resolved for the given context.");

            // Open the file dialog
            var storageFile = await topLevel.StorageProvider.SaveFilePickerWithResultAsync(
                new FilePickerSaveOptions()
                {
                    Title = title ?? "Save file as",
                    DefaultExtension = defaultExtension,
                    FileTypeChoices = fileTypes,
                    ShowOverwritePrompt = true,
                    SuggestedFileName = suggestedFileName
                });

            // return the result
            return storageFile;
        }

        /// <summary>
        /// Shows a dialog window for a given context
        /// </summary>
        /// <param name="windowTitle">The dialog's window title</param>
        /// <param name="content">The content to show</param>
        /// <param name="contentTemplate">Optional: An <see cref="IDataTemplate"/> to represnet the <see cref="content"/></param>
        /// <typeparam name="T">The expected type to return</typeparam>
        /// <returns>The result or null if dialog was canceled</returns>
        /// <exception cref="InvalidOperationException">The dialog window can only be shown if the app is a desktop app.</exception>
        public async Task<T?> ShowDialogWindow<T>(string windowTitle, object content, IDataTemplate? contentTemplate = null)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Get the owner window. If it is null, throw an exception
            var ownerWindow = DialogManager.GetTopLevelForContext(context) as Window
                              ?? throw new InvalidOperationException(
                                  "The method ShowDialogWindow can only be used on a Window");

            // Prepare the dialog window.
            var dialog = new Window()
            {
                Title = windowTitle,
                Content = content,
                ContentTemplate = contentTemplate,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            // Show the dialog and return it's result
            return await dialog.ShowDialog<T>(ownerWindow);
        }

        /// <summary>
        /// Adds a notification to the <see cref="WindowNotificationManager"/>.
        /// </summary>
        /// <param name="title">The title of the notification</param>
        /// <param name="message">The message of the notification</param>
        /// <param name="notificationType">The see <see cref="NotificationType"/> to use</param>
        /// <param name="expiration">The expiration time of the notification</param>
        /// <exception cref="InvalidOperationException">If no WindowNotificationManager was found for the given context</exception>
        public void ShowNotificationMessage(
            string title, string message,
            NotificationType notificationType = NotificationType.Information,
            TimeSpan? expiration = null)
        {
            context.ShowNotificationMessage(
                new Notification(title, message, notificationType,
                    expiration ?? TimeSpan.FromSeconds(3)));
        }

        /// <summary>
        /// Adds a given <see cref="Notification"/> to the <see cref="WindowNotificationManager"/>.
        /// </summary>
        /// <param name="notification">The notification to display</param>
        /// <exception cref="InvalidOperationException">If no WindowNotificationManager was found for the given context</exception>
        public void ShowNotificationMessage(Notification notification)
        {
            ArgumentNullException.ThrowIfNull(context);

            var notificationManager = DialogManager.GetVisualForContext(context) as WindowNotificationManager
                                      ?? throw new InvalidOperationException(
                                          "The method ShowNotificationMessage must be used on a WindowNotificationManager");

            notificationManager.Show(notification);
        }

        public Task<T?> ShowOverlayDialogAsync<T>(
            string? title,
            IDialogParticipant<T>? content,
            params DialogCommand[] dialogCommands)
        {
            return context.ShowOverlayDialogAsyncPrivate<T>(title, content, dialogCommands);
        }

        /// <summary>
        /// Shows an overlay dialog for a given context.
        /// </summary>
        /// <param name="title">The title of the dialog</param>
        /// <param name="content">The content to show</param>
        /// <param name="dialogCommands">The <see cref="DialogCommands"/> to show built in commands</param>
        /// <typeparam name="T">The expected return type</typeparam>
        /// <returns>the dialog result</returns>
        /// <exception cref="InvalidOperationException">If either the Toplevel or the Overlayer wasn't found</exception>
        public Task<T?> ShowOverlayDialogAsync<T>(
            string title,
            object? content,
            params DialogCommand[] dialogCommands)
        {
            return context.ShowOverlayDialogAsyncPrivate<T>(title, content, dialogCommands);
        }

        private async Task<T?> ShowOverlayDialogAsyncPrivate<T>(
            string? title,
            object? content,
            params DialogCommand[] dialogCommands)
        {
            var tcs = new TaskCompletionSource<object?>();

            ArgumentNullException.ThrowIfNull(context);

            var overlayDialog = new OverlayDialog(tcs)
            {
                Content = content,
                Header = title,
                DialogCommands = dialogCommands
            };

            // Get the owner window. If it is null, throw an exception
            var topLevel = DialogManager.GetTopLevelForContext(context)
                ?? throw new InvalidOperationException("Unable to find TopLevel for context");

            var overlayLayer = OverlayLayer.GetOverlayLayer(topLevel)
                ?? throw new InvalidOperationException("Unable to find OverlayLayer");

            overlayLayer.Children.Add(overlayDialog);

            var result = await tcs.Task;

            overlayLayer.Children.Remove(overlayDialog);

            return result is T t ? t : default;
        }
    }

    /// <summary>
    /// Helpers for the dialog content itself, which knows the type of result it returns.
    /// </summary>
    /// <param name="context">The context to resolve the overlay dialog</param>
    /// <typeparam name="T">The result type</typeparam>
    extension<T>(IDialogParticipant<T>? context)
    {
        /// <summary>
        /// Closes an overlay dialog with the given result
        /// </summary>
        /// <param name="result">The result to return</param>
        /// <exception cref="InvalidOperationException">If no <see cref="OverlayDialog"/> was found for the context</exception>
        public void ReturnResultFromOverlayDialog(T? result)
        {
            ArgumentNullException.ThrowIfNull(context);

            var overlayDialog = DialogManager.GetVisualForContext(context).
                    FindAncestorOfType<OverlayDialog>(includeSelf: true)
                    ?? throw new InvalidOperationException("Unable to find OverlayDialog");
            overlayDialog.Close(result);
        }
    }
}
