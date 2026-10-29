import * as chat from '../../lib/chat';
import { ChatUnreadProvider, useChatUnread } from './ChatUnreadContext';
import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';

jest.mock('../../lib/chat', () => ({
  ...jest.requireActual('../../lib/chat'),
  acknowledge: jest.fn(),
}));

describe('ChatUnreadContext', () => {
  let container;
  let shared;

  const Probe = () => {
    shared = useChatUnread();
    return null;
  };

  const mount = (state) =>
    act(() => {
      ReactDOM.render(
        <ChatUnreadProvider unread={state}>
          <Probe />
        </ChatUnreadProvider>,
        container,
      );
    });

  const unread = {
    byUser: [{ count: 2, username: 'alice' }],
    unAcknowledgedConversations: 1,
    unAcknowledgedMessages: 2,
  };

  beforeEach(() => {
    chat.acknowledge.mockReset();
    container = document.createElement('div');
  });

  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
  });

  it('gives every reader the server’s count', () => {
    mount(unread);

    expect(shared.countOf('alice')).toBe(2);
    expect(shared.countOf('bob')).toBe(0);
    expect(shared.unread).toBe(unread);
  });

  it('asks the server only when there is something to read', async () => {
    expect.assertions(2);
    chat.acknowledge.mockResolvedValue({});
    mount(unread);

    await act(() => shared.acknowledge('bob'));
    expect(chat.acknowledge).not.toHaveBeenCalled();

    await act(() => shared.acknowledge('alice'));
    expect(chat.acknowledge).toHaveBeenCalledWith({ username: 'alice' });
  });

  it('does not ask twice for the same person while the first is on its way', async () => {
    expect.assertions(1);
    let answer;
    chat.acknowledge.mockReturnValue(
      new Promise((resolve) => {
        answer = resolve;
      }),
    );
    mount(unread);

    const first = shared.acknowledge('alice');
    shared.acknowledge('alice');
    shared.acknowledge('alice');

    expect(chat.acknowledge).toHaveBeenCalledTimes(1);

    await act(async () => {
      answer({});
      await first;
    });
  });

  it('survives a failed request and tries again next time', async () => {
    expect.assertions(1);
    jest.spyOn(console, 'error').mockImplementation(() => {});
    chat.acknowledge
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({});
    mount(unread);

    await act(() => shared.acknowledge('alice'));
    await act(() => shared.acknowledge('alice'));

    expect(chat.acknowledge).toHaveBeenCalledTimes(2);
    console.error.mockRestore();
  });
});
