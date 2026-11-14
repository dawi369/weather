# Windowsill

**Four seasons for your Windows desktop.** Snow in winter, cherry blossom and spring showers, a summer
meadow with fireflies after dusk, and autumn leaves. It all falls *behind* your windows and settles
*on* them: along their top edges and on the taskbar, drifting, piling up and tumbling off when you move things.

Clicks always go straight through. It lives in the tray and follows the calendar (or pick a season).

## The seasons

**Winter.** Snow that builds into uneven mounds, drifts in the wind, slides off when you drag a window and
flies off when you flick one. A little snowplough comes along to clear the taskbar when it gets deep.

**Spring.** Cherry-blossom petals flutter down into a thin pink carpet. Every so often a shower passes over,
with splashes on the ledges. Grass and spring flowers grow along the taskbar, and butterflies wander across and
rest on your windows (get close and they're off).

**Summer.** Dandelion seeds drift on the breeze, the meadow is tall with poppies and cornflowers, and after
8 pm fireflies come out. Hold the mouse still and they gather round it.

**Autumn.** Leaves tumble down like real ones, rocking side to side, and heap up on every ledge. Now and then a
dust devil spins them up off the taskbar, and a leaf blower clears it when it's deep.

## Play with it

Hold **Ctrl + Alt** and use the mouse (normal clicks are never touched):

| | Drag | Right-click |
|---|---|---|
| Winter | scoop snow off a ledge, roll it bigger, throw it. A plain click throws it at the screen: splat | build a snowman from the snow there, or knock one over |
| Spring | sweep through the petals | start or stop a shower |
| Summer | blow a dandelion clock | release a butterfly (fireflies at night) |
| Autumn | kick up the leaves | start a dust devil |

## Download

**[⬇ Download Windowsill.exe](https://github.com/dawi369/weather/releases/latest/download/Windowsill.exe)** (Windows 10/11, 64-bit)

Just run it, nothing to install. It shows up in the system tray; click it for settings.
Windows may say the app is from an unknown publisher: click **More info** → **Run anyway**.

A fresh exe is built on every push to `main` (see [Releases](https://github.com/dawi369/weather/releases)).
The app checks for a newer build once a day and asks before updating itself.
Earlier versions (called WeatherSnow) update to Windowsill by themselves and keep their settings.

## Good manners

- Steps aside for maximized windows, apps snapped side by side, and full-screen games and video.
- Pauses while the PC is locked. On battery it runs at 30 frames a second with a little less falling.
- Only the parts of the screen that change are redrawn and sent to the compositor.

If things sit in the wrong place on a PC, **Settings → About → Diagnostics** (`%APPDATA%\Windowsill\diagnostics.txt`)
shows the screen, taskbar and scaling values the app is working with.

## Building it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run -c Release
```

Or build the same single exe the release uses:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

The icon (`Windowsill.ico`) is drawn by the app itself: `Windowsill.exe --make-icon Windowsill.ico`.

## How it works

Two transparent, click-through windows: one covers the screen, one sits on the taskbar and is moved by a system
hook the instant the taskbar moves. Each window's top edge gets its own little height map, and a small physics loop
handles falling things, settling, wind drift, and chunks being thrown around. Snow, petals and leaves share the same
physics; they just pile, look and fall differently. Everything is drawn by a small software rasterizer.

Plain C# and Win32. No game engine, no dependencies.

---

Inspired by the snow in [KoiArt](https://koiart.app/) for macOS.
