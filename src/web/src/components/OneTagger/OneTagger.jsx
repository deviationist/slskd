import * as oneTagger from '../../lib/oneTagger';
import React from 'react';
import { toast } from 'react-toastify';
import { Menu, Popup } from 'semantic-ui-react';

/**
 * OneTagger's mark, in the colour of what it sits in. Decorative: whatever it
 * is beside already says OneTagger.
 */
export const OneTaggerMark = ({ className, style }) => (
  <svg
    aria-hidden="true"
    className={className}
    fill="currentColor"
    focusable="false"
    style={style}
    viewBox={oneTagger.MARK.viewBox}
  >
    <path d={oneTagger.MARK.path} />
  </svg>
);

/**
 * OneTagger in the menu, where the server says so. A new tab: slskd holds live
 * transfers and a scroll position that leaving the page would lose.
 */
export const OneTaggerMenuItem = ({ options }) => {
  const url = oneTagger.menuUrl(options);

  if (!url) {
    return null;
  }

  return (
    <a
      href={url}
      rel="noopener noreferrer"
      target="_blank"
    >
      <Menu.Item>
        <OneTaggerMark className="icon onetagger-mark" />
        OneTagger
      </Menu.Item>
    </a>
  );
};

const openIn = async (file) => {
  try {
    await oneTagger.open(file.id);
  } catch (error) {
    toast.error(
      `Could not open ${file.filename.split(/[\\/]/u).pop()} in OneTagger: ${
        error?.response?.status === 404 ? 'its file is gone' : error.message
      }`,
    );
  }
};

/**
 * Opens one finished download in OneTagger's Tag Editor. Drawn only where the
 * download has a file to open.
 */
export const OneTaggerRowButton = ({ file }) => {
  if (!oneTagger.isOpenable(file)) {
    return null;
  }

  return (
    <Popup
      content="Open in OneTagger's Tag Editor"
      position="left center"
      trigger={
        <span
          aria-label="Open in OneTagger"
          className="onetagger-row"
          onClick={() => openIn(file)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              openIn(file);
            }
          }}
          role="button"
          tabIndex={0}
        >
          <OneTaggerMark />
        </span>
      }
    />
  );
};
