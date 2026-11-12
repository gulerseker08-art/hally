using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Hally.Infrastructure.Storage;

public class StorageOptions
{
    public const string Section = "Storage";
    public string Root { get; set; } = "./data/documents";
    /// <summary>Base64 encoded 32-byte AES key. Supplied via environment/secret store.</summary>
    public string EncryptionKey { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 30;
}

/// <summary>Stores captured originals. Implementations must encrypt at rest.</summary>
public interface IDocumentStore
{
    Task<string> SaveAsync(Guid familyId, byte[] content, CancellationToken ct);
    Task<byte[]?> ReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>AES-256-GCM encrypted files on disk (swap for Azure Blob in production behind the same interface).
/// File layout: nonce(12) | tag(16) | ciphertext.</summary>
public class EncryptedFileDocumentStore : IDocumentStore
{
    private readonly string _root;
    private readonly byte[] _key;

    public EncryptedFileDocumentStore(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.Root);
        _key = string.IsNullOrWhiteSpace(options.Value.EncryptionKey) ? [] : Convert.FromBase64String(options.Value.EncryptionKey);
    }

    public async Task<string> SaveAsync(Guid familyId, byte[] content, CancellationToken ct)
    {
        EnsureKey();
        var key = $"{familyId:N}/{Guid.NewGuid():N}.bin";
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[content.Length];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Encrypt(nonce, content, cipher, tag);
        }

        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [.. nonce, .. tag, .. cipher], ct);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        EnsureKey();
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var data = await File.ReadAllBytesAsync(path, ct);
        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain);
        return plain;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return path;
    }

    private void EnsureKey()
    {
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Storage:EncryptionKey must be a base64 encoded 32-byte key.");
        }
    }
}
