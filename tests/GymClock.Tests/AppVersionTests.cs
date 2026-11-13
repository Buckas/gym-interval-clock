using Xunit;

namespace GymClock.Tests
{
    public class AppVersionTests
    {
        [Theory]
        [InlineData("3.1.0", "3.1")]
        [InlineData("3.1.2", "3.1.2")]
        [InlineData("4.0.0", "4.0")]
        [InlineData("not a version", "not a version")]
        public void TitleVersion_DropsAZeroPatchNumber(string version, string expected)
        {
            Assert.Equal(expected, AppVersion.Format(version));
        }

        [Fact]
        public void Version_ComesFromTheProjectFile()
        {
            // <Version> in GymClock.csproj, e.g. 3.1.0 -> "Version 3.1" in the window titles.
            System.Version project = typeof(AppVersion).Assembly.GetName().Version;
            Assert.Equal(project.ToString(3), AppVersion.Full);
            Assert.Equal(AppVersion.Format(project.ToString(3)), AppVersion.Display);
            Assert.DoesNotContain("+", AppVersion.Full);
        }
    }
}
