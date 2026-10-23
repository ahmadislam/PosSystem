using System.Security.Cryptography;
using System.Text;

namespace PosSystem.Api.Controllers;

public static class AuthOriginPolicy
{
    public static bool IsAllowed(string? origin, IEnumerable<string> allowedOrigins)
    {
        if (!TryParseOrigin(origin, out var requestOrigin)) return false;
        return allowedOrigins.Any(allowed =>
            TryParseOrigin(allowed, out var allowedOrigin) &&
            string.Equals(requestOrigin.GetLeftPart(UriPartial.Authority),
                allowedOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseOrigin(string? value, out Uri origin)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp) &&
            string.IsNullOrEmpty(parsed.UserInfo) &&
            parsed.AbsolutePath == "/" &&
            string.IsNullOrEmpty(parsed.Query) &&
            string.IsNullOrEmpty(parsed.Fragment))
        {
            origin = parsed;
            return true;
        }

        origin = null!;
        return false;
    }
}

public interface ILoginAttemptLimiter
{
    TimeSpan? GetRetryAfter(string? clientAddress, string username);
    void RecordFailure(string? clientAddress, string username);
    void RecordSuccess(string? clientAddress, string username);
}

public sealed class LoginAttemptLimiter : ILoginAttemptLimiter
{
    private const int AddressLimit = 20;
    private const int AddressAndUsernameLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaximumEntries = 10_000;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, WindowState> _windows = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public LoginAttemptLimiter(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public TimeSpan? GetRetryAfter(string? clientAddress, string username)
    {
        var now = _timeProvider.GetUtcNow();
        var keys = Keys(clientAddress, username).ToArray();
        lock (_sync)
        {
            Prune(now);
            var retryAfter = keys
                .Where(item => _windows.TryGetValue(item.Key, out var state) && state.Attempts >= item.Limit)
                .Select(item => Window - (now - _windows[item.Key].StartedAt))
                .Where(remaining => remaining > TimeSpan.Zero)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();
            return retryAfter > TimeSpan.Zero ? retryAfter : null;
        }
    }

    public void RecordFailure(string? clientAddress, string username)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            Prune(now);
            foreach (var (key, _) in Keys(clientAddress, username))
            {
                if (!_windows.TryGetValue(key, out var state) || now - state.StartedAt >= Window)
                    _windows[key] = new WindowState(now, 1);
                else
                    _windows[key] = state with { Attempts = state.Attempts + 1 };
            }
            TrimIfNeeded(now);
        }
    }

    public void RecordSuccess(string? clientAddress, string username)
    {
        lock (_sync)
        {
            foreach (var (key, _) in Keys(clientAddress, username)) _windows.Remove(key);
        }
    }

    private IEnumerable<(string Key, int Limit)> Keys(string? clientAddress, string username)
    {
        var address = string.IsNullOrWhiteSpace(clientAddress) ? "unknown" : clientAddress.Trim();
        var normalizedUsername = (username ?? string.Empty).Trim().ToUpperInvariant();
        yield return ("ip:" + Digest(address), AddressLimit);
        yield return ("pair:" + Digest(address + "\0" + normalizedUsername), AddressAndUsernameLimit);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _windows.Where(entry => now - entry.Value.StartedAt >= Window)
                     .Select(entry => entry.Key).ToArray())
            _windows.Remove(key);
    }

    private void TrimIfNeeded(DateTimeOffset now)
    {
        if (_windows.Count <= MaximumEntries) return;
        Prune(now);
        foreach (var key in _windows.OrderBy(entry => entry.Value.StartedAt)
                     .Take(Math.Max(0, _windows.Count - MaximumEntries)).Select(entry => entry.Key).ToArray())
            _windows.Remove(key);
    }

    private sealed record WindowState(DateTimeOffset StartedAt, int Attempts);
}
