# RetroEmu user guide

Extract the complete Windows x64 package and open RetroEmu.exe. The package includes
its .NET runtime and native libraries; playing needs no SDK, Python, AI account or AI CLI.
Bring your own games. A graphics driver providing OpenGL 3.3 is required.
Firmware supplied with the package retains its respective licenses.
--no-ai starts a manual session without loading or saving personal AI settings and blocks
automatic provider creation. It can be combined with --preparar for local preparation.

## Language and interface

Choose Options > Preferences > Language > English. The selection is saved across restarts
and updates. --language en selects English at startup; --language es selects Spanish.
Menus, help, diagnostics, terminal vocabulary, CLI output and included design guides use
the selected language. Existing game text, personal titles, paths, source code and saved
identifiers retain their original content.

Retro is the graphical workbench. Green terminal uses the same menus and buttons in a
green palette. Real terminal provides RetroEmu commands with help, history and completion.
Choose Options > Interface styles to preview or change them. Looking does not apply a style.

Every confirmed update asks for an interface again. A manual update is detected by the
changed program version. An ordinary restart of that version does not repeat the question.
Keep current retains the existing choice; Use this style applies the preview. The Release
notes window opens afterward and remains available from Help or the news command.

## Play and the library

Select a library folder, open a game or drop a supported file on the window. The library
provides search, system/genre/year filters, favorites, recents and details. Recognizing
a format does not prove gameplay or readiness for fusion; these are separate evidence levels.
Optional online metadata lookup is off by default and personal corrections take priority.
After renaming or moving files within the selected folder, scan again. Identical copies
share one favorite and recent entry. Missing entries remain visible and disabled; adding
their folder and scanning restores access by content. Activity stores identities only,
so an unknown missing game shows a short key. Card Save and Cancel stay below the scrolling fields.

Return to game resumes from the library or tools. While playing, the keyboard belongs to
the emulated machine. In the library, dialogs and pause, Tab and arrows navigate, Enter or
Space activates and Escape goes back. Left Alt reaches menus. Tool shortcuts use Ctrl+Shift
on machines that need Ctrl themselves. Help lists shortcuts for the active machine.

No sound device is required to keep playing. Sound shows the condition and Retry audio can
find a newly connected output. Volume, mute, background mute and audio buffer are shared by
all styles. Larger buffers trade lower sensitivity to brief stalls for more latency.

Preferences offers text sizes 100, 125, 150, 175 and 200 percent, high contrast, fullscreen,
integer pixel scaling, controls and performance settings. Changes apply immediately.
A file from a later version is read conservatively: known valid values may be used, but
the file remains unchanged. Recover file keeps a complete backup before saving this version.

## Real terminal

Graphical search also lists commands with parameters and examples. Selecting one opens
an editable console line with its help; it does not execute it. Complete the arguments
and press Enter. Returning to the library or game retains the selected graphical style.

Press Ctrl+Shift+T to switch between game and console. The console is an internal command
interpreter, without host shell execution.

~~~text
games
filter genre Platformers
play 1
help play
options
option volume 40
option language en
interface real
tasks
news
~~~

Numbers refer to the latest game list. Use help for the complete current catalog and
help <command> for arguments, examples, requirements and recovery suggestions.
how <topic> searches capabilities. Tab completes commands and context values.
Up/Down recall history and Ctrl+R searches it. Ctrl+C copies selection, interrupts following
or clears the line according to context. Pasted multi-line commands are reviewed before
execution. Existing Spanish commands remain accepted for saved scripts.

Long operations return the prompt. tasks lists state and measured progress, task <id>
shows detail, follow <id> follows activity and cancel <id> requests cancellation.
Already produced results remain. resume-task works only for operations that support retry.
An unavailable command explains its graphical alternative instead of pretending to execute.

## Create, fuse and investigate

Create offers standalone assembly templates, fusions retaining an original base, and
new scenes created with originals. Manual copy/paste and agent-folder workflows are available;
AI is optional for ordinary play and editable templates.

Saved projects and recoverable work live inside each workshop. A draft, completed technical
test and finished playable game are different states. Keep original files and their associated
profiles together when continuing research.

Fusion preparation investigates startup, controls and edition-specific data. A loaded file,
screen coordinate or successful write does not establish a variable's meaning.
Code, analysis and debugging operate on the same running machine. Read hypotheses and
permissions before applying changes. Saved states for originals do not save a complete fusion
script plus all its machines.

The included English assembly, design and fusion API guides follow the selected language.
They describe capabilities and limits without requiring a specific AI provider.

## CLI

retro-cli --language en prints English usage. Examples without AI:

~~~text
retro-cli --language en info own-game.nes
retro-cli --language en create c64 my-game.s
retro-cli --language en asm my-game.s --cpu 6502 --output my-game.prg
retro-cli --language en prepare own-game.nes --ai none --no-save
~~~

The full usage lists supported machines and options. English aliases coexist with Spanish
commands; filenames and persisted schema fields are unchanged.

## If the window cannot open

A supervisor that does not load graphics or audio offers recovery if initialization fails
or no first frame appears within a minute. Windows asks before checking for a newer version
and again before downloading and installing it in the same folder.
Open RetroEmu.exe --repair to reach this recovery directly.

Update packages are checked by size, SHA-256 and manifest. Updates preserve games, projects,
saves and settings. Interrupted replacements recover through the update journal.
The details are saved to %LOCALAPPDATA%/RetroEmu/fallo.txt with personal names anonymized.

Recovery still requires Windows to load the executable and managed runtime, a valid manifest,
a writable installation folder, connectivity and a newer available version. It cannot repair
an incompatible graphics driver or an executable that Windows cannot run. Do not delete
pending update jobs manually.

## Known limits

Emulation compatibility depends on system and edition. Automated tests do not establish fun,
universal compatibility or complete gameplay. A physical audio failure, an OpenGL-incompatible
computer, Windows 10, Linux desktop, hardware controllers and actual OS display scaling still
need their own checks. Text-size screenshots do not prove OS DPI behavior.

Dear ImGui draws the interface without exposing a native screen-reader control tree.
Keyboard navigation and high contrast do not imply screen-reader support.
Older technical and historical Markdown documents remain in Spanish; this guide covers
the current user flow in English.
