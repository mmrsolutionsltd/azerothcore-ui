using AzerothCore_UI.Web.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class SlideOverPanelTests : BunitContext
{
    public SlideOverPanelTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ClickingTheCloseButtonInvokesOnClose()
    {
        var closed = false;
        var component = Render<SlideOverPanel>(parameters => parameters
            .Add(panel => panel.OnClose, () => closed = true)
            .AddChildContent("<p>Tool content</p>"));

        component.Find(".tool-drawer-close").Click();

        Assert.True(closed);
    }

    [Fact]
    public void PressingEscapeInsideThePanelInvokesOnClose()
    {
        var closed = false;
        var component = Render<SlideOverPanel>(parameters => parameters
            .Add(panel => panel.OnClose, () => closed = true)
            .AddChildContent("<p>Tool content</p>"));

        component.Find(".tool-drawer").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
    }

    [Fact]
    public void OtherKeysDoNotInvokeOnClose()
    {
        var closed = false;
        var component = Render<SlideOverPanel>(parameters => parameters
            .Add(panel => panel.OnClose, () => closed = true)
            .AddChildContent("<p>Tool content</p>"));

        component.Find(".tool-drawer").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.False(closed);
    }

    [Fact]
    public void RendersTheProvidedChildContent()
    {
        var component = Render<SlideOverPanel>(parameters => parameters
            .Add(panel => panel.OnClose, () => { })
            .AddChildContent("<p>Tool content</p>"));

        Assert.Contains("Tool content", component.Markup);
    }
}
