# Drawing room protocol v2

Deploy the desktop client and server together. The versioned join methods reject a legacy client before it can join a
room. Run one server instance. It keeps accepted room history and credentials in memory. A server restart ends every
room instance.

## Editing and ownership

The server issues each participant a public drawing ID and a private 256-bit reconnect token. Only the joining
connection receives the token. The desktop service keeps it in memory for that server and room code. The existing
serialized field `CreatorConnectionId` carries the participant ID on room events and elements. Connection IDs still
route messages and identify roster entries.

A new room takes its first member’s solo board. The server assigns the seed to that participant. A board with multiple
previous authors enters as a baseline of its visible elements so old selection gestures cannot acquire new targets. That
baseline starts a new undo history. In an existing room the joining client installs the server snapshot and recovers its
own undoable actions.

Selection, erasing, and edits operate on owned elements. Both client and server validate each target of a batch. The
server also tracks ownership of action IDs, eraser gestures and selection bounds. A paste creates new elements owned by
the participant who pasted them. Board replacement is a solo operation.

Edits to distinct strokes can still interact through layer order. The server assigns each accepted event a room sequence
and acknowledges the writer. Writers see their own edit immediately. When a confirmation arrives out of order, the
client places it in room order, invalidates its old checkpoints and replays. A late join starts with the same accepted
sequence and merges live events by ID.

## Leaving and returning

Leaving immediately removes the member from the roster. After a leave or disconnect, rejoining within ten minutes with
the same token restores the participant ID. The newest connection takes over editing rights. A disconnected desktop
returns to solo mode and the user can join again. Explicit Leave never automatically rejoins.

The server retains the accepted board even when the last member leaves. Solo edits made afterward cannot enter that room
on rejoin. An expired credential grants a new identity, including when the room code has been reused. Closing the app
discards its tokens. The desktop explains when a saved credential can no longer reclaim ownership.

## Transfer limits and measurements

The application caps the canonical UTF-8 history at **8 MiB**. Seed admission and live edits enforce the same limit so
all accepted boards remain joinable. SignalR allows twice the application size plus 64 KiB for escaped JSON and envelope
fields. A failed join preserves the solo board. A failed live send retains the local edit and surfaces the existing
synchronization warning.