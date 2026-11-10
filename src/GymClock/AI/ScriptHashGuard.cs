using System;
using System.Security.Cryptography;
using System.Text;

namespace GymClock.AI
{
    public static class ScriptHashGuard
    {
        public static string ComputeHash(string script)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(script ?? string.Empty);
            using (SHA256 sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(bytes));
            }
        }

        public static bool IsStale(string baselineHash, string currentScript)
        {
            string currentHash = ComputeHash(currentScript);
            return !string.Equals(baselineHash, currentHash, StringComparison.Ordinal);
        }
    }
}
