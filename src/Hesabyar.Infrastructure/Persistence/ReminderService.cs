using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class ReminderService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ReminderListItem>> GetAsync(
        bool includeCompleted = false,
        DateOnly? throughDate = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Reminders.AsNoTracking();
        if (!includeCompleted)
            query = query.Where(x => !x.IsCompleted);
        if (throughDate is not null)
            query = query.Where(x => x.DueDate <= throughDate.Value);

        return await query
            .OrderBy(x => x.IsCompleted)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Title)
            .Select(x => new ReminderListItem(
                x.Id, x.Title, x.Kind, x.DueDate, x.NotifyAtUtc,
                x.IsCompleted, x.CompletedAtUtc, x.Note))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CreateAsync(
        CreateReminderRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.Title, request.Kind, request.Note);
        var reminder = new DbReminder
        {
            Title = request.Title.Trim(),
            Kind = request.Kind.Trim(),
            DueDate = request.DueDate,
            NotifyAtUtc = request.NotifyAtUtc?.ToUniversalTime(),
            Note = NormalizeNote(request.Note)
        };

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(cancellationToken);
        return reminder.Id;
    }

    public async Task<bool> UpdateAsync(
        Guid id,
        UpdateReminderRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.Title, request.Kind, request.Note);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reminder is null || reminder.IsCompleted)
            return false;

        reminder.Title = request.Title.Trim();
        reminder.Kind = request.Kind.Trim();
        reminder.DueDate = request.DueDate;
        reminder.NotifyAtUtc = request.NotifyAtUtc?.ToUniversalTime();
        reminder.Note = NormalizeNote(request.Note);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reminder is null)
            return false;
        if (reminder.IsCompleted)
            return true;

        reminder.IsCompleted = true;
        reminder.CompletedAtUtc = timeProvider.GetUtcNow();
        reminder.NotifyAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReopenAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reminder is null)
            return false;

        reminder.IsCompleted = false;
        reminder.CompletedAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ArchiveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reminder is null)
            return false;

        reminder.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Validate(string title, string kind, string? note)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160)
            throw new ArgumentException("Reminder title must contain 1 to 160 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().Length > 24)
            throw new ArgumentException("Reminder kind must contain 1 to 24 characters.", nameof(kind));
        if (note?.Length > 500)
            throw new ArgumentException("Reminder note cannot exceed 500 characters.", nameof(note));
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public sealed record CreateReminderRequest(
    string Title,
    string Kind,
    DateOnly DueDate,
    DateTimeOffset? NotifyAtUtc = null,
    string? Note = null);

public sealed record UpdateReminderRequest(
    string Title,
    string Kind,
    DateOnly DueDate,
    DateTimeOffset? NotifyAtUtc = null,
    string? Note = null);

public sealed record ReminderListItem(
    Guid Id,
    string Title,
    string Kind,
    DateOnly DueDate,
    DateTimeOffset? NotifyAtUtc,
    bool IsCompleted,
    DateTimeOffset? CompletedAtUtc,
    string? Note);
