# ScaleOS SDK
#
# Widget builders that apps use in Show-App, and shell commands for working with the timeline.
# Widgets are plain ordered hashtables; the ScaleOS core lays them out and draws them.
#
# App contract (functions an app module exports):
#   Start-App                      initialize fresh state                  (optional)
#   Restore-AppState -State <ht>   initialize from saved state on resume   (optional)
#   Show-App                       return the widget tree                  (required)
#   Invoke-AppInput <event>        handle an event; output 'exit' to close (optional)
#   Save-AppState                  return state to persist                 (optional)
#
# Events are hashtables: @{ type = 'click'|'select'|'submit'|'key'|'tick'|'suspend'|'resume'; target; index; value; key }
# 'suspend' arrives when the app is sent to the background (Ctrl+Z), 'resume' when it comes back.
#
# Background work: call Set-SosTimer to receive 'tick' events, which keep arriving while the app is
# suspended (Ctrl+Z). Send-SosNotification adds an entry to the timeline; clicking it opens the app.

function ConvertTo-WidgetList($Content) {
    if ($Content -is [scriptblock]) { return @(& $Content) }
    return @($Content)
}

function New-Panel {
    param(
        [Parameter(Position = 0)][string]$Title,
        [Parameter(Position = 1)]$Content,
        [string]$Fg
    )
    [ordered]@{ type = 'panel'; title = $Title; fg = $Fg; children = (ConvertTo-WidgetList $Content) }
}

function New-Column {
    param([Parameter(Position = 0)]$Content)
    [ordered]@{ type = 'column'; children = (ConvertTo-WidgetList $Content) }
}

function New-Row {
    param([Parameter(Position = 0)]$Content)
    [ordered]@{ type = 'row'; children = (ConvertTo-WidgetList $Content) }
}

function New-Text {
    param(
        [Parameter(Position = 0)][string]$Text,
        [string]$Fg,
        [string]$Bg,
        [switch]$Bold,
        [switch]$Dim
    )
    [ordered]@{ type = 'text'; text = $Text; fg = $Fg; bg = $Bg; bold = [bool]$Bold; dim = [bool]$Dim }
}

function New-Button {
    param(
        [Parameter(Position = 0, Mandatory)][string]$Text,
        [string]$Id
    )
    [ordered]@{ type = 'button'; text = $Text; id = $(if ($Id) { $Id } else { $Text }) }
}

function New-List {
    param(
        [string]$Id = 'list',
        [object[]]$Items = @(),
        [int]$Selected = -1,
        [string]$Placeholder
    )
    [ordered]@{
        type        = 'list'
        id          = $Id
        items       = [string[]]@($Items | ForEach-Object { "$_" })
        selected    = $Selected
        placeholder = $Placeholder
    }
}

function New-Input {
    param(
        [string]$Id = 'input',
        [string]$Value,
        [string]$Placeholder
    )
    [ordered]@{ type = 'input'; id = $Id; value = $Value; placeholder = $Placeholder }
}

function New-Divider {
    [ordered]@{ type = 'divider' }
}

function New-Canvas {
    <#
    A Width x Height pixel area (two pixels per terminal cell, stacked vertically).
    -Sprites maps names to @{ rows = @('.#.', '###'); color = '#ff0000'; palette = @{ r = '#ff0000' } }
    ('.' and ' ' are transparent). -Draw places sprites: @{ s = 'name'; x = 3; y = 4; color = optional }.
    -Rects fills rectangles: @{ x; y; w; h; color }.
    #>
    param(
        [int]$Width = 64,
        [int]$Height = 32,
        [string]$Bg,
        [hashtable]$Sprites = @{},
        [object[]]$Draw = @(),
        [object[]]$Rects = @(),
        [string]$Id = 'canvas'
    )
    [ordered]@{
        type    = 'canvas'
        id      = $Id
        width   = $Width
        height  = $Height
        bg      = $Bg
        sprites = $Sprites
        draw    = @($Draw)
        rects   = @($Rects)
    }
}

function Play-Sound {
    <#
    Plays a sound through the ScaleOS audio engine (one at a time; a new sound cuts off the previous one).
      Play-Sound victory.wav
      Play-Sound -Tone 440,80, 660,80, 880,160     # frequency Hz, milliseconds pairs; 0 Hz is a rest
      Play-Sound -Tone 523,200 -Sine               # softer sine wave instead of square
    #>
    [CmdletBinding(DefaultParameterSetName = 'File')]
    param(
        [Parameter(ParameterSetName = 'File', Mandatory, Position = 0)][string]$Path,
        [Parameter(ParameterSetName = 'Tone', Mandatory)][int[]]$Tone,
        [Parameter(ParameterSetName = 'Tone')][switch]$Sine
    )
    if (-not $global:Sos) { return }
    if ($PSCmdlet.ParameterSetName -eq 'Tone') {
        if ($Tone.Count % 2 -ne 0) { throw '-Tone takes frequency,milliseconds pairs.' }
        $notes = for ($i = 0; $i -lt $Tone.Count; $i += 2) { "$($Tone[$i])/$($Tone[$i + 1])" }
        $wave = if ($Sine) { 'sine' } else { 'tone' }
        $global:Sos.PlaySound("${wave}:$($notes -join ',')")
    }
    else {
        $global:Sos.PlaySound((Resolve-Path -LiteralPath $Path).ProviderPath)
    }
}

# ---------------------------------------------------------------------------- shell commands

function Assert-SosShell {
    if (-not $global:Sos -or -not $global:Sos.IsShell) { throw 'This command only works at the ScaleOS prompt.' }
}

function Send-SosNotification {
    <# Adds a notification to the timeline. Sent from an app, clicking it opens that app. #>
    param([Parameter(Mandatory, Position = 0)][string]$Text)
    if (-not $global:Sos) { throw 'Not running inside Sos.' }
    $global:Sos.Notify($Text)
}

function Set-SosTimer {
    <# Delivers a 'tick' event to Invoke-AppInput every N milliseconds, even in the background. 0 stops it. #>
    param([Parameter(Mandatory, Position = 0)][int]$Milliseconds)
    if (-not $global:Sos -or $global:Sos.IsShell) { throw 'Set-SosTimer only works inside an app.' }
    $global:Sos.SetTimer($Milliseconds)
}

function Get-SosApp {
    <# Lists installed ScaleOS apps (folders under $env:SOS_APPS containing <Name>/<Name>.psm1). #>
    if (-not $env:SOS_APPS -or -not (Test-Path -LiteralPath $env:SOS_APPS)) { return }
    Get-ChildItem -LiteralPath $env:SOS_APPS -Directory | ForEach-Object {
        $module = Join-Path $_.FullName "$($_.Name).psm1"
        if (Test-Path -LiteralPath $module) {
            [pscustomobject]@{ Name = ($_.Name -replace 'PS$', ''); Module = $_.Name; Path = $module }
        }
    }
}

function Start-SosApp {
    <# Launches an app as a new live entry at the bottom of the timeline. #>
    param([Parameter(Mandatory, Position = 0)][string]$Name)
    Assert-SosShell
    $app = Get-SosApp | Where-Object { $_.Name -eq $Name -or $_.Module -eq $Name } | Select-Object -First 1
    if (-not $app) { throw "No ScaleOS app named '$Name'. Run Get-SosApp to see what's installed." }
    $global:Sos.Launch($app.Name, $app.Path)
}

function Resume-App {
    <# Resumes a closed app artifact from history as a new live entry. #>
    param([Parameter(Mandatory, Position = 0)][long]$Id)
    Assert-SosShell
    $global:Sos.Resume($Id)
}

function Get-Timeline {
    <# Returns timeline entries as objects, oldest first. #>
    param([int]$Last = 20, [string]$Kind)
    $index = Join-Path $env:SOS_HOME 'timeline/index.jsonl'
    if (-not (Test-Path -LiteralPath $index)) { return }

    # The index is append-only; the last line for each id is its current state.
    $latest = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $index) {
        if ($line) { $e = $line | ConvertFrom-Json; $latest["$($e.id)"] = $e }
    }
    $latest.Values |
        Where-Object { -not $Kind -or $_.kind -eq $Kind } |
        Select-Object -Last $Last |
        ForEach-Object {
            [pscustomobject]@{
                Id     = $_.id
                Time   = ([datetime]$_.time).ToString('MM-dd HH:mm')
                Kind   = $_.kind
                Status = $_.status
                Title  = $_.title
            }
        }
}

function Register-SosApps {
    <# Defines a shortcut function per app (e.g. 'notes' runs Start-SosApp Notes). #>
    foreach ($app in Get-SosApp) {
        Set-Item -Path "function:global:$($app.Name)" -Value ([scriptblock]::Create("Start-SosApp '$($app.Name)'"))
    }
}

Export-ModuleMember -Function New-Panel, New-Column, New-Row, New-Text, New-Button, New-List, New-Input, New-Divider, New-Canvas, Play-Sound,
    Get-SosApp, Start-SosApp, Resume-App, Get-Timeline, Register-SosApps, Send-SosNotification, Set-SosTimer
