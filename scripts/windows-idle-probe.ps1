param(
    [ValidateRange(1, 1440)]
    [int]$DurationMinutes = 15,
    [ValidateRange(1, 300)]
    [int]$IntervalSeconds = 5,
    [string]$CsvPath = ".\mmaway-idle.csv"
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $CsvPath) {
    throw "Output already exists: $CsvPath. Choose a new -CsvPath to avoid overwriting it."
}

if (-not ('MMAwayIdleProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class MMAwayIdleProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static double IdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
    }
}
'@
}

$clock = [Diagnostics.Stopwatch]::StartNew()
$first = $true
Write-Host "Sampling Windows session idle time to $CsvPath. Press Ctrl+C to stop."
while ($clock.Elapsed.TotalMinutes -lt $DurationMinutes) {
    $sample = [pscustomobject]@{
        Timestamp = (Get-Date).ToString('o')
        IdleSeconds = [MMAwayIdleProbe]::IdleSeconds()
    }
    if ($first) {
        $sample | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -NoClobber
        $first = $false
    } else {
        $sample | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -Append
    }
    Write-Host ("{0}  idle: {1:N1} seconds" -f $sample.Timestamp, $sample.IdleSeconds)
    Start-Sleep -Seconds $IntervalSeconds
}
