# Midview — Resizable Classroom Tiles (Design)

Date: 2026-10-19
Status: Implemented 2026-10-19 (checked against fake students on loopback,
not yet on the lab)

## Context

The classroom grid drew every machine at a fixed 256 x 144. The teacher wants
to make the tiles smaller to see more machines at once, or bigger to see each
one better, and to find the grid the same way the next time Midview opens.
The choices below were made by the owner when the design was proposed.

## Design

- **Sizes:** the width of a tile's screen comes in steps, 160, 192, 224, 256,
  320, 384, 448, 512 and 640; the height is always 16:9. 256 is the default,
  the size tiles always had (`TileSize`).
- **Controls** (right of the status bar): a slider that snaps to the steps,
  zoom-out and zoom-in buttons on either side, and "Caber todas". Ctrl + the
  mouse wheel and Ctrl +, Ctrl - (main keys or number pad) move one step;
  Ctrl 0 goes back to 256. A touchpad's small wheel turns add up to whole
  steps.
- **"Caber todas"** picks, once, the largest step at which every machine is
  in view without scrolling, measured on the tiles as drawn (card, margin,
  name and badges, the tallest tile counting). When even 160 doesn't fit,
  160. Adding machines or resizing the window afterwards doesn't change the
  size again.
- **Long names** are cut short with "..." instead of widening their tile;
  the badges under the name wrap.
- **Remembered** in `%LOCALAPPDATA%\Midview\grid-view.json`
  (`{"TileWidth": 384}`), saved at each change. Missing or unreadable is
  256; a width that isn't a step is the nearest step.

## Thumbnail size

- `RequestThumbnail` now carries the box the thumbnail must fit in, width
  then height, uint16 big-endian each (`ThumbnailRequest`). The Session keeps
  the screen's aspect ratio inside it and never scales up, as before.
- The Master asks for the tile in real screen pixels (Windows' scale on the
  window's monitor, followed when it changes), up to 960 x 540 -- the
  largest tile at 150 %. Each poll reads the current size, so a resize shows
  sharp thumbnails from the next poll on (within 3 s).
- **Compatibility:** an empty request (a Master from before this) gets
  320 x 180, the old fixed size; an unreadable one too; a size past the
  maximum is capped. A Session from before this ignores the payload and
  sends 320 x 180, so its tiles only look soft when made bigger than that.
- Network (an estimate, not measured on the lab): roughly 4 Mbit/s for 30
  machines at 640 px, against about 1 at the old fixed size; small tiles now
  ask for less than before (160 x 90 at the smallest).

## Master's data folder

All of the teacher's files (`known-machines.json`, the key, the schedule,
the program block, the saved menus, the wake log, `grid-view.json`) now take
their folder from `MasterData.Folder`, which is
`%LOCALAPPDATA%\Midview` as before. A tool that runs Master's window against
made-up machines points it at a temporary folder, so the teacher's real
classroom is never touched or contacted.

## Tests

- `ThumbnailRequestTests` (Protocol): round trip, empty and malformed
  requests get the default, past the maximum is capped, `Encode` refuses
  sizes outside the limits.
- `ThumbnailRendererTests`, `SessionServerTests` (Session): a larger box
  gives a larger JPEG; one preview connection answers 160 x 90, then
  960 x 540, as asked.
- `ThumbnailPollerTests` (Master, loopback Session): the poller asks for the
  size it's given and follows a change on its next poll.
- `TileSizeTests`: steps, Larger/Smaller/Snap, "Caber todas" for 1, 4, 12
  and 30 machines, too narrow for one tile, and the request in screen pixels
  at 100-300 % never outside what the payload allows.
- `GridViewFileTests`: missing, round trip, unreadable, not a step.
- `MasterDataTests`: the folder is `%LOCALAPPDATA%\Midview`, and every file
  of the teacher's follows it.
- Checked by hand: the real `MainWindow` with 24 fake students on
  127.0.0.2-25 and one offline, Master's data in a temp folder. Ctrl - twice
  (256 to 192), "Caber todas" (160 in a 1440 x 760 window), Ctrl + wheel three
  notches (back to 256), slider to 640 (thumbnails arrive at 640 x 360),
  Ctrl 0 (256), and reopening at the saved 384.
