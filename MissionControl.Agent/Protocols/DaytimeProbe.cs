using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace MissionControl.Agent.Protocols;

internal sealed class DaytimeProbe : IProtocolProbe
{
    public string Protocol => "daytime";

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

        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 128,
            leaveOpen: true);

        string? response = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidDataException(
                "Daytime response was empty.");
        }

        if (!DateTimeOffset.TryParseExact(
                response,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset timestamp))
        {
            throw new InvalidDataException(
                $"Daytime response was not a valid round-trip timestamp: '{response}'.");
        }
    }
}