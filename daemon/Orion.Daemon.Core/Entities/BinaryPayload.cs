namespace Orion.Daemon.Core.Entities;

/// <summary>
/// Marker type for binary data that should be sent as a binary WebSocket frame
/// instead of base64-encoded JSON. Used by SynthesizeAction for WAV audio.
/// Protocol: [36-byte requestId UTF-8] + [raw bytes]
/// </summary>
public class BinaryPayload
{
    public byte[] Bytes { get; }

    public BinaryPayload(byte[] bytes)
    {
        Bytes = bytes;
    }
}
