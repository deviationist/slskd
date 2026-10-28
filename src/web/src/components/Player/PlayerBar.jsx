import './Player.css';
import { getPeaks } from '../../lib/player/api';
import { meterTaps } from '../../lib/player/audioGraph';
import { laneOf, lanesFor } from '../../lib/player/lanes';
import {
  clipState,
  dbfs,
  drawMeter,
  holdPeak,
  laneAt,
  METER_FLOOR,
  readLevels,
} from '../../lib/player/meter';
import {
  clock,
  FOLD_MS,
  FOLD_PULL,
  fractionAt,
  GRAB_CLICK,
  keyAction,
  modeFor,
  outline,
  scrubSeekDue,
  SEEK_STEP,
  seekOnRelease,
  shownTime,
  WAVE_HEIGHT,
  WAVE_WIDTH,
} from '../../lib/player/transport';
import { useAnalysis } from './analysis';
import {
  BackIcon,
  ChevronDownIcon,
  CogIcon,
  ForwardIcon,
  PauseIcon,
  PlayIcon,
  ReplayIcon,
  SpinnerIcon,
  VolumeIcon,
  VolumeOffIcon,
} from './Icons';
import {
  CloseButton,
  Cover,
  CoverLightbox,
  Findings,
  Lanes,
  PlayerSettings,
} from './Parts';
import { usePlayer } from './PlayerContext';
import { useSetting } from './settings';
import React, {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from 'react';

/*
 * The player bar, in its three shapes.
 *
 * Twin: PlayerBar in media-bridge web/app/src/features/plex/player.tsx, whose
 * shapes, gestures and timings these are. A fix to one belongs in the other.
 *
 * Wide is the bar as it always was, docked above the footer. A phone has no
 * room for a waveform beside a title beside a meter -- squeezed into one row
 * each came out a sliver -- so there it is a mini bar joined to the top of the
 * bottom menu, saying what is playing and pausing it, and opening into a drawer
 * with room for everything. The menu stays where it is: a player is a state,
 * not a page, and pausing has to be one tap from anywhere.
 */

// the fork's phone layout, the same media query App.css uses
const PHONE =
  'only screen and (max-width: 767px), only screen and (max-height: 500px) and (orientation: landscape)';

const phoneNow = () => window.matchMedia?.(PHONE).matches ?? false;

const usePhone = () => {
  const [phone, setPhone] = useState(phoneNow);

  useEffect(() => {
    const list = window.matchMedia?.(PHONE);

    if (!list) {
      return undefined;
    }

    const change = () => setPhone(list.matches);
    list.addEventListener('change', change);

    return () => list.removeEventListener('change', change);
  }, []);

  return phone;
};

// a Semantic modal is open: Escape is its, not the player's
const modalOpen = () =>
  Boolean(document.querySelector('.ui.modals.dimmer.active'));

// a native <dialog> is open -- media-bridge's send and Compare dialogs, which
// it injects into this page. Every key is its: they listen on document in the
// capture phase, and this listens on window, which runs first, so without this
// Escape cancelled the dialog *and* stopped the player, and an arrow or a
// digit in one seeked the track
const dialogOpen = () => Boolean(document.querySelector('dialog[open]'));

// the meter's neutrals, read from the theme so an empty bar reads on a white
// page as well as a black one
const meterColours = () => {
  const style = getComputedStyle(document.documentElement);

  return {
    empty: style.getPropertyValue('--player-meter-bar').trim() || undefined,
    light: style.getPropertyValue('--player-meter-light').trim() || undefined,
    rule: style.getPropertyValue('--player-meter-rule').trim() || undefined,
  };
};

/*
 * What the lock screen, Control Centre and headphones see. Twin: media-bridge
 * mediaSession.ts. The handlers call the same functions the buttons do, so the
 * lock screen cannot drift from the bar; they exist only while there is a
 * track, because iOS keeps its Now Playing panel up for as long as a page has
 * handlers, and an empty one with nothing to drive it reads as broken.
 */
const useMediaSession = (player) => {
  const latest = useRef(player);
  latest.current = player;
  const { art, current, playing } = player;
  const session =
    typeof navigator !== 'undefined' ? navigator.mediaSession : undefined;

  useEffect(() => {
    if (!session) {
      return;
    }

    try {
      session.metadata =
        current && window.MediaMetadata
          ? new window.MediaMetadata({
              album: current.album ?? '',
              artist: current.artist ?? current.username ?? '',
              artwork: art
                ? [{ src: new URL(art, window.location.href).href }]
                : [],
              title: current.title,
            })
          : null;
    } catch {
      session.metadata = null;
    }
  }, [art, current, session]);

  const active = Boolean(current);

  useEffect(() => {
    if (!session || !active) {
      return undefined;
    }

    const handle = (action, handler) => {
      try {
        session.setActionHandler(action, handler);
      } catch {
        // an action this browser does not know
      }
    };

    const element = () => latest.current.audio.current;
    handle('play', () => !latest.current.playing && latest.current.toggle());
    handle('pause', () => latest.current.playing && latest.current.toggle());
    handle('stop', () => latest.current.stop());
    handle(
      'seekto',
      (details) =>
        details.seekTime != null && latest.current.seek(details.seekTime),
    );
    handle('seekbackward', (details) =>
      latest.current.seek(
        Math.max(
          0,
          (element()?.currentTime ?? 0) - (details.seekOffset ?? SEEK_STEP),
        ),
      ),
    );
    handle('seekforward', (details) =>
      latest.current.seek(
        (element()?.currentTime ?? 0) + (details.seekOffset ?? SEEK_STEP),
      ),
    );

    return () => {
      for (const action of [
        'play',
        'pause',
        'stop',
        'seekto',
        'seekbackward',
        'seekforward',
      ]) {
        handle(action, null);
      }

      session.metadata = null;
      session.playbackState = 'none';
    };
  }, [active, session]);

  useEffect(() => {
    if (session) {
      session.playbackState = !current
        ? 'none'
        : playing
          ? 'playing'
          : 'paused';
    }
  }, [current, playing, session]);
};

const PlayerBar = () => {
  const player = usePlayer();

  if (!player?.current) {
    return null;
  }

  return <Bar player={player} />;
};

// one component with three shapes, as in media-bridge: splitting it would put
// the same state behind three sets of props
// eslint-disable-next-line complexity
const Bar = ({ player }) => {
  const { at, current, ended, failed, loading, playing, seek, stop, toggle } =
    player;
  const artworkOn = useSetting('slskd-player-artwork');
  const meterOn = useSetting('slskd-player-meter');
  const cursorOn = useSetting('slskd-player-cursor');
  const lanesOn = useSetting('slskd-player-lanes');
  const volumeOn = useSetting('slskd-player-volume-control');

  const bar = useRef(null);
  const dock = useRef(null);
  const meter = useRef(null);
  const [hover, setHover] = useState(null);
  const [scrub, setScrub] = useState(null);
  const lastScrubSeek = useRef(0);
  const lastScrubAt = useRef(null);
  const [zoom, setZoom] = useState(false);
  const [settings, setSettings] = useState(false);
  const [explain, setExplain] = useState(null);

  const phone = usePhone();
  const [expanded, setExpanded] = useState(false);
  const [folding, setFolding] = useState(false);
  const [dragY, setDragY] = useState(null);
  const swipeFrom = useRef(null);
  const grabFrom = useRef(null);
  const foldTimer = useRef();
  const mode = modeFor(phone, expanded);

  useMediaSession(player);

  // --- the waveform and the analysis ---------------------------------------

  const [peaks, setPeaks] = useState({ data: null, id: null });

  useEffect(() => {
    const controller = new AbortController();
    setPeaks({ data: null, id: current.id });

    (async () => {
      try {
        const data = await getPeaks(current.id, { signal: controller.signal });
        setPeaks({ data, id: current.id });
      } catch {
        setPeaks({ data: null, failed: true, id: current.id });
      }
    })();

    return () => controller.abort();
  }, [current.id]);

  const wave = peaks.id === current.id ? peaks.data : null;
  const pending = peaks.id === current.id && !peaks.data && !peaks.failed;

  // the decoded length: a container's own can be unknown until enough of it has
  // arrived, and a truncated file disagrees with its header
  const length = wave?.duration ?? 0;

  const answer = useAnalysis(current.id, {
    enabled: player.analysisOn,
    wait: true,
  });
  const analysis = answer?.state === 'ready' ? answer.analysis : null;
  const lanes = lanesOn ? lanesFor(analysis) : [];
  const clipLane = laneOf(analysis, 'clipping');
  const verdict = analysis?.verdict;

  // --- folding the drawer --------------------------------------------------

  useEffect(() => setExpanded(false), [current.id]);

  // folding plays the opening backwards, so the drawer stays mounted until it
  // ends -- on a timer as well as the animation, because an event that never
  // comes (reduced motion, a background tab) must not strand it half shut
  const fold = useCallback(() => {
    if (foldTimer.current !== undefined) {
      return;
    }

    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      setDragY(null);
      setExpanded(false);
      return;
    }

    setFolding(true);
    foldTimer.current = window.setTimeout(() => {
      foldTimer.current = undefined;
      setFolding(false);
      setDragY(null);
      setExpanded(false);
    }, FOLD_MS);
  }, []);

  useEffect(() => () => window.clearTimeout(foldTimer.current), []);

  // the More menu opening is somewhere else to go: the drawer gets out of its way
  useEffect(() => {
    if (mode !== 'full') {
      return undefined;
    }

    const observer = new MutationObserver(() => {
      if (document.documentElement.classList.contains('nav-open')) {
        fold();
      }
    });
    observer.observe(document.documentElement, {
      attributeFilter: ['class'],
      attributes: true,
    });

    return () => observer.disconnect();
  }, [fold, mode]);

  // the drag, as a native listener: React's are passive, and a passive one cannot
  // stop iOS scrolling the drawer's content under the finger while it moves the
  // drawer -- the two motions fight and the sheet judders
  useEffect(() => {
    const element = bar.current;

    if (!element || mode !== 'full') {
      return undefined;
    }

    const move = (event) => {
      const from = swipeFrom.current;

      if (from === null) {
        return;
      }

      const dy = event.touches[0].clientY - from;

      if (dy > 0) {
        event.preventDefault();
        setDragY(dy);
      } else if (dy < 0) {
        // up from the top is a scroll of the content, not a drag
        swipeFrom.current = null;
        setDragY(null);
      }
    };

    element.addEventListener('touchmove', move, { passive: false });

    return () => element.removeEventListener('touchmove', move);
  }, [mode]);

  // the page under the drawer must not scroll behind it, and the fixed things
  // the drawer covers are told so on the root
  useEffect(() => {
    if (mode !== 'full') {
      return undefined;
    }

    // not `player-full`: that is the drawer's own class, and on the root it
    // matched the drawer's rules too -- position: fixed, its top offset and
    // its padding -- so the whole page dropped 41px while the drawer was open
    document.documentElement.classList.add('player-drawer-open');

    return () =>
      document.documentElement.classList.remove('player-drawer-open');
  }, [mode]);

  // --- the dock ------------------------------------------------------------

  // the bar's height, measured rather than guessed -- it grows when lanes open
  // -- and published for the layout: the page reserves it, and the footer and
  // toasts stand above it. full screen keeps the mini bar's, which is what is
  // docked underneath the drawer
  useLayoutEffect(() => {
    const element = dock.current;

    if (!element) {
      return undefined;
    }

    const apply = () =>
      document.documentElement.style.setProperty(
        '--player-dock-h',
        `${element.getBoundingClientRect().height}px`,
      );
    apply();
    const observer = new ResizeObserver(apply);
    observer.observe(element);

    return () => observer.disconnect();
  }, [mode]);

  useEffect(
    () => () =>
      document.documentElement.style.removeProperty('--player-dock-h'),
    [],
  );

  // --- the meter -----------------------------------------------------------

  const levels = useRef([METER_FLOOR, METER_FLOOR]);
  const holds = useRef([METER_FLOOR, METER_FLOOR]);
  const clipAt = useRef(0);

  // the clip light asks the file, not the meter: it is about where the playhead
  // is, and a lossy decode overshoots a clean file
  useEffect(() => {
    if (playing && length && clipLane && laneAt(clipLane, at / length) > 0) {
      clipAt.current = Date.now();
    }
  }, [at, clipLane, length, playing]);

  const paintMeter = useCallback(() => {
    const canvas = meter.current;
    const context = canvas?.getContext('2d');

    if (!context) {
      return;
    }

    const since = clipAt.current
      ? Date.now() - clipAt.current
      : Number.POSITIVE_INFINITY;
    drawMeter(
      context,
      canvas.width,
      canvas.height,
      {
        clip: clipState(clipLane, since),
        holds: holds.current,
        levels: levels.current,
      },
      meterColours(),
    );
  }, [clipLane]);

  // read from the graph every frame while it plays. the graph is not built
  // here: it is started inside the press (audioGraph.js), and this only reads it
  useEffect(() => {
    if (!playing || !meterOn) {
      // fall to silence rather than freeze mid-bar, which reads as a crash
      levels.current = [METER_FLOOR, METER_FLOOR];
      holds.current = [METER_FLOOR, METER_FLOOR];
      paintMeter();
      return undefined;
    }

    let frame = 0;
    const tick = () => {
      const taps = meterTaps();

      if (taps) {
        const now = readLevels(taps).map(dbfs);
        levels.current = now;
        holds.current = now.map((db, index) =>
          holdPeak(db, holds.current[index]),
        );
      }

      paintMeter();
      frame = requestAnimationFrame(tick);
    };

    frame = requestAnimationFrame(tick);

    return () => cancelAnimationFrame(frame);
  }, [meterOn, mode, paintMeter, playing]);

  // --- the keyboard --------------------------------------------------------

  // the latest of what a key press needs, so the listener is added once rather
  // than on every tick of the playhead
  const keys = useRef();
  keys.current = { at, expanded, fold, length, seek, stop, toggle };

  useEffect(() => {
    const onKey = (event) => {
      if (event.defaultPrevented || dialogOpen()) {
        return;
      }

      const latest = keys.current;
      const action = keyAction(event, { at: latest.at, length: latest.length });

      if (!action) {
        return;
      }

      if (action.type === 'escape') {
        // innermost first: a modal over the player, then the drawer, then the
        // player itself -- each press closing one thing
        if (modalOpen()) {
          return;
        }

        if (latest.expanded) {
          latest.fold();
        } else {
          latest.stop();
        }

        return;
      }

      event.preventDefault();

      if (action.type === 'toggle') {
        latest.toggle();
      } else {
        latest.seek(action.to);
      }
    };

    // on window, in the capture phase: before Semantic's own handler, which
    // listens on document and closes a modal on the same Escape. listening
    // after it, the modal was already gone when the player asked, and one
    // press closed the sheet and the player. same-target listeners run in the
    // order they were added, so document alone is not enough -- and this one
    // used to be re-added on every tick of the playhead, which put it last
    window.addEventListener('keydown', onKey, true);

    return () => window.removeEventListener('keydown', onKey, true);
  }, []);

  // --- scrubbing -----------------------------------------------------------

  // a tap seeks; a drag scrubs, the playhead following the finger at once and
  // the audio following it in steps, so a drag plays a moment of each place it
  // passes. pointer events serve touch and mouse alike, and capture keeps a drag
  // that strays off the strip
  const onScrubStart = (event) => {
    if (!length || (event.pointerType === 'mouse' && event.button !== 0)) {
      return;
    }

    event.currentTarget.setPointerCapture?.(event.pointerId);
    const fraction = fractionAt(
      event.clientX,
      event.currentTarget.getBoundingClientRect(),
    );
    setScrub(fraction);
    lastScrubSeek.current = Date.now();
    lastScrubAt.current = fraction;
    seek(fraction * length);
  };

  const onScrubMove = (event) => {
    if (scrub === null) {
      return;
    }

    const fraction = fractionAt(
      event.clientX,
      event.currentTarget.getBoundingClientRect(),
    );
    setScrub(fraction);

    if (scrubSeekDue(Date.now(), lastScrubSeek.current)) {
      lastScrubSeek.current = Date.now();
      lastScrubAt.current = fraction;
      seek(fraction * length);
    }
  };

  const onScrubEnd = (event) => {
    if (scrub === null) {
      return;
    }

    const fraction = fractionAt(
      event.clientX,
      event.currentTarget.getBoundingClientRect(),
    );

    if (seekOnRelease(fraction, lastScrubAt.current, length)) {
      seek(fraction * length);
    }

    lastScrubAt.current = null;
    setScrub(null);
  };

  const shownAt = scrub !== null && length ? scrub * length : at;
  const path = wave ? outline(wave.peaks, WAVE_WIDTH, WAVE_HEIGHT) : '';

  // --- the parts -------------------------------------------------------------

  const art = player.art;
  const sub = (
    <>
      {current.artist || current.username}
      {verdict && (
        <>
          {' · '}
          <button
            className={`player-verdict player-verdict-${verdict.level}`}
            onClick={() => setExplain('')}
            title="Read the analysis"
            type="button"
          >
            {verdict.headline}
          </button>
        </>
      )}
      {failed && <span className="player-failed"> · {failed}</span>}
    </>
  );

  const transport = (size, glyph) => (
    <button
      aria-busy={loading || undefined}
      aria-label={playing ? 'Pause' : ended ? 'Play again' : 'Play'}
      className={`player-play${loading ? ' player-loading' : ''}`}
      onClick={() => toggle()}
      style={{ height: size, width: size }}
      type="button"
    >
      {/* both always drawn, CSS deciding which shows: the spinner gives way on
          hover and focus, so the one thing you might want during a slow load --
          to change your mind -- is never taken away */}
      <span className="player-glyph">
        {playing ? (
          <PauseIcon size={glyph} />
        ) : ended ? (
          <ReplayIcon size={glyph + 2} />
        ) : (
          <PlayIcon size={glyph} />
        )}
      </span>
      <span className="player-busy">
        <SpinnerIcon size={glyph + 2} />
      </span>
    </button>
  );

  const leading = (shape) => (
    <>
      {artworkOn && shape !== 'full' && (
        <Cover
          alt={`Cover of ${current.title}`}
          onOpen={shape === 'wide' && art ? () => setZoom(true) : undefined}
          size={shape === 'mini' ? 40 : 48}
          src={art}
        />
      )}
      {shape !== 'full' && transport(42, 16)}
      <div className={`player-meta player-meta-${shape}`}>
        <div
          className="player-title"
          title={current.title}
        >
          {current.title}
        </div>
        <div className="player-sub">{sub}</div>
      </div>
    </>
  );

  const waveform = (
    // the playhead and the cursor are divs over the svg rather than lines in it:
    // the svg is stretched, which would scale a 1px line into a soft smear
    <div
      className="player-wave"
      // the cursor is a mouse's: it says where a click would land, and a finger
      // has no "would". pointer events tell them apart -- a phone follows each
      // tap with imitation mouse events, which left the line stuck where the
      // finger lifted
      onPointerDown={(event) => event.pointerType !== 'mouse' && setHover(null)}
      onPointerLeave={() => setHover(null)}
      onPointerMove={(event) =>
        event.pointerType === 'mouse' &&
        setHover(
          fractionAt(
            event.clientX,
            event.currentTarget.getBoundingClientRect(),
          ),
        )
      }
    >
      <svg
        className="player-wave-svg"
        onPointerCancel={() => setScrub(null)}
        onPointerDown={onScrubStart}
        onPointerMove={onScrubMove}
        onPointerUp={onScrubEnd}
        preserveAspectRatio="none"
        role="presentation"
        viewBox={`0 0 ${WAVE_WIDTH} ${WAVE_HEIGHT}`}
      >
        {path && (
          <path
            className="player-wave-unplayed"
            d={path}
          />
        )}
        {path && length > 0 && (
          <>
            <defs>
              <clipPath id="player-played">
                <rect
                  height={WAVE_HEIGHT}
                  width={(shownAt / length) * WAVE_WIDTH}
                  x="0"
                  y="0"
                />
              </clipPath>
            </defs>
            {/* one shape under a clip, rather than two paths from the same
                numbers: one of them would end up drawn from a stale copy */}
            <path
              className="player-wave-played"
              clipPath="url(#player-played)"
              d={path}
            />
          </>
        )}
      </svg>
      {pending && (
        <span
          aria-label="Loading the waveform"
          className="player-wave-wait"
          role="status"
        />
      )}
      {length > 0 && (
        <span
          aria-hidden="true"
          className="player-playhead"
          style={{ left: `${(shownAt / length) * 100}%` }}
        />
      )}
      {cursorOn && hover !== null && length > 0 && (
        <>
          <span
            aria-hidden="true"
            className="player-cursor"
            style={{ left: `${hover * 100}%` }}
          />
          <span
            aria-hidden="true"
            className="player-cursor-time"
            style={{ left: `${hover * 100}%` }}
          >
            {clock(hover * length)}
          </span>
        </>
      )}
      <Lanes
        lanes={lanes}
        onExplain={(id) => setExplain(id)}
        onSeek={(fraction) => seek(fraction * length)}
      />
    </div>
  );

  const theClock = (
    <span className="player-clock">
      {clock(shownTime(shownAt, length))}{' '}
      <span className="player-dim">/ {clock(length)}</span>
    </span>
  );

  const volume = volumeOn && (
    <div className="player-volume">
      <button
        aria-label={player.muted || player.volume === 0 ? 'Unmute' : 'Mute'}
        className="player-icon-button"
        onClick={() => player.setMuted(!player.muted)}
        type="button"
      >
        {player.muted || player.volume === 0 ? (
          <VolumeOffIcon />
        ) : (
          <VolumeIcon />
        )}
      </button>
      <input
        aria-label="Volume"
        className="player-range"
        max={1}
        min={0}
        onChange={(event) => player.setVolume(Number(event.target.value))}
        step={0.01}
        style={{
          '--player-range-fill': `${(player.muted ? 0 : player.volume) * 100}%`,
        }}
        type="range"
        value={player.volume}
      />
    </div>
  );

  const meterCanvas = meterOn && (
    <canvas
      aria-hidden="true"
      className="player-meter"
      height={56}
      ref={meter}
      title="Bars: the level of the stream you are hearing (red means loud, not clipping — a lossy decode overshoots). Light: whether the file itself clips — dark none, dim somewhere, bright here."
      width={34}
    />
  );

  const cog = (
    <button
      aria-label="Player settings"
      className="player-icon-button player-cog"
      onClick={() => setSettings(true)}
      title="Player settings"
      type="button"
    >
      <CogIcon />
    </button>
  );

  const progress = length > 0 && (
    <span
      aria-hidden="true"
      className="player-progress"
      style={{ width: `${Math.min(1, at / length) * 100}%` }}
    />
  );

  const overlays = (
    <>
      {settings && <PlayerSettings onClose={() => setSettings(false)} />}
      {zoom && art && (
        <CoverLightbox
          alt={`Cover of ${current.title}`}
          onClose={() => setZoom(false)}
          src={art}
        />
      )}
      {explain !== null && analysis && (
        <Findings
          analysis={analysis}
          focus={explain}
          onClose={() => setExplain(null)}
          onSeek={(seconds) => seek(seconds)}
          title={current.title}
        />
      )}
    </>
  );

  if (mode === 'wide') {
    return (
      <div
        aria-label="Player"
        className="player player-wide"
        ref={(element) => {
          bar.current = element;
          dock.current = element;
        }}
        role="region"
      >
        {leading('wide')}
        {waveform}
        {meterCanvas}
        {theClock}
        {volume}
        {cog}
        <CloseButton
          className="player-icon-button player-close"
          onClick={stop}
        />
        {overlays}
      </div>
    );
  }

  const mini = (
    <>
      {progress}
      {leading('mini')}
      <CloseButton
        className="player-icon-button player-close"
        onClick={stop}
      />
    </>
  );

  if (mode === 'mini') {
    return (
      <div
        aria-label="Player"
        className="player player-mini"
        ref={(element) => {
          bar.current = element;
          dock.current = element;
        }}
        role="region"
      >
        {/* the whole bar opens the player; the two buttons sit above this */}
        <button
          aria-label="Open the player"
          className="player-expand"
          onClick={() => setExpanded(true)}
          type="button"
        />
        {mini}
        {overlays}
      </div>
    );
  }

  const grabDown = (event) => {
    if (event.pointerType === 'touch' || event.button !== 0) {
      return;
    }

    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    grabFrom.current = event.clientY;
  };

  const grabMove = (event) => {
    if (grabFrom.current === null) {
      return;
    }

    const dy = event.clientY - grabFrom.current;
    setDragY(dy > 0 ? dy : null);
  };

  const grabUp = (event) => {
    if (grabFrom.current === null) {
      return;
    }

    const dy = event.clientY - grabFrom.current;
    grabFrom.current = null;

    if (dy > FOLD_PULL || Math.abs(dy) < GRAB_CLICK) {
      fold();
    } else {
      setDragY(null);
    }
  };

  return (
    <>
      {/* the mini bar, already in place under the drawer, so folding never shows
          the page with no player for a beat. drawn from the same parts, inert.
          keyed so React never reuses this element as the real mini bar: same
          tag, same class, same place, so on folding it did, and `inert` --
          set here by hand, so not React's to remove -- stayed on. the mini bar
          then let every tap through to the page, and the player could not be
          opened again */}
      <div
        aria-hidden="true"
        className="player player-mini"
        key="player-mini-standin"
        ref={(element) => {
          dock.current = element;
          element?.setAttribute('inert', '');
        }}
      >
        {mini}
      </div>
      {/* the page behind, dimmed; a tap on it folds the drawer */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
      <div
        aria-hidden="true"
        className={`player-backdrop${folding ? ' player-folding' : ''}`}
        onClick={fold}
        style={
          dragY !== null && !folding
            ? {
                opacity: Math.max(
                  0,
                  1 - dragY / (bar.current?.offsetHeight || 600),
                ),
              }
            : undefined
        }
      />
      <div
        aria-label="Player"
        className={`player player-full${folding ? ' player-folding' : ''}${dragY !== null && !folding ? ' player-dragging' : ''}`}
        // a drawer comes down the way it went up -- from the top of its content
        // only, and not across the waveform, where a drag aims at a place
        onTouchCancel={() => {
          swipeFrom.current = null;
          setDragY(null);
        }}
        onTouchEnd={(event) => {
          const from = swipeFrom.current;
          swipeFrom.current = null;

          if (
            from !== null &&
            event.changedTouches[0].clientY - from > FOLD_PULL
          ) {
            fold();
          } else {
            setDragY(null);
          }
        }}
        onTouchStart={(event) => {
          const inWave = event.target.closest?.('.player-wave');
          swipeFrom.current =
            !inWave && event.currentTarget.scrollTop === 0
              ? event.touches[0].clientY
              : null;
        }}
        ref={bar}
        role="dialog"
        style={
          dragY !== null ? { transform: `translateY(${dragY}px)` } : undefined
        }
      >
        <span
          aria-hidden="true"
          className="player-grab"
          onPointerCancel={() => {
            grabFrom.current = null;
            setDragY(null);
          }}
          onPointerDown={grabDown}
          onPointerMove={grabMove}
          onPointerUp={grabUp}
        />
        <button
          aria-label="Minimise player"
          className="player-icon-button player-collapse"
          onClick={fold}
          title="Minimise"
          type="button"
        >
          <ChevronDownIcon />
        </button>
        {cog}
        <CloseButton
          className="player-icon-button player-close"
          onClick={stop}
        />
        {artworkOn && (
          <div className="player-bigcover">
            <Cover
              alt={`Cover of ${current.title}`}
              onOpen={art ? () => setZoom(true) : undefined}
              size={280}
              src={art}
            />
          </div>
        )}
        {leading('full')}
        {waveform}
        {theClock}
        {meterCanvas || <span className="player-meter-space" />}
        <button
          aria-label={`Back ${SEEK_STEP} seconds`}
          className="player-skip player-back"
          onClick={() => seek(Math.max(0, at - SEEK_STEP))}
          type="button"
        >
          <BackIcon />
        </button>
        <div className="player-transport">{transport(64, 24)}</div>
        <button
          aria-label={`Forward ${SEEK_STEP} seconds`}
          className="player-skip player-forward"
          onClick={() =>
            seek(length ? Math.min(length, at + SEEK_STEP) : at + SEEK_STEP)
          }
          type="button"
        >
          <ForwardIcon />
        </button>
        {volume || <span className="player-volume-space" />}
        {overlays}
      </div>
    </>
  );
};

export default PlayerBar;
