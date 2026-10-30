// <copyright file="RoomService.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

using Microsoft.Extensions.Options;

namespace slskd.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Serilog;
    using slskd.Events;
    using slskd.Users;

    using Soulseek;

    /// <summary>
    ///     Chat room management and event handling.
    /// </summary>
    public interface IRoomService
    {
        /// <summary>
        ///     Joins the specified <paramref name="roomName"/>.
        /// </summary>
        /// <param name="roomName">The name of the room to join.</param>
        /// <returns>The operation context, including information about the room.</returns>
        Task<RoomData> JoinAsync(string roomName);

        /// <summary>
        ///     Leaves the specified <paramref name="roomName"/>.
        /// </summary>
        /// <param name="roomName">The name of the room to leave.</param>
        /// <returns>The operation context.</returns>
        Task LeaveAsync(string roomName);

        /// <summary>
        ///     Attempts to join the specified <paramref name="roomNames"/>.
        /// </summary>
        /// <remarks>
        ///     Failures are logged but not thrown. Use JoinAsync() to trap Exceptions.
        /// </remarks>
        /// <param name="roomNames">The list of room names to join.</param>
        /// <returns>The operation context.</returns>
        Task TryJoinAsync(params string[] roomNames);

        /// <summary>
        ///     Marks everything in the specified <paramref name="roomName"/> read: the operator has seen it.
        /// </summary>
        /// <param name="roomName">The room.</param>
        void MarkRead(string roomName);

        /// <summary>
        ///     Takes in a message as though the server had sent it, making the room if it is not joined.
        /// </summary>
        /// <remarks>
        ///     It goes the way a real one does -- the blacklist, the room's messages, the unread count, the event that
        ///     notifications listen for -- so what it shows is what a real message would do. A room it makes is marked
        ///     <see cref="Room.IsSimulated"/>, and is never joined on the server.
        /// </remarks>
        /// <param name="roomName">The room.</param>
        /// <param name="username">Who it is from.</param>
        /// <param name="message">What they said.</param>
        /// <returns>The message, as the room holds it.</returns>
        RoomMessage Simulate(string roomName, string username, string message);
    }

    /// <summary>
    ///     Chat room management and event handling.
    /// </summary>
    public class RoomService : IRoomService
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RoomService"/> class.
        /// </summary>
        /// <param name="soulseekClient"></param>
        /// <param name="optionsMonitor"></param>
        /// <param name="stateMutator"></param>
        /// <param name="roomTracker"></param>
        /// <param name="userService"></param>
        /// <param name="eventBus"></param>
        public RoomService(
            ISoulseekClient soulseekClient,
            IOptionsMonitor<Options> optionsMonitor,
            IStateMutator<State> stateMutator,
            IRoomTracker roomTracker,
            IUserService userService,
            EventBus eventBus)
        {
            Client = soulseekClient;

            StateMutator = stateMutator;
            OptionsMonitor = optionsMonitor;

            RoomTracker = roomTracker;

            Users = userService;
            EventBus = eventBus;

            Client.LoggedIn += Client_LoggedIn;

            Client.RoomJoined += Client_RoomJoined;
            Client.RoomLeft += Client_RoomLeft;
            Client.RoomMessageReceived += Client_RoomMessageReceived;
        }

        private ISoulseekClient Client { get; }
        private ILogger Logger { get; set; } = Log.ForContext<RoomService>();
        private IStateMutator<State> StateMutator { get; }
        private IOptionsMonitor<Options> OptionsMonitor { get; }
        private IRoomTracker RoomTracker { get; set; }
        private IUserService Users { get; set; }
        private EventBus EventBus { get; }

        // unread per room. Changed under the lock and published whole, so two messages arriving at once cannot each
        // publish a count that misses the other
        private Dictionary<string, int> Unread { get; } = new(StringComparer.Ordinal);
        private object UnreadLock { get; } = new();

        /// <summary>
        ///     Joins the specified <paramref name="roomName"/>.
        /// </summary>
        /// <param name="roomName">The name of the room to join.</param>
        /// <returns>The operation context, including information about the room.</returns>
        public async Task<RoomData> JoinAsync(string roomName)
        {
            Logger.Debug("Joining room {Room}", roomName);

            try
            {
                var data = await Client.JoinRoomAsync(roomName);
                var room = Room.FromRoomData(data);
                RoomTracker.TryAdd(roomName, room);

                Logger.Debug("Room data for {Room}: {Info}", roomName, data.ToJson());
                return data;
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to join room {Room}: {Message}", roomName, ex.Message);
                throw;
            }
        }

        /// <summary>
        ///     Leaves the specified <paramref name="roomName"/>.
        /// </summary>
        /// <param name="roomName">The name of the room to leave.</param>
        /// <returns>The operation context.</returns>
        public async Task LeaveAsync(string roomName)
        {
            Logger.Debug("Leaving room {Room}", roomName);

            try
            {
                // a simulated room was never joined, so there is nothing to tell the server
                if (!(RoomTracker.TryGet(roomName, out var room) && room.IsSimulated))
                {
                    await Client.LeaveRoomAsync(roomName);
                }

                CountUnread(roomName, _ => 0);
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to leave room {Room}: {Message}", roomName, ex.Message);
                throw;
            }
        }

        /// <summary>
        ///     Attempts to join the specified <paramref name="roomNames"/>.
        /// </summary>
        /// <remarks>
        ///     Failures are logged but not thrown. Use JoinAsync() to trap Exceptions.
        /// </remarks>
        /// <param name="roomNames">The list of room names to join.</param>
        /// <returns>The operation context.</returns>
        public async Task TryJoinAsync(params string[] roomNames)
        {
            var tasks = new List<Task>();

            foreach (var room in roomNames)
            {
                tasks.Add(JoinAsync(room));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Caught Exception in JoinAsync");
            }
        }

        private async void Client_LoggedIn(object sender, EventArgs e)
        {
            var autoJoinRooms = OptionsMonitor.CurrentValue.Rooms;

            if (autoJoinRooms.Any())
            {
                Logger.Information("Auto-joining room(s) {Rooms}", string.Join(", ", autoJoinRooms));
                await TryJoinAsync(autoJoinRooms);
            }

            var previouslyJoinedRooms = RoomTracker.Rooms
                .Where(room => !room.Value.IsSimulated)
                .Select(room => room.Key)
                .Except(autoJoinRooms);

            if (previouslyJoinedRooms.Any())
            {
                Logger.Information("Attempting to rejoin room(s) {Rooms}", string.Join(", ", previouslyJoinedRooms));
                await TryJoinAsync(previouslyJoinedRooms.ToArray());
            }
        }

        private void Client_RoomJoined(object sender, RoomJoinedEventArgs args)
        {
            if (args.Username == Client.Username)
            {
                Logger.Information("Joined room {Room}", args.RoomName);
                StateMutator.SetValue(state => state with { Rooms = state.Rooms.Concat(new[] { args.RoomName }).Distinct().ToArray() });
            }
            else
            {
                RoomTracker.TryAddUser(args.RoomName, args.UserData);
            }
        }

        private void Client_RoomLeft(object sender, RoomLeftEventArgs args)
        {
            if (args.Username == Client.Username)
            {
                Logger.Information("Left room {Room}", args.RoomName);
                StateMutator.SetValue(state => state with { Rooms = state.Rooms.Where(room => room != args.RoomName).ToArray() });
                CountUnread(args.RoomName, _ => 0);
            }
            else
            {
                RoomTracker.TryRemoveUser(args.RoomName, args.Username);
            }
        }

        /// <inheritdoc/>
        public void MarkRead(string roomName) => CountUnread(roomName, _ => 0);

        /// <inheritdoc/>
        public RoomMessage Simulate(string roomName, string username, string message)
        {
            // a no-op for a room already there, joined or simulated
            RoomTracker.TryAdd(roomName, new Room { Name = roomName, IsSimulated = true });

            var simulated = new RoomMessage
            {
                Timestamp = DateTime.UtcNow,
                RoomName = roomName,
                Username = username,
                Message = message,
                Direction = IsSelf(username) ? MessageDirection.Out : MessageDirection.In,
            };

            Logger.Information("Simulated a message from {Username} in {Room}", username, roomName);
            Receive(simulated);

            return simulated;
        }

        private bool IsSelf(string username) => !string.IsNullOrEmpty(username) && username == Client.Username;

        /// <summary>
        ///     Changes one room's unread count and publishes them all.
        /// </summary>
        /// <param name="roomName">The room.</param>
        /// <param name="change">The new count, from the old.</param>
        private void CountUnread(string roomName, Func<int, int> change)
        {
            if (string.IsNullOrEmpty(roomName))
            {
                return;
            }

            lock (UnreadLock)
            {
                var before = Unread.GetValueOrDefault(roomName);
                var after = change(before);

                if (after == before)
                {
                    return;
                }

                if (after > 0)
                {
                    Unread[roomName] = after;
                }
                else
                {
                    Unread.Remove(roomName);
                }

                var byRoom = Unread
                    .OrderBy(room => room.Key, StringComparer.Ordinal)
                    .Select(room => new UnreadRoomState(room.Key, room.Value))
                    .ToList();

                StateMutator.SetValue(state => state with
                {
                    RoomsUnread = new RoomsUnreadState
                    {
                        UnreadMessages = byRoom.Sum(room => room.Count),
                        UnreadRooms = byRoom.Count,
                        ByRoom = byRoom,
                    },
                });
            }
        }

        private void Client_RoomMessageReceived(object sender, RoomMessageReceivedEventArgs args)
            => Receive(RoomMessage.FromEventArgs(args, DateTime.UtcNow));

        private void Receive(RoomMessage message)
        {
            if (Users.IsBlacklisted(message.Username))
            {
                Logger.Debug("Ignored message from blacklisted user {Username} in {Room}: {Message}", message.Username, message.RoomName, message.Message);
                return;
            }

            RoomTracker.AddOrUpdateMessage(message.RoomName, message);

            // the server sends the operator's own messages back like anyone else's; those are never unread. Counted
            // before the event is raised, so that a notifier hearing it finds the count already moved
            if (!IsSelf(message.Username))
            {
                CountUnread(message.RoomName, count => count + 1);
            }

            // todo: persist these in the database before raising (why are we not??)
            EventBus.Raise<RoomMessageReceivedEvent>(new RoomMessageReceivedEvent
            {
                Message = message,
            });
        }
    }
}