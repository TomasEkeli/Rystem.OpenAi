﻿using System.Text.Json.Serialization;

namespace Rystem.OpenAi.Chat
{
    public sealed class ChatMessageTool
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("function")]
        public ChatMessageFunctionResponse? Function { get; set; }
    }
}
