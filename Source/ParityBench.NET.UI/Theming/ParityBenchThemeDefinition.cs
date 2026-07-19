using MudBlazor;

namespace ParityBench.NET.UI.Theming;

internal static class ParityBenchThemeDefinition
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2563eb",
            Secondary = "#0f766e",
            Tertiary = "#475569",
            AppbarBackground = "#ffffff",
            AppbarText = "#0f172a",
            Background = "#f6f8fb",
            BackgroundGray = "#eef2f7",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            TextPrimary = "#0f172a",
            TextSecondary = "#64748b",
            LinesDefault = "#d7dee8",
            TableHover = "#eff6ff",
            ActionDefault = "#475569",
            Success = "#15803d",
            Warning = "#b45309",
            Error = "#b91c1c",
            Info = "#0369a1",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7aa2ff",
            Secondary = "#5eead4",
            Tertiary = "#a8b3c7",
            AppbarBackground = "#111827",
            AppbarText = "#f8fafc",
            Background = "#0b1120",
            BackgroundGray = "#111827",
            Surface = "#172033",
            DrawerBackground = "#111827",
            TextPrimary = "#f8fafc",
            TextSecondary = "#bac6d8",
            LinesDefault = "#2b364a",
            TableHover = "#1f2a44",
            ActionDefault = "#d5deed",
            Success = "#4ade80",
            Warning = "#fbbf24",
            Error = "#f87171",
            Info = "#7dd3fc",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Segoe UI Variable", "Segoe UI", "Roboto", "Arial", "sans-serif" },
                LetterSpacing = "0",
            },
            H4 = new H4Typography
            {
                FontWeight = "650",
                LetterSpacing = "0",
            },
            H5 = new H5Typography
            {
                FontWeight = "650",
                LetterSpacing = "0",
            },
            H6 = new H6Typography
            {
                FontWeight = "650",
                LetterSpacing = "0",
            },
        },
    };
}
