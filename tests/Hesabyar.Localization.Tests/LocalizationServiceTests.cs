using System.Globalization;
using Hesabyar.Localization;

namespace Hesabyar.Localization.Tests;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData("fa-IR", LayoutDirection.RightToLeft)]
    [InlineData("en-US", LayoutDirection.LeftToRight)]
    public void FlowDirection_FollowsCurrentUiCulture(string cultureName, LayoutDirection expected)
    {
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal(expected, new CultureLocalizationService().FlowDirection);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
