import { tapAudio } from './meter';

/*
 * The one Web Audio graph the meter reads from, and when it may be built.
 *
 * It is started inside the press, or not at all. iOS lets a page start an
 * AudioContext only during a user gesture, where desktop Chrome accepts any
 * time after the page was touched. Built in an effect once the element
 * reported `playing` -- after the press -- it stayed suspended on an iPhone
 * and the meter read silence.
 *
 * And the element is handed over only once the context runs. Routing it
 * through createMediaElementSource takes its sound out of the normal output and
 * into the graph; into a suspended graph, that is silence. The meter can fail by
 * being blank, never by muting the track.
 *
 * Module state: one element, one context, for the life of the page. A source
 * can be made from an element only once.
 */

let context = null;
let taps = null;

const tap = (element) => {
  if (!element || !context || context.state !== 'running' || taps) {
    return;
  }

  taps = tapAudio(element, context);
};

// call synchronously from a click or tap handler that is about to play
export const primeAudio = (element) => {
  // iOS treats Web Audio as ambient sound, which the silent switch mutes; a
  // plain <audio> is media. saying this page is a player (iOS 17+) keeps the
  // graph audible with the switch on silent
  try {
    const session = navigator.audioSession;

    if (session && session.type !== 'playback') {
      session.type = 'playback';
    }
  } catch {
    // an old WebKit that has the object but refuses the value
  }

  const Context = window.AudioContext ?? window.webkitAudioContext;

  if (!Context) {
    return;
  }

  try {
    context ??= new Context();
  } catch {
    return;
  }

  // resume() is the call that has to be inside the gesture; what it returns
  // may settle later, and tapping then needs no gesture of its own
  if (context.state === 'running') {
    tap(element);
    return;
  }

  const resuming = context.resume();

  (async () => {
    try {
      await resuming;
      tap(element);
    } catch {
      // stays untapped: plain playback
    }
  })();
};

// what the meter reads, or null while nothing live is there to read -- never
// built, or suspended since (iOS suspends on a call or a lock)
export const meterTaps = () =>
  context && context.state === 'running' ? taps : null;

/*
 * The encoding to ask for: AAC for WebKit, the default (Opus) otherwise.
 *
 * Safari plays WebM/Opus, but through a different pipeline from its native
 * formats, and createMediaElementSource reads silence from that one: on an
 * iPhone the meter worked for the MP3s that pass through and for none of the
 * transcoded AIFFs. Every browser on iOS is WebKit. By vendor rather than
 * canPlayType, because the question is not whether it plays -- it does -- but
 * whether Web Audio can hear it, which nothing reports.
 */
export const previewCodec = () => {
  try {
    return navigator.vendor?.startsWith('Apple') ? 'aac' : undefined;
  } catch {
    return undefined;
  }
};

// for the tests: forget the graph
export const resetAudioGraph = () => {
  context = null;
  taps = null;
};
