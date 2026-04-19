using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AchievementBadges.Models;

/// <summary>
/// One message in a conversation between two friends.
/// </summary>
public class Message
{
    [JsonPropertyName("id")]          public string   Id         { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("fromUserId")]  public string   FromUserId { get; set; } = string.Empty;
    [JsonPropertyName("fromUserName")]public string   FromUserName { get; set; } = string.Empty;
    [JsonPropertyName("toUserId")]    public string   ToUserId   { get; set; } = string.Empty;
    [JsonPropertyName("text")]        public string   Text       { get; set; } = string.Empty;
    [JsonPropertyName("sentAt")]      public DateTime SentAt     { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("readAt")]      public DateTime? ReadAt    { get; set; }
}

/// <summary>
/// On-disk payload: a map from conversation ID to that conversation's
/// messages in chronological order. Conversation ID is
/// <c>min(userA, userB) + "|" + max(userA, userB)</c> (both normalized).
/// </summary>
public class MessagingStore
{
    [JsonPropertyName("conversations")]
    public Dictionary<string, List<Message>> Conversations { get; set; } = new();
}

/// <summary>
/// Lightweight summary row used on the "my threads" list.
/// </summary>
public class MessageThreadSummary
{
    [JsonPropertyName("otherUserId")]   public string   OtherUserId   { get; set; } = string.Empty;
    [JsonPropertyName("otherUserName")] public string   OtherUserName { get; set; } = string.Empty;
    [JsonPropertyName("lastMessage")]   public string   LastMessage   { get; set; } = string.Empty;
    [JsonPropertyName("lastFromMe")]    public bool     LastFromMe    { get; set; }
    [JsonPropertyName("lastAt")]        public DateTime LastAt        { get; set; }
    [JsonPropertyName("unreadCount")]   public int      UnreadCount   { get; set; }
}
