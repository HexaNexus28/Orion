namespace Orion.Core.DTOs.Requests;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public bool VoiceMode { get; set; }
}
