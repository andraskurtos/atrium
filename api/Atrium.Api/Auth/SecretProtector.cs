using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Atrium.Api.Auth;

public sealed class SecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] key;

    public SecretProtector(IOptions<AuthOptions> authOptions)
    {
        ArgumentNullException.ThrowIfNull(authOptions);
        var signingKey = authOptions.Value.JwtSigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Cannot initialize SecretProtector without AuthOptions.JwtSigningKey.");
        }

        key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(signingKey),
            outputLength: 32,
            salt: "atrium-secret-protector-salt"u8.ToArray(),
            info: "atrium-ai-api-key-v1"u8.ToArray());
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        return $"{Prefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ciphertext)}:{Convert.ToBase64String(tag)}";
    }

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue) || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = protectedValue[Prefix.Length..].Split(':');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[0]);
            var ciphertext = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

