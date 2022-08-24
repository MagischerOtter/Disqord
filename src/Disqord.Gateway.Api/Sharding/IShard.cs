using System;
using System.Threading;
using System.Threading.Tasks;
using Disqord.Gateway.Api.Models;
using Disqord.Logging;
using Disqord.Serialization.Json;

namespace Disqord.Gateway.Api;

/// <summary>
///     Represents a gateway shard.
/// </summary>
public interface IShard : ILogging, IAsyncDisposable
{
    /// <summary>
    ///     Gets the ID of this shard.
    /// </summary>
    ShardId Id { get; }

    /// <summary>
    ///     Gets the intents of this shard.
    /// </summary>
    GatewayIntents Intents { get; }

    /// <summary>
    ///     Gets the large guild threshold of this shard.
    /// </summary>
    int LargeGuildThreshold { get; }

    /// <summary>
    ///     Gets or sets the presence of this shard.
    /// </summary>
    UpdatePresenceJsonModel? Presence { get; set; }

    /// <summary>
    ///     Gets the API client of this shard.
    /// </summary>
    IGatewayApiClient ApiClient { get; }

    /// <summary>
    ///     Gets the serializer of this shard.
    /// </summary>
    IJsonSerializer Serializer { get; }

    /// <summary>
    ///     Gets the gateway of this shard.
    /// </summary>
    IGateway Gateway { get; }

    /// <summary>
    ///     Gets the rate-limiter of this shard.
    /// </summary>
    IGatewayRateLimiter RateLimiter { get; }

    /// <summary>
    ///     Gets the heartbeater of this shard.
    /// </summary>
    IGatewayHeartbeater Heartbeater { get; }

    /// <summary>
    ///     Gets the session ID of the current gateway session.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    ///     Gets the last sequence number (<see cref="GatewayPayloadJsonModel.S"/>) received from the gateway.
    /// </summary>
    int? Sequence { get; }

    /// <summary>
    ///     Gets the URI via which the current gateway session should be resumed.
    /// </summary>
    Uri? ResumeUri { get; }

    /// <summary>
    ///     Gets the current connection state of the gateway.
    /// </summary>
    ShardState State { get; }

    /// <summary>
    ///     Gets the stopping token passed to <see cref="RunAsync(CancellationToken, Uri)"/>.
    /// </summary>
    CancellationToken StoppingToken { get; }

    Task SendAsync(GatewayPayloadJsonModel payload, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Waits for this shard to be ready.
    /// </summary>
    /// <remarks>
    ///     This should only be called prior to startup.
    ///     If the client is already ready this will hang until the shard identifies again.
    ///     This does not throw if the shard run fails.
    /// </remarks>
    /// <returns>
    ///     A <see cref="Task"/> representing the wait.
    /// </returns>
    Task WaitForReadyAsync();

    /// <summary>
    ///     Runs this shard.
    /// </summary>
    /// <param name="initialUri"> The initial URI of the Discord gateway to connect to. </param>
    /// <param name="stoppingToken"> The token used to signal connection stopping. </param>
    /// <returns> The <see cref="Task"/> representing the connection. </returns>
    Task RunAsync(Uri? initialUri, CancellationToken stoppingToken);
}
