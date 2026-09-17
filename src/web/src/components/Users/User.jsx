import { Icon, Item } from 'semantic-ui-react';

const ImagePlaceholder = () => (
  <div className="users-picture-placeholder ui small image">
    <Icon
      name="camera"
      size="big"
    />
  </div>
);

const Presence = ({ presence }) => {
  const colors = {
    Away: 'yellow',
    Online: 'green',
  };

  return (
    <Icon
      color={colors[presence] || 'grey'}
      name="circle"
    />
  );
};

const FreeUploadSlot = ({ hasFreeUploadSlot }) => (
  <Icon
    color={hasFreeUploadSlot ? 'green' : 'red'}
    name={hasFreeUploadSlot ? 'check' : 'close'}
  />
);

/**
 * The facts about a user that came back, and only those.
 *
 * A user who is not connected answers none of these -- their slots, their
 * queue and their address are read from *them*, not from the server -- and
 * the line used to render as a row of labels with nothing after the colons.
 * What is missing is said in the message beside the card instead.
 * @param {object} user - The merged lookup.
 * @param {string} user.address - Their IP address.
 * @param {boolean} user.hasFreeUploadSlot - Whether they can send now.
 * @param {number} user.port - Their port.
 * @param {number} user.queueLength - How long their queue is.
 * @param {number} user.uploadSlots - How many slots they have.
 * @returns {object} The line, or nothing when there is none to draw.
 */
const Facts = ({
  address,
  hasFreeUploadSlot,
  port,
  queueLength,
  uploadSlots,
}) => {
  const facts = [
    hasFreeUploadSlot === undefined || {
      key: 'slot',
      label: 'Free Upload Slot',
      value: <FreeUploadSlot hasFreeUploadSlot={hasFreeUploadSlot} />,
    },
    uploadSlots === undefined || {
      key: 'slots',
      label: 'Total Upload Slots',
      value: uploadSlots,
    },
    queueLength === undefined || {
      key: 'queue',
      label: 'Queue Length',
      value: queueLength,
    },
    address === undefined || {
      key: 'address',
      label: 'IP Address',
      value: address,
    },
    port === undefined || { key: 'port', label: 'Port', value: port },
  ].filter((fact) => fact !== true);

  if (facts.length === 0) {
    return null;
  }

  return (
    <Item.Meta>
      {facts.map((fact, index) => (
        <span key={fact.key}>
          {index > 0 && ', '}
          {fact.label}: {fact.value}
        </span>
      ))}
    </Item.Meta>
  );
};

const User = ({
  description,
  hasPicture,
  picture,
  presence,
  username,
  ...facts
}) => (
  <Item>
    {hasPicture ? (
      <Item.Image
        size="small"
        src={`data:image;base64,${picture}`}
      />
    ) : (
      <ImagePlaceholder />
    )}

    <Item.Content>
      {/*
        A header, not an anchor. It was rendered `as="a"` with no href and no
        handler, so it took a link's pointer cursor and hover colour while
        going nowhere -- and on this page there is nowhere for it to go, since
        the user it names is the user being shown.
      */}
      <Item.Header>
        <Presence presence={presence} />
        {username}
      </Item.Header>
      <Facts {...facts} />
      <Item.Description>{description || 'No user info.'}</Item.Description>
    </Item.Content>
  </Item>
);

export default User;
