using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components;

public partial class MoneyDisplay
{
    [Parameter]
    public uint Copper { get; set; }

    private uint Gold => Copper / 10_000;
    private uint Silver => Copper % 10_000 / 100;
    private uint CopperRemainder => Copper % 100;

    private string AccessibleValue =>
        $"{Gold} gold, {Silver} silver, {CopperRemainder} copper";
}
