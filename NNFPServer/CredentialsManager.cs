namespace NNFPServer;

public class CredentialsManager
{
    private Dictionary<string, string> _users = new()
    {
        { "user", "password" },
    };

    public bool IsValidUsername(string username) => _users.ContainsKey(username);

    public string GetHomeFolderFor(string username) => "/home/ansel/говно";

    public (byte[] plain, byte[] encr) GetEncryptionCheckBlob(string username)
    {
        //TODO
        return (Array.Empty<byte>(), Array.Empty<byte>());
    }

    public byte[] Encrypt(byte[] data)
    {
        //TODO
        return data;
    }
}