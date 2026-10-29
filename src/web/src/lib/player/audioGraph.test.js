import {
  meterTaps,
  previewCodec,
  primeAudio,
  resetAudioGraph,
} from './audioGraph';

// the two rules iOS makes:
// a context starts only inside the press, and an element handed to a suspended
// graph goes silent -- so the tap waits for `running`

const node = () => ({ connect: () => {} });

class FakeContext {
  static last = null;

  static made = 0;

  static resumable = true;

  constructor() {
    this.state = 'suspended';
    this.sources = 0;
    this.destination = {};
    FakeContext.made++;
    FakeContext.last = this;
  }

  createAnalyser() {
    return { ...node(), fftSize: 0, getFloatTimeDomainData: () => {} };
  }

  createChannelMerger() {
    return node();
  }

  createChannelSplitter() {
    return node();
  }

  createMediaElementSource() {
    this.sources++;

    return node();
  }

  resume() {
    if (!FakeContext.resumable) {
      return Promise.reject(new Error('NotAllowedError'));
    }

    this.state = 'running';

    return Promise.resolve();
  }
}

const element = {};

const settle = () =>
  new Promise((resolve) => {
    setTimeout(resolve, 0);
  });

describe('the audio graph', () => {
  beforeEach(() => {
    resetAudioGraph();
    FakeContext.made = 0;
    FakeContext.resumable = true;
    window.AudioContext = FakeContext;
  });

  afterEach(() => {
    delete window.AudioContext;
  });

  it('builds one context, and taps the element once it is running', async () => {
    expect.assertions(3);
    primeAudio(element);
    await settle();
    primeAudio(element);
    await settle();

    expect(FakeContext.made).toBe(1);
    expect(FakeContext.last.sources).toBe(1);
    expect(meterTaps()).not.toBeNull();
  });

  it('never hands the element to a context that would not start', async () => {
    expect.assertions(2);
    // routed into a suspended graph, the track would play silence
    FakeContext.resumable = false;
    primeAudio(element);
    await settle();

    expect(FakeContext.last.sources).toBe(0);
    expect(meterTaps()).toBeNull();
  });

  it('reads nothing once the context has been suspended again', async () => {
    expect.assertions(1);
    primeAudio(element);
    await settle();
    FakeContext.last.state = 'suspended';

    expect(meterTaps()).toBeNull();
  });

  it('asks for AAC on WebKit and the default everywhere else', () => {
    const vendor = jest.spyOn(navigator, 'vendor', 'get');

    vendor.mockReturnValue('Apple Computer, Inc.');
    expect(previewCodec()).toBe('aac');
    vendor.mockReturnValue('Google Inc.');
    expect(previewCodec()).toBeUndefined();
    vendor.mockRestore();
  });
});
