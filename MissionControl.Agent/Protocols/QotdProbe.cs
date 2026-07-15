using System.Buffers;
using System.Net.Sockets;

namespace MissionControl.Agent.Protocols;

internal sealed class QotdProbe : IProtocolProbe
{
    private const int MaximumResponseBytes = 8 * 1024;

    public string Protocol => "qotd";

    public async Task ExecuteAsync(
        ProbeOptions options,
        CancellationToken cancellationToken
    )
    {
        using var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        await using NetworkStream stream = client.GetStream();

        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            var totalBytesRead = 0;

            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer,
                    cancellationToken
                );

                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
                if (totalBytesRead > MaximumResponseBytes)
                {
                    throw new InvalidDataException(
                        $"QOTD response exceeded the " +
                        $"{MaximumResponseBytes}-byte limit.");
                }

                if (totalBytesRead == 0)
                {
                    throw new InvalidDataException(
                        "The QOTD service returned an empty response.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

        }
    }
}
