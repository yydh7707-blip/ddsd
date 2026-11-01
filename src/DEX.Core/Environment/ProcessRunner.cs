using System.Diagnostics;
using System.Text;
using DEX.Core.Security;

namespace DEX.Core.Probes;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool Killed);

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        int maxOutputBytes = SecurityManager.MaxProbeOutputBytes,
        Action<string>? onOutput = null,
        bool allowBatch = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fileName))
            throw new FileNotFoundException("Executable was not found.", fileName);

        var name = Path.GetFileName(fileName);
        var underJob = fileName.Contains(Path.Combine(Path.GetTempPath(), "DEX"), StringComparison.OrdinalIgnoreCase);
        if (!SecurityManager.IsAllowedExecutable(fileName) && !underJob && !allowBatch)
            throw new InvalidOperationException($"Executable is not registered: {name}");

        SecurityManager.EnsureSafePath(fileName);
        foreach (var arg in arguments)
        {
            if (arg.IndexOfAny(['\n', '\r', '\0']) >= 0)
                throw new InvalidOperationException("Argument contains a forbidden character.");
        }

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        if (environment is not null)
        {
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(stdout, e.Data, maxOutputBytes, onOutput);
        process.ErrorDataReceived += (_, e) => Append(stderr, e.Data, maxOutputBytes, onOutput);

        if (!process.Start())
            throw new InvalidOperationException("Process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var limit = timeout ?? SecurityManager.ProbeTimeout;
        using var timeoutCts = new CancellationTokenSource(limit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            TryKill(process);
            var timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), timedOut, true);
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false, false);
    }

    public async Task<ProcessResult> RunBatchAsync(
        string batchPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan? timeout,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        SecurityManager.EnsureSafePath(batchPath);
        if (!File.Exists(batchPath))
            throw new FileNotFoundException("Batch file was not found.", batchPath);
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var args = new List<string> { "/d", "/c", "call", batchPath };
        args.AddRange(arguments);
        return await RunAsync(cmd, args, workingDirectory, null, timeout, 2_000_000, onOutput, allowBatch: true, cancellationToken);
    }

    private static void Append(StringBuilder builder, string? line, int max, Action<string>? onOutput)
    {
        if (line is null || builder.Length >= max)
            return;
        onOutput?.Invoke(line);
        builder.AppendLine(line);
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }
}
