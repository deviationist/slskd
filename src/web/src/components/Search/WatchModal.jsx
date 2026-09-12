import * as optionsLibrary from '../../lib/options';
import * as library from '../../lib/watches';
import React, { useEffect, useState } from 'react';
import {
  Button,
  Checkbox,
  Dropdown,
  Form,
  Icon,
  Input,
  Message,
  Modal,
} from 'semantic-ui-react';

const hourOptions = Array.from({ length: 24 }, (_, hour) => ({
  key: hour,
  text: `${String(hour).padStart(2, '0')}:00`,
  value: hour,
}));

const presetOptions = library.PRESETS.map((preset) => ({
  key: preset.key,
  text: preset.label,
  value: preset.key,
}));

const PRESETS_BY_KEY = Object.fromEntries(
  library.PRESETS.map((preset) => [preset.key, preset]),
);

/**
 * The fields of a watch.
 *
 * Exported, and separate from the modal around it, so that it can be rendered
 * in a test. Semantic's Modal renders through a portal and produces nothing at
 * all server-side, so a test of the whole modal asserts nothing -- which is how
 * a version of this shipped with every field silently dropped.
 * @param {object} params
 * @param {string} params.configured - The address mail falls back to, if any.
 * @param {object} params.draft - The draft being edited.
 * @param {Function} params.onSearchTextChange - Called when the phrase is edited.
 * @param {Function} params.set - Applies changes to the draft.
 * @param {object} params.existing - The watch being edited, if any.
 * @param {string} params.searchText - The search this watch is for.
 * @returns {object} The form.
 */
export const WatchForm = ({
  configured = undefined,
  draft,
  existing,
  onSearchTextChange = () => {},
  searchText,
  set,
}) => {
  const takesHour = PRESETS_BY_KEY[draft.key]?.hour;

  return (
    <Form>
      {existing ? (
        // a watch's search cannot be changed once it exists: the search is
        // already created, and what the watch has reported is keyed on its id.
        // shown as a heading rather than a field nobody can use
        <Form.Field>
          <strong>{searchText}</strong>
        </Form.Field>
      ) : (
        <Form.Field
          control={Input}
          label="Search"
          onChange={(_event, { value }) => onSearchTextChange(value)}
          value={searchText ?? ''}
        />
      )}
      <Form.Group widths="equal">
        <Form.Field
          control={Dropdown}
          fluid
          label="How often"
          onChange={(_event, { value }) => set({ key: value })}
          options={presetOptions}
          selection
          value={draft.key}
        />
        {takesHour && (
          <Form.Field
            control={Dropdown}
            fluid
            label="At"
            onChange={(_event, { value }) => set({ hour: value })}
            options={hourOptions}
            selection
            value={draft.hour}
          />
        )}
      </Form.Group>
      <Form.Field
        control={Input}
        label="Only report files matching"
        onChange={(_event, { value }) => set({ filter: value })}
        placeholder="islossless minbitdepth:24 -bootleg"
        value={draft.filter}
      />
      <Form.Field>
        <small>
          The same filter the results page uses. Note that an attribute no peer
          reported counts as zero, so a minimum excludes a file whose value is
          unknown.
        </small>
      </Form.Field>
      <Form.Field
        control={Input}
        label="Email"
        onChange={(_event, { value }) => set({ notifyEmail: value })}
        placeholder={
          configured
            ? `Leave blank to use ${configured}`
            : 'Leave blank to use the configured address'
        }
        value={draft.notifyEmail}
      />
      <Form.Field>
        <Checkbox
          checked={draft.includeLocked}
          label="Report locked files and folders too"
          onChange={() => set({ includeLocked: !draft.includeLocked })}
          toggle
        />
      </Form.Field>
      <Form.Field>
        <Checkbox
          checked={draft.requireFreeSlot}
          label="Only report peers with a free upload slot"
          onChange={() => set({ requireFreeSlot: !draft.requireFreeSlot })}
          toggle
        />
      </Form.Field>
      {!existing && (
        <Form.Field>
          <Checkbox
            checked={draft.seed}
            label="Don't tell me what this search has already found"
            onChange={() => set({ seed: !draft.seed })}
            toggle
          />
          <div>
            <small>
              Records what is here now without reporting it, so the first email
              is the first genuinely new thing.
            </small>
          </div>
        </Form.Field>
      )}
    </Form>
  );
};

/**
 * Creates or edits a watch.
 *
 * Every decision this makes — what a preset means, whether a draft can be
 * saved, how a schedule reads — lives in lib/watches.js, where it can be
 * tested. This renders.
 */
const WatchModal = ({
  existing = undefined,
  onClose,
  onSave,
  onSearchTextChange = () => {},
  open,
  searchText,
}) => {
  const initial = library.presetFor(existing?.rrule) ?? {
    hour: library.DEFAULT_HOUR,
    key: library.DEFAULT_PRESET,
  };

  const [draft, setDraft] = useState({
    filter: existing?.filter ?? '',
    hour: initial.hour,
    includeLocked: existing?.includeLocked ?? false,
    key: initial.key,
    notifyEmail: existing?.notifyEmail ?? '',
    requireFreeSlot: existing?.requireFreeSlot ?? false,
    seed: true,
  });
  const [saving, setSaving] = useState(false);
  const [configured, setConfigured] = useState(undefined);

  useEffect(() => {
    // the address a blank field falls back to. worth naming rather than
    // alluding to: "the configured address" does not answer the question an
    // operator actually has, which is whether there is one at all
    const load = async () => {
      try {
        const options = await optionsLibrary.getCurrent();

        setConfigured(options?.integrations?.mail?.to || undefined);
      } catch {
        // the placeholder falls back to the vaguer wording
      }
    };

    load();
  }, []);

  const set = (values) => setDraft((old) => ({ ...old, ...values }));

  const validation = library.validateDraft({ ...draft, searchText });
  const rrule = library.rruleFor({ hour: draft.hour, key: draft.key });

  const save = async () => {
    setSaving(true);

    try {
      await onSave({
        enabled: existing?.enabled ?? true,
        filter: draft.filter,
        includeLocked: draft.includeLocked,
        notifyEmail: draft.notifyEmail.trim(),
        requireFreeSlot: draft.requireFreeSlot,
        rrule,
        // only meaningful when the watch is created; a watch that has been
        // running has a memory, and re-seeding it would silence files it has
        // never reported
        seedFromCurrentResults: existing ? false : draft.seed,
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      className="watch-modal"
      closeIcon
      onClose={onClose}
      open={open}
      size="small"
    >
      <Modal.Header>
        <Icon name="clock outline" />
        {existing ? 'Edit watch' : 'Watch this search'}
      </Modal.Header>
      <Modal.Content>
        <p>
          {existing ? 'This search is re-run' : 'Re-run'}
          {
            ' this search on a schedule, and email when a file appears that has '
          }
          not been reported before.
        </p>
        <WatchForm
          configured={configured}
          draft={draft}
          existing={existing}
          onSearchTextChange={onSearchTextChange}
          searchText={searchText}
          set={set}
        />
        {validation.ok ? (
          <Message info>
            <Icon name="calendar outline" />
            {library.describeRecurrence(rrule)}
          </Message>
        ) : (
          <Message warning>{validation.reason}</Message>
        )}
      </Modal.Content>
      <Modal.Actions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          disabled={!validation.ok || saving}
          loading={saving}
          onClick={save}
          positive
        >
          {existing ? 'Save' : 'Watch'}
        </Button>
      </Modal.Actions>
    </Modal>
  );
};

export default WatchModal;
