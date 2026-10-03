using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

// Runs a UCI engine as a child process. Output lines are read on a background thread
// and queued; the main thread consumes them, so no Unity API is touched off the main thread.
public class UciEngine : IDisposable
{
    public string Name { get; }
    public string LastError { get; private set; }
    public bool IsRunning => process != null && !process.HasExited;

    Process process;
    Thread reader;
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();

    public UciEngine(string name)
    {
        Name = name;
    }

    public bool Start(string path, IEnumerable<KeyValuePair<string, string>> options)
    {
        try
        {
            if (!File.Exists(path))
            {
                LastError = "실행파일 없음: " + path;
                return false;
            }
            EnsureExecutable(path);
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path),
            };
            process = Process.Start(psi);
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "uci-" + Name };
            reader.Start();

            Send("uci");
            if (options != null)
                foreach (var kv in options)
                    Send($"setoption name {kv.Key} value {kv.Value}");
            Send("isready");
            return true;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return false;
        }
    }

    // Files copied into StreamingAssets or a build can lose the executable bit.
    static void EnsureExecutable(string path)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("/bin/chmod", $"+x \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(1000);
        }
        catch
        {
            // Not fatal: the file may already be executable.
        }
    }

    void ReadLoop()
    {
        try
        {
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
                lines.Enqueue(line);
        }
        catch
        {
            // Process closed.
        }
    }

    public void Send(string cmd)
    {
        try
        {
            if (!IsRunning) return;
            process.StandardInput.WriteLine(cmd);
            process.StandardInput.Flush();
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
    }

    public bool TryRead(out string line) => lines.TryDequeue(out line);

    public void Drain()
    {
        while (lines.TryDequeue(out _)) { }
    }

    // Sends commands and blocks until a line matching `isLast` arrives. Only for instant
    // queries (legalmoves/status/fen), never for searches.
    public List<string> Query(IEnumerable<string> cmds, Func<string, bool> isLast, int timeoutMs = 2000)
    {
        var result = new List<string>();
        foreach (var c in cmds) Send(c);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (lines.TryDequeue(out var line))
            {
                result.Add(line);
                if (isLast(line)) break;
            }
            else
            {
                Thread.Sleep(1);
            }
        }
        return result;
    }

    public void Dispose()
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                Send("quit");
                if (!process.WaitForExit(500)) process.Kill();
            }
        }
        catch
        {
            // Already gone.
        }
        process.Dispose();
        process = null;
    }
}
