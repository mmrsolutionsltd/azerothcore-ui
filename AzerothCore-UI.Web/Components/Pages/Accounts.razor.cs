using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class Accounts
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "search")]
    public string? Search { get; set; }

    [Parameter, SupplyParameterFromQuery(Name = "type")]
    public string Type { get; set; } = "human";

    [Parameter, SupplyParameterFromQuery(Name = "sort")]
    public string Sort { get; set; } = "username";

    [Parameter, SupplyParameterFromQuery(Name = "descending")]
    public bool? DescendingQuery { get; set; }

    [Parameter, SupplyParameterFromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [Parameter, SupplyParameterFromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = 25;

    private PagedAccounts result = new([], 1, 25, 0, 0);
    private string? searchInput;
    private string typeInput = "human";
    private string sortInput = "username";
    private string directionInput = "ascending";
    private bool isLoading = true;
    private string? errorMessage;
    private string? gmMessage;
    private uint? pendingGmAccountId;
    private bool isChangingGm, gmSucceeded;

    protected override async Task OnParametersSetAsync()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Type = Type is "human" or "playerbot" or "all" ? Type : "human";
        Sort = Sort is "username" or "accountId" or "lastLogin" or "characterCount" ? Sort : "username";
        Page = Math.Max(Page, 1);
        PageSize = PageSize is 10 or 25 or 50 ? PageSize : 25;

        searchInput = Search;
        typeInput = Type;
        sortInput = Sort;
        directionInput = Descending ? "descending" : "ascending";
        isLoading = true;
        errorMessage = null;

        try
        {
            result = await AccountsClient.GetAccountsAsync(
                Search, Type, Sort, Descending, Page, PageSize);

            if (result.TotalPages > 0 && Page > result.TotalPages)
            {
                NavigateToPage(result.TotalPages);
            }
        }
        catch (HttpRequestException)
        {
            errorMessage = "The accounts API could not be reached.";
        }
        catch (NotSupportedException)
        {
            errorMessage = "The accounts API returned an unsupported response.";
        }
        catch (System.Text.Json.JsonException)
        {
            errorMessage = "The accounts API returned an invalid response.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private bool Descending => DescendingQuery ?? false;

    private void ApplyFilters() => Navigate(
        searchInput,
        typeInput,
        sortInput,
        directionInput == "descending",
        1,
        PageSize);

    private void NavigateToPage(int page) => Navigate(Search, Type, Sort, Descending, page, PageSize);

    private void ChangePageSize(ChangeEventArgs args)
    {
        var pageSize = int.TryParse(args.Value?.ToString(), out var value) ? value : 25;
        Navigate(Search, Type, Sort, Descending, 1, pageSize);
    }

    private void Navigate(string? search, string type, string sort, bool descending, int page, int pageSize)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["search"] = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            ["type"] = type,
            ["sort"] = sort,
            ["descending"] = descending ? true : null,
            ["page"] = page > 1 ? page : null,
            ["pageSize"] = pageSize != 25 ? pageSize : null
        };

        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters("/accounts", parameters));
    }

    private string GetResultSummary()
    {
        var first = (result.Page - 1) * result.PageSize + 1;
        var last = Math.Min(result.Page * result.PageSize, result.TotalItems);
        return $"Showing {first}–{last} of {result.TotalItems}";
    }

    private static string FormatLastLogin(DateTime? lastLogin) =>
        lastLogin?.ToString("dd MMM yyyy HH:mm") ?? "Never";

    private async Task ToggleGmAsync(AccountSummary account)
    {
        if (pendingGmAccountId != account.AccountId)
        {
            pendingGmAccountId = account.AccountId;
            gmMessage = $"Click again to confirm changing GM access for {account.Username}.";
            gmSucceeded = false;
            return;
        }

        isChangingGm = true;
        try
        {
            var enabled = account.GmLevel < 2;
            var response = await AccountsClient.SetAccountGmAsync(new(account.Username, enabled, true));
            gmSucceeded = response?.Success == true;
            gmMessage = response?.Message;
            result = result with { Items = result.Items.Select(item => item.AccountId == account.AccountId
                ? item with { GmLevel = enabled ? (byte)2 : (byte)0 } : item).ToArray() };
        }
        catch (Exception exception) { gmSucceeded = false; gmMessage = exception.Message; }
        finally { pendingGmAccountId = null; isChangingGm = false; }
    }
}
