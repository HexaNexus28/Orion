// Voice Activity Detection Processor
// Uses amplitude-based detection with silence timeout

export interface VADProcessorOptions {
  threshold?: number;
  silenceTimeoutMs?: number;
  minSpeechDurationMs?: number;
}

export interface VADResult {
  isSpeech: boolean;
  amplitude: number;
  silenceDuration: number;
}

export class VADProcessor {
  private threshold: number;
  private silenceTimeoutMs: number;
  private minSpeechDurationMs: number;
  private isSpeaking: boolean = false;
  private speechStartTime: number = 0;
  private lastSpeechTime: number = 0;

  constructor(options: VADProcessorOptions = {}) {
    this.threshold = options.threshold ?? 0.02;
    this.silenceTimeoutMs = options.silenceTimeoutMs ?? 1500;
    this.minSpeechDurationMs = options.minSpeechDurationMs ?? 200;
  }

  process(audioData: Float32Array): VADResult {
    const amplitude = this.calculateRMS(audioData);
    const now = Date.now();
    const isAboveThreshold = amplitude > this.threshold;

    if (isAboveThreshold) {
      this.lastSpeechTime = now;

      if (!this.isSpeaking) {
        this.isSpeaking = true;
        this.speechStartTime = now;
      }
    }

    const silenceDuration = now - this.lastSpeechTime;
    const speechDuration = now - this.speechStartTime;

    // Check if we should end speech
    if (this.isSpeaking && silenceDuration > this.silenceTimeoutMs) {
      this.isSpeaking = false;
    }

    return {
      isSpeech: this.isSpeaking && speechDuration > this.minSpeechDurationMs,
      amplitude: Math.min(amplitude * 5, 1), // Normalize
      silenceDuration
    };
  }

  reset(): void {
    this.isSpeaking = false;
    this.speechStartTime = 0;
    this.lastSpeechTime = 0;
  }

  private calculateRMS(samples: Float32Array): number {
    let sum = 0;
    for (let i = 0; i < samples.length; i++) {
      sum += samples[i] * samples[i];
    }
    return Math.sqrt(sum / samples.length);
  }
}

// Factory function for easier use
export const createVAD = (options?: VADProcessorOptions) => new VADProcessor(options);
