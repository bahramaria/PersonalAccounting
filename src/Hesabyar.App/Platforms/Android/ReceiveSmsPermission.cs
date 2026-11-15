using Android;
using Microsoft.Maui.ApplicationModel;

namespace Hesabyar.App;

public sealed class ReceiveSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Manifest.Permission.ReceiveSms, true)];
}
