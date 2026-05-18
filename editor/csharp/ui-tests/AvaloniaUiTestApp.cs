using Avalonia;
using Avalonia.Headless;
using Soul.Editor;

[assembly: AvaloniaTestApplication(typeof(App))]

namespace Soul.Editor.UiTests;

public static class AvaloniaUiTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
