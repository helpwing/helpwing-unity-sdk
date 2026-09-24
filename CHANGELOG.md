# Changelog

Versions are immutable once tagged, so every line here describes something already
released. The tag is the release: `v0.1.0` is 0.1.0, and Package Manager installs it by
`#v0.1.0` on the git URL.

## Unreleased

- **First release.** The Helpwing support chat for Unity 2022.3 and newer, a port of
  `@helpwing/react-native` with the same behaviour:
  - `HelpwingClient`: one conversation for the whole game. It polls on the main thread,
    stops while the app is paused, and survives scene loads.
  - `SupportUI`: a launcher with an unread badge and a chat panel, built in UI Toolkit from
    code. There are no prefabs or TextMeshPro, and it works with either input system.
  - `SupportChatView`, to put the chat inside a screen of your own.
  - `HelpwingChat`: the client with no UI and nothing from `UnityEngine`. It keeps an
    offline queue with idempotent retries, never retries a start by itself, keeps each
    conversation with the player who opened it, and counts unread against the server's
    cursor.
  - Markdown in message bodies, rendered as TextCore / TextMeshPro rich text with
    everything anybody typed escaped. Pictures written into an email body are drawn where
    they were written.
  - The project's translated title, greeting and offline message; its accent and colour
    scheme, following iOS and Android night mode; and overrides for both, plus the
    typeface.
