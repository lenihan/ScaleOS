# InvadersPS: a small Space Invaders for ScaleOS.
# Validates graphics (canvas sprites), animation (timer ticks), input, audio and persistence:
# close or quit mid-game and Resume puts you back where you were.

$script:W = 64
$script:H = 40
$script:TickMs = 70
$script:Cols = 6
$script:Rows = 3
$script:Rng = [System.Random]::new()

$script:Sprites = @{
    a0   = @{ color = '#f778ba'; rows = @('..###..', '.#####.', '##.#.##', '.#...#.') }
    a1   = @{ color = '#f778ba'; rows = @('..###..', '.#####.', '##.#.##', '#.....#') }
    b0   = @{ color = '#79c0ff'; rows = @('#..#..#', '#######', '##.#.##', '.#...#.') }
    b1   = @{ color = '#79c0ff'; rows = @('...#...', '#######', '##.#.##', '#.....#') }
    ship = @{ color = '#7ee787'; rows = @('...#...', '.#####.', '#######') }
    boom = @{ color = '#ffa657'; rows = @('#..#..#', '.#.#.#.', '#..#..#') }
}

$script:High = 0

function New-Game {
    param([int]$Level = 1, [int]$Score = 0, [int]$Lives = 3)
    $script:Level = $Level
    $script:Score = $Score
    $script:Lives = $Lives
    $script:PlayerX = 28
    $script:Alive = [bool[]]::new($script:Cols * $script:Rows)
    for ($i = 0; $i -lt $script:Alive.Length; $i++) { $script:Alive[$i] = $true }
    $script:OriginX = 4
    $script:OriginY = 3 + [Math]::Min(($Level - 1) * 2, 10)
    $script:Dir = 1
    $script:Tick = 0
    $script:Frame = 0
    $script:Bullet = $null
    $script:Bombs = [System.Collections.Generic.List[hashtable]]::new()
    $script:Booms = [System.Collections.Generic.List[hashtable]]::new()
    Set-GameState 'playing'
}

function Set-GameState([string]$State) {
    $script:State = $State
    # Only tick while playing, so a paused or finished game costs nothing in the background.
    Set-SosTimer $(if ($State -eq 'playing') { $script:TickMs } else { 0 })
}

function Get-InvaderPosition([int]$Index) {
    $script:OriginX + ($Index % $script:Cols) * 9
    $script:OriginY + [Math]::Floor($Index / $script:Cols) * 6
}

# ---------------------------------------------------------------------------- app contract

function Start-App { New-Game }

function Save-AppState {
    @{
        level = $script:Level; score = $script:Score; lives = $script:Lives; high = $script:High
        playerX = $script:PlayerX; originX = $script:OriginX; originY = $script:OriginY; dir = $script:Dir
        alive = @($script:Alive); state = $script:State
    }
}

function Restore-AppState {
    param($State)
    New-Game -Level $State.level -Score $State.score -Lives $State.lives
    $script:High = [int]$State.high
    $script:PlayerX = [int]$State.playerX
    $script:OriginX = [int]$State.originX
    $script:OriginY = [int]$State.originY
    $script:Dir = [int]$State.dir
    $saved = @($State.alive)
    for ($i = 0; $i -lt $script:Alive.Length -and $i -lt $saved.Count; $i++) { $script:Alive[$i] = [bool]$saved[$i] }
    # Come back paused so the player isn't ambushed.
    Set-GameState $(if ($State.state -eq 'over') { 'over' } else { 'paused' })
}

function Show-App {
    $draw = [System.Collections.Generic.List[hashtable]]::new()
    for ($i = 0; $i -lt $script:Alive.Length; $i++) {
        if (-not $script:Alive[$i]) { continue }
        $x, $y = Get-InvaderPosition $i
        $kind = if ($i -lt $script:Cols) { 'b' } else { 'a' }
        $draw.Add(@{ s = "$kind$($script:Frame)"; x = $x; y = $y })
    }
    foreach ($b in $script:Booms) { $draw.Add(@{ s = 'boom'; x = $b.x; y = $b.y }) }
    if ($script:Lives -gt 0) { $draw.Add(@{ s = 'ship'; x = $script:PlayerX; y = $script:H - 3 }) }

    $rects = [System.Collections.Generic.List[hashtable]]::new()
    if ($script:Bullet) { $rects.Add(@{ x = $script:Bullet.x; y = $script:Bullet.y; w = 1; h = 2; color = '#ffffff' }) }
    foreach ($bomb in $script:Bombs) { $rects.Add(@{ x = $bomb.x; y = $bomb.y; w = 1; h = 2; color = '#ff7b72' }) }

    $status = switch ($script:State) {
        'playing' { '←→ move · Space fire · P pause · Ctrl+Z background · Esc close' }
        'paused' { 'Paused · press P to continue' }
        'over' { 'GAME OVER · press Enter to play again' }
    }

    New-Panel 'Space Invaders' {
        New-Row {
            New-Text "Score $($script:Score)" -Bold
            New-Text "Hi $($script:High)"
            New-Text "Level $($script:Level)"
            New-Text ('Lives ' + ('♥' * [Math]::Max(0, $script:Lives))) -Fg '#ff7b72'
        }
        New-Canvas -Width $script:W -Height $script:H -Bg '#0b0e14' -Sprites $script:Sprites -Draw $draw -Rects $rects
        New-Text $status -Dim
    }
}

function Invoke-AppInput {
    param($InputEvent)
    switch ($InputEvent.type) {
        'tick' { if ($script:State -eq 'playing') { Step-Game } }
        'suspend' { if ($script:State -eq 'playing') { Set-GameState 'paused' } }
        'key' {
            switch ($InputEvent.key) {
                'Left' { if ($script:State -eq 'playing') { $script:PlayerX = [Math]::Max(0, $script:PlayerX - 2) } }
                'Right' { if ($script:State -eq 'playing') { $script:PlayerX = [Math]::Min($script:W - 7, $script:PlayerX + 2) } }
                'Space' { if ($script:State -eq 'playing') { Invoke-Fire } }
                { $_ -in 'p', 'P' } {
                    if ($script:State -eq 'playing') { Set-GameState 'paused' }
                    elseif ($script:State -eq 'paused') { Set-GameState 'playing' }
                }
                'Enter' { if ($script:State -eq 'over') { New-Game } }
            }
        }
    }
}

# ---------------------------------------------------------------------------- game logic

function Invoke-Fire {
    if ($script:Bullet) { return }
    $script:Bullet = @{ x = $script:PlayerX + 3; y = $script:H - 5 }
    Play-Sound -Tone 1400, 25, 900, 25
}

function Step-Game {
    $script:Tick++

    # Player bullet: move, then check for hits.
    if ($script:Bullet) {
        $script:Bullet.y -= 2
        if ($script:Bullet.y -lt 0) { $script:Bullet = $null }
        else {
            for ($i = 0; $i -lt $script:Alive.Length; $i++) {
                if (-not $script:Alive[$i]) { continue }
                $x, $y = Get-InvaderPosition $i
                if ($script:Bullet.x -ge $x -and $script:Bullet.x -lt $x + 7 -and $script:Bullet.y -lt $y + 4 -and $script:Bullet.y + 2 -gt $y) {
                    $script:Alive[$i] = $false
                    $script:Bullet = $null
                    $script:Score += $(if ($i -lt $script:Cols) { 30 } else { 10 })
                    $script:Booms.Add(@{ x = $x; y = $y; t = 4 })
                    Play-Sound -Tone 160, 40, 90, 60
                    break
                }
            }
        }
    }

    $alive = @($script:Alive | Where-Object { $_ }).Count
    if ($alive -eq 0) {
        Play-Sound -Tone 523, 90, 659, 90, 784, 90, 1047, 200
        New-Game -Level ($script:Level + 1) -Score $script:Score -Lives $script:Lives
        return
    }

    # The formation marches faster as it shrinks.
    $every = [Math]::Max(1, [Math]::Ceiling($alive / 2))
    if ($script:Tick % $every -eq 0) {
        $script:Frame = 1 - $script:Frame
        $minCol = $script:Cols; $maxCol = -1; $maxRow = -1
        for ($i = 0; $i -lt $script:Alive.Length; $i++) {
            if (-not $script:Alive[$i]) { continue }
            $col = $i % $script:Cols
            $row = [Math]::Floor($i / $script:Cols)
            if ($col -lt $minCol) { $minCol = $col }
            if ($col -gt $maxCol) { $maxCol = $col }
            if ($row -gt $maxRow) { $maxRow = $row }
        }
        $left = $script:OriginX + $minCol * 9 + $script:Dir * 2
        $right = $script:OriginX + $maxCol * 9 + 7 + $script:Dir * 2
        if ($left -lt 0 -or $right -gt $script:W) {
            $script:OriginY += 2
            $script:Dir = -$script:Dir
        }
        else {
            $script:OriginX += $script:Dir * 2
        }
        if ($script:OriginY + $maxRow * 6 + 4 -ge $script:H - 3) { Stop-Game; return }
    }

    # Invaders drop bombs.
    if ($script:Bombs.Count -lt 3 -and $script:Rng.NextDouble() -lt 0.03 + 0.01 * $script:Level) {
        $candidates = @(for ($i = 0; $i -lt $script:Alive.Length; $i++) { if ($script:Alive[$i]) { $i } })
        $x, $y = Get-InvaderPosition $candidates[$script:Rng.Next($candidates.Count)]
        $script:Bombs.Add(@{ x = $x + 3; y = $y + 4 })
    }
    for ($b = $script:Bombs.Count - 1; $b -ge 0; $b--) {
        $bomb = $script:Bombs[$b]
        $bomb.y += 1
        if ($bomb.y -ge $script:H) { $script:Bombs.RemoveAt($b); continue }
        if ($bomb.y + 2 -gt $script:H - 3 -and $bomb.x -ge $script:PlayerX -and $bomb.x -lt $script:PlayerX + 7) {
            $script:Bombs.RemoveAt($b)
            $script:Lives--
            $script:Booms.Add(@{ x = $script:PlayerX; y = $script:H - 3; t = 6 })
            Play-Sound -Tone 300, 60, 200, 60, 100, 120
            if ($script:Lives -le 0) { Stop-Game; return }
        }
    }

    for ($b = $script:Booms.Count - 1; $b -ge 0; $b--) {
        $script:Booms[$b].t--
        if ($script:Booms[$b].t -le 0) { $script:Booms.RemoveAt($b) }
    }
}

function Stop-Game {
    $script:High = [Math]::Max($script:High, $script:Score)
    $script:Bullet = $null
    $script:Bombs.Clear()
    Set-GameState 'over'
    Play-Sound -Tone 392, 160, 330, 160, 262, 320
}

Export-ModuleMember -Function Start-App, Save-AppState, Restore-AppState, Show-App, Invoke-AppInput
