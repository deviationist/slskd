import { bundleIn } from './clientUpdate';

describe('bundleIn', () => {
  it("reads the bundle's name out of a production index.html", () => {
    const html =
      '<head><script defer="defer" src="./static/js/main.8cf88288.js"></script>' +
      '<link href="./static/css/main.956731f7.css" rel="stylesheet"></head>';

    expect(bundleIn(html)).toBe('main.8cf88288.js');
  });

  it('finds none in a page that is not the app, such as a proxy login', () => {
    // which is what keeps a login page from reading as a new build
    expect(bundleIn('<html><form action="/login"></form></html>')).toBeNull();
  });

  it('finds none in nothing', () => {
    expect(bundleIn(undefined)).toBeNull();
  });
});
