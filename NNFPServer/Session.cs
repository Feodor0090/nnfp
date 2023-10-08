using System.Net.Sockets;
using System.Text;

namespace NNFPServer;

public class Session
{
    private readonly TcpClient _socket;
    private readonly CredentialsManager _manager;
    private readonly NetworkStream _stream;

    private string? userName;
    private bool authentificated;
    private byte[]? expectedCheck;

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
            InputFrameType type = (InputFrameType)BitConverter.ToInt16(frameHeader, 4);
            byte[] data = new byte[length];
            await ReadExactBytes(data, cancellationToken);
            switch (type)
            {
                case InputFrameType.Login:
                {
                    string un = Encoding.UTF8.GetString(data);
                    authentificated = false;
                    if (_manager.IsValidUsername(un))
                    {
                        var check = _manager.GetEncryptionCheckBlob(un);
                        await Send(OutputFrameType.AuthCheckData, check.plain, cancellationToken);
                        expectedCheck = check.encr;
                        userName = un;
                    }
                    else
                    {
                        await SendAuthFailure(cancellationToken);
                    }

                    break;
                }

                case InputFrameType.Auth:
                {
                    if (expectedCheck == null || userName == null)
                    {
                        await SendAuthFailure(cancellationToken);
                    }
                    else if (expectedCheck.SequenceEqual(data))
                    {
                        authentificated = true;
                        expectedCheck = null;
                        await SendDirectoryContents("/", cancellationToken);
                    }
                    else
                    {
                        authentificated = false;
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
            }
        }
    }

    private async Task ReadExactBytes(byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            read += await _stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
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
        if (userName == null || !authentificated)
            return SendAuthFailure(cancellationToken);

        if (!path.StartsWith('/') || !path.EndsWith('/'))
            return SendAccessFailure(cancellationToken);

        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("..") || x.Equals("~")))
            return SendAccessFailure(cancellationToken);

        var realPath = _manager.GetHomeFolderFor(userName) + path;
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

    private async Task SendFile(string path, CancellationToken cancellationToken)
    {
        if (userName == null || !authentificated)
        {
            await SendAuthFailure(cancellationToken);
            return;
        }

        if (!path.StartsWith('/') || path.EndsWith('/'))
        {
            await SendAccessFailure(cancellationToken);
            return;
        }

        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("..") || x.Equals("~")))
        {
            await SendAccessFailure(cancellationToken);
            return;
        }

        var realPath = _manager.GetHomeFolderFor(userName) + path;

        if (!File.Exists(realPath))
        {
            await Send(OutputFrameType.AcceptFailure, cancellationToken);
            return;
        }


        await using var stream = File.Open(realPath, FileMode.Open, FileAccess.Read);

        byte[] accept = new byte[12];
        BitConverter.TryWriteBytes(accept, stream.Length);

        await Send(OutputFrameType.ServerToClientAccept, accept, cancellationToken);

        byte[] buf = new byte[1024 * 512];
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

    private enum InputFrameType : short
    {
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