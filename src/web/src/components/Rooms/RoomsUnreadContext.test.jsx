import * as rooms from '../../lib/rooms';
import { RoomsUnreadProvider, useRoomsUnread } from './RoomsUnreadContext';
import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';

jest.mock('../../lib/rooms', () => ({
  ...jest.requireActual('../../lib/rooms'),
  markRead: jest.fn(),
}));

describe('RoomsUnreadContext', () => {
  let container;
  let shared;

  const Probe = () => {
    shared = useRoomsUnread();
    return null;
  };

  const mount = (state) =>
    act(() => {
      ReactDOM.render(
        <RoomsUnreadProvider unread={state}>
          <Probe />
        </RoomsUnreadProvider>,
        container,
      );
    });

  const unread = {
    byRoom: [{ count: 5, roomName: 'techno' }],
    unreadMessages: 5,
    unreadRooms: 1,
  };

  beforeEach(() => {
    rooms.markRead.mockReset();
    container = document.createElement('div');
  });

  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
  });

  it('gives every reader the server’s count', () => {
    mount(unread);

    expect(shared.countOf('techno')).toBe(5);
    expect(shared.countOf('ambient')).toBe(0);
    expect(shared.unread).toBe(unread);
  });

  it('asks the server only when there is something to read', async () => {
    expect.assertions(2);
    rooms.markRead.mockResolvedValue({});
    mount(unread);

    await act(() => shared.markRead('ambient'));
    expect(rooms.markRead).not.toHaveBeenCalled();

    await act(() => shared.markRead('techno'));
    expect(rooms.markRead).toHaveBeenCalledWith({ roomName: 'techno' });
  });

  it('does not ask twice for the same room while the first is on its way', async () => {
    expect.assertions(1);
    let answer;
    rooms.markRead.mockReturnValue(
      new Promise((resolve) => {
        answer = resolve;
      }),
    );
    mount(unread);

    const first = shared.markRead('techno');
    shared.markRead('techno');
    shared.markRead('techno');

    expect(rooms.markRead).toHaveBeenCalledTimes(1);

    await act(async () => {
      answer({});
      await first;
    });
  });

  it('survives a failed request and tries again next time', async () => {
    expect.assertions(1);
    jest.spyOn(console, 'error').mockImplementation(() => {});
    rooms.markRead
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({});
    mount(unread);

    await act(() => shared.markRead('techno'));
    await act(() => shared.markRead('techno'));

    expect(rooms.markRead).toHaveBeenCalledTimes(2);
    console.error.mockRestore();
  });
});
