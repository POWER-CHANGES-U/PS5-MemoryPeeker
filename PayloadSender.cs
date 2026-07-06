using System.IO;
using System.Net;
using System.Net.Sockets;

namespace PS5MemoryPeeker;

public static class PayloadSender
{
    public static async Task SendAsync(string host, int port, string payloadPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(payloadPath))
        {
            throw new FileNotFoundException("Payload file was not found.", payloadPath);
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = 10000,
                ReceiveTimeout = 10000
            };

            socket.Connect(new IPEndPoint(IPAddress.Parse(host), port));
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                socket.SendFile(payloadPath);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Some payload loaders close the socket immediately after receiving the file.
            }
        }, cancellationToken);
    }
}
