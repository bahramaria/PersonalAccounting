using Android;
using Microsoft.Maui.ApplicationModel;

namespace Hesabyar.App;

public sealed class ReadSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Manifest.Permission.ReadSms, true)];
}
