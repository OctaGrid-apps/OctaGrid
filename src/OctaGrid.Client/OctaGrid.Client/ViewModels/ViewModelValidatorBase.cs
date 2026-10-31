using CommunityToolkit.Mvvm.ComponentModel;

namespace OctaGrid.Client.ViewModels;

public abstract class ViewModelValidatorBase : ObservableValidator
{
    /// <summary>
    /// Validates all properties of the ViewModel using data annotations.
    /// This method should be called before saving data to ensure validity.
    /// Populates the validation errors that can be accessed through HasErrors property.
    /// </summary>
    public void Validate() => ValidateAllProperties();
}
