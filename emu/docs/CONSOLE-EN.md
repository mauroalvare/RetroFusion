# Real terminal command reference

Choose English in Preferences. Existing Spanish aliases remain accepted.
Use help <command> for examples, requirements and errors.


### Library

| Command | Function |
| --- | --- |
| `cd <section>` (`seccion`, `ir`) | Changes the virtual section; does not browse system folders. |
| `pwd` | Shows the current section and active filters. |
| `ls` (`dir`) | Lists the current section. |
| `sections` | Shows each library section and its count. |
| `games [text…]` (`lista`) | Lists and numbers this section's games with active filters. |
| `search <text…>` (`find`) | Searches titles and genres while keeping other filters. |
| `filter <field> [value…]` (`filtro`) | Combines filters shared with the graphical library. |
| `filters` | Shows active filters and sort order. |
| `sort <name\|name-desc\|year-desc\|year\|recent\|added>` | Changes the list order. |
| `info <game…>` | Shows system, metadata, provenance, availability and preparation. |
| `correct <game> <genre\|year> <value…>` (`editar-ficha`) | Saves a personal correction, taking priority over detected data. |
| `favorite <game> [on\|off]` (`fav`) | Adds or removes the favorite shown in the library. |
| `favorites` | Lists your favorites. |
| `recent` | Lists recently opened games. |
| `identify [status\|on\|off\|retry]` (`catalogo`) | Queries or controls game identification. |
| `reject <game> [on\|off]` (`rechazar`) | Rejects or restores automatic identification for a game. |
| `candidates <game…>` | Queries possible game identities from the catalog for review. |
| `candidate <number> <accept\|reject>` | Accepts or rejects a candidate from the last search. |
| `folder [path…]` (`roms`) | Shows or changes the games folder and remembers it. |
| `rescan` (`actualizar-biblioteca`, `refrescar`) | Rescans the games folder in the background. |

### Play

| Command | Function |
| --- | --- |
| `play <game…>` (`correr-juego`) | Opens an original, creation or fusion and gives it the keyboard. |
| `open <path…>` | Opens a file on this computer in the emulator. |
| `continue` (`seguir-jugando`, `volver`, `resume`) | Returns to the game and gives it the keyboard. |
| `pause` (`pausar`) | Pauses the machine without leaving the console. |
| `reset [hard]` | Resets the session or fusion; hard also clears the original's memory. |
| `capture` (`foto`, `screenshot`) | Saves the emulated image at its resolution. |
| `slots` | Lists saves for this game and edition. |
| `save <slot> [replace]` | Saves the session to a slot. If occupied, you must enter replace. |
| `load <slot>` | Restores a slot for this game and edition. |
| `rewind [action]` | Enables history or rewinds the game. |
| `inputs [status\|record\|finish\|replay\|stop]` (`grabar`) | Records and replays this session's inputs. |
| `turbo [on\|off]` | Runs emulation as fast as possible. |
| `tape [status\|replay\|stop\|rewind\|eject\|load]` | Controls the ZX Spectrum tape. |
| `stop` | Ends the current fusion or creation. |
| `controls` | Shows how to play on the current machine. |
| `machines` (`sistemas`) | Lists systems and their actual implementation status. |
| `machine <id>` | Powers on an implemented machine with its local firmware; downloads nothing. |

### Projects

| Command | Function |
| --- | --- |
| `create` | Opens the visual creation workshop. |
| `fuse` | Opens the visual fusion workshop. |
| `projects` (`trabajos`) | Lists fusions, creations, saved work and research. |
| `project <proyecto…>` | Shows a creation: idea, originals, versions and checks still needed. |
| `validate <proyecto…>` (`probar`) | Tests a creation with automatic players in the background. |
| `resume-work <proyecto…>` | Opens the visual workshop at saved work or research. |
| `prepare <game…>` (`investigar`) | Researches an original for fusions in the background, without AI. |

### Analysis

| Command | Function |
| --- | --- |
| `registers` (`regs`, `r`) | Reads CPU registers. |
| `memory <address> [count]` (`mem`, `m`) | Shows memory without hardware read effects. |
| `write <address> <value>` (`poke`) | Writes a byte to memory to test a hypothesis. |
| `code [address] [count]` (`dis`, `u`) | Disassembles instructions without changing the machine. |
| `step [frame\|over]` (`s`) | Advances an instruction or a frame, or steps over a routine. |
| `run [until]` (`g`) | Runs the machine with the console open until a breakpoint or pause. |
| `bp [list\|add\|remove\|clear] [address]` (`breakpoint`) | Manages debugger breakpoints. |
| `scan-memory <action> [value]` (`buscar-memoria`) | Finds a variable by comparing memory at different points in the game. |
| `variables` | Lists known variables for this game with values and evidence. |

### Tasks

| Command | Function |
| --- | --- |
| `tasks [clear]` (`jobs`, `ps`) | Lists this session's long tasks and what each is doing. |
| `task <tarea>` | Shows task details: stage, results, errors and files. |
| `follow <tarea>` (`tail`) | Shows each task update in the console until completion. |
| `cancel <tarea>` (`kill`, `detener-tarea`) | Requests a task to stop. Output already produced is preserved. |
| `resume-task <tarea>` | Retries a canceled or failed task that supports it. |

### System

| Command | Function |
| --- | --- |
| `requirements` | Shows requirements and what was checked during this startup. |
| `news` | Shows new features and changes in recent versions. |
| `status` | Shows the machine, session, library, interface and tasks. |
| `options [text…]` (`ajustes`, `preferencias`) | Lists program settings with current values. |
| `option <id> [value…]` (`ajuste`) | Read, change or reset a setting and show its scope. |
| `interface <retro\|explorer\|real\|lounge\|preview>` (`estilo`, `tema`) | Changes the style or opens the preview selector. |
| `actions [text…]` | Lists program features with their current availability. |
| `do <id>` | Runs a catalog action; visual tools open their panel. |
| `update [status\|open]` (`version`, `actualizaciones`) | Checks the version or opens the program updater. |
| `diagnostics [view\|copy]` | Shows a technical summary without personal data; copying is an explicit action. |
| `quit` (`exit`) | Closes the program with the menu's protection for pending work. |

### Console

| Command | Function |
| --- | --- |
| `copy [last\|all]` | Copies the last command's result or all output, without decoration. |
| `shortcuts` (`teclas`) | Lists console and game keys. |
| `help [topic]` (`?`, `man`) | Explains a command or group, or lists all commands. |
| `how <topic…>` (`apropos`) | Finds how to do something without knowing the command name. |
| `clear` (`cls`) | Clears stored console output. |
| `history [text…]` | Lists, searches or clears previous commands. |

### Interface

| Command | Function |
| --- | --- |
| `palette [view\|list\|use\|color\|edit\|reset] [value] [rgb]` | Select a palette or customize eight RGB colors for the current interface. |
