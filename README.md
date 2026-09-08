# ETABS Story/Frame Logger — Plugin POC

A minimal ETABS External Plugin (VB.NET) that proves out the CSI OAPI
plugin architecture end-to-end: it loads inside ETABS, reads the open
model, and reports story names plus a column/beam breakdown. It's a
learning build, not production tooling — no configuration, no
installer, one plugin, one job.

## How it works

ETABS' External Plugin mechanism lets a compiled .NET DLL run inside a
live ETABS session without any external automation script. ETABS loads
the DLL via reflection and looks for a class implementing the
`ETABSv1.cPluginContract` interface, which has two members (confirmed
by reflecting over an installed `ETABSv1.dll` — this shape differs
from the older `cPlugin`/`cOAPI` pattern shown in some CSI examples):

- `Info(ByRef Text As String) As Integer` — returns the label ETABS
  shows for the plugin in the External Plugin list.
- `Main(ByRef SapModel As cSapModel, ByRef ISapPlugin As cPluginCallback)`
  — the entry point ETABS calls when the user runs the plugin.
  `SapModel` is the currently open model, handed to the plugin
  directly (it may be `Nothing` if no model is open). `ISapPlugin` is
  a callback object used to tell ETABS the plugin finished — call
  `ISapPlugin.Finish(0)` on success or `ISapPlugin.Finish(1)` on
  failure; there's no `ByRef ret` return value.

`src/StoryFrameLoggerPlugin/Plugin.vb` implements this:

1. Guards against `ISapPlugin` or `SapModel` being unavailable (no
   model open) and reports that instead of throwing.
2. Reads story names via `SapModel.Story.GetNameList`.
3. Reads all frame object names via `SapModel.FrameObj.GetNameList`,
   then classifies each with `SapModel.FrameObj.GetDesignOrientation`
   to count columns vs. beams (vs. anything else, e.g. braces).
4. Writes a timestamped report to
   `%UserProfile%\Documents\ETABS-Plugin-Logs\` and shows it in a
   message box.
5. Wraps everything in a `Try/Catch` so a COM failure or missing model
   shows an error dialog instead of taking down ETABS, and still
   signals completion back to ETABS via `ISapPlugin.Finish(...)`.

## Prerequisites

- ETABS 2024 or later, installed locally (provides the `ETABSv1.dll`
  interop assembly the plugin references).
- .NET Framework 4.8 targeting pack (ships with Visual Studio's ".NET
  desktop development" workload, or install separately).
- Visual Studio 2022, or the `dotnet` CLI with the .NET Framework
  build tools available.

## Build

`ETABSv1.dll` is licensed CSI software that ships with your ETABS
install — it is **not** included in this repo. Point the build at your
local copy via the `ETABS_INSTALL_DIR` MSBuild property (defaults to
`C:\Program Files\Computers and Structures\ETABS 22` if omitted):

```powershell
dotnet build src\StoryFrameLoggerPlugin\StoryFrameLoggerPlugin.vbproj `
  -p:ETABS_INSTALL_DIR="C:\Program Files\Computers and Structures\ETABS 22" `
  -c Release
```

Or in Visual Studio: open the `.vbproj`, then in Solution Explorer >
References, update the `ETABSv1` reference's path to your local
`ETABSv1.dll` before building.

The build produces `StoryFrameLoggerPlugin.dll` under
`src/StoryFrameLoggerPlugin/bin/Release/net48/`.

## Load it in ETABS

1. Open ETABS with a model (or a blank session).
2. Go to the **External Plugin** dialog — in recent ETABS versions
   this is under **File > External Plugin...** (menu wording has
   shifted between versions; look under **File** or **Tools** for
   "External Plugin" / "Add-Ins" if it's not there, or check CSI's
   "Plugin" help topic for your exact build).
3. **Add** and browse to `StoryFrameLoggerPlugin.dll`.
4. It should appear in the list labeled **"Story/Frame Logger (POC) -
   logs story names and column/beam counts for the open model"** (from
   `Info()`). Select it and click **Run**.
5. A message box shows the report; the same text is saved to
   `Documents\ETABS-Plugin-Logs\StoryFrameLogger_<timestamp>.log`.

Running it with no model open reports "No model is currently open" and
exits cleanly instead of erroring.

## Test fixture

`test-model/BUILD_TEST_MODEL.md` walks through building a 3-story
(`G`, `L1`, `L2`), 1x1-bay (4 columns), 2-beams-per-floor model in
ETABS' Grid Only wizard — a couple of minutes of clicking, since
`.edb` is a binary format with no public writer.

Expected output against that fixture:

```
Stories (3):
  - L2
  - L1
  - G

Total frame objects: 18
  Columns: 12
  Beams:   6
```

## Project layout

```
src/StoryFrameLoggerPlugin/
  StoryFrameLoggerPlugin.vbproj   Class library targeting net48
  Plugin.vb                       cPluginContract implementation
test-model/
  BUILD_TEST_MODEL.md             Steps to build the fixture model in ETABS
```
