﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Disqord.Http;
using Disqord.Logging;
using Disqord.Utilities.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qommon;
using Qommon.Binding;

namespace Disqord.Rest.Api.Default;

public sealed class DefaultRestRateLimiter : IRestRateLimiter
{
    /// <inheritdoc/>
    public ILogger Logger { get; }

    /// <inheritdoc/>
    public IRestApiClient ApiClient => _binder.Value;

    /// <summary>
    ///     Gets the maximum delay duration the rate-limiter will delay for
    ///     before throwing.
    /// </summary>
    public TimeSpan MaximumDelayDuration { get; }

    /// <summary>
    ///     Gets the date at which the global rate-limit is reset.
    /// </summary>
    public DateTimeOffset? GlobalResetsAt
    {
        get
        {
            lock (this)
            {
                return _globalResetsAt;
            }
        }
    }

    private DateTimeOffset? _globalResetsAt;

    private readonly Binder<IRestApiClient> _binder;

    private readonly Dictionary<IFormattableRoute, string> _hashes;
    private readonly Dictionary<string, Bucket> _buckets;
    private readonly HashSet<IFormattableRoute> _hitRateLimits;

    public DefaultRestRateLimiter(
        IOptions<DefaultRestRateLimiterConfiguration> options,
        ILogger<DefaultRestRateLimiter> logger)
    {
        var configuration = options.Value;
        Logger = logger;
        MaximumDelayDuration = configuration.MaximumDelayDuration;

        _binder = new Binder<IRestApiClient>(this);

        _hashes = new Dictionary<IFormattableRoute, string>();
        _buckets = new Dictionary<string, Bucket>();
        _hitRateLimits = new HashSet<IFormattableRoute>();
    }

    public void Bind(IRestApiClient value)
    {
        _binder.Bind(value);
    }

    /// <inheritdoc/>
    public bool IsRateLimited(IFormattedRoute? route = null)
    {
        if (route == null)
            return _globalResetsAt > DateTimeOffset.UtcNow;

        var bucket = GetBucket(route, false);
        if (bucket == null)
            return false;

        return bucket.Remaining == 0;
    }

    /// <inheritdoc/>
    public async Task<IRestResponse> ExecuteAsync(IRestRequest request, CancellationToken cancellationToken)
    {
        var bucket = GetBucket(request.Route, true)!;
        using (var token = await bucket.PostAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return await token.Task.ConfigureAwait(false);
        }
    }

    private Bucket? GetBucket(IFormattedRoute route, bool create)
    {
        lock (this)
        {
            var isUnlimited = !_hashes.TryGetValue(route.BaseRoute, out var hash);
            hash ??= $"unlimited+{route}";
            var bucketId = $"{hash}:{route.Parameters.GetGuildId()}:{route.Parameters.GetChannelId()}:{route.Parameters.GetWebhookId()}";
            if (!_buckets.TryGetValue(bucketId, out var bucket) && create)
            {
                bucket = new Bucket(this, isUnlimited);
                _buckets.Add(bucketId, bucket);
            }

            return bucket;
        }
    }

    private bool UpdateBucket(IFormattedRoute route, IHttpResponse response)
    {
        lock (this)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var headers = new DefaultRestResponseHeaders(response.Headers);
                if (headers.Bucket.HasValue)
                {
                    if (_hashes.TryAdd(route.BaseRoute, headers.Bucket.Value))
                        Logger.LogTrace("Cached bucket hash {0} -> {1}.", route, headers.Bucket.Value);
                }

                var bucket = GetBucket(route, true)!;
                if (response.StatusCode == HttpResponseStatusCode.TooManyRequests)
                {
                    if (headers.IsGlobal.GetValueOrDefault() || !headers.GetHeader("Via").HasValue)
                    {
                        var type = headers.IsGlobal.GetValueOrDefault()
                            ? "global"
                            : "Cloudflare";

                        Logger.LogError("Hit a {0} rate-limit! Expires after {1}.", type, headers.RetryAfter.Value);
                        _globalResetsAt = now + headers.RetryAfter.Value;
                    }
                    else
                    {
                        bucket.Remaining = 0;
                        bucket.ResetsAt = now + headers.RetryAfter.Value;
                        var isShared = headers.Scope == "shared";
                        var level = _hitRateLimits.Add(route.BaseRoute) && headers.RetryAfter.Value.TotalSeconds < 30 || isShared
                            ? LogLevel.Information
                            : LogLevel.Warning;

                        var message = isShared
                            ? "Hit a shared rate-limit on route {0}. Expires after {1}ms."
                            : "Hit a rate-limit on route {0}. Expires after {1}ms.";

                        Logger.Log(level, message, route, headers.RetryAfter.Value.TotalMilliseconds);
                        return true;
                    }
                }

                if (!headers.Bucket.HasValue)
                    return false;

                bucket.Limit = headers.Limit.Value;
                bucket.Remaining = headers.Remaining.Value;
                bucket.ResetsAt = now + headers.ResetsAfter.Value;
                Logger.LogDebug("Updated the bucket for route {0} to ({1}/{2}, {3})", route, bucket.Remaining, bucket.Limit, bucket.ResetsAt - now);
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Encountered an exception while updating bucket for {0}. Code: {2} Headers:\n{3}", route, response.StatusCode, string.Join('\n', response.Headers));
                return false;
            }
        }
    }

    private class Bucket : ILogging
    {
        public int Limit { get; internal set; } = 1;

        public int Remaining { get; internal set; } = 1;

        public DateTimeOffset ResetsAt { get; internal set; }

        public ILogger Logger => _rateLimiter.Logger;

        private readonly DefaultRestRateLimiter _rateLimiter;
        private readonly bool _isUnlimited;
        private readonly Channel<Token> _tokens;

        public Bucket(DefaultRestRateLimiter rateLimiter, bool isUnlimited)
        {
            _rateLimiter = rateLimiter;
            _isUnlimited = isUnlimited;

            _tokens = Channel.CreateUnbounded<Token>(new UnboundedChannelOptions
            {
                SingleReader = true,
            });

            _ = RunAsync();
        }

        public class Token : IDisposable
        {
            public IRestRequest Request { get; }

            public CancellationToken CancellationToken { get; }

            public Task<IRestResponse> Task => _tcs.Task;

            private readonly CancellationTokenRegistration _reg;
            private readonly Tcs<IRestResponse> _tcs;

            public Token(IRestRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                CancellationToken = cancellationToken;

                static void CancellationCallback(object? state, CancellationToken cancellationToken)
                {
                    var tcs = Unsafe.As<Tcs<IRestResponse>>(state!);
                    tcs.Cancel(cancellationToken);
                }

                _reg = cancellationToken.UnsafeRegister(CancellationCallback, _tcs);
                _tcs = new Tcs<IRestResponse>();
            }

            public void Complete(IRestResponse response)
            {
                _tcs.Complete(response);
            }

            public void Complete(Exception exception)
            {
                _tcs.Throw(exception);
            }

            public void Dispose()
            {
                _reg.Dispose();
            }
        }

        public async Task<Token> PostAsync(IRestRequest request, CancellationToken cancellationToken)
        {
            var token = new Token(request, cancellationToken);
            await _tokens.Writer.WriteAsync(token, cancellationToken).ConfigureAwait(false);
            return token;
        }

        public ValueTask PostAsync(Token token)
        {
            return _tokens.Writer.WriteAsync(token, token.CancellationToken);
        }

        private async Task RunAsync()
        {
            var reader = _tokens.Reader;
            await foreach (var token in reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (token.CancellationToken.IsCancellationRequested)
                    continue;

                var request = token.Request;
                bool retry;
                do
                {
                    retry = false;
                    try
                    {
                        if (_isUnlimited)
                        {
                            var bucket = _rateLimiter.GetBucket(request.Route, false)!;
                            if (bucket != this)
                            {
                                Logger.LogDebug("Route {0} is moving the request to the limited bucket.", request.Route);
                                await bucket.PostAsync(token).ConfigureAwait(false);
                                break;
                            }
                        }

                        var globalResetsAt = _rateLimiter.GlobalResetsAt;
                        var now = DateTimeOffset.UtcNow;
                        var isGloballyRateLimited = globalResetsAt > now;
                        if (Remaining == 0 || isGloballyRateLimited)
                        {
                            var delay = isGloballyRateLimited
                                ? globalResetsAt!.Value - now
                                : ResetsAt - now;

                            if (delay > TimeSpan.Zero)
                            {
                                var maximumDelayDuration = (request.Options as DefaultRestRequestOptions)?.MaximumDelayDuration ?? _rateLimiter.MaximumDelayDuration;
                                if (maximumDelayDuration != Timeout.InfiniteTimeSpan && delay > maximumDelayDuration)
                                {
                                    Logger.LogDebug("Route {0} is rate-limited - throwing as the delay {1} exceeds the maximum delay duration.", request.Route, delay);
                                    token.Complete(new MaximumRateLimitDelayExceededException(request, delay, isGloballyRateLimited));
                                    break;
                                }

                                var level = request.Route.BaseRoute.Equals(Route.Channel.CreateReaction)
                                    ? LogLevel.Debug
                                    : LogLevel.Information;

                                Logger.Log(level, "Route {0} is rate-limited - delaying for {1}.", request.Route, delay);
                                await Task.Delay(delay, token.CancellationToken).ConfigureAwait(false);
                            }
                        }

                        var response = await _rateLimiter.ApiClient.Requester.ExecuteAsync(request, token.CancellationToken).ConfigureAwait(false);
                        if (_rateLimiter.UpdateBucket(request.Route, response.HttpResponse))
                        {
                            Logger.LogInformation("Route {0} is retrying the last request due to a hit rate-limit.", request.Route);
                            retry = true;
                        }
                        else
                        {
                            token.Complete(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        token.Complete(ex);

                        if (ex is not (OperationCanceledException or TimeoutException))
                        {
                            Logger.LogError(ex, "Route {0} encountered an exception while processing the request.", request.Route);
                        }
                    }
                }
                while (retry);
            }
        }
    }
}
