using System.Runtime.CompilerServices;

namespace Hesabyar.Infrastructure.Tests;

internal static class SqliteProviderBootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}
