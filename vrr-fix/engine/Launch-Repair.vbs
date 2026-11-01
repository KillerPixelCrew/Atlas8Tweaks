' Runs the sign-in repair without a visible PowerShell window.
CreateObject("WScript.Shell").Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & _
    CreateObject("WScript.Shell").ExpandEnvironmentStrings("%ProgramData%") & "\AcerAtlasVrr\AcerAtlasVrr.ps1"" -Action Repair", 0, False
