using System.Net;
using System.Net.Sockets;

namespace NNFPServer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPEndPoint ipPoint = new IPEndPoint(IPAddress.Any, 2920);
        TcpListener socket = new TcpListener(ipPoint);
        try
        {
            socket.Start();

            while (!stoppingToken.IsCancellationRequested)
            {
                var accepted = await socket.AcceptTcpClientAsync(stoppingToken);
                _logger.Log(LogLevel.Information, "Session with {@Client} started", accepted.Client.RemoteEndPoint);
                ProcessConnection(accepted, stoppingToken);
            }
        }
        finally
        {
            socket.Stop();
            _logger.Log(LogLevel.Information, "Listener closed");
        }
    }

    private async void ProcessConnection(TcpClient client, CancellationToken cancellationToken)
    {
        var session = new Session(client, new CredentialsManager());
        try
        {
            await session.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // skip
        }
        catch (Exception e)
        {
            _logger.Log(LogLevel.Error, e, "Session handler failed");
        }
        finally
        {
            session.Dispose();
        }

        _logger.Log(LogLevel.Information, "Session closed");
    }
}