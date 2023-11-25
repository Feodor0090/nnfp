using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace NNFPServer;

public class Session : IDisposable
{
    private readonly TcpClient _socket;
    private readonly CredentialsManager _manager;
    private readonly NetworkStream _stream;

    private string? _userName;
    private bool _authentificated;
    private byte[]? _expectedCheck;

    private int _sendTransmissionCount;

    private int _receiveTransmissionCount;
    private readonly Dictionary<int, FileStream> _activeReceiveTransmissions = new();

    public void Dispose()
    {
        foreach (var value in _activeReceiveTransmissions.Values)
            value.Dispose();
        _activeReceiveTransmissions.Clear();
        _socket.Client.Shutdown(SocketShutdown.Both);
        _stream.Dispose();
        _socket.Dispose();
    }

    public Session(TcpClient socket, CredentialsManager manager)
    {
        _socket = socket;
        _manager = manager;
        socket.NoDelay = true;
        _stream = socket.GetStream();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        byte[] frameHeader = new byte[6];
        while (true)
        {
            // receiving size of next frame
            await ReadExactBytes(frameHeader, cancellationToken);
            int length = BitConverter.ToInt32(frameHeader, 0);

            // receiving type of next frame
            InputFrameType type = (InputFrameType)BitConverter.ToInt16(frameHeader, 4);

            // receiving data
            byte[] data = new byte[length];
            await ReadExactBytes(data, cancellationToken);

            switch (type)
            {
                case InputFrameType.Shutdown:
                {
                    // disposal will happen in Worker loop
                    return;
                }
                case InputFrameType.Login:
                {
                    string un = Encoding.UTF8.GetString(data);
                    _authentificated = false;
                    if (_manager.IsValidUsername(un))
                    {
                        var check = _manager.GetEncryptionCheckBlob(un);
                        await Send(OutputFrameType.AuthCheckData, check.plain, cancellationToken);
                        _expectedCheck = check.encrypted;
                        _userName = un;
                    }
                    else
                    {
                        await SendAuthFailure(cancellationToken);
                    }

                    break;
                }

                case InputFrameType.Auth:
                {
                    if (_expectedCheck == null || _userName == null)
                    {
                        await SendAuthFailure(cancellationToken);
                    }
                    else if (_expectedCheck.SequenceEqual(data))
                    {
                        _authentificated = true;
                        _expectedCheck = null;
                        await SendDirectoryContents("/", cancellationToken);
                    }
                    else
                    {
                        _authentificated = false;
                        await SendAuthFailure(cancellationToken);
                    }

                    break;
                }

                case InputFrameType.Explore:
                {
                    var path = Encoding.UTF8.GetString(data);
                    await SendDirectoryContents(path, cancellationToken);
                    break;
                }

                case InputFrameType.ServerToClientInit:
                {
                    var path = Encoding.UTF8.GetString(data);
                    await SendFile(path, cancellationToken);
                    break;
                }

                case InputFrameType.ClientToServerInit:
                {
                    var path = Encoding.UTF8.GetString(data);
                    await InitFileReceive(path, cancellationToken);
                    break;
                }

                case InputFrameType.FilePart:
                {
                    var trId = BitConverter.ToInt32(data, 0);
                    var stream = _activeReceiveTransmissions[trId];
                    await stream.WriteAsync(data.AsMemory(4), cancellationToken);
                    break;
                }

                case InputFrameType.Eof:
                {
                    var trId = BitConverter.ToInt32(data);
                    var stream = _activeReceiveTransmissions[trId];
                    await stream.DisposeAsync();
                    _activeReceiveTransmissions.Remove(trId);
                    break;
                }
            }
        }
    }

    private async Task ReadExactBytes(byte[] buffer, CancellationToken cancellationToken)
    {
        int readFails = 0;
        Stopwatch sw = new Stopwatch();

        int read = 0;
        while (read < buffer.Length)
        {
            sw.Reset();
            sw.Start();
            int readNow = await _stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            sw.Stop();
            read += readNow;
            if (readNow == 0)
            {
                if (!_socket.Connected)
                    throw new IOException("Disconnected");
                if (sw.ElapsedMilliseconds < 1000)
                {
                    readFails++;
                    if (readFails > 100)
                    {
                        throw new IOException("Socket is dead");
                    }
                }
            }
        }
    }

    private async Task Send(OutputFrameType type, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(BitConverter.GetBytes(data.Length).AsMemory(), cancellationToken);
        await _stream.WriteAsync(BitConverter.GetBytes((short)type).AsMemory(), cancellationToken);
        await _stream.WriteAsync(data, cancellationToken);
    }

    private async Task Send(OutputFrameType type, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, cancellationToken);
        await _stream.WriteAsync(BitConverter.GetBytes((short)type).AsMemory(), cancellationToken);
    }

    private Task SendAuthFailure(CancellationToken cancellationToken)
    {
        return Send(OutputFrameType.AuthFailure, cancellationToken);
    }

    private Task SendAccessFailure(CancellationToken cancellationToken)
    {
        return Send(OutputFrameType.AccessFailure, cancellationToken);
    }

    private Task SendDirectoryContents(string path, CancellationToken cancellationToken)
    {
        if (_userName == null || !_authentificated)
            return SendAuthFailure(cancellationToken);

        if (!path.StartsWith('/') || !path.EndsWith('/'))
            return SendAccessFailure(cancellationToken);

        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("..") || x.Equals("~")))
            return SendAccessFailure(cancellationToken);

        var realPath = _manager.GetHomeFolderFor(_userName) + path;
        string[] list;
        try
        {
            list = Directory.GetDirectories(realPath).Select(x => $"{x}/").Concat(Directory.GetFiles(realPath))
                .ToArray();
        }
        catch
        {
            return SendAccessFailure(cancellationToken);
        }

        List<byte> data = new List<byte>(BitConverter.GetBytes(list.Length));
        foreach (var entry in list)
        {
            var enc = Encoding.UTF8.GetBytes(entry);
            data.AddRange(BitConverter.GetBytes((ushort)enc.Length));
            data.AddRange(enc);
        }

        return Send(OutputFrameType.DirectoryContents, data.ToArray(), cancellationToken);
    }

    private async Task InitFileReceive(string path, CancellationToken cancellationToken)
    {
        var realPath = await CheckFilePath(path, cancellationToken);
        if (realPath == null) return;

        if (File.Exists(realPath))
        {
            await Send(OutputFrameType.AcceptFailure, cancellationToken);
            return;
        }

        var trId = _receiveTransmissionCount;
        _receiveTransmissionCount++;

        var stream = File.Create(realPath);
        _activeReceiveTransmissions[trId] = stream;

        await Send(OutputFrameType.ClientToServerAccept, BitConverter.GetBytes(trId), cancellationToken);
    }

    private async Task SendFile(string path, CancellationToken cancellationToken)
    {
        var realPath = await CheckFilePath(path, cancellationToken);
        if (realPath == null) return;

        if (!File.Exists(realPath))
        {
            await Send(OutputFrameType.AcceptFailure, cancellationToken);
            return;
        }


        await using var stream = File.Open(realPath, FileMode.Open, FileAccess.Read);

        _sendTransmissionCount++;
        var trId = _sendTransmissionCount;
        byte[] accept = new byte[12];
        BitConverter.TryWriteBytes(accept, stream.Length);
        BitConverter.TryWriteBytes(new Span<byte>(accept, 8, 4), trId);


        await Send(OutputFrameType.ServerToClientAccept, accept, cancellationToken);

        byte[] buf = new byte[1024 * 512];
        BitConverter.TryWriteBytes(buf, trId);
        while (true)
        {
            //first 4 bytes are id
            var r = await stream.ReadAsync(buf.AsMemory(4), cancellationToken);
            if (r == 0)
            {
                await Send(OutputFrameType.Eof, cancellationToken);
                return;
            }

            await Send(OutputFrameType.FilePart, buf.AsMemory(0, r + 4), cancellationToken);
        }
    }

    private async Task<string?> CheckFilePath(string path, CancellationToken cancellationToken)
    {
        if (_userName == null || !_authentificated)
        {
            await SendAuthFailure(cancellationToken);
            return null;
        }

        if (!path.StartsWith('/') || path.EndsWith('/'))
        {
            await SendAccessFailure(cancellationToken);
            return null;
        }

        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("..") || x.Equals("~")))
        {
            await SendAccessFailure(cancellationToken);
            return null;
        }

        var realPath = _manager.GetHomeFolderFor(_userName) + path;
        return realPath;
    }

    private enum InputFrameType : short
    {
        Shutdown = 0,

        // connection start
        Login = 1,
        Auth = 2,

        // info
        Explore = 3,
        ServerToClientInit = 4,
        ClientToServerInit = 5,

        // transmission
        Eof = 254,
        FilePart = 255,
    }

    private enum OutputFrameType : short
    {
        // connection start
        AuthCheckData = 1,
        AuthFailure = 2,

        // info
        DirectoryContents = 3,
        ServerToClientAccept = 4,
        ClientToServerAccept = 5,
        AccessFailure = 6,
        AcceptFailure = 7,

        // transmission
        Eof = 254,
        FilePart = 255,
    }
}