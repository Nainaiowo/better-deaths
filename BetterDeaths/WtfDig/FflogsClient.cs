using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal sealed partial class FflogsClient : IWtfDigEventSource, IDisposable
{
    private static readonly Uri DefaultEndpoint = new("https://wtfdig-analyzer.mczub.workers.dev/api/fflogs");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly bool ownsClient;

    public FflogsClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(45) }, DefaultEndpoint, ownsClient: true)
    {
    }

    internal FflogsClient(HttpClient httpClient, Uri endpoint, bool ownsClient = false)
    {
        this.httpClient = httpClient;
        this.endpoint = endpoint;
        this.ownsClient = ownsClient;
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BetterDeaths/FFLogsAnalyzer");
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Better-Deaths-Client", "native-analyzer");
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    internal static FflogsReportInput? ParseReportInput(string input)
    {
        var value = input.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (BareReportCodeRegex().IsMatch(value))
        {
            return new FflogsReportInput(value, null, false);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var match = ReportPathRegex().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return null;
        }

        var fightValue = FindParameter(uri.Query, "fight") ?? FindParameter(uri.Fragment, "fight");
        if (string.Equals(fightValue, "last", StringComparison.OrdinalIgnoreCase))
        {
            return new FflogsReportInput(match.Groups[1].Value, null, true);
        }

        return int.TryParse(fightValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fightId)
            ? new FflogsReportInput(match.Groups[1].Value, fightId, false)
            : new FflogsReportInput(match.Groups[1].Value, null, false);
    }

    internal async Task<FflogsReportSummary> FetchReportSummaryAsync(string code, CancellationToken cancellationToken)
    {
        var data = await CallAsync<ReportSummaryData>(
            "reportSummary",
            new Dictionary<string, object?> { ["code"] = code },
            cacheTtl: 30,
            cancellationToken).ConfigureAwait(false);
        var report = data.ReportData?.Report;
        if (report is null)
        {
            throw new InvalidOperationException("Report not found, still processing, or private.");
        }

        return new FflogsReportSummary
        {
            Code = code,
            Title = report.Title,
            StartTime = report.StartTime,
            EndTime = report.EndTime,
            Zone = report.Zone,
            Fights = report.Fights ?? [],
            Abilities = report.MasterData?.Abilities ?? [],
            Actors = report.MasterData?.Actors ?? [],
        };
    }

    public async Task<IReadOnlyList<FflogsEvent>> FetchAllEventsAsync(
        FflogsEventQuery query,
        CancellationToken cancellationToken)
    {
        var events = new List<FflogsEvent>();
        var startTime = query.StartTime;
        for (var page = 0; page < 200; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variables = new Dictionary<string, object?>
            {
                ["code"] = query.Code,
                ["fightID"] = query.FightId,
                ["hostilityType"] = query.HostilityType.ToString(),
                ["startTime"] = startTime,
                ["endTime"] = query.EndTime,
                ["includeResources"] = query.IncludeResources,
            };
            if (query.DataType is { } dataType)
            {
                variables["dataType"] = dataType.ToString();
            }

            if (query.AbilityId is { } abilityId)
            {
                variables["abilityID"] = abilityId;
            }

            if (!string.IsNullOrWhiteSpace(query.FilterExpression))
            {
                variables["filterExpression"] = query.FilterExpression;
            }

            var data = await CallAsync<EventsPageData>(
                "fightEvents",
                variables,
                query.CacheTtl,
                cancellationToken).ConfigureAwait(false);
            var eventPage = data.ReportData?.Report?.Events;
            if (eventPage is null)
            {
                break;
            }

            events.AddRange(eventPage.Data ?? []);
            if (eventPage.NextPageTimestamp is not { } next || eventPage.Data is not { Count: > 0 })
            {
                break;
            }

            if (next <= startTime)
            {
                throw new InvalidOperationException("FFLogs returned a repeated event page.");
            }

            startTime = next;
        }

        return events;
    }

    internal static int EventsCacheTtl(FflogsReportSummary report, FflogsFight fight, DateTime? nowUtc = null)
    {
        var reportStart = DateTimeOffset.FromUnixTimeMilliseconds((long)report.StartTime).UtcDateTime;
        var endedAt = reportStart.AddMilliseconds(fight.EndTime);
        return (nowUtc ?? DateTime.UtcNow) - endedAt > TimeSpan.FromMinutes(2)
            ? 60 * 60 * 48
            : 0;
    }

    private async Task<T> CallAsync<T>(
        string operation,
        IReadOnlyDictionary<string, object?> variables,
        int cacheTtl,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                endpoint,
                new ProxyRequest(operation, variables, cacheTtl),
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The WTF.DIG service took too long to respond. Try again.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Could not reach the WTF.DIG service. Check your connection and try again.");
        }

        using (response)
        {
            ProxyResponse<T>? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<ProxyResponse<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new InvalidOperationException($"The WTF.DIG service returned an unexpected response ({(int)response.StatusCode}).");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException(BuildRateLimitMessage());
            }

            if (!response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(body?.Error))
            {
                throw new InvalidOperationException(body?.Error ?? $"The WTF.DIG service request failed ({(int)response.StatusCode}).");
            }

            if (body?.Errors is { Count: > 0 })
            {
                var message = string.Join("; ", body.Errors.Select(error => error.Message));
                if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(BuildRateLimitMessage());
                }

                throw new InvalidOperationException(message);
            }

            return body is { HasData: true } && body.Data is not null
                ? body.Data
                : throw new InvalidOperationException("The WTF.DIG service returned no data.");
        }
    }

    private static string BuildRateLimitMessage()
    {
        var reset = DateTime.Now.AddHours(1);
        reset = new DateTime(reset.Year, reset.Month, reset.Day, reset.Hour, 0, 0, reset.Kind);
        return $"The analyzer has reached its FFLogs limit. Try again in a few minutes or after {reset:t}.";
    }

    private static string? FindParameter(string part, string name)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return null;
        }

        foreach (var pair in part.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            if (!string.Equals(Uri.UnescapeDataString(pieces[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
        }

        return null;
    }

    [GeneratedRegex("^(?:a:)?[a-zA-Z0-9]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex BareReportCodeRegex();

    [GeneratedRegex(@"(?:^|/)reports/((?:a:)?[a-zA-Z0-9]{16})(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReportPathRegex();

    private sealed record ProxyRequest(
        string Operation,
        IReadOnlyDictionary<string, object?> Variables,
        int CacheTtl);

    private sealed class ProxyResponse<T>
    {
        private T? data;

        public T? Data
        {
            get => data;
            init
            {
                data = value;
                HasData = true;
            }
        }

        public bool HasData { get; private set; }
        public string? Error { get; init; }
        public IReadOnlyList<ProxyError>? Errors { get; init; }
    }

    private sealed class ProxyError
    {
        public string Message { get; init; } = string.Empty;
    }

    private sealed class ReportSummaryData
    {
        public ReportDataContainer? ReportData { get; init; }
    }

    private sealed class ReportDataContainer
    {
        public RawReport? Report { get; init; }
    }

    private sealed class RawReport
    {
        public string Title { get; init; } = string.Empty;
        public double StartTime { get; init; }
        public double EndTime { get; init; }
        public FflogsZone? Zone { get; init; }
        public IReadOnlyList<FflogsFight>? Fights { get; init; }
        public RawMasterData? MasterData { get; init; }
        public RawEvents? Events { get; init; }
    }

    private sealed class RawMasterData
    {
        public IReadOnlyList<FflogsAbility>? Abilities { get; init; }
        public IReadOnlyList<FflogsActor>? Actors { get; init; }
    }

    private sealed class EventsPageData
    {
        public ReportDataContainer? ReportData { get; init; }
    }

    private sealed class RawEvents
    {
        public IReadOnlyList<FflogsEvent>? Data { get; init; }
        public double? NextPageTimestamp { get; init; }
    }
}
