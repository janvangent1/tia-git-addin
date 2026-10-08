using System;
using System.ComponentModel;

namespace TiaGitAddIn.UI
{
    internal static class WpfHostExceptions
    {
        private const int InvalidWindowHandle = 1400;

        public static bool IsTransientWindowHandleError(Exception exception) =>
            exception is Win32Exception win32 && win32.NativeErrorCode == InvalidWindowHandle;
    }
}
