using System.Globalization;

namespace Hesabyar.Localization;

public enum LayoutDirection
{
    LeftToRight,
    RightToLeft
}

public interface ILocalizationService
{
    LayoutDirection FlowDirection { get; }
}

public sealed class CultureLocalizationService : ILocalizationService
{
    public LayoutDirection FlowDirection =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? LayoutDirection.RightToLeft
            : LayoutDirection.LeftToRight;
}
