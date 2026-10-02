# CaptureView

A small app that shows live video from a capture device in a window, alongside some audio features.

Built for low-latency live preview, mainly having my laptop as a window on my PC or console gaming and tested with an Elgato HD60 Pro.

## Requirements

- Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer, to build

## Build and run

```
dotnet build
dotnet run --project src
```

## Setup

- **Camera privacy setting:** This app needs camera permissions to access a capture card display. 
    ```
    Settings -> Privacy & Security -> Camera -> "Let desktop apps access your camera"
    ```
- **Microphone privacy setting:** Additionally, this app needs microphone permissions to access any line-in or HDMI audio inputs.
    ```
    Settings -> Privacy & Security -> Microphone -> "Let desktop apps access your microphone"
    ```

## Problems and To-Do

- Audio drift is handled roughly, but some weird audio artefacts are still occuring.
- Introduce some keybinds.
- Possible minor improvements to latency.
    - Switch out from `MediaPlayer` to `MediaFrameReader` and write the frames manually.
- Create a linux version
    - Huge maybe, currently community support for the HD60 Pro seems to be on H.264 which introduces some latency + project currently relies on a lot of Windows API stuff currently but its not completely undoable.
