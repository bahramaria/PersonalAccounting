using System.Security.Cryptography;
using System.Text;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed record DocumentAttachmentSnapshot(
    Guid Id, Guid? TransactionId, Guid? PhysicalAssetId, string FileName,
    string ContentType, long Length, DateTimeOffset CreatedAtUtc);

public sealed class DocumentAttachmentService(
    IDbContextFactory<AppDbContext> contextFactory,
    string storageDirectory,
    IDatabaseKeyProvider keyProvider)
{
    public const int MaximumFileSize = 15 * 1024 * 1024;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const byte FormatVersion = 1;

    public async Task<Guid> AddAsync(
        Stream source, string fileName, Guid? transactionId = null,
        Guid? physicalAssetId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if ((transactionId.HasValue ? 1 : 0) + (physicalAssetId.HasValue ? 1 : 0) != 1)
            throw new ArgumentException("An attachment must belong to exactly one transaction or physical asset.");
        var normalizedName = Path.GetFileName(fileName?.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 180)
            throw new ArgumentException("File name is invalid.", nameof(fileName));

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumFileSize)
                throw new InvalidDataException("Attachment exceeds the 15 MB limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var plaintext = buffer.ToArray();
        var contentType = DetectContentType(plaintext);\n        var extension = Path.GetExtension(normalizedName).ToLowerInvariant();\n        var expectedExtension = contentType switch\n        {\n            "application/pdf" => ".pdf",\n            "image/jpeg" => ".jpg",\n            "image/png" => ".png",\n            "image/webp" => ".webp",\n            _ => ""\n        };\n        if (extension != expectedExtension && !(contentType == "image/jpeg" && extension == ".jpeg"))\n            throw new InvalidDataException("File extension does not match its content.");
        var id = Guid.CreateVersion7();
        var storedName = id.ToString("N") + ".hatt";
        Directory.CreateDirectory(storageDirectory);
        var fullPath = GetSafePath(storedName);
        var encrypted = Encrypt(plaintext);
        await File.WriteAllBytesAsync(fullPath, encrypted, cancellationToken);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (transactionId is { } tx &&
                !await db.Transactions.AnyAsync(x => x.Id == tx, cancellationToken))
                throw new InvalidOperationException("Transaction was not found.");
            if (physicalAssetId is { } asset &&
                !await db.PhysicalAssets.AnyAsync(x => x.Id == asset, cancellationToken))
                throw new InvalidOperationException("Physical asset was not found.");

            db.Set<DbDocumentAttachment>().Add(new DbDocumentAttachment
            {
                Id = id,
                TransactionId = transactionId,
                PhysicalAssetId = physicalAssetId,
                OriginalFileName = normalizedName,
                StoredFileName = storedName,
                ContentType = contentType,
                Length = plaintext.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(plaintext))
            });
            await db.SaveChangesAsync(cancellationToken);
            return id;
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<IReadOnlyList<DocumentAttachmentSnapshot>> GetAsync(
        Guid? transactionId = null, Guid? physicalAssetId = null,
        CancellationToken cancellationToken = default)
    {
        if ((transactionId.HasValue ? 1 : 0) + (physicalAssetId.HasValue ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one attachment owner.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Set<DbDocumentAttachment>().AsNoTracking();
        query = transactionId is { } tx ? query.Where(x => x.TransactionId == tx) :
            query.Where(x => x.PhysicalAssetId == physicalAssetId);
        return await query.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new DocumentAttachmentSnapshot(
                x.Id, x.TransactionId, x.PhysicalAssetId, x.OriginalFileName,
                x.ContentType, x.Length, x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<byte[]> ReadAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.Set<DbDocumentAttachment>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken)
            ?? throw new InvalidOperationException("Attachment was not found.");
        var path = GetSafePath(item.StoredFileName);
        if (!File.Exists(path)) throw new FileNotFoundException("Attachment file is missing.", path);
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        var plaintext = Decrypt(encrypted);
        if (plaintext.LongLength != item.Length ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(plaintext), Convert.FromHexString(item.Sha256)))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Attachment integrity validation failed.");
        }
        return plaintext;
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.Set<DbDocumentAttachment>()
            .SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (item is null) return;
        var path = GetSafePath(item.StoredFileName);
        item.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
        if (File.Exists(path)) File.Delete(path);
    }

    public async Task<int> CleanupOrphansAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(storageDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var known = await db.Set<DbDocumentAttachment>().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted).Select(x => x.StoredFileName).ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(storageDirectory, "*.hatt"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromHours(1)) continue;
            if (!known.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                File.Delete(file);
                removed++;
            }
        }
        return removed;
    }

    private string GetSafePath(string storedName)
    {
        if (Path.GetFileName(storedName) != storedName ||
            !storedName.EndsWith(".hatt", StringComparison.Ordinal))
            throw new InvalidDataException("Attachment storage name is invalid.");
        return Path.Combine(storageDirectory, storedName);
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(keyProvider.GetDatabasePassword()));
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            return [FormatVersion, .. nonce, .. tag, .. ciphertext];
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private byte[] Decrypt(byte[] payload)
    {
        if (payload.Length < 1 + NonceSize + TagSize || payload[0] != FormatVersion)
            throw new InvalidDataException("Attachment format is invalid.");
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(keyProvider.GetDatabasePassword()));
        var plaintext = new byte[payload.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.AsSpan(1, NonceSize),
                payload.AsSpan(1 + NonceSize + TagSize), payload.AsSpan(1 + NonceSize, TagSize), plaintext);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Attachment decryption or integrity validation failed.", exception);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static string DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new InvalidDataException("Only PDF, JPEG, PNG and WebP files are supported.");
    }
}