# ❄ Weather

Snow for your Windows desktop. It lives in the tray, falls over everything, and actually *lands*:
it piles up on top of your windows and on the taskbar, drifts in the wind, and reacts when you move things around.

Clicks always go straight through it.

## What it does

- **Builds up for real.** Snow settles into uneven mounds, gets blown into drifts, and spills off the edges in chunks once it gets deep enough.
- **Moves with your windows.** Drag a window slowly and the snow rides along. Flick it up fast and the snow flies off. Shake it side to side and it goes everywhere.
- **Plays nicely with other windows.** Drag a window down past one behind it and its snow gets left behind there. Drag it back up and it scoops it up again.
- **Rides the taskbar.** Snow sits on the taskbar and follows it when an auto-hide taskbar slides in and out.
- **Gets out of the way.** It hides behind maximized windows and full-screen games and videos, and drops what had piled up when you minimize.

## Getting started

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run -c Release
```

Or build a single exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Then look for the snowflake in your tray. Double-click it for settings: snowfall, flake size, wind and gusts, how sticky the snow is,
how deep it can get, and when to clear it. Right-click for pause, clear and start-with-Windows.

## How it works

A transparent, click-through window covers the screen. Each window's top edge gets its own little height map of snow,
and a small physics loop handles falling flakes, settling, wind drift, and chunks of snow being thrown around.
The taskbar snow lives in a separate window that a system hook moves the instant the taskbar does, so it never lags behind.

Everything is plain C# and Win32. No game engine, no dependencies.

---

Inspired by the snow in [KoiArt](https://koiart.app/) for macOS.
