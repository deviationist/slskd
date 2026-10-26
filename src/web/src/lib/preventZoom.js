// no zooming on a touch screen: the app is laid out for a phone, and a zoom only ever happens by accident -- a
// double tap on a button, a pinch while scrolling, or Safari enlarging the page when a field under 16px is focused,
// and then leaving it enlarged.
//
// The viewport meta (public/index.html) stops Safari's zoom on focus and every zoom on Android, and
// `touch-action: manipulation` (App.css) stops the double tap. iOS ignores `user-scalable=no` for a pinch, though, and
// the only thing it does honour is cancelling the gesture events it sends for one. Touch screens only, so a trackpad
// pinch in desktop Safari, which fires the same events, still zooms.
export const preventZoom = () => {
  if (!('ongesturestart' in window) || navigator.maxTouchPoints === 0) {
    return;
  }

  for (const type of ['gesturestart', 'gesturechange']) {
    document.addEventListener(type, (event) => event.preventDefault(), {
      passive: false,
    });
  }
};
