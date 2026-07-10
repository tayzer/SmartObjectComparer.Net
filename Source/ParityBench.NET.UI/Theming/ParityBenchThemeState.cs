namespace ParityBench.NET.UI.Theming;

public sealed class ParityBenchThemeState
{
    public event Action? Changed;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool SystemPrefersDark { get; private set; }

    public bool IsDarkMode => Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemPrefersDark,
    };

    public void Initialize(ThemeMode? storedMode, bool systemPrefersDark)
    {
        Mode = storedMode ?? ThemeMode.System;
        SystemPrefersDark = systemPrefersDark;
        NotifyChanged();
    }

    public void SetMode(ThemeMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        NotifyChanged();
    }

    public void SetSystemPrefersDark(bool systemPrefersDark)
    {
        if (SystemPrefersDark == systemPrefersDark)
        {
            return;
        }

        SystemPrefersDark = systemPrefersDark;
        if (Mode == ThemeMode.System)
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
