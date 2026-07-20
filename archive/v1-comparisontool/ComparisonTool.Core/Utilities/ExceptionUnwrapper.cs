using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Provides helper methods for unwrapping reflection-induced exception wrappers
/// such as <see cref="TargetInvocationException"/> so that callers see the real
/// error instead of a generic "Exception has been thrown by the target of an invocation" message.
/// </summary>
public static class ExceptionUnwrapper
{
    /// <summary>
    /// Invokes a <see cref="MethodInfo"/> and rethrows any
    /// <see cref="TargetInvocationException"/> as its inner exception,
    /// preserving the original stack trace.
    /// </summary>
    public static object? InvokeUnwrapped(this MethodInfo method, object? target, params object?[] parameters)
    {
        try
        {
            return method.Invoke(target, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // Unreachable but required by compiler
        }
    }

    /// <summary>
    /// Unwraps a <see cref="TargetInvocationException"/> to its innermost
    /// meaningful exception. Returns the original exception if it is not a
    /// <see cref="TargetInvocationException"/>.
    /// </summary>
    public static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException tie && tie.InnerException is not null)
        {
            ex = tie.InnerException;
        }

        return ex;
    }

    /// <summary>
    /// Builds a user-friendly error message from an exception, unwrapping
    /// <see cref="TargetInvocationException"/> and including the inner
    /// exception detail when present.
    /// </summary>
    public static string GetDetailedMessage(Exception ex)
    {
        var unwrapped = Unwrap(ex);

        if (unwrapped.InnerException is not null)
        {
            return $"{unwrapped.Message} → {unwrapped.InnerException.Message}";
        }

        return unwrapped.Message;
    }
}