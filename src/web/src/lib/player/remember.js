/*
 * What the player remembers, and where.
 *
 * The track and its position in sessionStorage: a reload of this tab comes back
 * to it, paused, and a new tab does not inherit someone else's evening. Volume in
 * localStorage, because a level is a preference. Mute is not remembered at all:
 * silence is a thing you do for a moment, and a player that comes back mute for
 * reasons you cannot see is the failure the volume control exists to avoid.
 *
 * Every read is checked for shape, because the store outlives this code: a
 * value written by an older version, or by hand, must read as nothing rather
 * than as a player stuck on a track that cannot load.
 */

export const LAST_KEY = 'slskd-player-last';
export const VOLUME_KEY = 'slskd-player-volume';

const store = (kind) => {
  try {
    return window[kind];
  } catch {
    // storage blocked, or a sandboxed frame
    return null;
  }
};

export const readLast = (storage = store('sessionStorage')) => {
  try {
    const value = JSON.parse(storage?.getItem(LAST_KEY) ?? 'null');

    if (
      !value ||
      typeof value.id !== 'string' ||
      !/^[\da-f-]{36}$/iu.test(value.id) ||
      typeof value.title !== 'string'
    ) {
      return null;
    }

    return {
      at: Number.isFinite(value.at) && value.at > 0 ? value.at : 0,
      id: value.id,
      title: value.title,
      username: typeof value.username === 'string' ? value.username : '',
    };
  } catch {
    return null;
  }
};

export const writeLast = (value, storage = store('sessionStorage')) => {
  try {
    if (value) {
      storage?.setItem(LAST_KEY, JSON.stringify(value));
    } else {
      storage?.removeItem(LAST_KEY);
    }
  } catch {
    // a full or blocked store loses only the convenience
  }
};

export const readVolume = (storage = store('localStorage')) => {
  try {
    const raw = storage?.getItem(VOLUME_KEY);
    const value = raw == null ? 1 : Number(raw);

    return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 1;
  } catch {
    return 1;
  }
};

export const writeVolume = (value, storage = store('localStorage')) => {
  try {
    storage?.setItem(VOLUME_KEY, String(value));
  } catch {
    // as above
  }
};

/*
 * The player's switches, behind the cog. Stored as 'on' / 'off', absent meaning
 * the default, so a switch added later starts at its default for everyone.
 */
export const SETTINGS = [
  {
    key: 'slskd-player-artwork',
    label: 'Album artwork',
    on: true,
    why: 'The sleeve at the left of the bar, from the file itself. Off leaves the space to the title and the waveform.',
  },
  {
    key: 'slskd-player-meter',
    label: 'Output meter',
    on: true,
    why: 'The stereo ladder beside the clock, with the clip light above it. Its red zone means loud; whether the file itself clips is the analysis’s answer, not the meter’s.',
  },
  {
    key: 'slskd-player-cursor',
    label: 'Waveform cursor',
    on: true,
    why: 'The line that follows the mouse across the waveform with the time it would seek to.',
  },
  {
    key: 'slskd-player-lanes',
    label: 'Problem lanes',
    on: true,
    why: 'The strip under the waveform marking where a fault sits in the track. Shown only when an analysis provider is configured.',
  },
  {
    key: 'slskd-player-volume-control',
    label: 'Volume control',
    on: false,
    why: 'A mute button and a slider. Off, the player plays at full level, so it can never be stuck quiet from last week.',
  },
];

export const settingOn = (key, storage = store('localStorage')) => {
  const fallback = SETTINGS.find((setting) => setting.key === key)?.on ?? true;

  try {
    const value = storage?.getItem(key);

    return value === 'on' ? true : value === 'off' ? false : fallback;
  } catch {
    return fallback;
  }
};

export const setSetting = (key, on, storage = store('localStorage')) => {
  try {
    storage?.setItem(key, on ? 'on' : 'off');
  } catch {
    // as above
  }
};
