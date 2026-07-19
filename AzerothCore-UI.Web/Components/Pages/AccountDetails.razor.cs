using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages
{
    public partial class AccountDetails
    {
        [Parameter] public long AccountId { get; set; }

        private AccountWithCharacters? account;
        private bool isLoading = true;
        private string? errorMessage;

        protected override async Task OnParametersSetAsync()
        {
            isLoading = true;
            errorMessage = null;

            if (AccountId is < 0 or > uint.MaxValue)
            {
                errorMessage = "The account ID is invalid.";
                isLoading = false;
                return;
            }

            try
            {
                account = await AccountsClient.GetAccountAsync((uint)AccountId);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                errorMessage = "The account was not found.";
            }
            catch (HttpRequestException)
            {
                errorMessage = "The account API could not be reached.";
            }
            finally
            {
                isLoading = false;
            }
        }
    }
}
