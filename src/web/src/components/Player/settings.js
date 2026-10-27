import { setSetting, settingOn } from '../../lib/player/remember';
import { useEffect, useState } from 'react';

/*
 * A switch from the player's settings, as a boolean, shared by every reader:
 * changing one in the settings sheet changes the bar at once.
 */

const listeners = new Set();

export const useSetting = (key) => {
  const [on, setOn] = useState(() => settingOn(key));

  useEffect(() => {
    const listener = () => setOn(settingOn(key));
    listeners.add(listener);

    return () => listeners.delete(listener);
  }, [key]);

  return on;
};

export const changeSetting = (key, on) => {
  setSetting(key, on);

  for (const listener of listeners) {
    listener();
  }
};
