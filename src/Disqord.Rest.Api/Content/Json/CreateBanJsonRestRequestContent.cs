﻿using Disqord.Serialization.Json;
using Qommon;

namespace Disqord.Rest.Api;

public class CreateBanJsonRestRequestContent : JsonModelRestRequestContent
{
    [JsonProperty("delete_message_days")]
    public Optional<int> DeleteMessageDays;

    [JsonProperty("reason")]
    public Optional<string> Reason;

    protected override void OnValidate()
    {
        OptionalGuard.CheckValue(Reason, static reason => Guard.HasSizeLessThanOrEqualTo(reason, Discord.Limits.Rest.MaxAuditLogReasonLength));
    }
}
