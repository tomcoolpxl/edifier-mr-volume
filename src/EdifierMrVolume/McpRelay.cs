using System.IO.Pipes;
using System.Security.Principal;

namespace EdifierMrVolume;

/// <summary>
/// "EdifierMrVolume.exe --mcp": what an MCP app starts. It passes the app's messages (stdin,
/// stdout) to the running tray app over a named pipe and back, unchanged; the tray app runs the
/// MCP server (McpBridge), because only it holds the speaker's one control link. The MCP spec
/// asks local servers for stdio, and for any other channel to be IPC with restricted access:
/// the pipe accepts only this Windows user, and the relay connects only to a pipe this user
/// owns, so another account can't pose as the app.
/// </summary>
internal static class McpRelay
{
    /// <summary>One pipe per Windows user.</summary>
    public static string PipeName { get; } = $"EdifierMrVolume.Mcp.{WindowsIdentity.GetCurrent().User?.Value}";

    public static int Run()
    {
        // CurrentUserOnly: .NET refuses a pipe whose owner isn't this user.
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(3000);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex is UnauthorizedAccessException
                ? "Edifier MR Volume: the pipe belongs to another Windows user; not connecting."
                : "Edifier MR Volume isn't running, or AI assistants (MCP) is off in its settings (Connection).");
            return 1;
        }

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        // Either side closing ends the relay: the MCP app quit, or the tray app exited.
        var toApp = input.CopyToAsync(pipe);
        var toClient = pipe.CopyToAsync(output);
        Task.WaitAny(toApp, toClient);
        return 0;
    }
}
