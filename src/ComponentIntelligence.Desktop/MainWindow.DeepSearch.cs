using System.Windows;
using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private async void DeepSearchComponent_Click(object sender, RoutedEventArgs e)
    {
        var manufacturer = SearchManufacturerText.Text?.Trim();
        var model = SearchModelText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            MessageBox.Show(this,
                T("請輸入製造商與型號 / 料號。", "Enter Manufacturer and Model / Part Number."),
                T("深度搜尋條件不足", "Missing deep-search criteria"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SearchButton.IsEnabled = false;
        DeepSearchButton.IsEnabled = false;
        AddSearchResultButton.IsEnabled = false;
        _pendingSearchResult = null;
        _pendingSearchView = null;
        try
        {
            StatusText.Text = T(
                $"深度搜尋中：{manufacturer} {model}（重新抓原廠 / PDF / 必要的第二來源）",
                $"Deep searching: {manufacturer} {model} (refreshing manufacturer / PDF / secondary sources as needed)");
            var search = ComponentRuntimeFactory.CreateOnlineSearchService(_databasePath);
            var response = await search.SearchAsync(manufacturer, model, forceRefresh: true);
            _pendingSearchResult = response;
            _pendingSearchView = BomViewRow.FromResult(response.Query, response.Result, _uiLanguage);
            _showingSearchPreview = true;
            BomGrid.SelectedItem = null;
            DetailsText.Text = _pendingSearchView.Details;
            AddSearchResultButton.IsEnabled = true;

            var specificationCount = response.Result.Raw?.Specifications.Count ?? response.Result.Component?.Specifications.Count ?? 0;
            var documentCount = response.Result.Raw?.Documents.Count ?? response.Result.Component?.Documents.Count ?? 0;
            var evidence = response.Result.Raw?.Evidence ?? response.Result.Component?.Specifications.SelectMany(spec => spec.Evidence).ToArray() ?? [];
            var sourceCount = evidence
                .Select(item => $"{item.SourceType}|{(item.DocumentUrl ?? item.SourceUrl)?.Host ?? "local"}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            StatusText.Text = T(
                $"深度搜尋完成：{LocalizeStatus(_pendingSearchView.StatusCode, UiLanguage.Chinese)}｜規格 {specificationCount}｜文件 {documentCount}｜來源 {sourceCount}。",
                $"Deep search complete: {LocalizeStatus(_pendingSearchView.StatusCode, UiLanguage.English)} | Specs {specificationCount} | Documents {documentCount} | Sources {sourceCount}.");
        }
        catch (Exception exception)
        {
            _showingSearchPreview = false;
            StatusText.Text = T("深度搜尋失敗", "Deep search failed");
            MessageBox.Show(this,
                App.FormatException(exception),
                T("深度搜尋失敗", "Deep search failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            DeepSearchButton.IsEnabled = true;
        }
    }
}
