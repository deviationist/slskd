import './Compare.css';
import * as compareLibrary from '../../lib/compare';
import { fileNameOf } from '../../lib/player/rows';
import { RowPlay } from '../Player/Parts';
import React, { useEffect, useState } from 'react';
import { Button, Loader, Message, Modal } from 'semantic-ui-react';

const errorOf = (error) =>
  error?.response?.data ?? error?.message ?? String(error);

// the folder a remote filename sits in, which is the part of the path that
// tells two "01 Track.flac" apart
const folderOf = (filename) => {
  const parts = String(filename ?? '').split(/[/\\]/u);

  return parts.length > 1 ? parts.at(-2) : '';
};

/**
 * One row of the answer: the file, where it came from, what sets it apart.
 */
const Member = ({ file, musicSeconds, note }) => (
  <li className="compare-member">
    <RowPlay file={file} />
    <div className="compare-member-text">
      <div className="compare-member-name">{fileNameOf(file?.filename)}</div>
      <div className="compare-member-note">
        {[file?.username, folderOf(file?.filename), note]
          .filter(Boolean)
          .join(' · ')}
      </div>
    </div>
    <span className="compare-member-length">
      {compareLibrary.clock(musicSeconds)}
    </span>
  </li>
);

const Section = ({ children, count, title }) => (
  <section className="compare-section">
    <h4>
      {title}
      {count ? <span className="compare-count"> ({count})</span> : null}
    </h4>
    {children}
  </section>
);

/**
 * The provider's answer, drawn. Separate from the dialog so it can be drawn
 * without one: Semantic's Modal renders through a portal, and a test of the
 * whole modal would see nothing.
 * @param {object} params
 * @param {object} params.document - The provider's document.
 * @param {object[]} params.picks - The rows compared, in the order sent.
 * @returns {object} The answer.
 */
export const CompareAnswer = ({ document, picks }) => {
  const arranged = compareLibrary.arrange(document, picks);
  const { verdict } = arranged;
  const together = arranged.groups.length > 0;

  return (
    <>
      <Message
        className={`compare-verdict compare-verdict-${verdict.level}`}
        color={compareLibrary.LEVEL_COLORS[verdict.level]}
      >
        <Message.Header>{verdict.headline}</Message.Header>
        {verdict.detail && <p>{verdict.detail}</p>}
      </Message>
      {arranged.groups.map((members) => (
        <Section
          count={`${members.length} copies`}
          key={members.map((m) => m.file?.id).join(',')}
          title="Same track"
        >
          <ul className="compare-members">
            {members.map((member) => (
              <Member
                key={member.file?.id}
                {...member}
              />
            ))}
          </ul>
        </Section>
      ))}
      {arranged.alone.length > 0 && (
        // with no group, everything stands alone and the banner has said so
        <Section title={together ? 'Not the same as any other' : 'Compared'}>
          <ul className="compare-members">
            {arranged.alone.map((member) => (
              <Member
                key={member.file?.id}
                {...member}
              />
            ))}
          </ul>
        </Section>
      )}
      {arranged.partly.length > 0 && (
        <Section title="Partly the same">
          {arranged.partly.map((pair) => (
            <p
              className="compare-line"
              key={`${pair.a?.id}-${pair.b?.id}`}
            >
              {fileNameOf(pair.a?.filename)} and {fileNameOf(pair.b?.filename)}{' '}
              {pair.note}.
            </p>
          ))}
        </Section>
      )}
      {arranged.unreadable.length > 0 && (
        <Section title="Could not be read">
          {arranged.unreadable.map(({ file, note }) => (
            <p
              className="compare-line"
              key={file?.id}
            >
              {fileNameOf(file?.filename)} — {note}
            </p>
          ))}
        </Section>
      )}
    </>
  );
};

/**
 * Asks the server about `picks` until it has an answer: `pending` is asked
 * again after the seconds it gives, everything else is final.
 */
const useComparison = (picks) => {
  const [answer, setAnswer] = useState({ state: 'pending' });

  useEffect(() => {
    let current = true;
    let timer;
    const ids = picks.map((file) => file.id);

    const ask = async () => {
      try {
        const response = await compareLibrary.compare({ ids, wait: false });

        if (!current) return;

        if (response.state === 'pending') {
          timer = window.setTimeout(ask, (response.retryAfter ?? 3) * 1_000);
        }

        setAnswer(response);
      } catch (error) {
        if (current) setAnswer({ reason: errorOf(error), state: 'error' });
      }
    };

    ask();

    return () => {
      current = false;
      window.clearTimeout(timer);
    };
    // asked about once per set of rows: the picks are fixed for the dialog's life
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return answer;
};

const STATE_MESSAGES = {
  error: 'Could not compare',
  off: 'Compare is not set up on this server',
  refused: 'These cannot be compared',
  unavailable: 'Compare is unavailable just now',
};

/**
 * Which of the ticked downloads are the same track. Opens at once and fills
 * in when the answer lands: a first comparison decodes every file, and a
 * button saying "Checking…" for half a minute reads as stuck.
 * @param {object} params
 * @param {Function} params.onClose - Closes the dialog.
 * @param {object[]} params.picks - The rows to compare, in order.
 * @returns {object} The dialog.
 */
const CompareDialog = ({ onClose, picks }) => {
  const answer = useComparison(picks);

  return (
    <Modal
      className="compare-dialog"
      onClose={onClose}
      open
      size="small"
    >
      <Modal.Header>Comparing {picks.length} tracks</Modal.Header>
      <Modal.Content scrolling>
        <p className="compare-why">
          Whether these are the same recording, from the audio rather than the
          name — a renamed copy, a different rip, or a different mix.
        </p>
        {answer.state === 'pending' && (
          <div className="compare-waiting">
            <Loader
              active
              inline
              size="small"
            />
            <span>
              Listening… a file heard for the first time takes a few seconds.
            </span>
          </div>
        )}
        {answer.state === 'ready' && (
          <CompareAnswer
            document={answer.compare}
            picks={picks}
          />
        )}
        {STATE_MESSAGES[answer.state] && (
          <Message
            content={answer.reason}
            header={STATE_MESSAGES[answer.state]}
            warning
          />
        )}
      </Modal.Content>
      <Modal.Actions>
        <Button onClick={onClose}>Close</Button>
      </Modal.Actions>
    </Modal>
  );
};

export default CompareDialog;
