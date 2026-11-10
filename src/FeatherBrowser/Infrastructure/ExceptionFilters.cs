namespace FeatherBrowser.Infrastructure;

internal static class ExceptionFilters
{
    public static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException
            or InsufficientExecutionStackException
            or AccessViolationException
            or StackOverflowException
            or System.Threading.ThreadAbortException
            or System.Runtime.InteropServices.SEHException);
}
