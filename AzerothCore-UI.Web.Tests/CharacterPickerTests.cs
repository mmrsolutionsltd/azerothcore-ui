using System.Security.Claims;
using AzerothCore_UI.Web.Components.Shared;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class CharacterPickerTests : BunitContext
{
    private static readonly IReadOnlyList<CharacterPickerItem> Items =
    [
        new("anduin", "Anduin", "Account owner", true),
        new("jaina", "Jaina", "Account owner", false),
        new("thrall", "Thrall", "Account bots", true, true)
    ];

    public CharacterPickerTests()
    {
        Services.AddSingleton<AuthenticationStateProvider>(
            new TestAuthenticationStateProvider("owner"));
        Services.AddScoped<SelectedCharacterStore>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShowsOnlyOnlineRealPlayersByDefault()
    {
        var picker = RenderPicker();

        Assert.Equal(["Anduin"], VisibleCharacterNames(picker));
        Assert.False(picker.Find("input[id$='-offline']").HasAttribute("checked"));
        Assert.False(picker.Find("input[id$='-bots']").HasAttribute("checked"));
    }

    [Fact]
    public void OfflineAndBotSwitchesRevealTheirCharacters()
    {
        var picker = RenderPicker();

        picker.Find("input[id$='-offline']").Change(true);
        Assert.Equal(["Anduin", "Jaina"], VisibleCharacterNames(picker));

        picker.Find("input[id$='-bots']").Change(true);
        Assert.Equal(["Anduin", "Thrall", "Jaina"], VisibleCharacterNames(picker));
    }

    [Fact]
    public void BotSwitchStaysDisabledWhenBotsAreNotAllowed()
    {
        var picker = RenderPicker(allowBots: false);

        Assert.True(picker.Find("input[id$='-bots']").HasAttribute("disabled"));
        picker.Find("input[id$='-offline']").Change(true);
        Assert.Equal(["Anduin", "Jaina"], VisibleCharacterNames(picker));
    }

    [Fact]
    public void SingleSelectionReturnsTheSelectedValue()
    {
        string? selectedValue = null;
        var picker = Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.RememberSelection, false)
            .Add(component => component.SelectedValueChanged,
                value => selectedValue = value));

        CharacterButton(picker, "Anduin").Click();

        Assert.Equal("anduin", selectedValue);
    }

    [Fact]
    public void DoubleClickOnlineHonoursTheMaximumAndClearRemovesEverySelection()
    {
        IReadOnlySet<string> selectedValues = new HashSet<string>();
        var picker = Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.Multiple, true)
            .Add(component => component.MaximumSelections, 2)
            .Add(component => component.ShowOfflineInitially, true)
            .Add(component => component.ShowBotsInitially, true)
            .Add(component => component.RememberSelection, false)
            .Add(component => component.SelectedValuesChanged,
                values => selectedValues = values));

        CharacterButton(picker, "Anduin").DoubleClick();
        Assert.Equal(2, selectedValues.Count);
        Assert.Contains("anduin", selectedValues);
        Assert.Contains("thrall", selectedValues);

        ButtonWithText(picker, "Clear").Click();
        Assert.Empty(selectedValues);
    }

    [Fact]
    public void SelectOnlineReplacesSelectionAndHonoursBotVisibility()
    {
        IReadOnlySet<string> selectedValues = new HashSet<string>();
        var picker = Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.Multiple, true)
            .Add(component => component.ShowOfflineInitially, true)
            .Add(component => component.RememberSelection, false)
            .Add(component => component.SelectedValues,
                new HashSet<string>(["jaina"]))
            .Add(component => component.SelectedValuesChanged,
                values => selectedValues = values));

        ButtonWithText(picker, "Online").Click();
        Assert.Equal(["anduin"], selectedValues);

        picker.Find("input[id$='-bots']").Change(true);
        ButtonWithText(picker, "Online").Click();
        Assert.Equal(
            ["anduin", "thrall"],
            selectedValues.OrderBy(value => value).ToArray());
    }

    [Fact]
    public void RightClickSelectsOnlyThatCharacter()
    {
        IReadOnlySet<string> selectedValues = new HashSet<string>();
        var picker = Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.Multiple, true)
            .Add(component => component.ShowOfflineInitially, true)
            .Add(component => component.RememberSelection, false)
            .Add(component => component.SelectedValues,
                new HashSet<string>(["anduin", "jaina"]))
            .Add(component => component.SelectedValuesChanged,
                values => selectedValues = values));

        CharacterButton(picker, "Jaina").ContextMenu();

        Assert.Equal(["jaina"], selectedValues);
    }

    [Fact]
    public async Task RestoresTheRememberedRealPlayer()
    {
        var selectedValue = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        JSInterop.Setup<string?>(
            "localStorage.getItem",
            invocation => invocation.Arguments.Count == 1).SetResult("Jaina");
        Assert.Equal(
            "Jaina",
            await Services.GetRequiredService<SelectedCharacterStore>().GetAsync());

        Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.SelectedValueChanged,
                value => { selectedValue.TrySetResult(value); }));

        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "localStorage.getItem");
        Assert.Equal(
            "jaina",
            await selectedValue.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void SelectingABotDoesNotRememberItAsTheDefaultCharacter()
    {
        string? selectedValue = null;
        var picker = Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.ShowBotsInitially, true)
            .Add(component => component.SelectedValueChanged,
                value => selectedValue = value));

        CharacterButton(picker, "Thrall").Click();

        Assert.Equal("thrall", selectedValue);
        Assert.DoesNotContain(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "localStorage.setItem");
    }

    private IRenderedComponent<CharacterPicker> RenderPicker(bool allowBots = true) =>
        Render<CharacterPicker>(parameters => parameters
            .Add(component => component.Items, Items)
            .Add(component => component.AllowBots, allowBots)
            .Add(component => component.RememberSelection, false));

    private static string[] VisibleCharacterNames(
        IRenderedComponent<CharacterPicker> picker) =>
        picker.FindAll(".character-picker-item strong")
            .Select(element => element.TextContent.Trim())
            .ToArray();

    private static AngleSharp.Dom.IElement CharacterButton(
        IRenderedComponent<CharacterPicker> picker,
        string characterName) =>
        picker.FindAll(".character-picker-item")
            .Single(button => button.TextContent.Contains(
                characterName, StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement ButtonWithText(
        IRenderedComponent<CharacterPicker> picker,
        string text) =>
        picker.FindAll("button")
            .Single(button => button.TextContent.Trim() == text);

    private sealed class TestAuthenticationStateProvider(string userId)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "Tests");
            return Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
