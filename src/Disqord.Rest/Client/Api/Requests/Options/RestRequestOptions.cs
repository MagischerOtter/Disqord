﻿using System;
using System.Threading;

namespace Disqord.Rest
{
    /// <summary>
    ///     Represents a set of options that can be used to customise REST requests.
    /// </summary>
    public sealed class RestRequestOptions : ICloneable
    {
        /// <summary>
        ///     Gets the <see cref="TimeSpan"/> representing how long the REST client should wait before timing out the request.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        ///     Gets the <see cref="System.Threading.CancellationToken"/> representing the cancellation token for the REST request.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        ///     Gets the audit log reason.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        ///     Gets the maximum rate-limit duration to delay for instead of throwing.
        /// </summary>
        public TimeSpan MaximumRateLimitDuration { get; }

        internal RestRequestOptions()
        { }

        internal RestRequestOptions(RestRequestOptionsBuilder builder)
        {
            Timeout = builder.Timeout;
            CancellationToken = builder.CancellationToken;
            Reason = builder.Reason;
            MaximumRateLimitDuration = builder.MaximumRateLimitDuration;
        }

        public static RestRequestOptions FromReason(string reason)
            => new RestRequestOptionsBuilder()
                .WithReason(reason)
                .Build();

        /// <summary>
        ///     Creates a copy of this <see cref="RestRequestOptions"/>.
        /// </summary>
        public RestRequestOptions Clone()
            => (RestRequestOptions) MemberwiseClone();

        object ICloneable.Clone()
            => Clone();
    }
}
