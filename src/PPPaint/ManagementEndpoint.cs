using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PPPaint.Core;

namespace PPPaint;

internal sealed class ManagementEndpoint : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MainWindow _window;
    private readonly LaunchOptions _options;
    internal static string PipeName(LaunchOptions options) => "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + options.ToolId;
    internal static string MutexName(LaunchOptions options) => "Local\\" + PipeName(options);

    internal ManagementEndpoint(MainWindow window, LaunchOptions options)
    {
        _window = window;
        _options = options;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName(_options), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_lifetime.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await ReadLineAsync(reader, deadline.Token);
                string command = line ?? "", requestId = "", path = "";
                int? expectedPid = null, expectedSession = null;
                DateTimeOffset? requestedDeadline = null;
                if (command.StartsWith('{'))
                {
                    using var document = JsonDocument.Parse(command);
                    var request = document.RootElement;
                    command = request.GetProperty("command").GetString() ?? "";
                    if (request.TryGetProperty("requestId", out var value)) requestId = value.GetString() ?? "";
                    if (request.TryGetProperty("expectedPid", out value) && value.ValueKind != JsonValueKind.Null) expectedPid = value.GetInt32();
                    if (request.TryGetProperty("expectedSessionId", out value) && value.ValueKind != JsonValueKind.Null) expectedSession = value.GetInt32();
                    if (request.TryGetProperty("deadlineUtc", out value) && value.ValueKind != JsonValueKind.Null) requestedDeadline = value.GetDateTimeOffset();
                    if (request.TryGetProperty("path", out value)) path = value.GetString() ?? "";
                }
                var clientSessionValid = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) && SameSession((int)clientPid);
                var result = "completed";
                var detail = "";
                var ready = false;
                var stopAccepted = false;
                var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (deadline.IsCancellationRequested || _lifetime.IsCancellationRequested) { applied.TrySetCanceled(); return; }
                    try
                    {
                        ready = _window.ManagementReady;
                        if (!clientSessionValid || expectedSession is { } session && session != Process.GetCurrentProcess().SessionId)
                        { result = "refused"; detail = "別セッションからの要求です。"; }
                        else if (requestedDeadline is { } end && end <= DateTimeOffset.UtcNow)
                        { result = "refused"; detail = "要求の有効期限が過ぎています。"; }
                        else if ((expectedPid.HasValue && expectedPid != Environment.ProcessId) || (command != "status" && expectedPid is null))
                        { result = "refused"; detail = "対象PIDを確認できません。"; }
                        else if (command == "show") _window.ManagementShow();
                        else if (command == "stop")
                        {
                            detail = _window.ManagementBlockReason() ?? "";
                            if (!ready && detail.Length == 0) detail = "画面を準備中です。";
                            stopAccepted = detail.Length == 0;
                            result = stopAccepted ? "accepted" : "refused";
                        }
                        else if (command == "open")
                        {
                            if (!ready || _window.ManagementBlockReason(checkUnsaved: false) is not null) { result = "refused"; detail = "別の編集操作が進行中です。"; }
                            else if (StartupFileRequest.FindSupportedPath(["PPPaint.exe", path]) is not { } fullPath || !Path.IsPathFullyQualified(path))
                            { result = "refused"; detail = "対応するファイルの絶対パスが必要です。"; }
                            else { result = "accepted"; _ = _window.ManagementOpenAsync(fullPath); }
                        }
                        else if (command != "status") { result = "unsupported"; detail = "未対応の操作です。"; }
                        applied.TrySetResult();
                    }
                    catch (Exception) { result = "failed"; detail = "UI操作を完了できませんでした。"; applied.TrySetResult(); }
                })) continue;
                await applied.Task.WaitAsync(deadline.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    protocolVersion = 2, requestId, pid = Environment.ProcessId, ready,
                    version = WindowTitle.Version, launchOrigin = _options.LaunchOrigin,
                    capabilities = new[] { "status", "show", "stop" }, result, detail
                }).AsMemory(), deadline.Token);
                if (stopAccepted) _window.DispatcherQueue.TryEnqueue(_window.ManagementStop);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or ArgumentException) { }
        }
    }

    private static bool SameSession(int pid)
    {
        try { using var client = Process.GetProcessById(pid); return client.SessionId == Process.GetCurrentProcess().SessionId; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellation) != 0)
        {
            if (buffer[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= 4096) throw new InvalidDataException("管理要求が長すぎます。");
            text.Append(buffer[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }

    internal static async Task<bool> ForwardAsync(LaunchOptions options)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    var status = await SendAsync(options, "status", timeout.Token);
                    var pid = status.GetProperty("pid").GetInt32();
                    using var process = Process.GetProcessById(pid);
                    if (!SameSession(pid) || !string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
                    if (!status.GetProperty("ready").GetBoolean()) { await Task.Delay(100, timeout.Token); continue; }
                    AllowSetForegroundWindow(pid);
                    var result = await SendAsync(options, JsonSerializer.Serialize(new
                    {
                        command = options.FilePath is null ? "show" : "open", path = options.FilePath, expectedPid = pid,
                        expectedSessionId = Process.GetCurrentProcess().SessionId, deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(2)
                    }), timeout.Token);
                    return result.GetProperty("result").GetString() is "completed" or "accepted";
                }
                catch (IOException) { await Task.Delay(100, timeout.Token); }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return false;
    }

    private static async Task<JsonElement> SendAsync(LaunchOptions options, string request, CancellationToken cancellation)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName(options), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(cancellation);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || !SameSession((int)serverPid)) throw new IOException("管理窓口のプロセスを確認できません。");
        using var server = Process.GetProcessById((int)serverPid);
        if (!string.Equals(server.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("別の実行ファイルです。");
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(request.AsMemory(), cancellation);
        using var response = JsonDocument.Parse(await ReadLineAsync(reader, cancellation) ?? "");
        if (response.RootElement.GetProperty("pid").GetInt32() != serverPid) throw new IOException("管理窓口と応答のプロセスが一致しません。");
        return response.RootElement.Clone();
    }

    public void Dispose() => _lifetime.Cancel();
}
