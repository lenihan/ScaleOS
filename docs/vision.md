# ScaleOS

> History is your desktop.

## What is ScaleOS?

ScaleOS is an experimental terminal-based operating environment where graphical applications run directly inside a terminal and remain accessible through persistent scrollback history.

Unlike traditional operating systems that organize work using windows, desktops, and taskbars, ScaleOS organizes work as a continuous timeline.

Applications do not disappear when closed. They leave behind visual artifacts in history that can be revisited, inspected, and resumed.

The terminal is not merely a shell. The terminal is the operating environment.

---

# Core Vision

Traditional operating systems organize work spatially.

```text
Desktop
├─ Window A
├─ Window B
├─ Window C
└─ Window D
```

ScaleOS organizes work temporally.

```text
09:00 Shell

09:05 Notes Application

09:12 Documentation Browser

09:15 Build Completion

09:20 File Manager

09:30 Space Invaders
```

Users navigate history instead of managing windows.

The timeline itself becomes the desktop.

---

# Design Goals

## Terminal First

Everything runs inside a terminal environment.

Support for:

- ANSI
- True Color
- Mouse Input
- Unicode
- Graphics
- Audio

The terminal should be powerful enough to host modern applications without requiring a traditional window manager.

---

## Graphical Applications Inside The Terminal

Applications are not limited to text.

Applications may contain:

- Images
- Panels
- Buttons
- Menus
- Lists
- Dashboards
- Games
- Visualizations

Potential rendering technologies:

- Sixel
- Custom renderer
- Canvas rendering
- Browser-based rendering

Example:

```text
┌──────────────────────────────┐
│ Notes                        │
├──────────────────────────────┤
│ - Build ScaleOS prototype    │
│ - Research PTYs              │
│ - Design app lifecycle       │
└──────────────────────────────┘
```

---

## Persistent History

History is the primary user interface.

Everything remains visible after use.

Example:

```text
Prompt

[ Notes App ]

Prompt

[ Build Output ]

Prompt

[ Browser ]

Prompt
```

Users can scroll upward and review prior application states.

---

## Resumable Applications

Applications can be resumed directly from history.

Traditional systems:

```text
Open App
Use App
Close App
Gone
```

ScaleOS:

```text
Open App
Use App
Close App
Artifact Remains
Resume Later
```

Example:

```powershell
Resume-App 42
```

or

```text
Scroll up
Click artifact
Resume application
```

---

# Why ScaleOS Exists

Modern operating systems are optimized around managing windows.

ScaleOS explores a different idea:

> What if application history became the primary workspace?

The project combines ideas from:

- Terminal computing
- PowerShell
- tmux
- screen
- Notebook interfaces
- Smalltalk persistence
- Time-based workflows

into a single operating environment.

---

# PowerShell's Role

PowerShell is the application platform.

PowerShell is not the renderer.

PowerShell is not the window manager.

PowerShell is not the operating system kernel.

PowerShell provides:

- Application scripting
- Modules
- Automation
- User customization
- Object-based programming

Applications should ideally be distributed as PowerShell modules.

Example:

```powershell
Install-Module NotesPS

Install-Module BrowserPS

Install-Module SpaceInvadersPS
```

---

# Runtime Responsibilities

The ScaleOS runtime provides:

- Rendering
- Input
- Audio
- Persistence
- Application lifecycle
- Timeline storage
- Session management

Conceptually:

```text
ScaleOS Runtime
│
├─ Timeline Engine
├─ Renderer
├─ Audio Engine
├─ Input Engine
├─ Session Manager
└─ PowerShell Host
```

---

# Application Lifecycle

Applications support:

```text
Launch
Run
Suspend
Resume
Persist
Terminate
```

Applications may:

- remain running in background
- save state and exit
- restore from snapshots

---

# Multiple Sessions

ScaleOS supports multiple active application contexts.

Examples:

```text
Session 1 - Shell
Session 2 - File Manager
Session 3 - Browser
Session 4 - Game
```

Switching should not require traditional windows.

Applications appear naturally in history.

---

# Background Tasks

Long-running tasks should be first-class.

Examples:

- Downloads
- Builds
- Music playback
- Chat clients

Notifications become timeline entries.

Example:

```text
Build Complete

Project compiled successfully.
```

---

# Graphics

Graphics are first-class.

The initial implementation should support:

- Images
- Panels
- Widgets
- Simple animation

Future goals:

- Canvas support
- Sprites
- Games
- Rich graphical applications

A Space Invaders clone should be possible.

---

# Mouse Support

Required.

Support:

- Click
- Double Click
- Hover
- Drag
- Scroll Wheel

Applications should receive abstract input events.

---

# Audio

Required.

Examples:

```powershell
Play-Sound victory.wav
```

Use cases:

- Games
- Notifications
- Music
- Media playback

---

# Browser Support

Web browsing is important, but should not define the platform.

Preferred progression:

## Phase 1

Reader-oriented browser.

Documentation.

Articles.

Knowledge bases.

## Phase 2

Interactive web applications.

## Phase 3

Full browser compatibility if required.

---

# Platforms

## Windows

Primary development platform.

## Browser

Easy sharing and experimentation.

## Raspberry Pi

Primary deployment target.

Goal:

```text
Power On
↓
Boot Linux
↓
Launch ScaleOS
↓
Ready
```

No traditional desktop environment required.

---

# Example Applications

## NotesPS

Persistent notes application.

## FilesPS

File manager.

## BrowserPS

Documentation and web content.

## MusicPS

Background audio player.

## SpaceInvadersPS

Demonstration game.

Validates:

- Graphics
- Input
- Audio
- Application persistence

---

# MVP

The first prototype should demonstrate:

- Embedded PowerShell
- Timeline rendering
- Mouse support
- Basic graphics
- Application launch
- Application suspend
- Application resume
- Persistent history

Success is achieved when a user can:

1. Launch an application.
2. Interact with it.
3. Exit it.
4. Scroll back and still see it.
5. Resume it from history.

If that works, the fundamental ScaleOS concept has been proven.

---

# One-Sentence Description

ScaleOS is a terminal-based operating environment where graphical PowerShell applications become persistent, resumable artifacts in a scrollable history.