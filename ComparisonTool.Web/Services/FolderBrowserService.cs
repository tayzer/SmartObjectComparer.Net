// <copyright file="FolderBrowserService.cs" company="PlaceholderCompany">



using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Service that provides folder browsing capabilities for the desktop environment.
/// </summary>
public static class FolderBrowserService
{
    /// <summary>
    /// Opens a native folder browser dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [JSInvokable]
    public static async Task<string?> BrowseFolderAsync(string title)
    {
        // Only available in desktop environments
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return null;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseFolderWindowsAsync(title).ConfigureAwait(false);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseFolderMacAsync(title).ConfigureAwait(false);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await BrowseFolderLinuxAsync(title).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error browsing for folder: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Checks if the application is running in a desktop environment.
    /// </summary>
    /// <returns></returns>
    [JSInvokable]
    public static bool IsDesktopEnvironment()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
               RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
               RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    /// <summary>
    /// Opens a Windows folder browser dialog.
    /// </summary>
    private static Task<string?> BrowseFolderWindowsAsync(string title)
    {
        // Use System.Windows.Forms for Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                // Create and configure a folder browser dialog
                var folderBrowser = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = title,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                };

                // Show the dialog
                var result = folderBrowser.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    return Task.FromResult<string?>(folderBrowser.SelectedPath);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error showing Windows folder browser: {ex.Message}");
            }
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Opens a macOS folder browser dialog.
    /// </summary>
    private static async Task<string?> BrowseFolderMacAsync(string title)
    {
        // Fallback to command line for macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                // Use NSOpenPanel through AppleScript on macOS
                var scriptArguments = $"-e 'tell application \"System Events\"' " +
                                    $"-e 'set folderPath to choose folder with prompt \"{title}\"' " +
                                    $"-e 'set posixPath to POSIX path of folderPath' " +
                                    $"-e 'return posixPath' " +
                                    $"-e 'end tell'";

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = scriptArguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    await process.WaitForExitAsync().ConfigureAwait(false);

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error showing macOS folder browser: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Opens a Linux folder browser dialog.
    /// </summary>
    private static async Task<string?> BrowseFolderLinuxAsync(string title)
    {
        // Fallback to command line for Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // Try to use zenity for folder selection
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--file-selection --directory --title=\"{title}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    await process.WaitForExitAsync().ConfigureAwait(false);

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error showing Linux folder browser: {ex.Message}");
            }
        }

        return null;
    }
}
