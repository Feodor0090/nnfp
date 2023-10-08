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
                var session = new Session(accepted, new CredentialsManager()).RunAsync(stoppingToken).ContinueWith(t =>
                {
                    _logger.Log(LogLevel.Information, "Session closed.");
                    if (t.IsFaulted)
                    {
                        _logger.Log(LogLevel.Error, t.Exception.InnerException, "Session handler failed");
                    }
                });
            }
        }
        finally
        {
            socket.Stop();
        }
    }
}