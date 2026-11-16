using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Domain.Accounts;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public partial class CategoriesPage : ContentPage
{
    private readonly CategoriesViewModel _viewModel;

    public CategoriesPage(CategoriesViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnAddCategoryClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.AddCategory, AppStrings.AccountNamePrompt,
            AppStrings.Save, AppStrings.Cancel, maxLength: 120);
        if (string.IsNullOrWhiteSpace(name)) return;

        var choice = await DisplayActionSheetAsync(AppStrings.CategoryTypePrompt, AppStrings.Cancel,
            null, AppStrings.ExpenseCategory, AppStrings.IncomeCategory);
        var type = choice switch
        {
            var value when value == AppStrings.ExpenseCategory => AccountType.Expense,
            var value when value == AppStrings.IncomeCategory => AccountType.Income,
            _ => (AccountType?)null
        };
        if (type is null) return;
        var currencyCode = await DisplayPromptAsync(AppStrings.CurrencyCodePrompt, AppStrings.CurrencyCodePrompt,
            AppStrings.Save, AppStrings.Skip, initialValue: "IRR", maxLength: 3);
        if (currencyCode is null) return;
        currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "IRR" : currencyCode.Trim().ToUpperInvariant();

        if (!await _viewModel.CreateAsync(name.Trim(), type.Value, currencyCode))
            await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
    }

    private async void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CategoryListItem category) return;
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;

        var action = await DisplayActionSheetAsync(category.Name, AppStrings.Cancel, null,
            AppStrings.CategoryEdit, AppStrings.CategoryArchive);
        if (action == AppStrings.CategoryEdit)
        {
            var name = await DisplayPromptAsync(AppStrings.CategoryEdit, AppStrings.AccountNamePrompt,
                AppStrings.Save, AppStrings.Cancel, initialValue: category.Name, maxLength: 120);
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name.Trim(), category.Name, StringComparison.Ordinal)) return;
            if (!await _viewModel.RenameAsync(category.Id, name.Trim()))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
        else if (action == AppStrings.CategoryArchive)
        {
            var confirmed = await DisplayAlertAsync(AppStrings.CategoryArchive,
                AppStrings.CategoryArchiveConfirmation, AppStrings.CategoryArchive, AppStrings.Cancel);
            if (confirmed && !await _viewModel.ArchiveAsync(category.Id))
                await DisplayAlertAsync(AppStrings.ValidationError, _viewModel.StatusMessage, AppStrings.Ok);
        }
    }
}
