param(
    [string]$Target = "127.0.0.1",
    [string]$AppPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "FrontEnd\bin\Debug\MiSTerCast.exe")
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fakePath = Join-Path $PSScriptRoot "fake-groovy-mister.ps1"
$fake = $null
$app = $null

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

function Get-VisibleText($root) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($text in $texts) {
        if ($text.Current.Name) {
            $values.Add($text.Current.Name)
        }
    }
    return $values
}

try {
    $fake = Start-Process powershell -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $fakePath,
        "-Address", "127.0.0.1",
        "-Once"
    ) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 500

    $app = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path $AppPath) -PassThru
    Start-Sleep -Seconds 2

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $app.Id)
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $windowCondition)
    if ($null -eq $window) {
        throw "MiSTerCast window not found."
    }

    $targetBox = Find-ChildByAutomationId $window "TargetIpAddresTextBox"
    if ($null -eq $targetBox) {
        throw "Target textbox not found."
    }
    $targetBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Target)

    $testButton = Find-ChildByName $window "Test Target"
    if ($null -eq $testButton) {
        throw "Test Target button not found."
    }

    $configureButton = Find-ChildByName $window "Configure Target"
    if ($null -eq $configureButton) {
        throw "Configure Target button not found."
    }

    $testButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 250
        $visible = Get-VisibleText $window
        if ($visible -match "Groovy_MiSTer detected") {
            Write-Host "Preflight UI test passed."
            exit 0
        }
    } while ((Get-Date) -lt $deadline)

    throw "Groovy_MiSTer detected status was not shown. Visible text: $($visible -join ' | ')"
}
finally {
    if ($app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -Force
    }
    if ($fake -and -not $fake.HasExited) {
        Stop-Process -Id $fake.Id -Force
    }
}
