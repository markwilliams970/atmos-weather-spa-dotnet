using System.Security.Cryptography;
using System.Text;

namespace Atmos.Web.Infrastructure;

/// <summary>
/// A one-way, truncated correlator for session-scoped log lines. CLAUDE.md
/// §14 says not to log session identifiers without a compelling reason — this
/// gives enough correlation power to trace a user's actions through the logs
/// (and, once distributed tracing is added, across services) without ever
/// writing the actual session cookie value anywhere.
/// </summary>
public static class SessionLogging
{
    public static string Correlator(string sessionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..12];
}
