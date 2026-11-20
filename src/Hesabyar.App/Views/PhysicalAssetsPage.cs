using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using System.Globalization;
using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;

namespace Hesabyar.App.Views;

public sealed class PhysicalAssetsPage : ContentPage
{
    private readonly PhysicalAssetService _service;
    private readonly DocumentAttachmentService _attachments;
    private readonly IDateCalendarService _calendar;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly Label _status = new() { FontSize = 13 };

    public PhysicalAssetsPage(PhysicalAssetService service, DocumentAttachmentService attachments, ILocalizationService localization, IDateCalendarService calendar)
    {
        _service = service;
        _attachments = attachments;
        _calendar = calendar;
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
                            new Label { Text = $"{GetTypeName(asset.Type)} · {asset.Quantity.ToString(CultureInfo.InvariantCulture)} {asset.Unit}" },
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
        var labels = choices.Select(GetTypeName).ToArray();
        var choice = await DisplayActionSheetAsync(AppStrings.AssetTypePrompt, AppStrings.Cancel, null, labels);
        var type = choices.FirstOrDefault(x => GetTypeName(x) == choice);
        if (!labels.Contains(choice, StringComparer.Ordinal))
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
        var actions = asset.IsArchived
            ? new[] { AppStrings.AssetHistory, AppStrings.AssetAttachments }
            : new[] { AppStrings.AssetEdit, AppStrings.AssetValuationAdd, AppStrings.AssetHistory, AppStrings.AssetAttachments, AppStrings.AssetArchive };
        var action = await DisplayActionSheetAsync(asset.Name, AppStrings.Cancel, null, actions);

        if (action == AppStrings.AssetEdit)
        {
            var name = await DisplayPromptAsync(AppStrings.AssetEdit, AppStrings.AssetNamePrompt,
                AppStrings.Save, AppStrings.Cancel, initialValue: asset.Name, maxLength: 120);
            if (string.IsNullOrWhiteSpace(name)) return;

            var quantityText = await DisplayPromptAsync(AppStrings.AssetQuantityPrompt, AppStrings.AssetQuantityPrompt,
                AppStrings.Save, AppStrings.Cancel,
                initialValue: asset.Quantity.ToString(CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
            if (!decimal.TryParse(NormalizeDigits(quantityText ?? string.Empty), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
                return;

            var unit = await DisplayPromptAsync(AppStrings.AssetUnitPrompt, AppStrings.AssetUnitPrompt,
                AppStrings.Save, AppStrings.Skip, initialValue: asset.Unit, maxLength: 24);
            var note = await DisplayPromptAsync(AppStrings.AssetNotePrompt, AppStrings.AssetNotePrompt,
                AppStrings.Save, AppStrings.Skip, initialValue: asset.Note, maxLength: 500);
            try
            {
                await _service.UpdateAsync(asset.Id, name, quantity, unit, note);
                await LoadAsync();
            }
            catch
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetSaveFailed, AppStrings.Ok);
            }
        }
        else if (action == AppStrings.AssetValuationAdd)
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
        else if (action == AppStrings.AssetHistory)
        {
            try
            {
                var history = await _service.GetValuationsAsync(asset.Id);
                var message = history.Count == 0
                    ? AppStrings.AssetHistoryEmpty
                    : string.Join(Environment.NewLine, history.Select(item =>
                        $"{_calendar.Format(item.ValuationDate)}  —  {MoneyDisplayService.Format(item.ValueMinor, item.CurrencyCode)}"));
                await DisplayAlertAsync(AppStrings.AssetHistory, message, AppStrings.Ok);
            }
            catch
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AssetSaveFailed, AppStrings.Ok);
            }
        }
        else if (action == AppStrings.AssetAttachments)
        {
            await ManageAttachmentsAsync(asset.Id);
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


    private async Task ManageAttachmentsAsync(Guid assetId)
    {
        while (true)
        {
            try
            {
                var items = await _attachments.GetAsync(physicalAssetId: assetId);
                var labelsById = items.ToDictionary(
                    x => x.Id,
                    x => $"{x.FileName} · {x.Id.ToString("N")[..4]}");
                var choices = new[] { AppStrings.AttachmentAdd }.Concat(labelsById.Values).ToArray();
                var selected = await DisplayActionSheetAsync(AppStrings.Attachments, AppStrings.Cancel, null, choices);
                if (selected == AppStrings.Cancel || selected is null) return;
                if (selected == AppStrings.AttachmentAdd)
                {
                    var addChoice = await DisplayActionSheetAsync(AppStrings.AttachmentAdd, AppStrings.Cancel, null,
                        AppStrings.AttachmentChooseFile, AppStrings.AttachmentCapturePhoto);
                    if (addChoice == AppStrings.AttachmentChooseFile)
                    {
                        var file = await FilePicker.Default.PickAsync();
                        if (file is null) continue;
                        await using var input = await file.OpenReadAsync();
                        await _attachments.AddAsync(input, file.FileName, assetId: assetId);
                    }
                    else if (addChoice == AppStrings.AttachmentCapturePhoto)
                    {
                        try
                        {
                            var photo = await MediaPicker.Default.CapturePhotoAsync();
                            if (photo is null) continue;
                            await using var input = await photo.OpenReadAsync();
                            await _attachments.AddAsync(input, photo.FileName, assetId: assetId);
                        }
                        catch (FeatureNotSupportedException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                        catch (FeatureNotEnabledException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                        catch (PermissionException)
                        {
                            await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentCameraUnavailable, AppStrings.Ok);
                        }
                    }
                    continue;
                }

                var item = items.FirstOrDefault(x => labelsById[x.Id] == selected);
                if (item is null) continue;
                var action = await DisplayActionSheetAsync(item.FileName, AppStrings.Cancel, null,
                    AppStrings.AttachmentOpen, AppStrings.AttachmentDelete);
                if (action == AppStrings.AttachmentOpen)
                {
                    var bytes = await _attachments.ReadAsync(item.Id);
                    var extension = Path.GetExtension(item.FileName);
                    var path = Path.Combine(FileSystem.CacheDirectory, $"attachment-{item.Id:N}{extension}");
                    await File.WriteAllBytesAsync(path, bytes);
                    await Launcher.Default.OpenAsync(new OpenFileRequest(item.FileName, new ReadOnlyFile(path)));
                }
                else if (action == AppStrings.AttachmentDelete)
                {
                    var confirmed = await DisplayAlertAsync(AppStrings.AttachmentDelete, item.FileName,
                        AppStrings.AttachmentDelete, AppStrings.Cancel);
                    if (confirmed) await _attachments.DeleteAsync(item.Id);
                }
            }
            catch
            {
                await DisplayAlertAsync(AppStrings.ValidationError, AppStrings.AttachmentFailed, AppStrings.Ok);
                return;
            }
        }
    }

    private static string GetTypeName(PhysicalAssetType type) => type switch
    {
        PhysicalAssetType.Gold => AppStrings.AssetTypeGold,
        PhysicalAssetType.ForeignCurrency => AppStrings.AssetTypeForeignCurrency,
        PhysicalAssetType.Vehicle => AppStrings.AssetTypeVehicle,
        PhysicalAssetType.Property => AppStrings.AssetTypeProperty,
        PhysicalAssetType.Equipment => AppStrings.AssetTypeEquipment,
        _ => AppStrings.AssetTypeOther
    };

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
