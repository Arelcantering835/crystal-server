using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Server.C2Bridge;

namespace Server.Listeners;

public sealed class ListenerInstance(uint listenerId, ushort bindPort, IServiceScopeFactory factory)
{
    public uint ListenerId { get; } = listenerId;
    public ushort BindPort { get; } = bindPort;
    public IServiceScopeFactory ScopeFactory { get; } = factory;
    public ListenerStatus Status { get; private set; } = ListenerStatus.Stopped;
    
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _accept;

    public void Start()
    {
        if (Status is ListenerStatus.Running)
            return;
        
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, BindPort);

        try
        {
            _listener.Start();
            Status = ListenerStatus.Running;

            _accept = AcceptClient(_cts.Token);
        }
        catch
        {
            Status = ListenerStatus.Stopped;
            throw;
        }
    }

    public async Task Stop()
    {
        if (Status is not ListenerStatus.Running)
            return;

        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }
        
        _listener?.Stop();
        _listener = null;

        if (_accept is not null)
        {
            try
            {
                await _accept;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
        
        Status = ListenerStatus.Stopped;
    }

    private async Task AcceptClient(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                await HandleClient(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();

        while (client.Connected && !ct.IsCancellationRequested)
        {
            // read length
            var lenBuf = new byte[4];
            var lenRead = await ReadExactly(stream, lenBuf, ct);

            if (lenRead < 4)
                break;

            var msgLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf.AsSpan());

            // read message
            var msg = new byte[msgLen];
            _ = await ReadExactly(stream, msg, ct);

            await using var scope = ScopeFactory.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<C2Manager>();
            var outbound = await manager.ProcessBeaconMessage(ListenerId, msg, ct);
            
            // write response length
            BinaryPrimitives.WriteInt32LittleEndian(lenBuf, outbound.Length);
            await stream.WriteAsync(lenBuf.AsMemory(), ct);
            
            // write response
            await stream.WriteAsync(outbound.AsMemory(), ct);
        }
    }

    private static async Task<int> ReadExactly(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            
            if (read == 0)
                break;
            
            totalRead += read;
        }
        
        return totalRead;
    }
}