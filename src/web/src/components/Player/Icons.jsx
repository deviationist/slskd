import React from 'react';

/*
 * Line icons, drawn rather than typed. Twin: media-bridge
 * web/app/src/components/icons.tsx.
 *
 * Characters such as ▶ and ❚❚ sit where their font puts them: on iOS the bars
 * rode high in the circle while the triangle looked centred. Drawn, they are
 * centred by geometry.
 *
 * Drawn at even sizes only, in even buttons: an odd glyph in an even box
 * centres on a half pixel and smears every edge across two device pixels,
 * which reads as off-centre and soft at once (media-bridge's
 * tests/perf/icon-probe.mjs measures the same rule there).
 */

const Stroked = ({ children, size }) => (
  <svg
    aria-hidden="true"
    fill="none"
    height={size}
    stroke="currentColor"
    strokeLinecap="round"
    strokeLinejoin="round"
    strokeWidth={1.8}
    viewBox="0 0 24 24"
    width={size}
  >
    {children}
  </svg>
);

const Filled = ({ children, size }) => (
  <svg
    aria-hidden="true"
    fill="currentColor"
    height={size}
    stroke="none"
    viewBox="0 0 24 24"
    width={size}
  >
    {children}
  </svg>
);

// media-bridge's triangle, as it is there. This used to be a rounded triangle
// moved 2.3 left to centre its centroid; that put the centroid at 10.5, so it
// read left of centre in every button. This one's centroid is at 11.7 and its
// box's centre at 13.5, which is where a triangle looks centred: between the
// two, just right of the middle.
export const PlayIcon = ({ size = 14 }) => (
  <Filled size={size}>
    <path d="M8 5.5v13l11-6.5z" />
  </Filled>
);

export const PauseIcon = ({ size = 14 }) => (
  <Filled size={size}>
    <rect
      height="15"
      rx="1"
      width="4.2"
      x="6"
      y="4.5"
    />
    <rect
      height="15"
      rx="1"
      width="4.2"
      x="13.8"
      y="4.5"
    />
  </Filled>
);

export const ReplayIcon = ({ size = 16 }) => (
  <Stroked size={size}>
    <path d="M3 12a9 9 0 1 0 2.64-6.36L3 8.28" />
    <path d="M3 3v5.28h5.28" />
  </Stroked>
);

// ten seconds back and forward, for the drawer: a phone has no arrow keys
export const BackIcon = ({ size = 24 }) => (
  <Stroked size={size}>
    <path d="M3 12a9 9 0 1 0 2.64-6.36L3 8.28" />
    <path d="M3 3v5.28h5.28" />
    <text
      fill="currentColor"
      fontSize="7"
      fontWeight="700"
      stroke="none"
      textAnchor="middle"
      x="12.5"
      y="14.6"
    >
      10
    </text>
  </Stroked>
);

export const ForwardIcon = ({ size = 24 }) => (
  <Stroked size={size}>
    <path d="M21 12a9 9 0 1 1-2.64-6.36L21 8.28" />
    <path d="M21 3v5.28h-5.28" />
    <text
      fill="currentColor"
      fontSize="7"
      fontWeight="700"
      stroke="none"
      textAnchor="middle"
      x="11.5"
      y="14.6"
    >
      10
    </text>
  </Stroked>
);

export const CloseIcon = ({ size = 17 }) => (
  <Stroked size={size}>
    <path d="M18 6 6 18" />
    <path d="m6 6 12 12" />
  </Stroked>
);

export const ChevronDownIcon = ({ size = 20 }) => (
  <Stroked size={size}>
    <path d="m6 9 6 6 6-6" />
  </Stroked>
);

// the cog reads larger than the close at the same size -- a dense glyph that
// fills its box -- so the bar asks for 15 where the close is 17, and they look
// level
export const CogIcon = ({ size = 15 }) => (
  <Stroked size={size}>
    <circle
      cx="12"
      cy="12"
      r="3.2"
    />
    <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
  </Stroked>
);

export const VolumeIcon = ({ size = 15 }) => (
  <Stroked size={size}>
    <path d="M11 5 6 9H3v6h3l5 4z" />
    <path d="M15.5 8.5a5 5 0 0 1 0 7" />
    <path d="M18.5 5.5a9 9 0 0 1 0 13" />
  </Stroked>
);

export const VolumeOffIcon = ({ size = 15 }) => (
  <Stroked size={size}>
    <path d="M11 5 6 9H3v6h3l5 4z" />
    <path d="m16 9 5 6" />
    <path d="m21 9-5 6" />
  </Stroked>
);

export const SpinnerIcon = ({ size = 16 }) => (
  <svg
    aria-hidden="true"
    className="player-spin"
    fill="none"
    height={size}
    stroke="currentColor"
    strokeLinecap="round"
    strokeWidth={2.6}
    viewBox="0 0 24 24"
    width={size}
  >
    <circle
      cx="12"
      cy="12"
      opacity=".28"
      r="8.5"
    />
    <path d="M12 3.5a8.5 8.5 0 0 1 8.5 8.5" />
  </svg>
);

export const NoteIcon = ({ size = 18 }) => (
  <Stroked size={size}>
    <path d="M9 18V5l12-2v13" />
    <circle
      cx="6"
      cy="18"
      r="3"
    />
    <circle
      cx="18"
      cy="16"
      r="3"
    />
  </Stroked>
);
