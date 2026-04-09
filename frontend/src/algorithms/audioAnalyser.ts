// Web Audio API Audio Analyser
// Provides amplitude visualization

export interface AudioAnalyserConfig {
  fftSize?: number;
  smoothingTimeConstant?: number;
  minDecibels?: number;
  maxDecibels?: number;
}

export class AudioAnalyser {
  private context: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private source: MediaStreamAudioSourceNode | null = null;
  private dataArray: Float32Array | null = null;

  constructor(private config: AudioAnalyserConfig = {}) { }

  async initialize(stream: MediaStream): Promise<void> {
    const AudioContextClass = window.AudioContext ||
      (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;

    this.context = new AudioContextClass({ sampleRate: 16000 });

    this.analyser = this.context.createAnalyser();
    this.analyser.fftSize = this.config.fftSize ?? 2048;
    this.analyser.smoothingTimeConstant = this.config.smoothingTimeConstant ?? 0.8;
    this.analyser.minDecibels = this.config.minDecibels ?? -100;
    this.analyser.maxDecibels = this.config.maxDecibels ?? -10;

    this.source = this.context.createMediaStreamSource(stream);
    this.source.connect(this.analyser);

    this.dataArray = new Float32Array(this.analyser.fftSize);
  }

  getAmplitude(): number {
    if (!this.analyser || !this.dataArray) return 0;

    this.analyser.getFloatTimeDomainData(this.dataArray as unknown as any);

    // Calculate RMS
    let sum = 0;
    for (let i = 0; i < this.dataArray.length; i++) {
      sum += this.dataArray[i] * this.dataArray[i];
    }
    const rms = Math.sqrt(sum / this.dataArray.length);

    // Normalize to 0-1 range with boost
    return Math.min(rms * 5, 1);
  }

  getFrequencyData(): Uint8Array | null {
    if (!this.analyser) return null;
    const data = new Uint8Array(this.analyser.frequencyBinCount);
    this.analyser.getByteFrequencyData(data);
    return data;
  }

  destroy(): void {
    this.source?.disconnect();
    this.context?.close();
    this.context = null;
    this.analyser = null;
    this.source = null;
  }
}
