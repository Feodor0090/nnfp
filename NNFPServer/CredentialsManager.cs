using System.Security.Cryptography;
using System.Text;

namespace NNFPServer;

public class CredentialsManager
{
    public CredentialsManager()
    {
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create), "nnfpserver");
        if (!Directory.Exists(dataFolder))
            Directory.CreateDirectory(dataFolder);

        var configFileName = Path.Combine(dataFolder, "nnfp.conf");
        if (!File.Exists(configFileName))
            throw new ApplicationException($"Config file not found! Please create it at {configFileName}");

        using var config = File.OpenText(configFileName);
        var lines = config.ReadToEnd().Split(Environment.NewLine);
        foreach (var line in lines)
        {
            var parts = line.Split(' ', 3);
            if (parts.Length != 3) continue;
            var u = new User(parts[0], parts[1], parts[2]);
            _users.Add(u);
        }
    }

    private readonly List<User> _users = new();

    public bool IsValidUsername(string username) => _users.Any(x => x.Username == username);

    public string GetHomeFolderFor(string username) => _users.First(x => x.Username == username).HomeFolder;

    public (byte[] plain, byte[] encrypted) GetEncryptionCheckBlob(string username)
    {
        var plain = RandomNumberGenerator.GetBytes(128);

        var user = _users.First(x => x.Username == username);
        var password = Encoding.UTF8.GetBytes(user.Password);

        var code = MD5.HashData(plain.Concat(password).ToArray());
        return (plain, code);
    }

    private class User
    {
        public string Username { get; }
        public string Password { get; }
        public string HomeFolder { get; }

        public User(string username, string password, string homeFolder)
        {
            Username = username;
            Password = password;
            HomeFolder = homeFolder;
        }
    }
}