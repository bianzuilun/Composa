using System.IO.Pipes;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Composa.App.Mcp;

/// <summary>
/// Hosts the MCP server inside the running application. Each client that connects to the pipe gets its own server
/// over that connection; the tools themselves (<see cref="ComposaTools"/>) run every edit on the UI thread through the
/// window's sessions, so an agent's change is undoable and appears as it happens.
/// </summary>
public sealed class McpHost : IDisposable
{
    private readonly MainWindow window;
    private readonly CancellationTokenSource stop = new();
    private int connections;

    public McpHost(MainWindow window, string? pipeName = null)
    {
        this.window = window;
        PipeName = pipeName ?? McpPipe.Name;
    }

    public string PipeName { get; }

    /// <summary>How many clients are connected right now. Raised on the UI thread.</summary>
    public int Connections => connections;
    public event Action? ConnectionsChanged;

    /// <summary>
    /// Starts listening. False when another Composa already answers on the pipe: only one window can own it, and the
    /// one that was there first keeps it.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (await SomeoneListens()) return false;
        if (!OperatingSystem.IsWindows())
        {
            // The socket is a file, which a run that crashed leaves behind, and a new server cannot bind over it.
            // Nobody answered on it, so it is safe to clear.
            Directory.CreateDirectory(Path.GetDirectoryName(PipeName)!);
            File.Delete(PipeName);
        }
        _ = AcceptAsync();
        return true;
    }

    private async Task<bool> SomeoneListens()
    {
        if (!OperatingSystem.IsWindows() && !File.Exists(PipeName)) return false;
        using var probe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await probe.ConnectAsync(300); return true; }
        catch (Exception) { return false; }
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                                 PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(stop.Token); }
            catch (Exception)
            {
                pipe.Dispose();
                if (stop.IsCancellationRequested) return;
                await Task.Delay(500);
                continue;
            }
            _ = ServeAsync(pipe);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        Count(+1);
        try
        {
            await using var transport = new StreamServerTransport(pipe, pipe, "composa");
            await using var server = McpServer.Create(transport, Options());
            await server.RunAsync(stop.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally
        {
            pipe.Dispose();
            Count(-1);
        }
    }

    private void Count(int delta)
    {
        Interlocked.Add(ref connections, delta);
        Dispatcher.UIThread.Post(() => ConnectionsChanged?.Invoke());
    }

    private McpServerOptions Options()
    {
        var tools = new ComposaTools(window);
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "composa", Title = "Composa", Version = AppInfo.Version },
            ServerInstructions = "Composa is a layer-based image editor. Call get_capabilities for editing routes and tools/list for exact schemas. " +
                "Liquify, healing, Camera Raw, RAW/PSD imports, native precision exports, CMYK proof, masks and workspace controls are available. " +
                "The tools act on the documents open in its window; " +
                                 "every change is an undoable step the person can see and undo. Coordinates are canvas pixels " +
                                 "with the origin at the top left. Call render to see the result of your changes.",
            ToolCollection = tools.Collection(),
            ResourceCollection = tools.Resources()
        };
    }

    public void Dispose() => stop.Cancel();
}
