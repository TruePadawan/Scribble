# Drawing room protocol v3

Deploy the desktop client and server together.~~~~ Run one server instance. It keeps accepted room history and
credentials in memory. A
server restart ends every
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

## Joining an image-heavy board

Admission freezes a board, its event history, and an image manifest at one confirmed sequence. Concurrent joins can
share the same cut. The desktop displays the ordered placements with placeholders, then requests images intersecting its
viewport, other placed images, and finally images needed only for undo. It resolves each distinct file once per
connection. A byte counter includes locally available data. Cancel discards the preview and preserves the solo document
and undo stack.

Drawing continues during a download. The joining client drains edits newer than its cut in sequence before enabling
input. The full history restores selection targets and the participant’s undo stack. The preview alone never becomes an
editable document.

## Storage and retention

| Resource                            | Limit or lifetime                               |
|-------------------------------------|-------------------------------------------------|
| Confirmed event metadata            | 8 MiB of canonical UTF-8 JSON per room          |
| Image files                         | 32 MiB of distinct encoded image bytes per room |
| Transfer chunk                      | 128 KiB before SignalR's JSON/base64 encoding   |
| Undo, deleted images and reconnects | Full accepted history until room expiry         |

Uploads use SHA-256 of the original file bytes. Each placement has its own ID, owner, layer, bounds and rotation. The
registry validates the completed hash, ownership and both quotas before charging for a new file. Deletion retains the
file for undo. Staging ends with the connection or submission. The room and its retained files expire when its last
reclaim credential expires. These limits cover stored payloads. Rendering, buffers, and active connections consume
additional memory.

## Concurrent join measurements

Linux Release build, one isolated Kestrel process per case. Two clients joined together over long polling with a
simulated 40 ms round trip and 10 Mbit/s downstream per connection. The image cases reuse a noisy 256 × 256 PNG.
Response bytes are the measured GET bodies for one joining client, including SignalR framing. Peak RSS includes seeding
and both joins. Times are observations from one run.

| Protocol and workload                | Response bytes | Server peak RSS | First preview |  Both joins editable | Client replay |
|--------------------------------------|---------------:|----------------:|--------------:|---------------------:|--------------:|
| v2, 8 MiB inline cap, 24 placements  |      8,154,500 |         291 MiB |             — |               6.99 s |        0.2 ms |
| v2, 32 MiB inline cap, 96 placements |     32,610,803 |         801 MiB |             — | Failed after 26.97 s |             — |
| v3, 24 placements                    |        337,556 |          91 MiB |        0.34 s |               0.73 s |        0.1 ms |
| v3, 96 placements                    |        419,897 |          93 MiB |        0.38 s |               0.81 s |        0.2 ms |
| v3, one 5,000-point pencil stroke    |      2,659,626 |         124 MiB |        0.44 s |               3.37 s |      243.5 ms |

The larger inline trial ended without a connected reader. The v3 asset allowance is an enforced storage budget. Repeated
placements cost metadata, while distinct files consume their encoded size. Stroke-heavy boards still pay for replay when
populating a fresh snapshot. To repeat the v3 benchmark, run
`dotnet test --solution Scribble.slnx -c Release --filter-method '*JoinRoom_LargeHistory_InstallsCompleteSnapshot'`.
