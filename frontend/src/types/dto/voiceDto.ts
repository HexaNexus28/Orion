// voiceDto.ts - DTOs Voice
export interface VoiceRequest {
  audioBase64: string;
  mimeType: string;
}

export interface VoiceResponse {
  transcript: string;
  confidence: number;
}
