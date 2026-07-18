using System.Net.Sockets;
using System.Text;

namespace MissionControl.Agent.Protocols;

internal class EchoProbe : IProtocolProbe
{
    public string Protocol => "echo";

    public async Task ExecuteAsync(ProbeOptions options, CancellationToken cancellationToken)
    {
        byte[] request = Encoding.ASCII.GetBytes(
            $"mission-control:{Guid.NewGuid():N}\r\n");

        byte[] response = new byte[request.Length];

        using var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(request, cancellationToken);
        await stream.ReadExactlyAsync(response, cancellationToken);

        if (!response.AsSpan().SequenceEqual(request))
        {
            throw new InvalidDataException(
                "The Echo service did not return the exact probe payload.");
        }
    }
}
