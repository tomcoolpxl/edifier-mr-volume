using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ModelContextProtocol.Server;

namespace EdifierMrVolume;

/// <summary>
/// The MCP server, inside the tray app, while "AI assistants (MCP)" is on: it waits on a named
/// pipe for "EdifierMrVolume.exe --mcp" helpers (McpRelay), one MCP session per helper, with the
/// tools from McpTools. The pipe allows only this Windows user and refuses network logons. While
/// nobody connects, it waits on the pipe without using the CPU.
/// </summary>
internal sealed class McpBridge(McpTools tools) : IDisposable
{
    private const int MaxSessions = 4;
    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptAsync(token));
    }

    /// <summary>Stops listening and ends the open sessions.</summary>
    public void Stop()
    {
        if (_cts is not { } cts) return;
        _cts = null;
        // Not disposed: sessions still winding down use its token.
        cts.Cancel();
    }

    public void Dispose() => Stop();

    private async Task AcceptAsync(CancellationToken token)
    {
        Log.Write("AI assistants (MCP): waiting for MCP apps.");
        var failing = false;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
                failing = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // All instances busy, or the name is taken: try again shortly (logged once).
                if (!failing) Log.Write($"AI assistants (MCP): pipe not available ({ex.Message}).");
                failing = true;
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                await pipe.WaitForConnectionAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await pipe.DisposeAsync();
                if (token.IsCancellationRequested) return;
                continue;
            }
            _ = ServeAsync(pipe, token);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        Log.Write("AI assistants (MCP): an MCP app connected.");
        try
        {
            await using var transport = new StreamServerTransport(pipe, pipe, "edifier-mr-volume");
            await using var server = McpServer.Create(transport, tools.CreateOptions());
            await server.RunAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Turned off, or the app is exiting.
        }
        catch (Exception ex)
        {
            Log.Write($"AI assistants (MCP): session ended: {ex.Message}");
        }
        finally
        {
            await pipe.DisposeAsync();
            Log.Write("AI assistants (MCP): an MCP app disconnected.");
        }
    }

    /// <summary>A pipe instance only this user can open, and not over the network.</summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        // A network logon carries the NETWORK group; denying it keeps remote clients out.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(McpRelay.PipeName, PipeDirection.InOut, MaxSessions,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}
