using System.Globalization;

namespace Hesabyar.Localization;

public enum TextDirection
{
    LeftToRight,
    RightToLeft
}

public interface ILocalizationService
{
    TextDirection FlowDirection { get; }
}

public sealed class CultureLocalizationService : ILocalizationService
{
    public TextDirection FlowDirection =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? TextDirection.RightToLeft
            : TextDirection.LeftToRight;
}
