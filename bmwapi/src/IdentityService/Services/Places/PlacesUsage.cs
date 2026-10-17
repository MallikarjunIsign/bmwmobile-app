using System.Collections.Concurrent;
using IdentityService.Infrastructure;
using IdentityService.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IdentityService.Services.Places;

public enum GoogleCall { Autocomplete, Details, Reverse }

/// <summary>
/// Cost protection for Google Maps: per-user limits, the service's daily call limit, and a usage line in the log
/// for every Google call ("Google Maps calls 2026-10-16: autocomplete 12, details 3, reverse 2, total 17/5000,
/// cache hits 4"). Never logs the key, user IDs, search text or coordinates. Counters live in memory (reset on restart).
/// </summary>
public class PlacesUsage(IOptions<GoogleMapsOptions> options, TimeProvider clock, ILogger<PlacesUsage> logger) : IDisposable
{
    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private readonly int[] _calls = new int[3];
    private DateOnly _day;
    private int _cacheHits;
    private bool _limitWarned;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastWarning = new();
    private readonly MemoryCache _userCounters = new(new MemoryCacheOptions());

    private GoogleMapsOptions Opt => options.Value;

    /// <summary>
    /// Counts one request of <paramref name="bucket"/> for the user; 429 PLACES_RATE_LIMITED with a friendly message
    /// when over the per-minute or per-day limit.
    /// </summary>
    public void CheckUser(string userId, string bucket, int perMinute, int perDay)
    {
        var now = clock.GetUtcNow();
        var minute = Count($"{bucket}:{userId}:m:{now.ToUnixTimeSeconds() / 60}", TimeSpan.FromMinutes(2));
        var day = Count($"{bucket}:{userId}:d:{DateOnly.FromDateTime(now.UtcDateTime):yyyyMMdd}", TimeSpan.FromDays(1.1));
        if (day > perDay)
            throw AppException.TooMany("You've searched for places a lot today. Please try again tomorrow or pick a city from the list.",
                "PLACES_RATE_LIMITED");
        if (minute > perMinute)
            throw AppException.TooMany("You're searching very fast. Please wait a moment and try again.", "PLACES_RATE_LIMITED");
    }

    /// <summary>True (and counted) when the daily limit allows another Google call; false = use the city list.</summary>
    public bool TryStartGoogleCall(GoogleCall kind)
    {
        lock (_lock)
        {
            RollDay();
            var total = _calls.Sum();
            if (total >= Opt.DailyCallLimit)
            {
                if (!_limitWarned)
                    logger.LogWarning("Google Maps daily call limit reached ({Total}/{Limit} on {Day}) – using the built-in city list until midnight UTC",
                        total, Opt.DailyCallLimit, _day);
                _limitWarned = true;
                return false;
            }
            _calls[(int)kind]++;
            logger.LogInformation(
                "Google Maps calls {Day}: autocomplete {Autocomplete}, details {Details}, reverse {Reverse}, total {Total}/{Limit}, cache hits {CacheHits} (+1 {Kind})",
                _day, _calls[0], _calls[1], _calls[2], total + 1, Opt.DailyCallLimit, _cacheHits, kind);
            return true;
        }
    }

    public void CacheHit()
    {
        lock (_lock)
        {
            RollDay();
            _cacheHits++;
        }
    }

    /// <summary>Google is not used for this request (no key, error): a warning, at most every 5 minutes per reason.</summary>
    public void Fallback(string reason)
    {
        var now = clock.GetUtcNow();
        var last = _lastWarning.GetValueOrDefault(reason);
        if (now - last < WarnEvery) return;
        _lastWarning[reason] = now;
        logger.LogWarning("Google Maps not available ({Reason}) – using the built-in city list", reason);
    }

    /// <summary>Today's numbers (for tests and diagnostics).</summary>
    public (int Autocomplete, int Details, int Reverse, int CacheHits) Today()
    {
        lock (_lock)
        {
            RollDay();
            return (_calls[0], _calls[1], _calls[2], _cacheHits);
        }
    }

    private void RollDay()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (today == _day) return;
        _day = today;
        Array.Clear(_calls);
        _cacheHits = 0;
        _limitWarned = false;
    }

    private int Count(string key, TimeSpan keep)
    {
        var counter = _userCounters.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = keep;
            return new Counter();
        })!;
        return Interlocked.Increment(ref counter.Value);
    }

    private sealed class Counter
    {
        public int Value;
    }

    public void Dispose() => _userCounters.Dispose();
}
