export const formatSeconds = (seconds) => {
  if (seconds === undefined) return '';
  const date = new Date(1_970, 0, 1);
  date.setSeconds(seconds);
  if (seconds >= 3_600) {
    return date.toTimeString().replace(/.*(\d{2}:\d{2}:\d{2}).*/u, '$1');
  }

  return date.toTimeString().replace(/.*(\d{2}:\d{2}).*/u, '$1');
};

export const formatBytesAsUnit = (bytes, unit, decimals = 2) => {
  if (unit === 'B') return bytes + ' ' + unit;

  const k = 1_024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = { EB: 6, GB: 3, KB: 1, MB: 2, PB: 5, TB: 4, YB: 8, ZB: 7 };

  return Number.parseFloat((bytes / k ** sizes[unit]).toFixed(dm));
};

export const formatBytes = (
  bytes,
  decimals = 2,
  padding = 0,
  paddingCharacter = ' ',
) => {
  if (bytes === 0 || bytes < 1) return '0 B';

  const k = 1_024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'];

  const index = Math.floor(Math.log(bytes) / Math.log(k));

  const number = Number.parseFloat((bytes / k ** index).toFixed(dm));
  const padded = number.toString().padStart(padding, paddingCharacter);

  return padded + ' ' + sizes[index];
};

export const formatSpeed = (bytesPerSecond, decimals = 1) => {
  if (!bytesPerSecond || bytesPerSecond === 0) return '0 B/s';
  return `${formatBytes(bytesPerSecond, decimals)}/s`;
};

export const formatWait = (seconds) => {
  if (!seconds || seconds === 0) return '0s';
  if (seconds < 60) return `${Math.round(seconds)}s`;
  return `${(seconds / 60).toFixed(1)}m`;
};

/*
 * Every date and time in the UI is rendered from this file, in one fixed
 * presentation: day first, 24-hour. 14/09/2026, 03:00:21.
 *
 * Both come from naming a locale, because Intl offers no separate control over
 * either -- date order is not a property you can set, it is decided by the
 * locale, and the clock likewise. So this is one decision, not two: the
 * browser's locale is not consulted at all.
 *
 * That is a deliberate reversal of what this file did an hour ago, and worth
 * stating rather than leaving as a puzzle. Following the browser sounds like
 * the respectful default and is not, on a single-operator deployment: a browser
 * takes its date order and its clock from its *language*, which is a different
 * setting from either, and an operator whose system is set to 24-hour and to
 * day-first is still shown 9/14/2026, 2:04 PM by an en-US browser -- with
 * nothing anywhere in reach to change it.
 *
 * Which locale is a deployment's choice, not this file's: `web.locale` in the
 * server options, defaulting to 'en-GB' and settable to anything BCP 47 --
 * 'en-US' for 9/14/2026, 'nb-NO' for 14.09.2026 with Norwegian month names.
 * Blank means follow the browser after all, for anyone who wants that.
 *
 * It arrives over the options hub rather than being read here, so `setLocale`
 * is how it gets in and `DEFAULT_LOCALE` is what is used until it does. The
 * window is the moment between the app mounting and the hub's first message;
 * the fallback matches the server's own default, so in practice nothing
 * changes under the reader unless the deployment has set something else.
 *
 * 'h23' is applied on top of whichever locale is in force, rather than left to
 * en-GB which would give it anyway. That is the point: the clock is the
 * guarantee that survives a change of locale, including to 'en-US'. It is not
 * `hour12: false`, which selects the h24 cycle in some locales and writes
 * midnight as 24:00 -- the two are mutually exclusive and `hour12` wins
 * silently.
 *
 * The option bags are exported for the tests.
 */
export const DEFAULT_LOCALE = 'en-GB';
export const HOUR_CYCLE = 'h23';

/*
 * Undefined is meaningful to Intl -- it means the browser's locale -- so it
 * cannot double as "not configured yet". This holds the configured value, and
 * `locale()` resolves what to actually pass.
 */
let configured;

/**
 * Sets the locale the UI formats in. Called with the server's `web.locale`.
 * @param {string} value - A BCP 47 locale, or blank to follow the browser.
 */
export const setLocale = (value) => {
  configured = value;
};

/**
 * The locale to format in: the configured one, or the browser's if it is
 * blank, or the default until the server has been heard from.
 * @returns {string|undefined} What to pass to Intl.
 */
export const locale = () => {
  if (configured === undefined) {
    return DEFAULT_LOCALE;
  }

  // deliberately blank: undefined is how Intl is told to use the browser's
  return configured === '' ? undefined : configured;
};

/**
 * A full date and time: the default wherever there is room for one.
 */
export const DATE_TIME_OPTIONS = {
  day: 'numeric',
  hour: '2-digit',
  hourCycle: HOUR_CYCLE,
  minute: '2-digit',
  month: 'numeric',
  second: '2-digit',
  year: 'numeric',
};

/**
 * A time of day on its own, for a column where the date is already known.
 */
export const TIME_OPTIONS = {
  hour: '2-digit',
  hourCycle: HOUR_CYCLE,
  minute: '2-digit',
  second: '2-digit',
};

/**
 * The day and time, without a year: enough to place a message.
 */
export const DAY_TIME_OPTIONS = {
  day: 'numeric',
  hour: '2-digit',
  hourCycle: HOUR_CYCLE,
  minute: '2-digit',
  month: 'numeric',
};

/**
 * An axis tick, where seconds are noise and the date sits on the axis already.
 */
export const HOUR_MINUTE_OPTIONS = {
  hour: '2-digit',
  hourCycle: HOUR_CYCLE,
  minute: '2-digit',
};

/**
 * A date without a time, for an axis tick spanning more than a day.
 */
export const DAY_MONTH_OPTIONS = {
  day: 'numeric',
  month: 'short',
};

export const formatDate = (date) => {
  return new Date(date).toLocaleString(locale(), DATE_TIME_OPTIONS);
};

export const formatTime = (date) => {
  return new Date(date).toLocaleTimeString(locale(), TIME_OPTIONS);
};

export const formatDayTime = (date) => {
  return new Date(date).toLocaleString(locale(), DAY_TIME_OPTIONS);
};

export const formatHourMinute = (date) => {
  return new Date(date).toLocaleTimeString(locale(), HOUR_MINUTE_OPTIONS);
};

export const formatDayMonth = (date) => {
  return new Date(date).toLocaleDateString(locale(), DAY_MONTH_OPTIONS);
};

export const truncate = (text, maxLength) => {
  if (!text) return '';
  if (text.length <= maxLength) return text;
  return `${text.slice(0, maxLength)}...`;
};

export const getFileName = (fullPath) => {
  return fullPath.split('\\').pop().split('/').pop();
};

/**
 * The file's extension, lowercased and without its dot.
 *
 * Read from the *name* rather than from the whole path: a peer's folder is
 * routinely called something like `Artist - Album (1998) [FLAC]`, and a dot in
 * it has nothing to say about the file inside it. A name with no dot, or one
 * ending in a dot, has no extension rather than an empty one -- there is
 * nothing to show and nothing to sort on.
 * @param {string} fullPath - The file's name or its whole remote path.
 * @returns {string} The extension, or '' where there is none.
 */
export const getFileExtension = (fullPath) => {
  const name = getFileName(String(fullPath ?? ''));
  const dot = name.lastIndexOf('.');

  // a leading dot is a hidden file, not an extension: `.sync` is the whole name
  if (dot <= 0 || dot === name.length - 1) {
    return '';
  }

  return name.slice(dot + 1).toLowerCase();
};

export const getDirectoryName = (fullPath) => {
  let path = fullPath;

  if (path.lastIndexOf('\\') > 0) {
    path = path.slice(0, Math.max(0, path.lastIndexOf('\\')));
  }

  if (path.lastIndexOf('/') > 0) {
    path = path.slice(0, Math.max(0, path.lastIndexOf('/')));
  }

  return path;
};

export const formatAttributes = ({
  bitRate,
  isVariableBitRate,
  bitDepth,
  sampleRate,
}) => {
  const isLossless = Boolean(sampleRate) && Boolean(bitDepth);

  if (isLossless) {
    return `${bitDepth}/${sampleRate / 1_000}kHz`;
  }

  if (isVariableBitRate) {
    return `${bitRate} Kbps, VBR`;
  }

  return bitRate ? `${bitRate} Kbps` : '';
};

export const sleep = (milliseconds) => {
  return new Promise((resolve) => {
    setTimeout(resolve, milliseconds);
  });
};

/* https://www.npmjs.com/package/js-file-download
 *
 * Copyright 2017 Kenneth Jiang
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
 * documentation files (the "Software"), to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
 * and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions
 * of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
 * TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
 * CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE
 */
export const downloadFile = (data, filename, mime) => {
  const blob = new Blob([data], { type: mime || 'application/octet-stream' });
  if (typeof window.navigator.msSaveBlob !== 'undefined') {
    // IE workaround for "HTML7007: One or more blob URLs were
    // revoked by closing the blob for which they were created.
    // These URLs will no longer resolve as the data backing
    // the URL has been freed."
    window.navigator.msSaveBlob(blob, filename);
  } else {
    const blobURL = window.URL.createObjectURL(blob);
    const temporaryLink = document.createElement('a');
    temporaryLink.style.display = 'none';
    temporaryLink.href = blobURL;
    temporaryLink.setAttribute('download', filename);

    // Safari thinks _blank anchor are pop ups. We only want to set _blank
    // target if the browser does not support the HTML5 download attribute.
    // This allows you to download files in desktop safari if pop up blocking
    // is enabled.
    if (typeof temporaryLink.download === 'undefined') {
      temporaryLink.setAttribute('target', '_blank');
    }

    document.body.append(temporaryLink);
    temporaryLink.click();
    temporaryLink.remove();
    window.URL.revokeObjectURL(blobURL);
  }
};

/**
 * The nearest ancestor that actually scrolls, or null if none does.
 *
 * A virtualiser has to watch the element the reader is scrolling, and in this
 * app that is neither the window nor the list's own parent: the layout puts
 * `overflow-y: auto` on a wrapper near the root and lets the page grow inside
 * it, so `window.scrollY` never moves however far down the list you are.
 *
 * Found rather than named, because a selector would be a second place to
 * remember when the layout changes -- and the failure it produces is a list
 * that renders its first screenful and then nothing, which reads as data
 * missing rather than as a scroll container in the wrong place.
 *
 * `hidden` does not count: an element can be taller than its box and clip
 * without ever scrolling, which is exactly what the wrapper directly above the
 * page content does here.
 * @param {Element} element - Where to start looking, exclusive.
 * @returns {Element|null} The scrolling ancestor.
 */
export const scrollParentOf = (element) => {
  let node = element?.parentElement;

  while (node) {
    const { overflowY } = window.getComputedStyle(node);

    if (
      overflowY === 'auto' ||
      overflowY === 'scroll' ||
      overflowY === 'overlay'
    ) {
      return node;
    }

    node = node.parentElement;
  }

  return null;
};

/**
 * How far an element sits below the top of its scroll container's content.
 *
 * What a virtualiser wants as its `scrollMargin`: without it the rows are
 * positioned as though the list began at the top of the scrolling area, and
 * every one of them lands that far out of place.
 * @param {Element} element - The element.
 * @param {Element} scrollParent - Its scrolling ancestor.
 * @returns {number} The offset in px, or 0 if either is missing.
 */
export const offsetWithin = (element, scrollParent) => {
  if (!element || !scrollParent) {
    return 0;
  }

  return (
    element.getBoundingClientRect().top -
    scrollParent.getBoundingClientRect().top +
    scrollParent.scrollTop
  );
};
