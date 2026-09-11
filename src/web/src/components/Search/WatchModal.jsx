import * as library from '../../lib/watches';
import React, { useState } from 'react';
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

  const set = (values) => setDraft((old) => ({ ...old, ...values }));

  const validation = library.validateDraft(draft);
  const rrule = library.rruleFor({ hour: draft.hour, key: draft.key });
  const takesHour = library.PRESETS.find((p) => p.key === draft.key)?.hour;

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
        <Form>
          <Form.Field label="Search">
            <Input
              disabled
              value={searchText ?? ''}
            />
          </Form.Field>
          <Form.Group widths="equal">
            <Form.Field label="How often">
              <Dropdown
                fluid
                onChange={(_event, { value }) => set({ key: value })}
                options={presetOptions}
                selection
                value={draft.key}
              />
            </Form.Field>
            {takesHour && (
              <Form.Field label="At">
                <Dropdown
                  fluid
                  onChange={(_event, { value }) => set({ hour: value })}
                  options={hourOptions}
                  selection
                  value={draft.hour}
                />
              </Form.Field>
            )}
          </Form.Group>
          <Form.Field label="Only report files matching">
            <Input
              onChange={(_event, { value }) => set({ filter: value })}
              placeholder="islossless minbitdepth:24 -bootleg"
              value={draft.filter}
            />
            <small>
              The same filter the results page uses. Note that an attribute no
              peer reported counts as zero, so a minimum excludes a file whose
              value is unknown.
            </small>
          </Form.Field>
          <Form.Field label="Email">
            <Input
              onChange={(_event, { value }) => set({ notifyEmail: value })}
              placeholder="leave blank to use the configured address"
              value={draft.notifyEmail}
            />
          </Form.Field>
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
                  Records what is here now without reporting it, so the first
                  email is the first genuinely new thing.
                </small>
              </div>
            </Form.Field>
          )}
        </Form>
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
