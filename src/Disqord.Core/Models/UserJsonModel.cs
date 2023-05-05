﻿using Disqord.Serialization.Json;
using Qommon;

namespace Disqord.Models;

public class UserJsonModel : JsonModel
{
    [JsonProperty("id")]
    public Snowflake Id;

    [JsonProperty("username")]
    public string Username = null!;

    [JsonProperty("discriminator")]
    public short Discriminator;

    [JsonProperty("global_name")]
    public string? GlobalName;

    [JsonProperty("avatar")]
    public string? Avatar;

    [JsonProperty("bot")]
    public Optional<bool> Bot;

    [JsonProperty("system")]
    public Optional<bool> System;

    [JsonProperty("mfa_enabled")]
    public Optional<bool> MfaEnabled;

    [JsonProperty("banner")]
    public Optional<string?> Banner;

    [JsonProperty("accent_color")]
    public Optional<int?> AccentColor;

    [JsonProperty("locale")]
    public Optional<string?> Locale;

    [JsonProperty("verified")]
    public Optional<bool> Verified;

    [JsonProperty("email")]
    public Optional<string?> Email;

    [JsonProperty("flags")]
    public Optional<UserFlags> Flags;

    [JsonProperty("premium_type")]
    public Optional<NitroType> PremiumType;

    [JsonProperty("public_flags")]
    public Optional<UserFlags> PublicFlags;
}
