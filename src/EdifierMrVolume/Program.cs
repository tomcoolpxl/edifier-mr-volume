namespace EdifierMrVolume;

/// <summary>
/// The entry point. Normally it starts WinUI and the app, as the generated Main would. With
/// "--mcp" an MCP app (Claude, VS Code) started it: it only relays that app's messages to the
/// running tray app (McpRelay), without loading WinUI, so each such helper stays small.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--mcp")) return McpRelay.Run();
        XamlGeneratedProgram.XamlGeneratedMain();
        return 0;
    }
}
