import {
  artUrl,
  audioUrl,
  failure,
  getTags,
  getTicket,
} from '../../lib/player/api';
import { previewCodec, primeAudio } from '../../lib/player/audioGraph';
import {
  readLast,
  readVolume,
  writeLast,
  writeVolume,
} from '../../lib/player/remember';
import { isPlayable, trackOf } from '../../lib/player/rows';
import { useSetting } from './settings';
import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

/*
 * One player for the whole app, and the one <audio> it plays through.
 *
 * Twin: the provider half of media-bridge web/app/src/features/plex/player.tsx,
 * keyed by transfer id rather than by a path.
 *
 * The element belongs here rather than to the bar because more than one thing
 * commands it and needs to know what it is doing -- the bar's own button, the
 * ▶ on every row, the badge's findings -- and because the bar unmounts when the
 * player closes while this does not.
 *
 * The source is set by hand, inside the press, never by a render. iOS starts
 * sound only during the tap that asked for it; React 16 renders a click's state
 * change synchronously but runs effects after paint, which is outside the tap,
 * so a play() in an effect is refused there. That is also why the stream ticket
 * is held before anything is pressed (see StreamTickets.cs): the address has to
 * be known without a round trip.
 */

const PlayerContext = createContext(null);

// may be null: the player is off, or this is a relay agent
export const usePlayer = () => useContext(PlayerContext);

// asked once: a browser does not change vendor mid-page
const CODEC = previewCodec();

// a ticket idles out after an hour (StreamTickets.Idle); renewing at half that
// means the one in hand is always good for the next track
const TICKET_EVERY_MS = 30 * 60 * 1_000;

// how often the position is remembered while playing. paused, it is the exact
// second: that is when someone closes the tab
const POS_EVERY = 5;

const LOAD_FAILED = 'could not load the audio — press play to retry';

// a promise whose rejection means something, awaited without .then. the call
// that makes it -- play() -- has already happened, synchronously, in the press
const attempt = async (promise, onRejected) => {
  try {
    await promise;
  } catch (error) {
    onRejected(error);
  }
};

const emit = (type, current) =>
  document.dispatchEvent(
    new CustomEvent('slskd:player', { detail: { current, type } }),
  );

export const PlayerProvider = ({ children, enabled = true }) => {
  const audio = useRef(null);
  const ticket = useRef(null);
  const [hasTicket, setHasTicket] = useState(false);
  const [available, setAvailable] = useState(enabled);
  const [analysisOn, setAnalysisOn] = useState(false);

  // restored paused, never autoplayed: a page that starts making noise on its
  // own is what browsers spent a decade stamping out
  const restored = useRef(readLast());
  const [current, setCurrent] = useState(() =>
    restored.current
      ? {
          artist: null,
          id: restored.current.id,
          title: restored.current.title,
          username: restored.current.username,
        }
      : null,
  );
  const resumeAt = useRef(restored.current?.at ?? 0);
  const [playing, setPlaying] = useState(false);
  const [loading, setLoading] = useState(false);
  const [at, setAt] = useState(restored.current?.at ?? 0);
  const [ended, setEnded] = useState(false);
  const [failed, setFailed] = useState('');
  const pressed = useRef(false);
  const currentRef = useRef(current);
  currentRef.current = current;

  // --- the ticket ------------------------------------------------------------

  const renew = useCallback(async () => {
    try {
      const issued = await getTicket();
      ticket.current = issued.ticket;
      setAnalysisOn(issued.analysis === true);
      setHasTicket(true);
      setAvailable(true);

      return ticket.current;
    } catch (error) {
      // the player is off on this server; nothing to draw
      if (error?.response?.status === 404) {
        setAvailable(false);
      }

      return null;
    }
  }, []);

  useEffect(() => {
    if (!enabled) {
      setAvailable(false);
      return undefined;
    }

    renew();
    const timer = setInterval(renew, TICKET_EVERY_MS);

    return () => clearInterval(timer);
  }, [enabled, renew]);

  // --- playing ---------------------------------------------------------------

  // what to say when play() rejects -- which is not always a failure. an
  // AbortError is the load() just before it, and the track goes on to play; a
  // refused autoplay is the ordinary end of a restore and says nothing
  const started = useCallback((error, unattended = false) => {
    if (error?.name === 'AbortError') {
      return;
    }

    setLoading(false);

    if (unattended && error?.name === 'NotAllowedError') {
      return;
    }

    if (audio.current?.error) {
      setFailed(LOAD_FAILED);
      return;
    }

    setFailed(
      error?.name === 'NotAllowedError'
        ? 'the browser blocked playback — press play'
        : 'the browser would not start it',
    );
  }, []);

  // point the element at a track, inside the press. `autoplay` false readies it
  // without fetching: preload is none, so nothing is pulled until it is played
  const load = useCallback(
    (track, { autoplay = true, from = 0 } = {}) => {
      const element = audio.current;

      if (!element || !ticket.current) {
        return false;
      }

      resumeAt.current = from;
      setAt(from);
      setFailed('');
      setEnded(false);
      element.src = audioUrl(track.id, ticket.current, CODEC);
      element.load();

      if (autoplay) {
        setLoading(true);
        attempt(element.play(), started);
      }

      return true;
    },
    [started],
  );

  // a restored track gets its source once there is a ticket for it
  useEffect(() => {
    if (
      hasTicket &&
      current &&
      audio.current &&
      !audio.current.getAttribute('src')
    ) {
      load(current, { autoplay: false, from: resumeAt.current });
    }
    // only when the ticket arrives; a press loads for itself
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasTicket]);

  const begin = useCallback(
    (track, from = 0) => {
      pressed.current = true;
      // inside the press, which is the only moment iOS allows it
      primeAudio(audio.current);
      setCurrent(track);

      if (!load(track, { from })) {
        // no ticket yet: fetch one and try. on iOS the tap may be lost by then,
        // and the player says press play
        (async () => {
          if (await renew()) {
            load(track, { from });
          }
        })();
      }
    },
    [load, renew],
  );

  const stop = useCallback(() => {
    // pause it, do not merely forget it: an element whose src is removed keeps
    // playing, so the bar would vanish with the music still going
    const element = audio.current;

    if (element) {
      element.pause();
      element.removeAttribute('src');
      element.load();
    }

    resumeAt.current = 0;
    setCurrent(null);
    setPlaying(false);
    setLoading(false);
    setAt(0);
    setEnded(false);
    setFailed('');
    writeLast(null);
    emit('stop', null);
  }, []);

  const seek = useCallback((seconds) => {
    const element = audio.current;

    if (element && Number.isFinite(seconds) && seconds >= 0) {
      element.currentTime = seconds;
      setAt(seconds);

      if (!element.duration || seconds < element.duration) {
        setEnded(false);
      }
    }
  }, []);

  const toggle = useCallback(
    (track = currentRef.current) => {
      if (!track) {
        return;
      }

      const element = audio.current;
      pressed.current = true;
      primeAudio(element);

      if (!currentRef.current || currentRef.current.id !== track.id) {
        begin(track);
        return;
      }

      if (!element) {
        return;
      }

      // a restored track whose source never arrived, or one that failed
      if (!element.getAttribute('src') || element.error) {
        load(track, {
          from: element.error ? element.currentTime : resumeAt.current,
        });
        return;
      }

      // from the top, explicitly: play() rewinds an ended element only while it
      // still counts as ended
      if (ended) {
        element.currentTime = 0;
        setAt(0);
      }

      if (element.paused) {
        setLoading(true);
        attempt(element.play(), started);
      } else {
        element.pause();
      }
    },
    [begin, ended, load, started],
  );

  // play a track from a point: the same track seeks and plays on, another
  // one loads there
  const play = useCallback(
    (track, { at: from = 0 } = {}) => {
      const element = audio.current;

      if (
        currentRef.current?.id !== track.id ||
        !element?.getAttribute('src')
      ) {
        begin(track, from);
        return;
      }

      pressed.current = true;
      primeAudio(element);
      seek(from);

      if (element.paused) {
        setLoading(true);
        attempt(element.play(), started);
      }
    },
    [begin, seek, started],
  );

  // --- what the bar says about the track -----------------------------------

  const id = current?.id;

  useEffect(() => {
    if (!id) {
      return undefined;
    }

    const controller = new AbortController();

    (async () => {
      try {
        const tags = await getTags(id, { signal: controller.signal });
        setCurrent((was) =>
          was?.id === id
            ? {
                ...was,
                album: tags.album ?? null,
                artist: tags.artist ?? null,
                title: tags.title || was.title,
              }
            : was,
        );
      } catch {
        // the file name stands
      }
    })();

    return () => controller.abort();
  }, [id]);

  // remembered: the track, and where it is, at POS_EVERY while it plays and
  // to the second when it stops
  const whole = Math.floor(at);
  const position = playing ? Math.floor(whole / POS_EVERY) * POS_EVERY : whole;

  useEffect(() => {
    if (current) {
      writeLast({
        at: position,
        id: current.id,
        title: current.title,
        username: current.username,
      });
    }
  }, [current, position]);

  // an <audio> error says nothing about why. the tags route answers with the
  // reason the file cannot be read (gone, still arriving, refused); a good
  // answer there means it was the ticket, and a new one is tried once
  const recover = useCallback(async () => {
    const track = currentRef.current;
    const element = audio.current;

    if (!track || !element) {
      return;
    }

    setLoading(false);

    try {
      await getTags(track.id);
    } catch (error) {
      setFailed(failure(error));
      return;
    }

    const was = element.currentTime;

    if (await renew()) {
      load(track, { autoplay: pressed.current, from: was });
    } else {
      setFailed(LOAD_FAILED);
    }
  }, [load, renew]);

  // the volume. full scale unless the control is switched on, so "no control"
  // can never mean "stuck at 30% from last week"
  const [volume, setVolume] = useState(readVolume);
  const [muted, setMuted] = useState(false);

  const changeVolume = useCallback((value) => {
    setVolume(value);
    writeVolume(value);

    // moving the slider is asking to hear something, so it lifts a mute
    if (value > 0) {
      setMuted(false);
    }
  }, []);

  const volumeOn = useSetting('slskd-player-volume-control');

  useEffect(() => {
    const element = audio.current;

    if (element) {
      element.volume = volumeOn ? volume : 1;
      element.muted = volumeOn ? muted : false;
    }
  }, [id, muted, volume, volumeOn]);

  const api = useMemo(
    () => ({
      analysisOn,
      art: current && hasTicket ? artUrl(current.id, ticket.current) : null,
      at,
      audio,
      available,
      current,
      ended,
      failed,
      loading,
      muted,
      play,
      playing,
      seek,
      setMuted,
      setVolume: changeVolume,
      stop,
      toggle,
      volume,
    }),
    [
      analysisOn,
      at,
      available,
      changeVolume,
      current,
      ended,
      failed,
      hasTicket,
      loading,
      muted,
      play,
      playing,
      seek,
      stop,
      toggle,
      volume,
    ],
  );

  // the hook the scripts injected into this page use: Compare in media-bridge's
  // slskd-ingest.js plays a copy through here rather than through a player of
  // its own. by row, so a caller needs nothing but what the row already says
  const apiRef = useRef(api);
  apiRef.current = api;

  useEffect(() => {
    window.slskd = window.slskd ?? {};
    // by id, or by the peer and the remote name -- what Compare in
    // slskd-ingest.js knows a copy by -- looked up in this user's downloads
    const find = async ({ filename, id, username }) => {
      if (id) {
        return { filename, id, username };
      }

      if (!username || !filename) {
        return null;
      }

      const { data } = await api.get(
        `/transfers/downloads/${encodeURIComponent(username)}`,
      );
      const files = (data?.directories ?? []).flatMap((directory) => directory.files ?? []);

      return files.find((file) => file.filename === filename) ?? null;
    };

    window.slskd.player = {
      current: () => apiRef.current.current,
      pause: () => audio.current?.pause(),
      play: async (wanted) => {
        const found = await find(wanted ?? {});
        const row = {
          direction: 'Download',
          state: 'Completed, Succeeded',
          username: wanted?.username,
          ...found,
        };

        if (found && isPlayable(row)) {
          apiRef.current.toggle(trackOf(row));
        }
      },
    };

        if (isPlayable(row)) {
          apiRef.current.toggle(trackOf(row));
        }
      },
    };

    return () => {
      delete window.slskd.player;
    };
  }, []);

  return (
    <PlayerContext.Provider value={available ? api : null}>
      {children}
      {/* one element for the app, which outlives the bar. music, so there is
          nothing to caption */}
      {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
      <audio
        onEnded={() => {
          setEnded(true);
          emit('ended', currentRef.current);
        }}
        onError={() => {
          if (audio.current?.getAttribute('src')) {
            recover();
          }
        }}
        onLoadedMetadata={(event) => {
          // the one moment a seek sticks: setting currentTime before the
          // metadata is discarded, silently
          const want = resumeAt.current;
          resumeAt.current = 0;
          const element = event.currentTarget;

          if (
            want > 0 &&
            Number.isFinite(element.duration) &&
            want < element.duration - 1
          ) {
            element.currentTime = want;
            setAt(want);
          }
        }}
        onPause={() => {
          setPlaying(false);
          setLoading(false);
          emit('pause', currentRef.current);
        }}
        onPlay={() => {
          setPlaying(true);
          setEnded(false);
          setFailed('');
          emit('play', currentRef.current);
        }}
        // `playing`, not `play`: play fires when playback is asked for, which is
        // when the wait begins rather than when it ends
        onPlaying={() => {
          setLoading(false);
          setFailed('');
        }}
        onTimeUpdate={(event) => setAt(event.currentTarget.currentTime)}
        onWaiting={() => setLoading(true)}
        preload="none"
        ref={audio}
      />
    </PlayerContext.Provider>
  );
};
