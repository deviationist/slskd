import { activeChatKey } from '../../config';
import * as chat from '../../lib/chat';
import Chat from './Chat';
import ChatUnreadContext from './ChatUnreadContext';
import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { MemoryRouter } from 'react-router-dom';

jest.mock('../../lib/chat', () => ({
  ...jest.requireActual('../../lib/chat'),
  get: jest.fn(),
  getAll: jest.fn(),
}));

// the open conversation is read only while the page can be seen: one left open
// in a background tab keeps its messages unread, so they are still notified
describe('Chat', () => {
  let container;
  let visibility;
  const acknowledge = jest.fn();

  const setVisibility = (state) => {
    visibility = state;
    document.dispatchEvent(new Event('visibilitychange'));
  };

  const mount = () =>
    act(async () => {
      ReactDOM.render(
        <MemoryRouter>
          <ChatUnreadContext.Provider
            value={{ acknowledge, countOf: () => 1, unread: {} }}
          >
            <Chat state={{ user: { username: 'me' } }} />
          </ChatUnreadContext.Provider>
        </MemoryRouter>,
        container,
      );
    });

  beforeEach(() => {
    jest
      .spyOn(document, 'visibilityState', 'get')
      .mockImplementation(() => visibility);
    acknowledge.mockReset();
    chat.getAll.mockResolvedValue([
      { hasUnAcknowledgedMessages: true, username: 'alice' },
    ]);
    chat.get.mockResolvedValue({ messages: [], username: 'alice' });
    sessionStorage.setItem(activeChatKey, 'alice');
    container = document.createElement('div');
  });

  afterEach(() => {
    act(() => {
      ReactDOM.unmountComponentAtNode(container);
    });
    jest.restoreAllMocks();
    sessionStorage.clear();
  });

  it('reads the open conversation while the page is seen', async () => {
    expect.hasAssertions();
    visibility = 'visible';
    await mount();

    expect(acknowledge).toHaveBeenCalledWith('alice');
  });

  it('leaves it unread in a background tab, and reads it on coming back', async () => {
    expect.hasAssertions();
    visibility = 'hidden';
    await mount();

    expect(acknowledge).not.toHaveBeenCalled();

    act(() => setVisibility('visible'));

    expect(acknowledge).toHaveBeenCalledWith('alice');
  });
});
