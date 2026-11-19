using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed record PhysicalAssetSnapshot(
    Guid Id,
    string Name,
    PhysicalAssetType Type,
    decimal Quantity,
    string? Unit,
    string CurrencyCode,
    DateOnly? AcquiredOn,
    long AcquisitionCostMinor,
    long? CurrentValueMinor,
    DateOnly? ValuationDate,
    long? UnrealizedGainMinor,
    bool IsArchived,
    string? Note);

public sealed record AssetValuationSnapshot(Guid Id, DateOnly ValuationDate, long ValueMinor, string CurrencyCode, string? Note);

public sealed record CreatePhysicalAssetRequest(
    string Name,
    PhysicalAssetType Type,
    decimal Quantity,
    string? Unit,
    string CurrencyCode,
    DateOnly? AcquiredOn,
    long AcquisitionCostMinor,
    string? Note);

public sealed class PhysicalAssetService(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<IReadOnlyList<PhysicalAssetSnapshot>> GetAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var assets = await db.PhysicalAssets
            .AsNoTracking()
            .Where(x => includeArchived || !x.IsArchived)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var ids = assets.Select(x => x.Id).ToArray();
        var valuations = await db.AssetValuations.AsNoTracking()
            .Where(x => ids.Contains(x.AssetId))
            .OrderByDescending(x => x.ValuationDate)
            .ToListAsync(cancellationToken);

        var valuationsByAsset = valuations
            .GroupBy(x => x.AssetId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(x => x.ValuationDate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id)
                .First());

        return assets.Select(asset =>
        {
            valuationsByAsset.TryGetValue(asset.Id, out var latest);
            return new PhysicalAssetSnapshot(
                asset.Id, asset.Name, asset.Type, asset.Quantity, asset.Unit,
                asset.CurrencyCode, asset.AcquiredOn, asset.AcquisitionCostMinor,
                latest?.ValueMinor ?? asset.AcquisitionCostMinor, latest?.ValuationDate,
                latest is null ? null : checked(latest.ValueMinor - asset.AcquisitionCostMinor),
                asset.IsArchived, asset.Note);
        }).ToArray();
    }

    public async Task<Guid> CreateAsync(
        CreatePhysicalAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim();
        var currency = NormalizeCurrency(request.CurrencyCode);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new ArgumentException("Asset name must contain 1 to 120 characters.", nameof(request));
        if (!Enum.IsDefined(request.Type))
            throw new ArgumentOutOfRangeException(nameof(request), "Asset type is invalid.");
        if (request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Asset quantity must be positive.");
        if (request.AcquisitionCostMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Acquisition cost cannot be negative.");
        if (request.Unit?.Length > 24 || request.Note?.Length > 500)
            throw new ArgumentException("Asset unit or note exceeds its maximum length.", nameof(request));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asset = new DbPhysicalAsset
        {
            Name = name,
            Type = request.Type,
            Quantity = request.Quantity,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim(),
            CurrencyCode = currency,
            AcquiredOn = request.AcquiredOn,
            AcquisitionCostMinor = request.AcquisitionCostMinor,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        db.PhysicalAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return asset.Id;
    }

    public async Task<IReadOnlyList<AssetValuationSnapshot>> GetValuationsAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        if (assetId == Guid.Empty)
            throw new ArgumentException("Asset id is required.", nameof(assetId));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.PhysicalAssets.AnyAsync(x => x.Id == assetId, cancellationToken);
        if (!exists)
            throw new InvalidOperationException("Asset was not found.");

        var items = await db.AssetValuations.AsNoTracking()
            .Where(x => x.AssetId == assetId)
            .OrderByDescending(x => x.ValuationDate)
            .ToListAsync(cancellationToken);

        return items.OrderByDescending(x => x.ValuationDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new AssetValuationSnapshot(x.Id, x.ValuationDate, x.ValueMinor, x.CurrencyCode, x.Note))
            .ToArray();
    }

    public async Task UpdateAsync(
        Guid assetId,
        string name,
        decimal quantity,
        string? unit,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 120)
            throw new ArgumentException("Asset name must contain 1 to 120 characters.", nameof(name));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unit?.Length > 24 || note?.Length > 500)
            throw new ArgumentException("Asset unit or note exceeds its maximum length.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asset = await db.PhysicalAssets.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        if (asset.IsArchived)
            throw new InvalidOperationException("Archived assets cannot be edited.");

        asset.Name = normalizedName;
        asset.Quantity = quantity;
        asset.Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        asset.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddValuationAsync(
        Guid assetId,
        DateOnly valuationDate,
        long valueMinor,
        string currencyCode,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (assetId == Guid.Empty)
            throw new ArgumentException("Asset id is required.", nameof(assetId));
        if (valueMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(valueMinor));
        if (note?.Length > 300)
            throw new ArgumentException("Valuation note cannot exceed 300 characters.", nameof(note));

        var currency = NormalizeCurrency(currencyCode);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asset = await db.PhysicalAssets.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        if (asset.IsArchived)
            throw new InvalidOperationException("Archived assets cannot be revalued.");
        if (!string.Equals(asset.CurrencyCode, currency, StringComparison.Ordinal))
            throw new InvalidOperationException("Valuation currency must match the asset currency.");

        var valuation = new DbAssetValuation
        {
            AssetId = assetId,
            ValuationDate = valuationDate,
            ValueMinor = valueMinor,
            CurrencyCode = currency,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
        db.AssetValuations.Add(valuation);
        await db.SaveChangesAsync(cancellationToken);
        return valuation.Id;
    }

    public async Task SetArchivedAsync(
        Guid assetId,
        bool archived,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asset = await db.PhysicalAssets.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        asset.IsArchived = archived;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeCurrency(string? currencyCode)
    {
        var currency = currencyCode?.Trim().ToUpperInvariant();
        if (currency is null || currency.Length != 3 || currency.Any(x => x is < 'A' or > 'Z'))
            throw new ArgumentException("Currency code must contain three ASCII letters.", nameof(currencyCode));
        return currency;
    }
}
