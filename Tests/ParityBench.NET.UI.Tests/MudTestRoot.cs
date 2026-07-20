using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

using MudBlazor;

namespace ParityBench.NET.UI.Tests;

public sealed class MudTestRoot : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MudDialogProvider>(1);
        builder.CloseComponent();
        builder.OpenComponent<MudSnackbarProvider>(2);
        builder.CloseComponent();
        builder.AddContent(3, ChildContent);
    }
}
