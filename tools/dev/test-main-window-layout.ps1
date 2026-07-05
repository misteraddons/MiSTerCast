param(
    [string]$AppPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "FrontEnd\bin\Debug\MiSTerCast.exe")
)

$ErrorActionPreference = "Stop"

function Find-ChildByAutomationId($root, [string]$automationId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ChildByName($root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Assert-ControlVisible($window, $control, [string]$name) {
    if ($null -eq $control) {
        throw "$name not found."
    }

    $isOffscreen = [bool]$control.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsOffscreenProperty)
    if ($isOffscreen) {
        throw "$name is offscreen."
    }

    $windowRect = $window.Current.BoundingRectangle
    $controlRect = $control.Current.BoundingRectangle
    if ($controlRect.Width -le 0 -or $controlRect.Height -le 0) {
        throw "$name has no visible bounds."
    }

    if ($controlRect.Left -lt $windowRect.Left -or
        $controlRect.Top -lt $windowRect.Top -or
        $controlRect.Right -gt $windowRect.Right -or
        $controlRect.Bottom -gt $windowRect.Bottom) {
        throw "$name is clipped outside the main window."
    }
}

function Assert-ControlAbove($control, $lowerControl, [string]$name, [string]$lowerName) {
    $controlRect = $control.Current.BoundingRectangle
    $lowerRect = $lowerControl.Current.BoundingRectangle
    if ($controlRect.Bottom -gt $lowerRect.Top) {
        throw "$name overlaps $lowerName."
    }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Get-Process MiSTerCast -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$process = Start-Process -FilePath $AppPath -WorkingDirectory $repoRoot -PassThru
try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $window = $null
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 250
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $process.Id)
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    } while ($null -eq $window -and (Get-Date) -lt $deadline)

    if ($null -eq $window) {
        throw "MiSTerCast window not found."
    }

    $checks = @(
        @{ Name = "Auto Fill"; Element = Find-ChildByAutomationId $window "AutofillModelineButton" },
        @{ Name = "Enable Audio"; Element = Find-ChildByAutomationId $window "EnableAudioCheckBox" },
        @{ Name = "Enable Preview"; Element = Find-ChildByAutomationId $window "EnablePreviewCheckBox" },
        @{ Name = "Capture Width"; Element = Find-ChildByAutomationId $window "CaptureWidth" },
        @{ Name = "Capture X Offset"; Element = Find-ChildByAutomationId $window "CaptureXOffset" },
        @{ Name = "Native shortcut"; Element = Find-ChildByName $window "Native" },
        @{ Name = "Reset shortcut"; Element = Find-ChildByName $window "Reset" },
        @{ Name = "Logs"; Element = Find-ChildByName $window "Logs" }
    )

    foreach ($check in $checks) {
        Assert-ControlVisible $window $check.Element $check.Name
    }

    $logs = Find-ChildByName $window "Logs"
    foreach ($check in $checks | Where-Object { $_.Name -ne "Logs" }) {
        Assert-ControlAbove $check.Element $logs $check.Name "Logs"
    }
}
finally {
    Get-Process -Id $process.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
