using libdebug;
using System.Net.Sockets;

namespace PS5MemoryPeeker;

public interface IConsoleDebugClient : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(string host, CancellationToken cancellationToken);
    Task DisconnectAsync();
    Task<IReadOnlyList<ProcessItem>> GetProcessesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MemorySection>> GetMemoryMapAsync(int pid, CancellationToken cancellationToken);
    Task<byte[]> ReadMemoryAsync(int pid, ulong address, int length, CancellationToken cancellationToken);
    Task WriteMemoryAsync(int pid, ulong address, byte[] bytes, CancellationToken cancellationToken);
    Task PauseProcessAsync(int pid, CancellationToken cancellationToken);
    Task ResumeProcessAsync(CancellationToken cancellationToken);
    Task KillProcessAsync(CancellationToken cancellationToken);
    void AbortActiveConnection();
}

public sealed class LibdebugConsoleClient : IConsoleDebugClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private PS4DBG? _debugger;
    private int? _attachedPid;
    private string _host = "";
    private readonly object _syncRoot = new();

    public bool IsConnected => _debugger?.IsConnected ?? false;

    public async Task ConnectAsync(string host, CancellationToken cancellationToken)
    {
        string trimmedHost = host.Trim();
        lock (_syncRoot)
        {
            _attachedPid = null;
            _host = trimmedHost;
            _debugger?.Disconnect();
            _debugger = null;
        }

        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            PS4DBG candidate = new(trimmedHost);
            Task connectTask = Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidate.Connect();
            }, CancellationToken.None);

            Task completed = await Task.WhenAny(connectTask, Task.Delay(ConnectTimeout, cancellationToken));
            if (completed != connectTask)
            {
                _ = connectTask.ContinueWith(task =>
                {
                    _ = task.Exception;
                }, TaskContinuationOptions.OnlyOnFaulted);

                TryDisconnect(candidate);
                lastError = new TimeoutException($"PS5Debug did not answer within {ConnectTimeout.TotalSeconds:0}s.");
            }
            else
            {
                try
                {
                    await connectTask;
                    lock (_syncRoot)
                    {
                        _debugger?.Disconnect();
                        _debugger = candidate;
                        _host = trimmedHost;
                    }

                    return;
                }
                catch (Exception ex)
                {
                    TryDisconnect(candidate);
                    lastError = ex;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException("PS5Debug did not answer after payload/connect retries. Check payload port, PS5Debug compatibility, and that the exploit host accepted the payload.", lastError);
    }

    private static void TryDisconnect(PS4DBG debugger)
    {
        try
        {
            debugger.Disconnect();
        }
        catch
        {
        }
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            lock (_syncRoot)
            {
                _attachedPid = null;
                _debugger?.Disconnect();
                _debugger = null;
            }
        });
    }

    public Task<IReadOnlyList<ProcessItem>> GetProcessesAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ProcessItem>>(() =>
        {
            PS4DBG dbg = RequireClient();
            List<ProcessItem> processes = [];
            libdebug.Process[] list = RunWithReconnect(dbg => dbg.GetProcessList().processes);
            foreach (libdebug.Process process in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = process.name ?? "";
                if (!name.Equals("eboot.bin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ProcessInfo? info = TryGetProcessInfo(process.pid);
                string path = info?.path ?? "";
                string titleId = info?.titleid ?? "";
                string contentId = info?.contentid ?? "";
                int rank = GetGameProcessRank(name, path, titleId, contentId);

                processes.Add(new ProcessItem
                {
                    Pid = process.pid,
                    Name = "eboot.bin",
                    Path = path,
                    TitleId = titleId,
                    ContentId = contentId,
                    GameTitle = "",
                    IsGameProcess = rank >= 50,
                    Rank = rank
                });
            }

            return processes
                .OrderByDescending(p => p.Rank)
                .ThenBy(p => p.GameTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<MemorySection>> GetMemoryMapAsync(int pid, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<MemorySection>>(() =>
        {
            ProcessMap map = RunWithReconnect(dbg => dbg.GetProcessMaps(pid));
            List<MemorySection> sections = BuildSectionsLikePs4Cheater(map);

            SectionSelector.ScoreSections(sections);
            return sections;
        }, cancellationToken);
    }

    public Task<byte[]> ReadMemoryAsync(int pid, ulong address, int length, CancellationToken cancellationToken)
    {
        return Task.Run(() => RunWithReconnect(dbg => dbg.ReadMemory(pid, address, length)), cancellationToken);
    }

    public Task WriteMemoryAsync(int pid, ulong address, byte[] bytes, CancellationToken cancellationToken)
    {
        return Task.Run(() => RunWithReconnect(dbg =>
        {
            dbg.WriteMemory(pid, address, bytes);
            return true;
        }), cancellationToken);
    }

    public Task PauseProcessAsync(int pid, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            RunWithReconnect(dbg =>
            {
                if (_attachedPid != pid)
                {
                    dbg.AttachDebugger(pid, null);
                    _attachedPid = pid;
                }

                dbg.ProcessStop();
                return true;
            });
        }, cancellationToken);
    }

    public Task ResumeProcessAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => RunWithReconnect(dbg =>
        {
            dbg.ProcessResume();
            return true;
        }), cancellationToken);
    }

    public Task KillProcessAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => RunWithReconnect(dbg =>
        {
            dbg.ProcessKill();
            return true;
        }), cancellationToken);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _debugger?.Disconnect();
        }
    }

    private PS4DBG RequireClient()
    {
        return _debugger ?? throw new InvalidOperationException("Not connected to PS5Debug.");
    }

    private T RunWithReconnect<T>(Func<PS4DBG, T> action)
    {
        lock (_syncRoot)
        {
            PS4DBG debugger = RequireClient();
            try
            {
                return action(debugger);
            }
            catch (Exception ex) when (!string.IsNullOrWhiteSpace(_host) && IsRecoverableSocketFailure(ex))
            {
                _attachedPid = null;
                _debugger?.Disconnect();
                _debugger = new PS4DBG(_host);
                _debugger.Connect();
                debugger = _debugger;

                return action(debugger);
            }
        }
    }

    private static bool IsRecoverableSocketFailure(Exception ex)
    {
        return ex is ObjectDisposedException ||
               ex is SocketException ||
               ex.InnerException is ObjectDisposedException ||
               ex.InnerException is SocketException ||
               ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Socket", StringComparison.OrdinalIgnoreCase);
    }

    public void AbortActiveConnection()
    {
        lock (_syncRoot)
        {
            try
            {
                _attachedPid = null;
                _debugger?.Disconnect();
                _debugger = null;
            }
            catch
            {
                _debugger = null;
            }
        }
    }

    private ProcessInfo? TryGetProcessInfo(int pid)
    {
        try
        {
            return RunWithReconnect(dbg => dbg.GetProcessInfo(pid));
        }
        catch
        {
            return null;
        }
    }

    private static List<MemorySection> BuildSectionsLikePs4Cheater(ProcessMap map)
    {
        List<MemorySection> sections = [];
        foreach (MemoryEntry entry in map.entries)
        {
            if ((entry.prot & 0x1) != 0x1 || entry.end <= entry.start)
            {
                continue;
            }

            ulong length = entry.end - entry.start;
            ulong start = entry.start;
            ulong chunkLength = 128UL * 1024UL * 1024UL;
            int index = 0;

            if ((entry.prot & 0x5) == 0x5)
            {
                chunkLength = length;
            }

            while (length != 0)
            {
                ulong currentLength = Math.Min(chunkLength, length);
                sections.Add(new MemorySection
                {
                    Name = string.IsNullOrWhiteSpace(entry.name) ? "unnamed" : entry.name,
                    Index = index,
                    Start = start,
                    End = start + currentLength,
                    Protection = entry.prot
                });

                start += currentLength;
                length -= currentLength;
                index++;
            }
        }

        return sections.OrderBy(s => s.Start).ToList();
    }

    private static int GetGameProcessRank(string name, string path, string titleId, string contentId)
    {
        string haystack = $"{name} {path} {titleId} {contentId}".ToLowerInvariant();
        int rank = 0;

        if (haystack.Contains("eboot.bin"))
        {
            rank += 100;
        }

        if (!string.IsNullOrWhiteSpace(titleId) || !string.IsNullOrWhiteSpace(contentId))
        {
            rank += 70;
        }

        if (haystack.Contains("/app0/") || haystack.Contains("\\app0\\") || haystack.Contains("/mnt/sandbox/") || haystack.Contains("/user/app/"))
        {
            rank += 45;
        }

        if (haystack.Contains("sce_sys") || haystack.Contains(".sprx") || haystack.Contains(".prx") || haystack.Contains(".so") || haystack.Contains(".elf") || haystack.Contains("lib"))
        {
            rank -= 80;
        }

        return rank;
    }

}
