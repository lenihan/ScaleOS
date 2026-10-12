# NotesPS: a persistent notes app for ScaleOS.
# Close it and it stays in the timeline; resume it later and your notes come back.

$script:Notes = [System.Collections.Generic.List[string]]::new()
$script:Selected = -1

function Start-App {
    $script:Notes.Clear()
    $script:Selected = -1
}

function Restore-AppState {
    param($State)
    $script:Notes.Clear()
    foreach ($note in @($State.notes)) {
        if ($null -ne $note) { $script:Notes.Add([string]$note) }
    }
    $script:Selected = [Math]::Min([int]$State.selected, $script:Notes.Count - 1)
}

function Save-AppState {
    @{ notes = @($script:Notes); selected = $script:Selected }
}

function Show-App {
    New-Panel 'Notes' {
        New-List -Id 'notes' -Items $script:Notes -Selected $script:Selected -Placeholder 'No notes yet.'
        New-Divider
        New-Input -Id 'new' -Placeholder 'Type a note and press Enter'
        New-Row {
            New-Button 'Delete' -Id 'delete'
            New-Button 'Clear all' -Id 'clear'
            New-Button 'Close' -Id 'close'
        }
        New-Text "$($script:Notes.Count) note(s) · ↑↓ select · Del delete · Esc close" -Dim
    }
}

function Invoke-AppInput {
    param($InputEvent)
    switch ($InputEvent.type) {
        'submit' {
            $text = "$($InputEvent.value)".Trim()
            if ($text) {
                $script:Notes.Add($text)
                $script:Selected = $script:Notes.Count - 1
            }
        }
        'select' { $script:Selected = [int]$InputEvent.index }
        'click' {
            switch ($InputEvent.target) {
                'delete' { Remove-SelectedNote }
                'clear' { $script:Notes.Clear(); $script:Selected = -1 }
                'close' { 'exit' }
            }
        }
        'key' {
            switch ($InputEvent.key) {
                'Up' { if ($script:Selected -gt 0) { $script:Selected-- } }
                'Down' { if ($script:Selected -lt $script:Notes.Count - 1) { $script:Selected++ } }
                'Delete' { Remove-SelectedNote }
            }
        }
    }
}

function Remove-SelectedNote {
    if ($script:Selected -ge 0 -and $script:Selected -lt $script:Notes.Count) {
        $script:Notes.RemoveAt($script:Selected)
        $script:Selected = [Math]::Min($script:Selected, $script:Notes.Count - 1)
    }
}

Export-ModuleMember -Function Start-App, Restore-AppState, Save-AppState, Show-App, Invoke-AppInput
