namespace Hesabyar.Sync.Contracts;

public static class SyncContractValidator
{
    public const int MaximumBatchSize = 500;
    public const int MaximumPullPageSize = 500;

    public static IReadOnlyList<string> Validate(SyncPushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        if (request.DeviceId == Guid.Empty)
            errors.Add("DeviceId is required.");

        if (request.Changes is null || request.Changes.Count == 0)
        {
            errors.Add("At least one change is required.");
            return errors;
        }

        if (request.Changes.Count > MaximumBatchSize)
            errors.Add($"A push batch cannot contain more than {MaximumBatchSize} changes.");

        var changeIds = new HashSet<Guid>();
        long previousSequence = 0;

        foreach (var change in request.Changes)
        {
            errors.AddRange(Validate(change));

            if (change.Stamp.DeviceId != request.DeviceId)
                errors.Add($"Change '{change.ChangeId}' belongs to a different device.");

            if (!changeIds.Add(change.ChangeId))
                errors.Add($"Change '{change.ChangeId}' is duplicated in the batch.");

            if (change.Stamp.DeviceSequence <= previousSequence)
                errors.Add("Device sequences must be strictly increasing within a batch.");

            previousSequence = change.Stamp.DeviceSequence;
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(SyncChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var errors = new List<string>();
        if (change.ChangeId == Guid.Empty)
            errors.Add("ChangeId is required.");
        if (change.AggregateId == Guid.Empty)
            errors.Add("AggregateId is required.");
        if (string.IsNullOrWhiteSpace(change.AggregateType) || change.AggregateType.Length > 64)
            errors.Add("AggregateType must contain between 1 and 64 characters.");
        if (!Enum.IsDefined(change.Operation))
            errors.Add("Operation is invalid.");
        if (change.SchemaVersion < 1)
            errors.Add("SchemaVersion must be positive.");
        if (change.Stamp.DeviceId == Guid.Empty)
            errors.Add("Stamp.DeviceId is required.");
        if (change.Stamp.DeviceSequence < 1)
            errors.Add("Stamp.DeviceSequence must be positive.");
        if (change.Stamp.ChangedAtUtc.Offset != TimeSpan.Zero)
            errors.Add("Stamp.ChangedAtUtc must use UTC.");

        if (change.Fields is null)
        {
            errors.Add("Fields are required.");
            return errors;
        }

        if (change.Operation == SyncOperation.Upsert && change.Fields.Count == 0)
            errors.Add("An upsert must contain at least one field.");
        if (change.Operation == SyncOperation.Delete && change.Fields.Count != 0)
            errors.Add("A delete must not contain field values.");

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in change.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 100)
                errors.Add("Field names must contain between 1 and 100 characters.");
            else if (!fieldNames.Add(field.Name))
                errors.Add($"Field '{field.Name}' is duplicated.");
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(SyncPullRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PageSize is < 1 or > MaximumPullPageSize)
            return [$"PageSize must be between 1 and {MaximumPullPageSize}."];

        if (request.Cursor is { Length: > 2048 })
            return ["Cursor cannot exceed 2048 characters."];

        return [];
    }
}
