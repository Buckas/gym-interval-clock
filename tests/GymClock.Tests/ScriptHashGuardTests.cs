using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class ScriptHashGuardTests
    {
        [Fact]
        public void SameScript_IsNotStale()
        {
            string script = "PROGRAM \"A\"\nMODE SHARED\nWORK 30";
            string baselineHash = ScriptHashGuard.ComputeHash(script);

            Assert.False(ScriptHashGuard.IsStale(baselineHash, script));
        }

        [Fact]
        public void DifferentScript_IsStale()
        {
            string baseline = "PROGRAM \"A\"\nMODE SHARED\nWORK 30";
            string current = "PROGRAM \"A\"\nMODE SHARED\nWORK 40";
            string baselineHash = ScriptHashGuard.ComputeHash(baseline);

            Assert.True(ScriptHashGuard.IsStale(baselineHash, current));
        }

        [Fact]
        public void Hash_IsStable_ForSameInput()
        {
            string script = "PROGRAM \"A\"\nMODE SHARED\nWORK 30";
            string h1 = ScriptHashGuard.ComputeHash(script);
            string h2 = ScriptHashGuard.ComputeHash(script);
            Assert.Equal(h1, h2);
        }
    }
}
