using System.Buffers;
using System.Net.Sockets;

namespace MissionControl.Agent.Protocols;

internal sealed class GopherProbe : IProtocolProbe
{
    private const int BufferSize = 4 * 1024;
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly byte[] RootSelectorRequest =
    "\r\n"u8.ToArray();

    private static readonly byte[] EmptyMenuResponse =
        ".\r\n"u8.ToArray();

    private static readonly byte[] ResponseTerminator =
        "\r\n.\r\n"u8.ToArray();

    public string Protocol => "gopher";

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
        await stream.WriteAsync(RootSelectorRequest, cancellationToken);

        await stream.FlushAsync();
        using var response = new MemoryStream();

        byte[] buffer =
            ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, BufferSize), cancellationToken);

                if (bytesRead == 0)
                    break;

                if (response.Length + bytesRead > MaximumResponseBytes)
                {
                    throw new InvalidDataException(
                        $"Gopher response exceeded the " +
                        $"{MaximumResponseBytes}-byte limit.");
                }

                response.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (response.Length == 0)
        {
            throw new InvalidDataException(
                "The Gopher service returned an empty response.");
        }

        ReadOnlySpan<byte> payload =
            response.GetBuffer().AsSpan(
                0,
                checked((int)response.Length));

        // Item type 3 is the Gopher error response type.
        if (payload[0] == (byte)'3')
        {
            throw new InvalidDataException(
                "The Gopher service returned an error response.");
        }

        if (!HasValidTerminator(payload))
        {
            throw new InvalidDataException(
                "The Gopher response did not contain a valid terminator.");
        }
    }

    private static bool HasValidTerminator(
       ReadOnlySpan<byte> response)
    {
        // A completely empty but valid menu consists only of ".\r\n".
        return response.SequenceEqual(EmptyMenuResponse) ||
               response.EndsWith(ResponseTerminator);
    }
}
