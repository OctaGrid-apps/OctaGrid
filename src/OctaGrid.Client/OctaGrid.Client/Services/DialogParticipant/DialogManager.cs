using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace OctaGrid.Client.Services;

/// <summary>
/// Manages registration and lookup of dialog participants (typically ViewModels).
/// Provides the infrastructure for connecting ViewModels to their corresponding UI elements
/// using attached properties to establish the ViewModel-to-View relationship.
/// </summary>
public class DialogManager
{
    /// <summary>
    /// Attached property used to bind Views to their ViewModels.
    /// Set this property in XAML to register the ViewModel-View relationship.
    /// Typically bound to the ViewModel's DataContext or DataContext property.
    /// </summary>
    public static readonly AttachedProperty<IDialogParticipant> RegisterProperty =
        AvaloniaProperty.RegisterAttached<DialogManager, Visual, IDialogParticipant>(
            "Register");

    /// <summary>
    /// Internal weak table that maps ViewModels (IDialogParticipant) to their Visual elements.
    /// Uses ConditionalWeakTable so registrations do not keep ViewModels or Views alive when they are no longer in use.
    /// Key: The ViewModel or any object implementing IDialogParticipant
    /// Value: The Visual control (View, Window, etc.) associated with the ViewModel
    /// </summary>
    private static readonly ConditionalWeakTable<IDialogParticipant, Visual> RegistrationMapper = [];

    /// <summary>
    /// Static constructor that initializes the registration system.
    /// Sets up event listener for the RegisterProperty attached property.
    /// </summary>
    static DialogManager()
    {
        RegisterProperty.Changed.AddClassHandler<Visual>(RegisterChanged);
    }

    /// <summary>
    /// Gets the RegisterProperty attached property from an element.
    /// Used internally to retrieve the associated ViewModel.
    /// </summary>
    /// <param name="element">The Visual control to get the registered ViewModel from</param>
    /// <returns>The registered ViewModel or null if none is registered</returns>
    public static IDialogParticipant GetRegister(AvaloniaObject element)
    {
        return element.GetValue(RegisterProperty);
    }

    /// <summary>
    /// Finds the TopLevel (Window or equivalent) for a dialog participant.
    /// Used to determine the parent window for showing dialogs.
    /// </summary>
    /// <param name="context">The ViewModel or object implementing IDialogParticipant</param>
    /// <returns>The associated TopLevel window, or active/main window as fallback, or null if not found</returns>
    public static TopLevel? GetTopLevelForContext(IDialogParticipant? context)
    {
        if (context is not null)
        {
            var visual = GetVisualForContext(context);
            if (visual is not null)
            {
                var topLevel = TopLevel.GetTopLevel(visual);
                if (topLevel is not null)
                {
                    return topLevel;
                }
            }
        }

        // Fully-qualified: OctaGrid.Application is a sibling namespace under OctaGrid that
        // shadows the unqualified "Application" name Avalonia expects here.
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView);
        }

        return null;
    }

    /// <summary>
    /// Looks up the Visual element associated with a dialog participant.
    /// Used by DialogHelper to find the UI element for a given ViewModel.
    /// </summary>
    /// <param name="context">The ViewModel or object implementing IDialogParticipant</param>
    /// <returns>The associated Visual element, or null if no registration found</returns>
    public static Visual? GetVisualForContext(IDialogParticipant? context)
    {
        if (context is null)
        {
            return null;
        }

        return RegistrationMapper.TryGetValue(context, out var visual) ? visual : null;
    }

    /// <summary>
    /// Sets the RegisterProperty attached property on an element.
    /// Called by XAML binding engine or manually in code.
    /// </summary>
    /// <param name="element">The Visual control to register the ViewModel on</param>
    /// <param name="value">The ViewModel implementing IDialogParticipant</param>
    public static void SetRegister(AvaloniaObject element, IDialogParticipant value)
    {
        element.SetValue(RegisterProperty, value);
    }

    /// <summary>
    /// Handles changes to the RegisterProperty attached property.
    /// Called when a View binds to a ViewModel or when that binding changes.
    /// </summary>
    /// <param name="sender">The Visual control that has the RegisterProperty</param>
    /// <param name="e">Event arguments containing old and new IDialogParticipant values</param>
    /// <exception cref="InvalidOperationException">Thrown if sender is not a Visual</exception>
    private static void RegisterChanged(Visual sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is null)
        {
            throw new InvalidOperationException("The DialogManager can only be registered on a Visual");
        }

        // Unregister any previously registered ViewModel
        if (e.GetOldValue<IDialogParticipant>() is { } oldValue)
        {
            RegistrationMapper.Remove(oldValue);
        }

        // Register the new ViewModel (if any)
        if (e.GetNewValue<IDialogParticipant>() is { } newValue)
        {
            RegistrationMapper.AddOrUpdate(newValue, sender);
            sender.AttachedToVisualTree -= Sender_AttachedToVisualTree;
            sender.AttachedToVisualTree += Sender_AttachedToVisualTree;
        }
        else
        {
            sender.AttachedToVisualTree -= Sender_AttachedToVisualTree;
        }
    }

    private static void Sender_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual visual && GetRegister(visual) is { } participant)
        {
            RegistrationMapper.AddOrUpdate(participant, visual);
        }
    }
}
