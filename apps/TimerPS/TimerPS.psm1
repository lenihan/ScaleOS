# TimerPS: a countdown timer for ScaleOS.
# Start it, press Ctrl+Z to send it to the background, and a notification lands in the timeline when it ends.

$script:Remaining = [timespan]::Zero   # time left while paused
$script:EndsAt = $null                 # [datetime] while running
$script:Label = ''

function Start-App {
    $script:Remaining = [timespan]::FromMinutes(5)
    $script:EndsAt = $null
    $script:Label = ''
}

function Restore-AppState {
    param($State)
    # A timer that was running when it closed comes back paused with the time it had left.
    $script:Remaining = [timespan]::FromSeconds([double]$State.remainingSeconds)
    $script:EndsAt = $null
    $script:Label = [string]$State.label
}

function Save-AppState {
    @{ remainingSeconds = (Get-TimeLeft).TotalSeconds; label = $script:Label }
}

function Get-TimeLeft {
    if ($script:EndsAt) {
        $left = $script:EndsAt - [datetime]::Now
        if ($left -lt [timespan]::Zero) { return [timespan]::Zero }
        return $left
    }
    $script:Remaining
}

function Format-TimeLeft([timespan]$t) {
    $seconds = [Math]::Ceiling($t.TotalSeconds)
    '{0:00}:{1:00}' -f [Math]::Floor($seconds / 60), ($seconds % 60)
}

function Show-App {
    $left = Get-TimeLeft
    $status = if ($script:EndsAt) { "Running · ends at $($script:EndsAt.ToString('HH:mm:ss'))" }
              elseif ($left -gt [timespan]::Zero) { 'Paused' }
              else { 'Done' }
    $title = if ($script:Label) { "Timer · $script:Label" } else { 'Timer' }

    New-Panel $title {
        New-Row {
            New-Text (Format-TimeLeft $left) -Bold -Fg '#61afef'
            New-Text "  $status" -Dim
        }
        New-Input -Id 'set' -Placeholder 'Minutes and optional label, e.g. "25 focus" or "0.5", then Enter'
        New-Row {
            New-Button 'Start' -Id 'start'
            New-Button 'Pause' -Id 'pause'
            New-Button '+1 min' -Id 'plus'
            New-Button 'Reset' -Id 'reset'
        }
        New-Text 'Ctrl+Z sends it to the background; you will get a notification when it ends.' -Dim
    }
}

function Start-Countdown {
    if ((Get-TimeLeft) -le [timespan]::Zero) { return }
    $script:EndsAt = [datetime]::Now + (Get-TimeLeft)
    Set-SosTimer 250
}

function Stop-Countdown {
    $script:Remaining = Get-TimeLeft
    $script:EndsAt = $null
    Set-SosTimer 0
}

function Invoke-AppInput {
    param($InputEvent)
    switch ($InputEvent.type) {
        'submit' {
            $parts = "$($InputEvent.value)".Trim() -split '\s+', 2
            $minutes = 0.0
            if ([double]::TryParse($parts[0], [ref]$minutes) -and $minutes -gt 0) {
                Stop-Countdown
                $script:Remaining = [timespan]::FromMinutes($minutes)
                $script:Label = if ($parts.Count -gt 1) { $parts[1] } else { '' }
                Start-Countdown
            }
        }
        'click' {
            switch ($InputEvent.target) {
                'start' { Start-Countdown }
                'pause' { Stop-Countdown }
                'plus' {
                    if ($script:EndsAt) { $script:EndsAt = $script:EndsAt.AddMinutes(1) }
                    else { $script:Remaining = $script:Remaining.Add([timespan]::FromMinutes(1)) }
                }
                'reset' { Stop-Countdown; $script:Remaining = [timespan]::FromMinutes(5); $script:Label = '' }
            }
        }
        'tick' {
            if ($script:EndsAt -and [datetime]::Now -ge $script:EndsAt) {
                Stop-Countdown
                $what = if ($script:Label) { "'$script:Label'" } else { 'Countdown' }
                Send-SosNotification "$what finished at $([datetime]::Now.ToString('HH:mm'))."
            }
        }
    }
}

Export-ModuleMember -Function Start-App, Restore-AppState, Save-AppState, Show-App, Invoke-AppInput
