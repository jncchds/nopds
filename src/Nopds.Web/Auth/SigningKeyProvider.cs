using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Auth;

/// <summary>Provides the JWT signing key from configuration or a generated key persisted in the data dir.</summary>
public sealed class SigningKeyProvider
{
    public SymmetricSecurityKey Key { get; }

    public SigningKeyProvider(IOptions<NopdsOptions> options)
    {
        var o = options.Value;
        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(o.Jwt.Key))
        {
            bytes = Encoding.UTF8.GetBytes(o.Jwt.Key);
            if (bytes.Length < 32)
            {
                throw new InvalidOperationException("Nopds:Jwt:Key must be at least 32 bytes long.");
            }
        }
        else
        {
            Directory.CreateDirectory(o.DataDir);
            var path = Path.Combine(o.DataDir, "jwt.key");
            if (File.Exists(path))
            {
                bytes = Convert.FromBase64String(File.ReadAllText(path).Trim());
            }
            else
            {
                bytes = RandomNumberGenerator.GetBytes(64);
                File.WriteAllText(path, Convert.ToBase64String(bytes));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }

        Key = new SymmetricSecurityKey(bytes);
    }
}
