using Avalonia;

namespace Composa.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The stdio bridge an MCP client launches: no window, just bytes carried to the running application.
        // With --launch it also starts the application when nothing answers.
        if (args is ["--mcp", ..]) return Mcp.McpBridge.RunAsync(Mcp.McpPipe.Name, launch: args.Contains("--launch")).GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
