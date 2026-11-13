using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MMAway;

internal sealed class SingleInstance : IDisposable
{
    private readonly string pipeName;
    private readonly Mutex mutex;
    private CancellationTokenSource? cancellation;
    private Task? listener;

    public bool IsPrimary { get; }

    public SingleInstance(string? instanceName = null)
    {
        var identity = $"{Environment.UserName}\n{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}";
        var name = instanceName ?? $"MMAway-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]}";
        pipeName = OperatingSystem.IsLinux() ? $"/tmp/{name}" : name;
        mutex = new Mutex(false, $"Global\\{name}", out var createdNew);
        IsPrimary = createdNew;
    }

    public void StartListening(Action activate, Action<Exception> onError)
    {
        if (!IsPrimary || listener is not null)
            throw new InvalidOperationException("The activation listener can only be started once by the primary instance.");

        cancellation = new CancellationTokenSource();
        listener = ListenAsync(activate, onError, cancellation.Token);
    }

    public async Task ActivatePrimaryAsync()
    {
        if (IsPrimary)
            throw new InvalidOperationException("The primary instance cannot activate itself.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token);
        var data = new byte[sizeof(int)];
        await client.ReadExactlyAsync(data, timeout.Token);
        if (OperatingSystem.IsWindows())
            _ = AllowSetForegroundWindow(BitConverter.ToInt32(data));
        await client.WriteAsync(new byte[] { 1 }, timeout.Token);
        await client.ReadExactlyAsync(data.AsMemory(0, 1), timeout.Token);
        if (data[0] != 1)
            throw new IOException("The running MMAway instance did not acknowledge the activation request.");
    }

    private async Task ListenAsync(Action activate, Action<Exception> onError, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), requestTimeout.Token).ConfigureAwait(false);
                    var request = new byte[1];
                    await server.ReadExactlyAsync(request, requestTimeout.Token).ConfigureAwait(false);
                    if (request[0] != 1)
                        throw new IOException("Invalid activation request.");
                    activate();
                    await server.WriteAsync(request, requestTimeout.Token).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    Trace.TraceWarning($"MMAway activation request failed: {ex.Message}");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    Trace.TraceWarning("MMAway activation request timed out.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onError(ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        cancellation?.Cancel();
        listener?.GetAwaiter().GetResult();
        cancellation?.Dispose();
        mutex.Dispose();
    }
}
