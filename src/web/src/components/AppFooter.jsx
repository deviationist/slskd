import { urlBase } from '../config';
import { formatBytes } from '../lib/util';
import React, { useState } from 'react';
import { Button, Icon, Menu, Modal, Table } from 'semantic-ui-react';

// unpadded: padding to a fixed width kept the text beside it from moving as
// the speed changed, at the cost of up to three spaces before every speed --
// room the footer shares with a page's actions, which are right-aligned and
// do not move either way
const formatSpeed = (bytesPerSecond) => {
  if (!bytesPerSecond || bytesPerSecond < 1) {
    return '0 B/s';
  }

  return `${formatBytes(bytesPerSecond, 1)}/s`;
};

/**
 * The release a version string names, without the build and the suffix after
 * it: 0.26.2 for 0.26.2.65534-homelab. The rest is in the version modal.
 * @param {string} version - The full version.
 * @returns {string} The short version, or the full one if it has no such shape.
 */
export const shortVersion = (version) =>
  /^\d+\.\d+\.\d+/u.exec(version ?? '')?.[0] ?? version ?? '';

/**
 * One direction's transfers, compactly: the speed, and how many are moving
 * and waiting, abbreviated -- the footer is shared with whatever a page puts
 * there, and the full words are in the title, for whoever points at it.
 */
const TransferStat = ({ active, color, icon, label, queued, speed }) => {
  const text = formatSpeed(speed);

  return (
    <Menu.Item
      className="footer-stat"
      title={`${label}: ${text}, ${active} active, ${queued} queued`}
    >
      <Icon
        color={color}
        name={icon}
      />
      <span className={`footer-${label.toLowerCase()}`}>{text}</span>
      <span className="footer-stat-detail">
        {active} act &middot; {queued} q
      </span>
    </Menu.Item>
  );
};

/**
 * What the footer's version stands for, in full, with where it comes from.
 */
const VersionModal = ({ onClose, version }) => (
  <Modal
    onClose={onClose}
    open
    size="mini"
  >
    <Modal.Header>
      <img
        alt=""
        className="footer-logo"
        src={`${urlBase}/favicon.ico`}
      />{' '}
      slskd
    </Modal.Header>
    <Modal.Content>
      <Table
        basic="very"
        compact
        unstackable
      >
        <Table.Body>
          <Table.Row>
            <Table.Cell>Version</Table.Cell>
            <Table.Cell className="footer-version-full">
              {version.current ?? 'unknown'}
            </Table.Cell>
          </Table.Row>
          {(version.isCanary || version.isDevelopment) && (
            <Table.Row>
              <Table.Cell>Build</Table.Cell>
              <Table.Cell>
                {version.isDevelopment ? 'Development' : 'Canary'}
              </Table.Cell>
            </Table.Row>
          )}
          <Table.Row>
            <Table.Cell>License</Table.Cell>
            <Table.Cell>AGPLv3</Table.Cell>
          </Table.Row>
          <Table.Row>
            <Table.Cell>Source</Table.Cell>
            <Table.Cell>
              <a
                href="https://github.com/slskd/slskd"
                rel="noreferrer"
                target="_blank"
              >
                github.com/slskd/slskd
              </a>
            </Table.Cell>
          </Table.Row>
        </Table.Body>
      </Table>
    </Modal.Content>
    <Modal.Actions>
      <Button onClick={onClose}>Close</Button>
    </Modal.Actions>
  </Modal>
);

const AppFooter = ({
  server = {},
  transferMetrics = {},
  user = {},
  version = {},
}) => {
  const [showingVersion, setShowingVersion] = useState(false);

  const { isConnected } = server;
  const { username } = user;
  const { current } = version;
  const build = version.isDevelopment
    ? 'development build'
    : version.isCanary
      ? 'canary build'
      : undefined;
  const downloads = transferMetrics?.downloads;
  const uploads = transferMetrics?.uploads;

  return (
    <Menu
      className="footer"
      inverted
    >
      <Menu.Item
        className="footer-user"
        title={isConnected ? `Connected as ${username}` : 'Disconnected'}
      >
        <Icon
          color={isConnected ? 'green' : 'red'}
          name="circle"
        />
        <span className="footer-username">
          {isConnected ? username : 'Disconnected'}
        </span>
      </Menu.Item>
      <TransferStat
        active={downloads?.inProgress?.files ?? 0}
        color="blue"
        icon="arrow down"
        label="Download"
        queued={downloads?.queued?.files ?? 0}
        speed={downloads?.inProgress?.averageSpeed}
      />
      <TransferStat
        active={uploads?.inProgress?.files ?? 0}
        color="green"
        icon="arrow up"
        label="Upload"
        queued={uploads?.queued?.files ?? 0}
        speed={uploads?.inProgress?.averageSpeed}
      />
      {/*
       * Somewhere for a page to put an action that has to stay reachable while
       * its own content scrolls. The footer is the one strip that is always on
       * screen and already has room; a page-owned bar floating over the
       * content has to win a stacking contest with this one and then lose the
       * same space again to anything else that wants the bottom edge.
       *
       * Empty collapses to nothing, so the footer is unchanged until something
       * fills it.
       */}
      <div
        className="footer-slot"
        id="footer-action-slot"
      />
      <Menu.Menu position="right">
        {/* the short version alone, to leave the footer to the actions a page
            puts in it: the full version, the licence and the source are a
            click away */}
        <Menu.Item
          as="button"
          className="footer-about"
          onClick={() => setShowingVersion(true)}
          title={[current ? `slskd ${current}` : 'slskd', build]
            .filter(Boolean)
            .join(', ')}
          type="button"
        >
          <img
            alt=""
            className="footer-logo"
            src={`${urlBase}/favicon.ico`}
          />
          {current && (
            <span className="footer-version">{shortVersion(current)}</span>
          )}
          {/* a mark, not a menu entry: every build of ours is a canary by
              upstream's definition (the .65534 revision), so a whole menu
              item said the same thing on every page; the modal names it */}
          {build && (
            <Icon
              className="footer-build"
              color="yellow"
              name="flask"
            />
          )}
        </Menu.Item>
      </Menu.Menu>
      {showingVersion && (
        <VersionModal
          onClose={() => setShowingVersion(false)}
          version={version}
        />
      )}
    </Menu>
  );
};

export default AppFooter;
