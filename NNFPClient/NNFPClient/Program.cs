using System.Net.Sockets;
using System.Text;

Console.Write("Host: ");
string? host = Console.ReadLine();
if (host == null)
    return;

TcpClient client = new TcpClient(host, 2920);

var _stream = client.GetStream();

usernamePrompt:

Console.Write("Username: ");
var user = Console.ReadLine();
if (user == null)
    return;
await Send(OutputFrameType.Login, Encoding.UTF8.GetBytes(user));
var res = await Receive();
{
    if (res.type == InputFrameType.AuthFailure)
        goto usernamePrompt;
    if (res.type != InputFrameType.AuthCheckData)
        throw new InvalidOperationException();

    passwordPrompt:
    Console.Write("Password: ");
    var pw = Console.ReadLine();
    if (pw == null)
        return;
    await Send(OutputFrameType.Auth, Array.Empty<byte>());

    res = await Receive();
    if (res.type == InputFrameType.AuthFailure)
        goto passwordPrompt;
}

Stack<string> path = new();

while (true)
{
    switch (res.type)
    {
        case InputFrameType.AuthFailure:
            Console.WriteLine("Сбой аутентификации. Начните заново.");
            return;
        case InputFrameType.AccessFailure:
            Console.WriteLine("Отказано в доступе по пути. Проверьте опечатки.");
            goto fileNamePrompt;
        case InputFrameType.DirectoryContents:
            // pass
            break;
        default:
            throw new NotImplementedException();
    }

    var count = BitConverter.ToInt32(res.data, 0);
    var p = 4;
    Console.WriteLine("Список файлов:");
    for (int i = 0; i < count; i++)
    {
        var len = BitConverter.ToUInt16(res.data, p);
        p += 2;
        var name = Encoding.UTF8.GetString(res.data, p, len);
        p += len;
        Console.WriteLine(name);
    }

    fileNamePrompt:

    Console.WriteLine();
    Console.Write("Имя файла/папки (.. для 1 уровня вверх): ");
    var nextName = Console.ReadLine();
    if (nextName == null)
        return;
    if (nextName.Equals(".."))
    {
        if (path.Count > 0)
            path.Pop();
        else
            goto fileNamePrompt;
    }
    else if (nextName.EndsWith('/'))
    {
        path.Push(nextName);
    }
    else if (nextName.Contains('/'))
    {
        Console.WriteLine("Имя не должно содержать слеши!");
        goto fileNamePrompt;
    }
    else
    {
        await Send(OutputFrameType.ServerToClientInit,
            Encoding.UTF8.GetBytes("/" + string.Join("", path.Reverse()) + nextName));
        var tr = await Receive();
        switch (tr.type)
        {
            case InputFrameType.AcceptFailure:
            case InputFrameType.AccessFailure:
            case InputFrameType.AuthFailure:
                Console.WriteLine("Нет доступа");
                goto fileNamePrompt;
            case InputFrameType.ServerToClientAccept:
            {
                var len = BitConverter.ToInt64(tr.data);
                var id = BitConverter.ToInt32(tr.data, 8);
                await using var stream = File.Open(nextName, FileMode.CreateNew, FileAccess.Write);
                while (true)
                {
                    tr = await Receive();
                    if (tr.type == InputFrameType.Eof)
                    {
                        Console.WriteLine("Передача завершена");
                        break;
                    }
                    else if (tr.type == InputFrameType.FilePart)
                    {
                        var tid = BitConverter.ToInt32(tr.data);
                        if (tid != id)
                            throw new InvalidOperationException();
                        await stream.WriteAsync(tr.data.AsMemory(4, tr.data.Length - 4));
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }

                break;
            }
        }
    }

    await Send(OutputFrameType.Explore, Encoding.UTF8.GetBytes("/" + string.Join("", path.Reverse())));
    res = await Receive();
}


async Task Send(OutputFrameType type, byte[] data)
{
    try
    {
        await _stream.WriteAsync(BitConverter.GetBytes(data.Length).AsMemory());
        await _stream.WriteAsync(BitConverter.GetBytes((short)type).AsMemory());
        await _stream.WriteAsync(data.AsMemory());
    }
    catch
    {
        Console.WriteLine("Соединение разорвано.");
        Environment.Exit(1);
    }
}

async Task<(InputFrameType type, byte[] data)> Receive()
{
    byte[] frameHeader = new byte[6];
    await ReadExactBytes(frameHeader);
    int length = BitConverter.ToInt32(frameHeader, 0);
    InputFrameType type = (InputFrameType)BitConverter.ToInt16(frameHeader, 4);
    byte[] data = new byte[length];
    await ReadExactBytes(data);

    return (type, data);
}

async Task ReadExactBytes(byte[] buffer)
{
    int read = 0;
    while (read < buffer.Length)
    {
        read += await _stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
    }
}

enum OutputFrameType : short
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

enum InputFrameType : short
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