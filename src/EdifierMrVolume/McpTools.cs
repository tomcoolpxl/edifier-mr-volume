using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using EdifierMrVolume.Core;
using Microsoft.UI.Dispatching;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EdifierMrVolume;

/// <summary>What the MCP tools ask the app to do. Each runs on the UI thread.</summary>
internal sealed record McpActions(
    Func<int, int?> SetVolume,
    Func<int, int?> ChangeVolume,
    Func<bool, int?> SetMuted,
    Func<string, bool> SelectEqualizer,
    Func<int[], string?> SetCustomEqualizer,
    Func<int?, string?, bool?, string?> SetRoom,
    Func<Task<bool>> PowerOff);

/// <summary>
/// The MCP tools: read the speakers' state and change what the panel and settings can change.
/// Each change goes through the same code as the control it stands for (Volume limit, the
/// equalizer's "Your curve", the room-tuning rules), runs on the UI thread, and the tool then
/// waits briefly for the speaker to report the new value before it answers with the state.
/// Tools are declared by hand, with their JSON schemas, rather than found by reflection, which
/// Native AOT can't do.
/// </summary>
internal sealed class McpTools(SpeakerConnection speaker, AppSettings settings, DispatcherQueue ui, McpActions actions, string version)
{
    private static readonly string[] ModeNames = [.. EdifierProtocol.SoundModeNames];
    private static readonly string[] EqChoices = [.. EqPresets.All.Select(p => p.Name), EqPresets.CustomLabel];
    private static readonly string[] BassChoices = [.. RoomPresets.Bass.Select(b => b.Name)];

    private const string Instructions =
        "Controls Edifier MR studio monitors (MR4.5, MR3, MR5) through the Edifier MR Volume app, " +
        "which holds their Bluetooth control link. Volume goes from 0 to 30; the app's Volume limit " +
        "(set by the user) caps what it sets. Call get_status first to see the current values.";

    public McpServerOptions CreateOptions()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "edifier-mr-volume", Title = "Edifier MR Volume", Version = version },
            ServerInstructions = Instructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
        };
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = Tools });
        options.Handlers.CallToolHandler = (context, token) => new ValueTask<CallToolResult>(CallAsync(context.Params!, token));
        return options;
    }

    private static readonly List<Tool> Tools =
    [
        Define("get_status", "Speaker status",
            "The speakers' connection, volume, mute, sound mode, equalizer, room tuning and power saving.",
            """{"type":"object","properties":{}}""",
            readOnly: true),
        Define("set_volume", "Set volume",
            "Sets the volume (0 to 30). Never above the Volume limit the user set in the app.",
            """{"type":"object","properties":{"level":{"type":"integer","minimum":0,"maximum":30,"description":"The volume level."}},"required":["level"]}""",
            idempotent: true),
        Define("change_volume", "Change volume",
            "Turns the volume up or down by a number of steps, like the speaker knob. Never above the Volume limit.",
            """{"type":"object","properties":{"steps":{"type":"integer","minimum":-30,"maximum":30,"description":"Steps up (positive) or down (negative)."}},"required":["steps"]}"""),
        Define("set_mute", "Mute",
            "Mutes (volume 0) or unmutes (back to the level before muting, or 10).",
            """{"type":"object","properties":{"muted":{"type":"boolean"}},"required":["muted"]}""",
            idempotent: true),
        Define("set_sound_mode", "Set sound mode",
            "Monitor: neutral, as recorded. Music: warmer, with more mid-bass. Custom: the equalizer curve.",
            """{"type":"object","properties":{"mode":{"type":"string","enum":""" + JsonList(ModeNames) + """}},"required":["mode"]}""",
            idempotent: true),
        Define("set_equalizer_preset", "Set equalizer preset",
            "Chooses the equalizer curve and switches to Custom mode, where it is heard. \"Your curve\" puts back the user's own curve; the presets don't change it.",
            """{"type":"object","properties":{"preset":{"type":"string","enum":""" + JsonList(EqChoices) + """}},"required":["preset"]}""",
            idempotent: true),
        Define("set_custom_equalizer", "Set your own equalizer curve",
            "Replaces the user's own curve (\"Your curve\") with these steps from flat, one per band, lowest band first (get_status lists the bands and the range), and switches to Custom mode.",
            """{"type":"object","properties":{"steps":{"type":"array","items":{"type":"integer","minimum":-6,"maximum":6},"description":"Steps from flat per band, lowest frequency first."}},"required":["steps"]}""",
            idempotent: true, destructive: true),
        Define("set_room", "Set room tuning",
            "Bass for where the speakers stand. position: 0 = middle of the room ... 4 = in a corner (less bass near walls). bass: how deep the bass goes. on_desk: compensates for a desk in front of the speakers. Give any of them.",
            """{"type":"object","properties":{"position":{"type":"integer","minimum":0,"maximum":4},"bass":{"type":"string","enum":""" + JsonList(BassChoices) + """},"on_desk":{"type":"boolean"}}}""",
            idempotent: true),
        Define("set_power_saving", "Set power saving",
            "The speakers' own standby after 15 minutes without sound.",
            """{"type":"object","properties":{"on":{"type":"boolean"}},"required":["on"]}""",
            idempotent: true),
        Define("power_off", "Turn off the speakers",
            "Switches the speakers off. They can only be turned on again with their power button.",
            """{"type":"object","properties":{}}""",
            idempotent: true, destructive: true),
    ];

    private static Tool Define(string name, string title, string description, string schema,
        bool readOnly = false, bool idempotent = false, bool destructive = false)
    {
        using var document = JsonDocument.Parse(schema);
        return new Tool
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = document.RootElement.Clone(),
            Annotations = new ToolAnnotations
            {
                Title = title,
                ReadOnlyHint = readOnly,
                DestructiveHint = !readOnly && destructive,
                IdempotentHint = readOnly || idempotent,
                // Only these speakers, nothing outside this PC.
                OpenWorldHint = false,
            },
        };
    }

    /// <summary>A JSON array of strings, with "+" kept readable.</summary>
    private static string JsonList(IEnumerable<string> values)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartArray();
            foreach (var value in values) w.WriteStringValue(value);
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<CallToolResult> CallAsync(CallToolRequestParams call, CancellationToken token)
    {
        var args = call.Arguments ?? new Dictionary<string, JsonElement>();
        Log.Write($"AI assistants (MCP): {call.Name}{(args.Count > 0 ? " " + string.Join(", ", args.Select(a => $"{a.Key}={a.Value.GetRawText()}")) : "")}");
        try
        {
            if (call.Name == "get_status") return await StatusAsync(null);
            if (call.Name == "power_off")
            {
                var off = await OnUi(actions.PowerOff).Unwrap();
                return off ? Text("Turned the speakers off. Turn them on again with their power button.")
                    : Error("Could not turn the speakers off: not connected.");
            }
            if (await OnUi(() => speaker.State == SpeakerState.Connected && speaker.Volume is not null) is false)
                return Error(NotConnected());

            switch (call.Name)
            {
                case "set_volume":
                    {
                        var target = await OnUi(() => actions.SetVolume(Int(args, "level")));
                        return await AfterVolumeAsync(target, token);
                    }
                case "change_volume":
                    {
                        var target = await OnUi(() => actions.ChangeVolume(Int(args, "steps")));
                        return await AfterVolumeAsync(target, token);
                    }
                case "set_mute":
                    {
                        var target = await OnUi(() => actions.SetMuted(Bool(args, "muted")));
                        return await AfterVolumeAsync(target, token);
                    }
                case "set_sound_mode":
                    {
                        var mode = Array.IndexOf(ModeNames, String(args, "mode"));
                        if (mode < 0) return Error($"Unknown mode. Use one of: {string.Join(", ", ModeNames)}.");
                        if (!await OnUi(() => speaker.RequestSoundMode(mode))) return Error("The speaker hasn't reported its sound mode yet.");
                        Log.Write($"Sound mode -> {ModeNames[mode]} (MCP)");
                        return await StatusAsync(() => speaker.SoundMode?.Mode == mode, token);
                    }
                case "set_equalizer_preset":
                    {
                        var label = String(args, "preset");
                        if (!EqChoices.Contains(label)) return Error($"Unknown preset. Use one of: {string.Join(", ", EqChoices)}.");
                        if (!await OnUi(() => actions.SelectEqualizer(EqPresets.ChoiceFor(label))))
                            return Error("The speaker hasn't reported its equalizer yet.");
                        return await StatusAsync(() => speaker.SoundMode?.Mode == 2, token);
                    }
                case "set_custom_equalizer":
                    {
                        if (!args.TryGetValue("steps", out var array) || array.ValueKind != JsonValueKind.Array)
                            throw new ArgumentException("steps must be an array of integers.");
                        var steps = array.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                        var problem = await OnUi(() => actions.SetCustomEqualizer(steps));
                        if (problem is not null) return Error(problem);
                        return await StatusAsync(() => speaker.Equalizer?.Steps.SequenceEqual(steps) == true && speaker.SoundMode?.Mode == 2, token);
                    }
                case "set_room":
                    {
                        int? position = args.ContainsKey("position") ? Int(args, "position") : null;
                        var bass = args.ContainsKey("bass") ? String(args, "bass") : null;
                        bool? desk = args.ContainsKey("on_desk") ? Bool(args, "on_desk") : null;
                        if (position is null && bass is null && desk is null) return Error("Give position, bass or on_desk.");
                        var problem = await OnUi(() => actions.SetRoom(position, bass, desk));
                        if (problem is not null) return Error(problem);
                        return await StatusAsync(() => RoomIs(position, bass, desk), token);
                    }
                case "set_power_saving":
                    {
                        var on = Bool(args, "on");
                        if (!await OnUi(() => speaker.RequestPowerSaving(on))) return Error("The speaker hasn't reported power saving yet.");
                        Log.Write($"Power saving -> {(on ? "on" : "off")} (MCP)");
                        return await StatusAsync(() => speaker.PowerSaving == on, token);
                    }
                default:
                    return Error($"Unknown tool {call.Name}.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Error(ex.Message);
        }
    }

    private Task<CallToolResult> AfterVolumeAsync(int? target, CancellationToken token) =>
        target is { } level
            ? StatusAsync(() => speaker.Volume?.Current == level, token)
            : Task.FromResult(Error(NotConnected()));

    private bool RoomIs(int? position, string? bass, bool? desk)
    {
        if (speaker.SoundMode is not { } sound || !EdifierProtocol.TryDecodeRoomTuning(sound, out var room)) return false;
        return (position is null || room.Space == position)
            && (bass is null || RoomPresets.MatchBass(room)?.Name == bass)
            && (desk is null || room.Desktop == desk);
    }

    /// <summary>Waits up to 3 s for the speaker to report the change, then answers with the state.</summary>
    private async Task<CallToolResult> StatusAsync(Func<bool>? done, CancellationToken token = default)
    {
        var confirmed = true;
        if (done is not null)
        {
            var until = Environment.TickCount64 + 3000;
            while (!done() && Environment.TickCount64 < until) await Task.Delay(100, token);
            confirmed = done();
        }
        var json = await OnUi(StatusJson);
        var text = confirmed ? json : "The speaker hasn't confirmed the change yet. Current state:\n" + json;
        using var document = JsonDocument.Parse(json);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = document.RootElement.Clone(),
        };
    }

    /// <summary>The state as JSON: what get_status returns and every change answers with.</summary>
    private string StatusJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("connection", speaker.State switch
            {
                SpeakerState.Connected => "connected",
                SpeakerState.Connecting => "connecting",
                SpeakerState.Searching when speaker.SwitchedOff => "turned off",
                SpeakerState.Searching => "looking for the speakers",
                _ => "released (Connect to speaker is off)",
            });
            if (speaker.ModelName is { } model) w.WriteString("model", model);
            if (speaker.Volume is { } volume)
            {
                w.WriteNumber("volume", volume.Current);
                w.WriteNumber("max_volume", volume.Max);
                w.WriteBoolean("muted", volume.Current == 0);
            }
            if (settings.EffectiveVolumeLimit is { } limit) w.WriteNumber("volume_limit", limit);
            else w.WriteNull("volume_limit");
            if (speaker.SoundMode is { } sound)
            {
                w.WriteString("sound_mode", ModeNames[Math.Clamp(sound.Mode, 0, ModeNames.Length - 1)]);
                if (EdifierProtocol.TryDecodeRoomTuning(sound, out var room))
                {
                    w.WriteStartObject("room");
                    w.WriteNumber("position", room.Space);
                    w.WriteString("position_scale", "0 = middle of the room, 4 = in a corner");
                    if (RoomPresets.MatchBass(room) is { } bass) w.WriteString("bass", bass.Name);
                    w.WriteString("bass_numbers", RoomPresets.BassNumbers(room));
                    w.WriteBoolean("on_desk", room.Desktop);
                    w.WriteEndObject();
                }
            }
            if (speaker.Equalizer is { } eq)
            {
                w.WriteStartObject("equalizer");
                w.WriteString("curve", EqPresets.Label(EqPresets.Resolve(settings.EqChoice, settings.CustomEq, eq).Choice));
                w.WriteString("heard_in", "Custom mode");
                w.WriteNumber("steps_range", eq.Range);
                w.WriteStartArray("bands");
                var steps = eq.Steps;
                for (var i = 0; i < eq.Bands.Count; i++)
                {
                    w.WriteStartObject();
                    w.WriteNumber("hz", eq.Bands[i].Frequency);
                    w.WriteNumber("steps", steps[i]);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            if (speaker.PowerSaving is { } saving) w.WriteBoolean("power_saving", saving);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string NotConnected() => speaker.State switch
    {
        SpeakerState.Searching when speaker.SwitchedOff => "The speakers are turned off. Turn them on with their power button.",
        SpeakerState.Stopped => "The app has released the speakers (Connect to speaker is off).",
        _ => "The app isn't connected to the speakers right now.",
    };

    /// <summary>Runs on the UI thread, where the app's state lives, and returns the result.</summary>
    private Task<T> OnUi<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ui.TryEnqueue(() =>
            {
                try { result.SetResult(action()); }
                catch (Exception ex) { result.SetException(ex); }
            }))
            result.SetException(new InvalidOperationException("The app is closing."));
        return result.Task;
    }

    private static int Int(IDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i : throw new ArgumentException($"{name} must be a whole number.");

    private static bool Bool(IDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : throw new ArgumentException($"{name} must be true or false.");

    private static string String(IDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : throw new ArgumentException($"{name} must be text.");

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) => new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
