using System;
using MySqlBackupAgent;
using Xunit;

namespace AgentTests
{
    public class ViewHelperTests
    {
        [Fact]
        public void TestPositiveShortTimeSpan()
        {
            var span = TimeSpan.FromSeconds(3);
            Assert.Equal("In a few seconds", span.ToHumanReadable());
        }
        
        [Fact]
        public void TestNegativeShortTimeSpan()
        {
            var span = -TimeSpan.FromSeconds(3);
            Assert.Equal("a few seconds ago", span.ToHumanReadable());
        }
        
        [Fact]
        public void TestPositiveHours()
        {
            var span = TimeSpan.FromHours(3);
            Assert.Equal("In 3 hours", span.ToHumanReadable());
        }
        
        [Fact]
        public void TestNegativeHours()
        {
            var span = -TimeSpan.FromHours(3);
            Assert.Equal("3 hours ago", span.ToHumanReadable());
        }
    }
}