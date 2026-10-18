using TiaGitAddIn.Services.Hmi;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class EngineeringSnapshotRestorerTests
    {
        private enum Sample
        {
            Off = 0,
            On = 1
        }

        [Fact]
        public void CoerceKeepsTheCurrentType()
        {
            Assert.True(EngineeringSnapshotRestorer.TryCoerce(false, "true", out object flag));
            Assert.Equal(true, flag);

            Assert.True(EngineeringSnapshotRestorer.TryCoerce(0, "12", out object number));
            Assert.Equal(12, number);

            Assert.True(EngineeringSnapshotRestorer.TryCoerce(Sample.Off, "On", out object choice));
            Assert.Equal(Sample.On, choice);

            Assert.True(EngineeringSnapshotRestorer.TryCoerce("Old", "Start", out object text));
            Assert.Equal("Start", text);
        }
    }
}
