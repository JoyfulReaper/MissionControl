using System.Buffers;
using System.Net.Sockets;

namespace MissionControl.Agent.Protocols;

internal sealed class GopherProbe : IProtocolProbe
{
    private const int BufferSize = 4 * 1024;
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly byte[] HealthSelectorRequest = "/healthz\r\n"u8.ToArray();
    private static readonly byte[] HealthyStatusLine = "OK\r\n"u8.ToArray();
    private static readonly byte[] ResponseTerminator = "\r\n.\r\n"u8.ToArray();

    public string Protocol => "gopher";

    public async Task ExecuteAsync(
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
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
            HealthSelectorRequest,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);

        using var response = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, BufferSize),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    break;
                }

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
            throw new InvalidDataException("The Gopher health endpoint returned an empty response.");
        }

        ReadOnlySpan<byte> payload =
            response.GetBuffer().AsSpan(0, checked((int)response.Length));

        // Item type 3 is the Gopher error-response type.
        if (payload[0] == (byte)'3')
        {
            throw new InvalidDataException("The Gopher health endpoint returned an error response.");
        }

        if (!payload.StartsWith(HealthyStatusLine))
        {
            throw new InvalidDataException("The Gopher health endpoint did not return an OK status.");
        }

        if (!payload.EndsWith(ResponseTerminator))
        {
            throw new InvalidDataException("The Gopher health response did not contain a valid terminator.");
        }
    }
}