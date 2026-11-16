using Hesabyar.App.Services;
using Hesabyar.App.Resources.Strings;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hesabyar.App;

public sealed class WindowsReminderNotificationScheduler : IReminderNotificationScheduler
{
    private const string AppUserModelId = "com.example.hesabyar";
    private static readonly Guid AppUserModelIdKey = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public Task ScheduleAsync(Guid reminderId, string title, DateTimeOffset notifyAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStartMenuShortcut();
        var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
        RemoveExisting(notifier, reminderId);
        if (notifyAtUtc <= DateTimeOffset.UtcNow)
            return Task.CompletedTask;

        var escapedTitle = System.Security.SecurityElement.Escape(title) ?? "Hesabyar";
        var xml = new XmlDocument();
        xml.LoadXml($"<toast><visual><binding template='ToastGeneric'><text>{escapedTitle}</text><text>{System.Security.SecurityElement.Escape(AppStrings.ReminderNotificationBody)}</text></binding></visual></toast>");
        var scheduled = new ScheduledToastNotification(xml, notifyAtUtc.ToLocalTime())
        {
            Tag = reminderId.ToString("N"),
            Group = "reminders"
        };
        notifier.AddToSchedule(scheduled);
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStartMenuShortcut();
        RemoveExisting(ToastNotificationManager.CreateToastNotifier(AppUserModelId), reminderId);
        return Task.CompletedTask;
    }

    private static void RemoveExisting(ToastNotifier notifier, Guid reminderId)
    {
        var tag = reminderId.ToString("N");
        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            if (scheduled.Tag == tag && scheduled.Group == "reminders")
                notifier.RemoveFromSchedule(scheduled);
        }
    }

    private static void EnsureStartMenuShortcut()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;

        var programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(programs);
        var shortcutPath = Path.Combine(programs, "Hesabyar.lnk");

        var link = (IShellLinkW)(object)new ShellLink();
        try
        {
            link.SetPath(executable);
            link.SetDescription("Hesabyar");
            link.SetWorkingDirectory(Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory);

            var propertyStore = (IPropertyStore)link;
            var key = new PropertyKey(AppUserModelIdKey, 5);
            var value = new PropVariant
            {
                VariantType = 31,
                PointerValue = Marshal.StringToCoTaskMemUni(AppUserModelId)
            };
            try
            {
                propertyStore.SetValue(ref key, ref value);
                propertyStore.Commit();
                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.PointerValue);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }
}
