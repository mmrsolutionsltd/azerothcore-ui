using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Shared.PlayerActions;

public abstract class PlayerActionToolBase : ComponentBase
{
    private string? targetSignature;
    private long targetRevision;

    [Inject] protected AccountsApiClient AccountsClient { get; set; } = null!;
    [Parameter, EditorRequired] public IReadOnlyList<PlayerActionTarget> Targets { get; set; } = [];
    [Parameter] public bool Available { get; set; }

    protected bool IsWorking { get; private set; }
    protected bool HasActionResult { get; private set; }
    protected bool OperationSucceeded { get; private set; }
    protected string? ResultMessage { get; private set; }
    protected IReadOnlyList<PlayerActionResult> Results { get; private set; } = [];
    protected bool CanExecute => Available && !IsWorking && Targets.Count > 0;
    protected long TargetRevision => targetRevision;
    protected bool TargetsUnchanged(long revision) => revision == targetRevision;

    protected override void OnParametersSet()
    {
        var newSignature = string.Join(
            '\u001f',
            Targets.Select(target => target.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        if (targetSignature is not null
            && !targetSignature.Equals(newSignature, StringComparison.OrdinalIgnoreCase))
        {
            ++targetRevision;
            ClearActionResult();
            OnTargetsChanged();
        }
        targetSignature = newSignature;
    }

    protected virtual void OnTargetsChanged()
    {
    }

    protected async Task RunBatchAsync(
        string action,
        Func<string, Task<AdministrationResult?>> operation)
    {
        if (!CanExecute)
            return;

        IsWorking = true;
        var revision = targetRevision;
        var operationTargets = Targets.ToArray();
        HasActionResult = false;
        ResultMessage = null;
        var results = new List<PlayerActionResult>();
        try
        {
            foreach (var target in operationTargets)
            {
                try
                {
                    var response = await operation(target.Name);
                    results.Add(new(
                        target.Name,
                        response?.Success == true,
                        response?.Message ?? "No response returned."));
                }
                catch (Exception exception)
                {
                    results.Add(new(target.Name, false, exception.Message));
                }
            }

            if (!TargetsUnchanged(revision))
                return;

            Results = results;
            var successCount = results.Count(result => result.Success);
            OperationSucceeded = successCount == results.Count;
            ResultMessage = OperationSucceeded
                ? $"{action} completed for all {successCount} selected characters."
                : $"{action} completed for {successCount} of {results.Count} selected characters.";
            HasActionResult = true;
        }
        finally
        {
            IsWorking = false;
        }
    }

    protected async Task<AdministrationResult?> RunAsync(
        Func<Task<AdministrationResult?>> operation)
    {
        if (IsWorking)
            return null;

        IsWorking = true;
        var revision = targetRevision;
        HasActionResult = false;
        ResultMessage = null;
        Results = [];
        try
        {
            var response = await operation();
            if (!TargetsUnchanged(revision))
                return response;
            OperationSucceeded = response?.Success == true;
            ResultMessage = response?.Message;
            HasActionResult = true;
            return response;
        }
        catch (Exception exception)
        {
            if (!TargetsUnchanged(revision))
                return null;
            OperationSucceeded = false;
            ResultMessage = exception.Message;
            HasActionResult = true;
            return null;
        }
        finally
        {
            IsWorking = false;
        }
    }

    protected void SetActionFailure(string message)
    {
        HasActionResult = true;
        OperationSucceeded = false;
        ResultMessage = message;
        Results = [];
    }

    private void ClearActionResult()
    {
        HasActionResult = false;
        OperationSucceeded = false;
        ResultMessage = null;
        Results = [];
    }
}
