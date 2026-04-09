import { Hands, Results } from '@mediapipe/hands';
import { Camera } from '@mediapipe/camera_utils';

/**
 * HandTracker - Détection de gestes mains via MediaPipe
 * 21 points par main, 30fps, WebAssembly — 0 serveur
 * Permet : pointer, pinch, glisser, paume ouverte, poing fermé
 */

export type HandGesture =
  | 'open_palm'      // Paume ouverte → ORION écoute
  | 'closed_fist'    // Poing fermé → ORION se tait
  | 'pointing'       // Pointer → sélectionner élément
  | 'pinch'          // Pouce + index → attraper
  | 'thumbs_up'
  | 'thumbs_down'
  | 'none';          // Aucun geste détecté

export interface HandTrackingResult {
  gesture: HandGesture;
  handPresent: boolean;
  landmarks?: Array<{ x: number; y: number; z: number }>;
  handedness?: 'Left' | 'Right';
  timestamp: number;
}

export interface HandTrackerConfig {
  maxNumHands?: number;
  modelComplexity?: 0 | 1;
  minDetectionConfidence?: number;
  minTrackingConfidence?: number;
}

const DEFAULT_CONFIG: HandTrackerConfig = {
  maxNumHands: 1,
  modelComplexity: 1,
  minDetectionConfidence: 0.7,
  minTrackingConfidence: 0.5,
};

class HandTracker {
  private hands: Hands | null = null;
  private camera: Camera | null = null;
  private onResult: ((result: HandTrackingResult) => void) | null = null;
  private config: HandTrackerConfig;

  constructor(config: HandTrackerConfig = {}) {
    this.config = { ...DEFAULT_CONFIG, ...config };
  }

  /**
   * Détecte le geste à partir des landmarks de la main
   * 21 points : 0=poignet, 4=pouce, 8=index, 12=majeur, 16=annulaire, 20=auriculaire
   */
  private detectGesture(landmarks: Array<{ x: number; y: number; z: number }>): HandGesture {
    // Points clés
    const wrist = landmarks[0];
    const thumbTip = landmarks[4];
    const thumbIp = landmarks[3];
    const thumbMcp = landmarks[2];
    const indexTip = landmarks[8];
    const middleTip = landmarks[12];
    const ringTip = landmarks[16];
    const pinkyTip = landmarks[20];
    const indexPip = landmarks[6];  // PIP = joint intermédiaire
    const middlePip = landmarks[10];
    const ringPip = landmarks[14];
    const pinkyPip = landmarks[18];

    // Distance du poignet
    const dist = (p1: typeof wrist, p2: typeof wrist) =>
      Math.sqrt(Math.pow(p1.x - p2.x, 2) + Math.pow(p1.y - p2.y, 2));

    // Doigts étendus (tip plus loin du poignet que PIP)
    const thumbExtended = dist(wrist, thumbTip) > dist(wrist, thumbIp);
    const indexExtended = dist(wrist, indexTip) > dist(wrist, indexPip);
    const middleExtended = dist(wrist, middleTip) > dist(wrist, middlePip);
    const ringExtended = dist(wrist, ringTip) > dist(wrist, ringPip);
    const pinkyExtended = dist(wrist, pinkyTip) > dist(wrist, pinkyPip);

    const extendedCount = [indexExtended, middleExtended, ringExtended, pinkyExtended]
      .filter(Boolean).length;
    const foldedCount = [indexExtended, middleExtended, ringExtended, pinkyExtended]
      .filter((extended) => !extended).length;
    const thumbVerticalOffset = thumbTip.y - thumbMcp.y;
    const thumbHorizontalOffset = Math.abs(thumbTip.x - thumbMcp.x);

    // Pinch : puce et index proches
    const pinchDistance = dist(thumbTip, indexTip);
    if (pinchDistance < 0.05) {
      return 'pinch';
    }

    if (thumbExtended && foldedCount >= 3 && thumbHorizontalOffset < 0.18) {
      if (thumbVerticalOffset < -0.12 && thumbTip.y < wrist.y) {
        return 'thumbs_up';
      }

      if (thumbVerticalOffset > 0.12 && thumbTip.y > wrist.y) {
        return 'thumbs_down';
      }
    }

    // Pointing : seul index étendu
    if (indexExtended && !middleExtended && !ringExtended && !pinkyExtended) {
      return 'pointing';
    }

    // Paume ouverte : 4 doigts étendus
    if (extendedCount >= 4) {
      return 'open_palm';
    }

    // Poing fermé : 0-1 doigt étendu
    if (extendedCount <= 1) {
      return 'closed_fist';
    }

    return 'none';
  }

  async initialize(
    videoElement: HTMLVideoElement,
    onResult: (result: HandTrackingResult) => void
  ): Promise<void> {
    this.onResult = onResult;

    // Initialize MediaPipe Hands
    this.hands = new Hands({
      locateFile: (file: string) => {
        return `https://cdn.jsdelivr.net/npm/@mediapipe/hands/${file}`;
      },
    });

    this.hands.setOptions({
      maxNumHands: this.config.maxNumHands,
      modelComplexity: this.config.modelComplexity,
      minDetectionConfidence: this.config.minDetectionConfidence,
      minTrackingConfidence: this.config.minTrackingConfidence,
    });

    this.hands.onResults((results: Results) => {
      if (!this.onResult) return;

      if (results.multiHandLandmarks && results.multiHandLandmarks.length > 0) {
        const landmarks = results.multiHandLandmarks[0];
        const gesture = this.detectGesture(landmarks);
        const handedness = results.multiHandedness?.[0]?.label as 'Left' | 'Right' | undefined;

        this.onResult({
          gesture,
          handPresent: true,
          landmarks: landmarks.map((l: { x: number; y: number; z: number }) => ({ x: l.x, y: l.y, z: l.z })),
          handedness,
          timestamp: Date.now(),
        });
      } else {
        this.onResult({
          gesture: 'none',
          handPresent: false,
          timestamp: Date.now(),
        });
      }
    });

    // Initialize camera
    this.camera = new Camera(videoElement, {
      onFrame: async () => {
        if (this.hands && videoElement.readyState >= 2) {
          await this.hands.send({ image: videoElement });
        }
      },
      width: 640,
      height: 480,
    });
  }

  async start(): Promise<void> {
    if (!this.camera) {
      throw new Error('HandTracker not initialized. Call initialize() first.');
    }
    await this.camera.start();
  }

  stop(): void {
    this.camera?.stop();
  }

  dispose(): void {
    this.stop();
    this.hands?.close();
    this.hands = null;
    this.camera = null;
  }
}

export const createHandTracker = (config?: HandTrackerConfig) => new HandTracker(config);
export default HandTracker;
