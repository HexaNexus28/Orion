/**
 * Capture audio dans un AudioWorklet — le fil AUDIO, pas le fil principal.
 *
 * POURQUOI CE FICHIER EXISTE. La capture utilisait un ScriptProcessorNode : RETIRÉ de la
 * spécification, et exécuté sur le fil principal. Or ce même fil fait tourner une scène 3D
 * (three.js + requestAnimationFrame) et tout le rendu React. Quand il sature — ce qui arrive vite
 * sur un téléphone — les callbacks audio sont ABANDONNÉS, silencieusement. Le micro paraît actif,
 * les indicateurs sont au vert, et rien ne part.
 *
 * Un AudioWorklet tourne sur le fil audio, isolé du rendu : la scène 3D ne peut plus affamer la
 * capture.
 *
 * Le code du processeur est une CHAÎNE puis un Blob, volontairement : `audioWorklet.addModule()`
 * exige une URL, et le passer par le bundler ajoute une dépendance de build fragile pour trente
 * lignes qui ne changeront jamais.
 */

/** Nom enregistré du processeur, partagé entre la chaîne et l’instanciation. */
export const PROCESSOR_NAME = 'orion-vad';

const SOURCE = `
class OrionVad extends AudioWorkletProcessor {
  constructor(options) {
    super();
    this.size = options.processorOptions.size;
    this.buffer = new Float32Array(this.size);
    this.filled = 0;
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || !input[0]) return true;

    const samples = input[0];
    for (let i = 0; i < samples.length; i++) {
      this.buffer[this.filled++] = samples[i];

      if (this.filled === this.size) {
        const block = this.buffer.slice(0);

        let sum = 0;
        for (let k = 0; k < block.length; k++) sum += block[k] * block[k];

        // Le buffer est TRANSFÉRÉ, pas copié : à 16 kHz cela fait ~8 messages par seconde,
        // et une copie par message chargerait le ramasse-miettes pour rien.
        this.port.postMessage({ rms: Math.sqrt(sum / block.length), block }, [block.buffer]);
        this.filled = 0;
      }
    }
    return true;
  }
}
registerProcessor('orion-vad', OrionVad);
`;

let moduleUrl: string | null = null;

/** URL du module worklet, créée une seule fois pour toute la session. */
export function workletUrl(): string {
  if (!moduleUrl) {
    moduleUrl = URL.createObjectURL(new Blob([SOURCE], { type: 'application/javascript' }));
  }
  return moduleUrl;
}