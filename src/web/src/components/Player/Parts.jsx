import { labelSide, markOpacity, runs } from '../../lib/player/lanes';
import { SETTINGS } from '../../lib/player/remember';
import { isPlayable, trackOf } from '../../lib/player/rows';
import { useAnalysis } from './analysis';
import { CloseIcon, NoteIcon, PauseIcon, PlayIcon, SpinnerIcon } from './Icons';
import { usePlayer } from './PlayerContext';
import { changeSetting, useSetting } from './settings';
import React, { useEffect, useState } from 'react';
import { Checkbox, Modal } from 'semantic-ui-react';

/*
 * The sleeve, from the file's own tag block -- a 204 when it carries none, which
 * an <img> reports as an error, and that is what draws the empty frame. An empty
 * frame of the same size
 * when there is none, so the bar keeps its shape between tracks; a button only
 * when there is a picture behind it, because a control that opens nothing is
 * worse than none.
 */
export const Cover = ({ alt, onOpen, size, src }) => {
  const [failed, setFailed] = useState(false);

  useEffect(() => setFailed(false), [src]);

  const style = { height: size, width: size };

  if (!src || failed) {
    return (
      <span
        aria-hidden="true"
        className="player-cover player-cover-empty"
        style={style}
      >
        <NoteIcon size={Math.round(size * 0.4)} />
      </span>
    );
  }

  const image = (
    <img
      alt={alt}
      draggable={false}
      onError={() => setFailed(true)}
      src={src}
    />
  );

  return onOpen ? (
    <button
      aria-label="Show the cover larger"
      className="player-cover"
      onClick={onOpen}
      style={style}
      type="button"
    >
      {image}
    </button>
  ) : (
    <span
      className="player-cover"
      style={style}
    >
      {image}
    </span>
  );
};

export const CoverLightbox = ({ alt, onClose, src }) => (
  <Modal
    basic
    className="player-lightbox"
    closeIcon
    onClose={onClose}
    open
    size="small"
  >
    <Modal.Content>
      <img
        alt={alt}
        src={src}
      />
    </Modal.Content>
  </Modal>
);

/*
 * The problem lanes: a flat timeline under the waveform, one row per thing
 * wrong that has a place in the track, on the waveform's own x-axis. A click
 * seeks there and opens the findings, because seeking alone leaves "what am I
 * listening for" and explaining alone leaves scrubbing for the moment by hand.
 */
export const Lanes = ({ lanes, onExplain, onSeek }) => {
  if (!lanes.length) {
    return null;
  }

  return (
    <div className="player-lanes">
      {lanes.map(({ finding, lane }) => (
        <button
          className={`player-lane player-lane-${finding.level}`}
          key={finding.id}
          onClick={(event) => {
            const box = event.currentTarget.getBoundingClientRect();
            onSeek(
              Math.min(1, Math.max(0, (event.clientX - box.left) / box.width)),
            );
            onExplain(finding.id);
          }}
          title={`${finding.label}: tap to hear it and read why`}
          type="button"
        >
          {runs(lane).map((run) => (
            <i
              key={run.from}
              style={{
                left: `${run.from * 100}%`,
                // never narrower than a hairline: a one-bucket fault is still a
                // fault, and a zero-width mark is one nobody can see or tap
                opacity: markOpacity(run.weight),
                width: `max(2px, ${(run.to - run.from) * 100}%)`,
              }}
            />
          ))}
          <span
            className={`player-lane-name${labelSide(lane) === 'right' ? ' player-at-end' : ''}`}
          >
            {finding.label}
          </span>
        </button>
      ))}
    </div>
  );
};

// text as the contract sends it: runs, some of them a point in the track
export const Runs = ({ onSeek, runs: parts = [] }) =>
  parts.map((part, index) =>
    part.seek == null || !onSeek ? (
      // eslint-disable-next-line react/no-array-index-key
      <React.Fragment key={index}>{part.text}</React.Fragment>
    ) : (
      <button
        className="player-seek"
        // eslint-disable-next-line react/no-array-index-key
        key={index}
        onClick={() => onSeek(part.seek)}
        title={`Play from ${part.text}`}
        type="button"
      >
        {part.text}
      </button>
    ),
  );

/*
 * The findings: what the analysis provider found, row by row, worst first, with
 * its times as places to jump to. A sheet rather than a panel in the row: it has
 * to open from the bar as well as from a badge, and on a phone a row has no room
 * to grow a box of prose.
 */
export const Findings = ({ analysis, focus, onClose, onSeek, title }) => {
  useEffect(() => {
    if (!focus) {
      return;
    }

    // where a lane tap lands. a colour that fades rather than a scroll alone:
    // the row may already be on screen, and then nothing would seem to happen
    const row = document.querySelector(
      `[data-player-finding="${CSS.escape(focus)}"]`,
    );
    row?.scrollIntoView({ block: 'nearest' });
    row?.classList.add('player-flash');
  }, [focus]);

  return (
    <Modal
      className="player-findings"
      closeIcon
      onClose={onClose}
      open
      size="small"
    >
      <Modal.Header>{title}</Modal.Header>
      <Modal.Content scrolling>
        <p className="player-findings-summary">{analysis.summary}</p>
        {(analysis.findings ?? []).map((finding) => (
          <div
            className={`player-finding player-finding-${finding.level}`}
            data-player-finding={finding.id}
            key={finding.id}
          >
            <div className="player-finding-head">
              <span className="player-finding-label">{finding.label}</span>
              <span className="player-finding-value">
                <Runs
                  onSeek={onSeek}
                  runs={finding.value}
                />
              </span>
            </div>
            {finding.note?.length > 0 && (
              <div className="player-finding-note">
                <Runs
                  onSeek={onSeek}
                  runs={finding.note}
                />
              </div>
            )}
          </div>
        ))}
        <p className="player-findings-provider">
          From {analysis.provider?.name ?? 'the analysis provider'}
        </p>
      </Modal.Content>
    </Modal>
  );
};

export const PlayerSettings = ({ onClose }) => (
  <Modal
    className="player-settings"
    closeIcon
    onClose={onClose}
    open
    size="tiny"
  >
    <Modal.Header>Player settings</Modal.Header>
    <Modal.Content>
      {SETTINGS.map((setting) => (
        <SettingRow
          key={setting.key}
          setting={setting}
        />
      ))}
      <p className="player-settings-note">Stored in this browser only.</p>
    </Modal.Content>
  </Modal>
);

const SettingRow = ({ setting }) => {
  const on = useSetting(setting.key);

  return (
    <div className="player-setting">
      <Checkbox
        checked={on}
        label={setting.label}
        onChange={(_, data) => changeSetting(setting.key, data.checked)}
        toggle
      />
      <p>{setting.why}</p>
    </div>
  );
};

/*
 * The play button on a row. Its icon is the player's state for this row --
 * pause while it plays, a spinner while it loads -- because "is that the one
 * playing" is the question a column of identical triangles cannot answer.
 */
export const RowPlay = ({ file }) => {
  const player = usePlayer();

  if (!player?.available || !isPlayable(file)) {
    return null;
  }

  const mine = player.current?.id === file.id;
  const label = mine && player.playing ? 'Pause' : 'Play';

  return (
    <button
      aria-label={`${label} ${trackOf(file).title}`}
      className={`player-row-play${mine ? ' player-active' : ''}`}
      data-mbp="player"
      onClick={(event) => {
        event.stopPropagation();
        player.toggle(trackOf(file));
      }}
      title={label}
      type="button"
    >
      {mine && player.loading ? (
        <SpinnerIcon size={12} />
      ) : mine && player.playing ? (
        <PauseIcon size={12} />
      ) : (
        <PlayIcon size={12} />
      )}
    </button>
  );
};

/*
 * The quality badge on a row: colour carries the verdict, the text the
 * headline. Drawn only once there is a verdict, and only when a provider is
 * configured -- a column of spinners for an answer that is never coming is
 * noise.
 */
export const RowBadge = ({ file }) => {
  const player = usePlayer();
  const enabled = Boolean(player?.analysisOn) && isPlayable(file);
  const answer = useAnalysis(file?.id, { enabled, wait: false });
  const [open, setOpen] = useState(false);

  if (
    !enabled ||
    !answer ||
    answer.state === 'off' ||
    answer.state === 'none'
  ) {
    return null;
  }

  if (answer.state !== 'ready') {
    return (
      <span
        aria-label="Analysing"
        className="player-badge player-badge-pending"
        data-mbp="player"
      >
        <SpinnerIcon size={10} />
      </span>
    );
  }

  const { verdict } = answer.analysis;

  return (
    <>
      <button
        className={`player-badge player-badge-${verdict.level}`}
        data-mbp="player"
        onClick={(event) => {
          event.stopPropagation();
          setOpen(true);
        }}
        title={`${verdict.headline}: tap to read the analysis`}
        type="button"
      >
        {verdict.headline}
      </button>
      {open && (
        <Findings
          analysis={answer.analysis}
          onClose={() => setOpen(false)}
          onSeek={(seconds) => player.play(trackOf(file), { at: seconds })}
          title={trackOf(file).title}
        />
      )}
    </>
  );
};

export const CloseButton = ({ className, onClick }) => (
  <button
    aria-label="Close player"
    className={className}
    onClick={onClick}
    title="Close"
    type="button"
  >
    <CloseIcon />
  </button>
);
