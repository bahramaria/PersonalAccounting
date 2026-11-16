using Hesabyar.Application.Common;

namespace Hesabyar.Application.Planning;

public sealed class TagService(IPlanningRepository repository, IClock clock)
{
    public Task<IReadOnlyList<TagSnapshot>> GetAsync(CancellationToken cancellationToken = default) =>
        repository.GetTagsAsync(cancellationToken);

    public async Task<Result<Guid>> SaveAsync(Guid? id, string name, string? color,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 || color?.Length > 16)
            return Result<Guid>.Failure("tag.details.invalid", "Tag name or color is invalid.");
        var saved = await repository.SaveTagAsync(id, name.Trim(),
            string.IsNullOrWhiteSpace(color) ? null : color.Trim(), clock.UtcNow, cancellationToken);
        return saved is Guid value ? Result<Guid>.Success(value) :
            Result<Guid>.Failure("tag.duplicate_or_missing", "The tag already exists or could not be found.");
    }

    public async Task<Result<bool>> SetTransactionTagsAsync(Guid transactionId, IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        if (tagIds.Count > 10 || tagIds.Distinct().Count() != tagIds.Count)
            return Result<bool>.Failure("tag.selection.invalid", "Select up to ten distinct tags.");
        var saved = await repository.SetTransactionTagsAsync(transactionId, tagIds, clock.UtcNow, cancellationToken);
        return saved ? Result<bool>.Success(true) :
            Result<bool>.Failure("tag.transaction.invalid", "The transaction or one of its tags is unavailable.");
    }
}
