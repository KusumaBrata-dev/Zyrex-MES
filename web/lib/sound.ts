/**
 * Kiosk feedback sounds via WebAudio. Pure helpers are exported separately so
 * they can be unit-tested without an AudioContext.
 */

/** Frequencies (Hz) for the success chime: two ascending sine notes. */
export function toneFreqs(): { ok: [number, number]; ng: [number] } {
  return { ok: [880, 1320], ng: [180] };
}

const OK_NOTE_MS = 90;
const NG_MS = 350;

function createAudioContext(): AudioContext | null {
  if (typeof window === "undefined") return null; // SSR / test guard
  const Ctor =
    window.AudioContext ??
    (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  return Ctor ? new Ctor() : null;
}

function playTone(ac: AudioContext, type: OscillatorType, freq: number, durationMs: number, startAt: number): void {
  const osc = ac.createOscillator();
  const gain = ac.createGain();
  osc.type = type;
  osc.frequency.value = freq;
  const t0 = ac.currentTime + startAt;
  const t1 = t0 + durationMs / 1000;
  // Simple attack/decay envelope to avoid clicks.
  gain.gain.setValueAtTime(0.0001, t0);
  gain.gain.exponentialRampToValueAtTime(0.25, t0 + 0.01);
  gain.gain.exponentialRampToValueAtTime(0.0001, t1);
  osc.connect(gain).connect(ac.destination);
  osc.start(t0);
  osc.stop(t1);
}

function playSequence(type: OscillatorType, freqs: number[], noteMs: number): void {
  const ac = createAudioContext();
  if (!ac) return;
  try {
    freqs.forEach((freq, i) => playTone(ac, type, freq, noteMs, (i * noteMs) / 1000));
  } finally {
    window.setTimeout(() => void ac.close(), (freqs.length * noteMs) / 1000 + 100);
  }
}

/** Success: two ascending sine notes 880 Hz → 1320 Hz, 90 ms each. */
export function playOk(): void {
  playSequence("sine", toneFreqs().ok, OK_NOTE_MS);
}

/** Failure: one low square buzz at 180 Hz for 350 ms. */
export function playNg(): void {
  playSequence("square", toneFreqs().ng, NG_MS);
}
