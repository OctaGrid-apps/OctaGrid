using Avalonia;
using Avalonia.Headless;

namespace OctaGrid.Client.Tests;

/// <summary>Entry point the headless test session uses to start the real application class without a window system.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>Runs code on the UI thread of a headless Avalonia application that uses the real <see cref="App"/>.</summary>
internal static class HeadlessUi
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder), AvaloniaTestIsolationLevel.PerAssembly);

    public static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);

    public static Task<T> Run<T>(Func<T> func) => Session.Dispatch(func, CancellationToken.None);

    /// <summary>Runs async code on the UI thread, so awaits continue there and bindings update as they do in the app.</summary>
    public static Task RunAsync(Func<Task> action) => Session.Dispatch(action, CancellationToken.None);
}

/// <summary>
/// Tests that touch the static <see cref="App.Services"/> or the headless application must not run in parallel
/// with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UiCollection
{
    public const string Name = "Ui";
}
