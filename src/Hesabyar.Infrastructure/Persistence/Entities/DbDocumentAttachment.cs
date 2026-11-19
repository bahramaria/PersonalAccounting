namespace Hesabyar.Infrastructure.Persistence.Entities;

public sealed class DbDocumentAttachment : DbEntity
{
    public Guid? TransactionId { get; set; }
    public Guid? PhysicalAssetId { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}