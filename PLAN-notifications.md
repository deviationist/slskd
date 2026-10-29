# Notifications — plan

One way for slskd to tell the operator something, on one or more **channels**
(mail, ntfy), for a set of **events** (a chat message, watch results, a
problem), each routed and paced on its own. Built on slskd's event bus and
state, so no feature has to call it by hand except where it composes its own
message.

## What exists, and what changes

| Today | Becomes |
|---|---|
| Search watches mail their results through `MailService` | They notify through this system; mail is one channel, ntfy can be another |
| Share links mail recipients their links through `MailService` | **Unchanged.** That is mail to other people, a product feature, not a notification to the operator |
| Private messages and room mentions push to Pushbullet (upstream, `Application.cs`), off on quim | **Unchanged and untouched**, so upstream merges stay clean. The new system covers the same events without it |
| Webhooks and scripts on any event (upstream) | **Unchanged.** They are automation, not telling a person something |
| No ntfy | A channel |

## Decisions (2026-10-29)

1. **Events in v1:** private messages, room mentions, search watch results, and
   problems (the Soulseek connection lost and not coming back; a share scan
   failing).
2. **Chat pacing:** notify only when a message is **still unread after a delay**
   (default 60 s), and at most **once per conversation until it is read**.
   Nothing is sent while the operator is replying in Chat.
3. **Pushbullet:** left alone.
4. **Routing:** per event, by name. An event that names no channels goes to
   every enabled channel.

## Configuration

```yaml
integrations:
  mail: ...                  # as today; the mail channel sends through it
  notifications:
    ntfy:
      url: https://ntfy.ichiva.no
      topic: slskd
      token: <access token>  # [Secret]
    events:
      private_message:
        enabled: true
        channels: [ntfy]
        delay: 60            # seconds a message must stay unread
      room_mention:
        enabled: true
        channels: [ntfy]
      watch_results:
        enabled: true        # the default, so existing watches keep mailing
        channels: [mail, ntfy]
      connection_lost:
        enabled: true
        after: 10            # minutes logged out before it is a problem
      share_scan_failed:
        enabled: true
```

- **Channels:** `mail` is on when `integrations.mail` is; `ntfy` is on when it
  has a URL and topic.
- **Opt-in:** every event is off by default except `watch_results`, which is on
  so that existing watches keep mailing exactly as they do today. Nothing new is
  sent until it is configured.

## Shape

- `Integrations/Notifications/`:
  - `Notification`: event, title, a short plain text, an optional click URL,
    priority, and for mail an optional HTML body and recipient. No tags: ntfy
    draws a tag it knows as an emoji, and notifications are plain text. A sender
    composes one and does not know which channels carry it.
  - `INotificationChannel`: `Name`, `IsEnabled`, `SendAsync`. Two in v1:
    - `MailNotificationChannel` sends through `MailService`. The recipient is the
      notification's own (a watch's address) or `integrations.mail.to`.
    - `NtfyNotificationChannel` publishes as JSON (`POST {url}` with topic,
      title, message, priority, click), with a bearer token. JSON rather
      than headers, because headers cannot carry a title like "Bliss Inc Remix —
      2 copies" safely.
  - `NotificationService.NotifyAsync(notification)` routes to the event's
    channels and returns each channel's result. One failing channel does not
    stop the others, and nothing is thrown at the caller.
  - Event sources, each a small subscriber:
    - **Private messages:** on `PrivateMessageReceivedEvent`, waits `delay`, then
      notifies if that person still has unread messages (the same
      `state.conversations` the menu reads) and nothing has been sent since they
      were last read. Clears its "sent" mark when their count goes to 0.
    - **Room mentions:** on `RoomMessageReceivedEvent` mentioning the logged-in
      username, at most once per room per 10 minutes.
    - **Connection:** from the state, logged out longer than `after` minutes
      notifies once, and a recovery after that notifies "back online".
    - **Share scans:** the share state becoming `Faulted` notifies once.
  - **Watch results:** `WatchService` composes its notification as it composes
    its mail today (subject, text, HTML, the watch's address) and hands it to
    `NotifyAsync`. Its record of what was sent keeps working: it stores the mail
    channel's result as it stored `MailService`'s.
- `POST /api/v0/notifications/test` with `{ channel }` sends a test notification
  through one channel, so a configuration can be checked without waiting for a
  message.

## Phasing

1. **Core:** `Notification`, the channel interface, the mail and ntfy channels,
   `NotificationService`, the options, the test route. Tests with fake channels
   and a fake ntfy server.
2. **Private messages,** with the unread delay and once-per-conversation rule.
3. **Watch results** moved onto it, with their history and per-watch address
   kept.
4. **Room mentions and problems.**
5. **Deploy:** on xavi's ntfy, a write-only token for topic `slskd` and read
   access for the operator's phone (as for `hospex`); then
   `integrations.notifications` on quim, and a test through each channel.

## Not in v1

- A settings page: configuration is the YAML, as for mail.
- More channels (Pushover, Gotify, Discord): each would be one class behind
  `INotificationChannel` plus its options.
- Quiet hours, or a digest.
