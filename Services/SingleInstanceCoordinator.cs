using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace RemotePC.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string AppId = "RemotePC";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenTask;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, GetMutexName(), out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public event EventHandler? OpenRequested;

    public void StartListening()
    {
        if (!IsPrimary)
        {
            return;
        }

        _listenTask = Task.Run(ListenAsync);
    }

    public static async Task SignalExistingAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", GetPipeName(), PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(timeout.Token);
            var bytes = Encoding.UTF8.GetBytes("open");
            await pipe.WriteAsync(bytes, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        catch
        {
        }
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    GetPipeName(),
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(_shutdown.Token);
                if (message.Trim().Equals("open", StringComparison.OrdinalIgnoreCase))
                {
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private static string GetMutexName()
    {
        return $@"Local\{AppId}-{GetUserHash()}";
    }

    private static string GetPipeName()
    {
        return $"{AppId}-{GetUserHash()}";
    }

    private static string GetUserHash()
    {
        var user = Environment.UserName;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(user));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            if (IsPrimary)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
        }

        _mutex.Dispose();
        _shutdown.Dispose();
    }
}
