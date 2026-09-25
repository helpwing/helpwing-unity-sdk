# Helpwing for Unity

The [Helpwing](https://helpwing.app) support chat for Unity games and apps. Game chat,
website chat and email tickets land in the same inbox.

- Drop-in launcher and chat panel, or a `HelpwingChat` client to draw your own
- Themed from your project's accent colour, or overridden to match your game
- Every string is an inspector field, so it speaks whatever language your game does
- Survives tunnels and app kills: messages queue, persist and retry
- No dependencies: UI Toolkit built from code, no prefabs, no TextMeshPro, either input system

Unity **2022.3** or newer. iOS, Android, desktop and WebGL.

## Install

**Window → Package Manager → + → Add package from git URL…**

```
https://github.com/helpwing/helpwing-unity-sdk.git
```

or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "ru.fanyagin.helpwing": "https://github.com/helpwing/helpwing-unity-sdk.git#v0.1.0"
  }
}
```

## Setup

1. Add an empty GameObject to your first scene and give it **Helpwing Client**
   (`Add Component → Helpwing → Helpwing Client`). Fill in the API URL and project key.
2. Give the same object **Support UI** (`Add Component → Helpwing → Support UI`).

That is the whole thing: a launcher appears in the corner once the project's config has
loaded, and opens the chat over the game. The client survives scene loads by default.

Or from code:

```csharp
using Helpwing;
using Helpwing.UI;

var client = HelpwingClient.Create("https://api.helpwing.app", "pk_live_...");
client.gameObject.AddComponent<SupportUI>();
```

The project key is public. It is the same one the website snippet carries in `data-project`,
and shipping it in a build is expected.

`apiUrl` is whichever host serves `/widget.js`, because that is the host `/widget/` is
routed on.

## Your own button instead

The launcher is a convenience. A game with its own "Support" button in a pause menu turns
it off (`Show Launcher` on Support UI) and opens the panel itself:

```csharp
supportButton.onClick.AddListener(HelpwingClient.Instance.Open);
badge.text = HelpwingClient.Instance.State.UnreadCount.ToString();
```

`HelpwingClient.StateChanged` fires on every change, so the badge can follow it. To put the
chat inside your own UI Toolkit screen rather than over the game, add a `SupportChatView`
to it. While that view is on a panel, the player counts as reading the conversation:

```csharp
myScreen.Add(new SupportChatView(HelpwingClient.Instance, labels));
```

## Your own UI entirely

Everything the components draw comes from `HelpwingClient.Chat`, a `HelpwingChat` that
has no idea how it is drawn. `Chat.State` holds `Messages`, `UnreadCount`, `Typing`,
`Offline`, `Config` and `Conversation`. Draw them with uGUI or TextMeshPro:

```csharp
client.StateChanged += state =>
{
    foreach (var message in state.Messages)
    {
        // Same tags TextMeshPro speaks, and nothing anybody typed can become one.
        var body = RichText.Render(message.Text);
    }
};
await client.Send(inputField.text);
```

Call `client.Chat.SetPresent(true)` while your transcript is on screen and `false` when it
is not. That flag is what stops an agent's reply also being emailed to a player who is
watching it arrive.

## Telling us who the player is

```csharp
await client.Identify(new Identity
{
    Id = account.Id,
    Email = account.Email,
    Name = account.DisplayName,
    UserHash = account.SupportHash,
});
```

Compared by value, so calling it on every sign-in, or every frame, only talks to the server
when something changed. It applies to a conversation that started before anyone signed in
too. A player who asked something anonymously and then signed in is the ordinary case, and
the conversation moves to their customer record when they do.

**`UserHash` is an HMAC-SHA256 of the user id, keyed with your project's identity secret,
and your server computes it.** A game build is a zip file with your code in it, and a
secret shipped inside one is not a secret. Fetch the hash from your own backend alongside
the rest of the signed-in account, the way you would a session token.

With identity verification turned on in the dashboard, an unproved claim is not refused.
It simply buys nothing. The player gets a customer record of their own, and what they
claimed is kept where an agent can see it without the profile implying anybody vouched for
it.

On sign-out, `Identify(null)` stops the *next* conversation being attributed to whoever
just left. `ResetConversation()` also forgets the conversation itself, which is what you
want on a shared device.

Signing straight in as somebody else needs neither. A conversation belongs to whoever
opened it: when the client sees A and then B, B does not inherit A's. It is dropped from
the device and B opens their own. The server draws the same line and refuses to move a
conversation between two identified customers.

## Text

Two kinds of string, and they come from different places.

**The chat's own chrome** ("Send", "Write a message…"). No translations ship with this
package. Every string is a field on **Support UI → Labels**, or `SupportLabels` in code:

```csharp
new SupportChatView(client, new SupportLabels { placeholder = "Écrivez un message…", send = "Envoyer" });
```

Your game knows what language it is in and already has a way to say so. Shipping a
dictionary of our own would mean deciding which of two translation systems wins on one
screen.

**What the project wrote**: the header, the greeting, the offline message and the typing
text. Those are translated in the dashboard under **Chat widget → Appearance**, and picked
by the client's **Locale** field. Left blank, it uses the device language. `ru-RU` finds
`ru`, and a language the project has not translated falls back to what it wrote without one,
never to a stock line of ours. Read them with `client.Copy` if you draw your own empty
state.

The typing text (`client.Copy.TypingText`) is what the transcript shows while an agent is
writing a reply, with every `{name}` swapped for the agent's name. A project that has not
written one falls back to `SupportLabels.typing`, the built-in `SupportChatView` already
uses — so an integrator's own override still applies until the project writes its own text.

## Colours

The project picks an accent in the dashboard and everything else is worked out from it,
including whether text on top of it should be white or ink.

Light or dark comes from the same place (**Chat widget → Appearance → Colour scheme**) and
ships as `Auto`, which follows the device: iOS and Android night mode, and the editor's own
skin while you are in play mode. Set **Color Scheme** on the client to `Light` or `Dark` for
a game that is one or the other whatever the phone says.

A game with an art direction of its own names only what differs, under **Theme Colors** on
the client, or in code:

```csharp
client.ThemeColors = new ThemeColors { accent = "#c6ff3a", background = "#0a0a0b", surface = "#141517" };
```

The typeface is **Font** (a regular `Font`) or **Font Asset** (a UI Toolkit SDF font asset)
on Support UI. Left empty, it is Unity's built-in runtime font.

## Delivery, polling and offline

Handled for you, but worth knowing because it is what you will see:

- New messages are polled every 5 seconds while the chat is on screen and every 30 while
  it is not, so the unread badge stays honest. Polling stops entirely while the app is
  paused or, on a phone, loses focus. Both intervals are inspector fields.
- Polling runs off `Update`, on the main thread, so it works on WebGL too.
- Sends are idempotent and retried automatically, so a request lost mid-tunnel does not
  produce a duplicate. Starting a brand new conversation is the exception: if its answer is
  lost, it is shown as a failed send, and retrying it is the player's decision.
- Unsent messages are written to PlayerPrefs, so they go out on the next launch, in order,
  even if the process was killed.
- Unread counts are computed against the server's cursor, never the device clock.

## Operating hours

The project's **Settings → Operating hours** screen decides whether the chat greets a
player or offers its offline message: `State.Config.IsOnline` is that answer, and the chat
view already draws both. A project may also choose to disappear outside its hours rather
than show an offline message. Then `State.Status` is `Unconfigured` while it is closed, the
same state a widget that was turned off reports, so a game that handles one handles both.

## Markdown

Agents answer from a composer that is a markdown editor with a preview, so a reply arrives
formatted rather than as the markers that made it: **bold**, _italic_, `code`, links,
bulleted and numbered lists, quotes, headings, fenced code, rules and tables. Nothing to
turn on, and no dependency. `Markdown.Parse` produces plain data, and `RichText.Render`
turns it into TextCore rich-text tags. Those are the same tags TextMeshPro reads. Every `<`
anybody typed is wrapped in `<noparse>`, so nothing in a message can become markup.

`ChatMessage.Markdown` says whether `Text` is markdown. It is false only for a message
stored before the server translated email into markdown, whose `Text` is the flattened
reading of markup that is all it has.

A picture pasted into an email comes back as an `IsInline` attachment, and the body points
at it by `ContentId`: `![alt](cid:<content_id>)`. The chat draws it where it was written and
leaves it out of the file list underneath. A `cid` with no file behind it renders as its alt
text.

Links open with `Application.OpenURL`, and only ever `http`, `https`, `mailto` and `tel`.
Any other destination is printed as the words it was made of. On Unity 2023.2 and newer the
link itself is tappable. Before that, UI Toolkit has no link events, so each link is also
listed under the message as a line of its own.

## What it does not do

- **Push notifications.** Nothing arrives while the game is closed. Ask for an email address
  (`require_email` on the widget settings screen), and a reply written while the player is
  away reaches them there. That is the same fallback the website chat has.
- **Attachments from the player.** The API has no endpoint for it yet. A file that came with
  a message is listed by filename, except a picture written into the body, which is drawn
  there.
- **One conversation on two devices.** A visitor token belongs to one installation, so a
  reinstall starts a new conversation.
- **WebGL on another origin.** A WebGL build is a web page, and the browser sends its
  origin. Add that origin under the widget's allowed origins, as you would for the website
  snippet.

## The chat without the components

`HelpwingChat` lives in `Runtime/Core` and uses nothing from `UnityEngine`. You hand it an
`ITransportHttp`, an `IHelpwingStorage` and an `IScheduler`, which is how the tests run it
under plain `dotnet test`.

| | |
| --- | --- |
| `StartAsync()` | Load the config and whatever conversation is stored. Never throws. |
| `SendAsync(text, email)` | Send, drawing the message before it has gone anywhere. |
| `RetryAsync(clientMessageId)` | Try a failed message again. |
| `IdentifyAsync(identity \| null)` | Say who the player is, now or later. |
| `SetPresent(bool)` | Whether the conversation is in front of them. |
| `SetActive(bool)` | Whether the app is in the foreground. |
| `MarkRead()` | Clear the unread count. |
| `RefreshAsync()` | Ask now rather than at the next tick. |
| `ResetAsync()` | Forget the player and the conversation on this device. For sign-out. |
| `Subscribe(listener)` / `StateChanged` | Called on every change. `Subscribe` also calls immediately. |
| `Dispose()` | Stop everything. |

Errors land in `State.Error` rather than being thrown. There is nothing a game can usefully
do with an exception raised while it was drawing a button.

## Reference

| Field on `HelpwingClient` | |
| --- | --- |
| `Api Url` | Where the API lives. |
| `Project Key` | `pk_…`. Required. |
| `Poll Interval Seconds` | While the chat is on screen. Default 5. |
| `Background Poll Interval Seconds` | While it is not. Default 30; `0` to stop. |
| `Locale` | Picks the project's translated copy, and is sent when a conversation opens. Blank is the device language. |
| `Color Scheme` | `Project` (default), `Light` or `Dark`. |
| `Theme Colors` | Override any colour. Blank fields keep the project's. |
| `Dont Destroy On Load` | Keep the client across scenes. Default on. |

| Field on `SupportUI` | |
| --- | --- |
| `Client` | Left empty, `HelpwingClient.Instance`. |
| `Panel Settings` | Left empty, one is made at runtime. |
| `Sorting Order` | Where the chat draws among your other UI Toolkit panels. Default 1000. |
| `Font` / `Font Asset` | The typeface. |
| `Labels` | Every string the chat draws. |
| `Show Launcher`, `Launcher Corner`, `Launcher Offset` | The button in the corner. |
| `Title` | The panel's heading. Left empty, the project's own heading, then its name. |
| `Full Screen Below Width` | Narrower panels get the chat full screen, wider ones a card. |

Also public: `SupportChatView`, `SupportLauncherView`, `MessageBubble`, `MarkdownView`,
`SupportLabels`, `SupportTheme`, `HelpwingChat`, `HelpwingException`, `Markdown`,
`RichText`, `Copy`, `Palette`, `Theme`, `PlayerPrefsStorage`, `MemoryStorage`,
`UnityWebRequestHttp`, `FrameScheduler`, `DeviceInfo`, and the types for all of them.

## Development

```bash
dotnet test Tests~/Helpwing.Core.Tests
```

runs the client, markdown, copy and palette suites against `Runtime/Core` with no Unity at
all. `Tests/Editor` holds a smaller EditMode suite for the Unity Test Runner. Add
`"testables": ["ru.fanyagin.helpwing"]` to a project's manifest to see it there.

## Links

- [Documentation](https://helpwing.app/docs/unity)
- [Issues](https://github.com/helpwing/helpwing-unity-sdk/issues)
- [Changelog](CHANGELOG.md)

MIT © Helpwing
