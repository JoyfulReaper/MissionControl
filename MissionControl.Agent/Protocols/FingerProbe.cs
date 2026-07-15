using System.Buffers;
using System.Net.Sockets;

namespace MissionControl.Agent.Protocols;

internal sealed class FingerProbe : IProtocolProbe
{
    private const int BufferSize = 4 * 1024;
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly byte[] DirectoryRequest =
        "\r\n"u8.ToArray();

    public string Protocol => "finger";

    public async Task ExecuteAsync(
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient
        {
            NoDelay = true
        };

        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        await using NetworkStream stream = client.GetStream();

        await stream.WriteAsync(DirectoryRequest, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        var totalBytesRead = 0;
        var hasVisibleContent = false;
        byte previousByte = 0;
        byte lastByte = 0;

        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                if (totalBytesRead + bytesRead > MaximumResponseBytes)
                {
                    throw new InvalidDataException(
                        $"Finger response exceeded the " +
                        $"{MaximumResponseBytes}-byte limit.");
                }

                for (var i = 0; i < bytesRead; i++)
                {
                    byte value = buffer[i];
                    previousByte = lastByte;
                    lastByte = value;

                    if (value is not (byte)'\r' and
                        not (byte)'\n' and
                        not (byte)'\t' and
                        not (byte)' ')
                    {
                        hasVisibleContent = true;
                    }

                }
                totalBytesRead += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (totalBytesRead == 0)
        {
            throw new InvalidDataException(
                "The Finger service returned an empty response.");
        }

        if (!hasVisibleContent)
        {
            throw new InvalidDataException(
                "The Finger service returned no visible content.");
        }

        if (previousByte != (byte)'\r' ||
            lastByte != (byte)'\n')
        {
            throw new InvalidDataException(
                "The Finger response did not end with CRLF.");
        }
    }
}