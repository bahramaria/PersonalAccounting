using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Localization.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.ViewModels;

public sealed class RemindersViewModel(
    IServiceScopeFactory scopeFactory,
    IDateCalendarService calendar,
    ReminderNotificationSyncService notificationSync) : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public ObservableCollection<ReminderListItemViewModel> Reminders { get; } = [];
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
            var reminders = await service.GetAsync(includeCompleted: true, cancellationToken: cancellationToken);
            await notificationSync.SyncAsync(cancellationToken);
            Reminders.Clear();
            foreach (var reminder in reminders)
                Reminders.Add(new ReminderListItemViewModel(
                    reminder.Id,
                    reminder.Title,
                    reminder.Kind,
                    calendar.Format(reminder.DueDate),
                    reminder.DueDate,
                    reminder.IsCompleted,
                    reminder.NotifyAtUtc is null ? string.Empty : reminder.NotifyAtUtc.Value.ToLocalTime().ToString("HH:mm"),
                    reminder.Note ?? string.Empty));
            StatusMessage = Reminders.Count == 0 ? AppStrings.RemindersEmpty : string.Empty;
        }
        catch
        {
            StatusMessage = AppStrings.RemindersLoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> CreateAsync(string title, string kind, DateOnly dueDate, DateTimeOffset? notifyAtUtc, string? note)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
        try
        {
            await service.CreateAsync(new CreateReminderRequest(title, kind, dueDate, notifyAtUtc, note));
            await LoadAsync();
            return true;
        }
        catch (ArgumentException)
        {
            StatusMessage = AppStrings.ReminderInvalid;
            return false;
        }
    }

    public async Task<bool> UpdateAsync(Guid id, string title, string kind, DateOnly dueDate, DateTimeOffset? notifyAtUtc, string? note)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
        try
        {
            var updated = await service.UpdateAsync(id, new UpdateReminderRequest(title, kind, dueDate, notifyAtUtc, note));
            await LoadAsync();
            return updated;
        }
        catch (ArgumentException)
        {
            StatusMessage = AppStrings.ReminderInvalid;
            return false;
        }
    }

    public async Task<bool> CompleteAsync(Guid id) => await ExecuteAsync(service => service.CompleteAsync(id));
    public async Task<bool> ReopenAsync(Guid id) => await ExecuteAsync(service => service.ReopenAsync(id));
    public async Task<bool> ArchiveAsync(Guid id)
    {
        var archived = await ExecuteAsync(service => service.ArchiveAsync(id));
        if (archived)
            await notificationSync.CancelAsync(id);
        return archived;
    }

    private async Task<bool> ExecuteAsync(Func<ReminderService, Task<bool>> action)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
        var result = await action(service);
        if (result) await LoadAsync();
        return result;
    }
}

public sealed record ReminderListItemViewModel(
    Guid Id,
    string Title,
    string Kind,
    string DueDateText,
    DateOnly DueDate,
    bool IsCompleted,
    string NotifyTimeText,
    string Note);
