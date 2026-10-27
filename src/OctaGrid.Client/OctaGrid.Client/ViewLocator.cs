using System.Linq.Expressions;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ViewForAttribute : Attribute
{
    public ViewForAttribute(Type viewModelType) => ViewModelType = viewModelType;

    public Type ViewModelType { get; }
}

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _registrations = [];

    public ViewLocator(params Assembly[] assemblies)
    {
        var scanTargets = assemblies.Length > 0 ? assemblies : [Assembly.GetExecutingAssembly()];

        foreach (var assembly in scanTargets)
        {
            RegisterFromAssembly(assembly);
        }
    }

    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "No ViewModel supplied" };
        }

        var vmType = param.GetType();

        return _registrations.TryGetValue(vmType, out var factory)
            ? factory()
            : new TextBlock { Text = $"No view registered for {vmType.FullName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;

    /// <summary>Escape hatch for views that need constructor injection.</summary>
    public ViewLocator Register<TViewModel>(Func<Control> factory)
    {
        _registrations[typeof(TViewModel)] = factory;
        return this;
    }

    private void RegisterFromAssembly(Assembly assembly)
    {
        var candidates = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<ViewForAttribute>()))
            .Where(x => x.Attr is not null);

        foreach (var (viewType, attr) in candidates)
        {
            if (!typeof(Control).IsAssignableFrom(viewType))
            {
                throw new InvalidOperationException(
                    $"{viewType.FullName} has [ViewFor] but does not derive from Control.");
            }

            var vmType = attr!.ViewModelType;

            if (_registrations.ContainsKey(vmType))
            {
                throw new InvalidOperationException(
                    $"Duplicate view registration for {vmType.FullName}: " +
                    $"{viewType.FullName} conflicts with an existing mapping.");
            }

            // Compile a parameterless constructor call instead of Activator.CreateInstance
            // for every resolve — cheap up front, faster on every subsequent Build().
            var ctor = viewType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    $"{viewType.FullName} needs a public parameterless constructor, " +
                    $"or register it manually with a factory.");

            var newExpr = Expression.New(ctor);
            var lambda = Expression.Lambda<Func<Control>>(newExpr).Compile();

            _registrations[vmType] = lambda;
        }
    }
}
