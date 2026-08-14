using System.Net.Sockets;
using System.Text;

namespace MissionControl.Agent.Protocols;

internal sealed class DiscardProbe : IProtocolProbe
{
    public string Protocol => "discard";

    public async Task ExecuteAsync(
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.ASCII.GetBytes($"mission-control:{Guid.NewGuid():N}\r\n");

        using var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(
            options.Host,
            options.Port,
            cancellationToken);

        await using NetworkStream stream = client.GetStream();

        await stream.WriteAsync(
            payload,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);

        client.Client.Shutdown(SocketShutdown.Send);

        byte[] response = new byte[1];

        int bytesRead = await stream.ReadAsync(
            response,
            cancellationToken);

        if (bytesRead != 0)
        {
            throw new InvalidDataException(
                "The Discard service unexpectedly returned data.");
        }
    }
}