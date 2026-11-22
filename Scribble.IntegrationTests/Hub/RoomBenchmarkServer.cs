using System.Diagnostics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Scribble.IntegrationTests.Hub;

/// <summary>Runs an isolated server so the join benchmark can distinguish server memory from client allocations.</summary>
internal sealed class RoomBenchmarkServer : IDisposable
{
    private readonly Process _process = new();
    private readonly TaskCompletionSource<string> _address = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _responseBytes;

    /// <summary>Starts Kestrel on a free loopback port and waits for its listening address.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _process.StartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            ArgumentList = { typeof(Program).Assembly.Location, "--urls", "http://127.0.0.1:0" }
        };
        _process.OutputDataReceived += (_, args) =>
        {
            const string prefix = "Now listening on: ";
            if (args.Data?.Trim() is { } line && line.StartsWith(prefix, StringComparison.Ordinal))
            {
                _address.TrySetResult(line[prefix.Length..]);
            }
        };
        _process.Start();
        _process.BeginOutputReadLine();
        await _address.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>Reports the high-water resident memory of the server alone.</summary>
    public long PeakBytes => _process.PeakWorkingSet64;

    /// <summary>Counts the HTTP response body bytes delivered to the measured joining client.</summary>
    public long ResponseBytes => Interlocked.Read(ref _responseBytes);

    /// <summary>Connects to the benchmark server with an optional simulated WAN link.</summary>
    public HubConnection Connect(bool delayed, bool measure = false) => new HubConnectionBuilder()
        .WithUrl(_address.Task.Result + "/drawingHub", options =>
        {
            if (delayed)
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = inner => new DelayedResponse(inner,
                    bytes =>
                    {
                        if (measure)
                        {
                            Interlocked.Add(ref _responseBytes, bytes);
                        }
                    });
            }
        }).Build();

    /// <summary>Simulates a 40 ms round trip and a 10 Mbit/s downstream for each client.</summary>
    private sealed class DelayedResponse(HttpMessageHandler inner, Action<int> count) : DelegatingHandler(inner)
    {
        /// <summary>Accounts for the buffered response before releasing it to SignalR.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            await Task.Delay(40, token);
            var response = await base.SendAsync(request, token);
            if (request.Method == HttpMethod.Get)
            {
                var body = await response.Content.ReadAsByteArrayAsync(token);
                count(body.Length);
                await Task.Delay(TimeSpan.FromSeconds(body.Length / 1_250_000d), token);
            }

            return response;
        }
    }

    /// <summary>Stops the private server when the measurements finish.</summary>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }

        _process.Dispose();
    }
}