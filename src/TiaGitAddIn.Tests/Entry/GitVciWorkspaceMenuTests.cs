using System;
using System.ComponentModel;
using TiaGitAddIn.UI;
using Xunit;

namespace TiaGitAddIn.Tests.Entry
{
    public sealed class GitVciWorkspaceMenuTests
    {
        [Fact]
        public void InvalidWindowHandleIsTreatedAsATransientOpenError()
        {
            Assert.True(WpfHostExceptions.IsTransientWindowHandleError(new Win32Exception(1400)));
        }

        [Fact]
        public void OtherFailuresStillCloseThePanel()
        {
            Assert.False(WpfHostExceptions.IsTransientWindowHandleError(new Win32Exception(5)));
            Assert.False(WpfHostExceptions.IsTransientWindowHandleError(new InvalidOperationException("Invalid window handle")));
        }
    }
}
