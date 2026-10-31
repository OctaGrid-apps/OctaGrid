namespace OctaGrid.Client;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ViewForAttribute : Attribute
{
    public ViewForAttribute(Type viewModelType) => ViewModelType = viewModelType;

    public Type ViewModelType { get; }
}
