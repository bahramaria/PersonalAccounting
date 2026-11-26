using Hesabyar.Application.Common;

namespace Hesabyar.Infrastructure.Time;

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
