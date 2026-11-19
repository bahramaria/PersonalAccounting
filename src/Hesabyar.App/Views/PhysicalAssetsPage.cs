using System.Globalization;
using Hesabyar.App.Resources.Strings;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public sealed class PhysicalAssetsPage : ContentPage
{
    private readonly PhysicalAssetService _service;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly Label _status = new() { FontSize = 13 };

    public PhysicalAssetsPage(PhysicalAssetService service, ILocalizationService localization)
    {
        _service = service;
        Title = AppStrings.AssetsHeading;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        var add = new Button { Text = AppStrings.AssetsAdd };
        add.Clicked += OnAddClicked;
        var content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 14,
            Children =
            {
                new Label { Text = AppStrings.AssetsHeading, FontSize = 24, FontAttributes = FontAttributes.Bold },
                add,
                _status,
                _items
            }
        };
        Content = new ScrollView { Content = content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var assets = await _service.GetAsync();
            _items.Clear();
            foreach (var asset in assets)
            {
                var value = asset.CurrentValueMinor is null
                    ? "—"
                    : MoneyDisplayService.Format(asset.CurrentValueMinor.Value, asset.CurrencyCode);
                var gain = asset.UnrealizedGainMinor is null
                    ? string.Empty
                    : $"{AppStrings.AssetGainLabel}: {MoneyDisplayService.Format(asset.UnrealizedGainMinor.Value, asset.CurrencyCode)}";
                var card = new Border
                {
                    Padding = 14,
                    StrokeThickness = 1,
                    Stroke = new SolidColorBrush(Colors.Gray),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            new Label { Text = asset.Name, FontAttributes = FontAttributes.Bold, FontSize = 17 },
                            new Label { Text = $"{asset.Type} · {asset.Quantity.ToString(CultureInfo.InvariantCulture)} {asset.Unit}" },
                            new Label { Text = value, FontAttributes = FontAttributes.Bold },
                            new Label { Text = gain, FontSize = 12 }
                        }
                    }
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await OnAssetTappedAsync(asset);
                card.GestureRecognizers.Add(tap);
                _items.Add(card);
            }
            _status.Text = assets.Count == 0 ? AppStrings.AssetsEmpty : string.Empty;
        }
        catch
        {
            _status.Text = AppStrings.AssetSaveFailed;
        }
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.AssetsAdd, AppStrings.AssetNamePrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 120);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var choices = Enum.GetValues<PhysicalAssetType>();
        var choice = await DisplayActionSheetAsync(AppStrings.AssetTypePrompt, AppStrings.Cancel, null,
            choices.Select(x => x.ToString()).ToArray());
        if (!Enum.TryParse<PhysicalAssetType>(choice, out var type))
            return;

        var quantityText = await DisplayPromptAsync(AppStrings.AssetQuantityPrompt, AppStrings.AssetQuantityPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: "1", keyboard: Keyboard.Numeric);
        if (!decimal.TryParse(NormalizeDigits(quantityText ?? string.Empty), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetQuantityPrompt, AppStrings.Ok);
            return;
        }

        var unit = await DisplayPromptAsync(AppStrings.AssetUnitPrompt, AppStrings.AssetUnitPrompt,
            AppStrings.Save, AppStrings.Skip, maxLength: 24);
        var currency = await DisplayPromptAsync(AppStrings.AssetCurrencyPrompt, AppStrings.AssetCurrencyPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: "IRR", maxLength: 3);
        if (string.IsNullOrWhiteSpace(currency))
            return;
        currency = currency.Trim().ToUpperInvariant();

        var costText = await DisplayPromptAsync(AppStrings.AssetCostPrompt, AppStrings.AssetCostPrompt,
            AppStrings.Save, AppStrings.Cancel, initialValue: "0", keyboard: Keyboard.Numeric);
        if (!long.TryParse(NormalizeDigits(costText ?? string.Empty), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var cost) || cost < 0)
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetCostPrompt, AppStrings.Ok);
            return;
        }

        try
        {
            await _service.CreateAsync(new CreatePhysicalAssetRequest(
                name.Trim(), type, quantity, unit, currency, DateOnly.FromDateTime(DateTime.Today), cost, null));
            await LoadAsync();
        }
        catch
        {
            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetSaveFailed, AppStrings.Ok);
        }
    }

    private async Task OnAssetTappedAsync(PhysicalAssetSnapshot asset)
    {
        var action = await DisplayActionSheetAsync(asset.Name, AppStrings.Cancel, null,
            AppStrings.AssetValuationAdd, AppStrings.AssetArchive);
        if (action == AppStrings.AssetValuationAdd)
        {
            var amountText = await DisplayPromptAsync(AppStrings.AssetValuationAdd,
                $"{AppStrings.AssetValuationPrompt} ({asset.CurrencyCode})", AppStrings.Save,
                AppStrings.Cancel, keyboard: Keyboard.Numeric);
            if (!long.TryParse(NormalizeDigits(amountText ?? string.Empty), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var amount) || amount < 0)
                return;
            try
            {
                await _service.AddValuationAsync(asset.Id, DateOnly.FromDateTime(DateTime.Today),
                    amount, asset.CurrencyCode);
                await LoadAsync();
            }
            catch
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetSaveFailed, AppStrings.Ok);
            }
        }
        else if (action == AppStrings.AssetArchive)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.AssetArchive, asset.Name,
                AppStrings.AssetArchive, AppStrings.Cancel);
            if (confirmed)
            {
                await _service.SetArchivedAsync(asset.Id, true);
                await LoadAsync();
            }
        }
    }

    private static string NormalizeDigits(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => character
            });
        return builder.ToString();
    }
}
