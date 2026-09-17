import { browsePath } from '../../lib/users';
import React from 'react';
import { Link } from 'react-router-dom';
import { Icon, Popup } from 'semantic-ui-react';

/**
 * A link to a user's shares, for wherever their name is shown.
 *
 * Beside the name rather than among a card's other controls, because it is
 * about the peer the name belongs to rather than about the card. Muted until
 * pointed at: on a search result the card is a list of files and this is not
 * one of them.
 * @param {object} params
 * @param {string} params.username - The user whose files to browse.
 * @returns {object} The link.
 */
const BrowseLink = ({ username }) => (
  <Popup
    content={`Browse ${username}'s files`}
    position="top center"
    trigger={
      <Link
        className="browse-link"
        to={browsePath(username)}
      >
        <Icon name="folder open" />
      </Link>
    }
  />
);

export default BrowseLink;
