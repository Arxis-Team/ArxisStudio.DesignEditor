<#
.SYNOPSIS
Запускает DesignEditor.Demo и управляет им для визуальной проверки правок.

.DESCRIPTION
Обвязка над Win32 для сценария "собрал -> посмотрел -> потыкал -> закрыл".
Процесс ищется по имени, поэтому состояние между вызовами не нужно.

ВАЖНО: координаты click/wheel задаются ОТНОСИТЕЛЬНО ОКНА (как на скриншоте),
скрипт сам переводит их в экранные. Скриншот снимается по прямоугольнику окна,
поэтому пиксель (x, y) на картинке -> те же (x, y) в параметрах.

.EXAMPLE
demo.ps1 -Action start
demo.ps1 -Action shot  -Out C:\tmp\1.png
demo.ps1 -Action click -X 706 -Y 453
demo.ps1 -Action drag  -X 706 -Y 453 -ToX 760 -ToY 500
demo.ps1 -Action drag  -X 706 -Y 453 -ToX 760 -ToY 500 -Modifier Alt
demo.ps1 -Action key   -Key Right -Notches 5
demo.ps1 -Action wheel -X 300 -Y 200 -Notches 4
demo.ps1 -Action stop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('start', 'shot', 'click', 'rightclick', 'drag', 'wheel', 'key', 'stop', 'status')]
    [string]$Action,

    [string]$Out,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$ToX = 0,
    [int]$ToY = 0,
    [ValidateSet('None', 'Ctrl', 'Shift', 'Alt')]
    [string]$Modifier = 'None',

    [ValidateSet('Left', 'Right', 'Up', 'Down', 'Delete', 'Escape', 'A')]
    [string]$Key = 'Right',

    [int]$Notches = 3,
    [int]$TimeoutSec = 20
)

$ErrorActionPreference = 'Stop'
$ProcessName = 'DesignEditor.Demo'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DemoDriver
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    private static byte Vk(string modifier)
    {
        if (modifier == "Ctrl") return 0x11;
        if (modifier == "Shift") return 0x10;
        if (modifier == "Alt") return 0x12;
        return 0;
    }

    private static void ModifierDown(string modifier)
    {
        byte vk = Vk(modifier);
        if (vk != 0) { keybd_event(vk, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
    }

    private static void ModifierUp(string modifier)
    {
        byte vk = Vk(modifier);
        if (vk != 0) { keybd_event(vk, 0, 0x0002, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }

    public static void Focus(IntPtr h) { SetForegroundWindow(h); System.Threading.Thread.Sleep(600); }

    public static void Shot(IntPtr h, string path)
    {
        RECT r = Rect(h);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using (var bmp = new Bitmap(w, ht))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, ht));
            bmp.Save(path, ImageFormat.Png);
        }
    }

    // wx/wy - координаты относительно окна
    public static void Click(IntPtr h, int wx, int wy)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(900);
    }

    // Правый клик: контекстное меню редактора открывается по RMB.
    public static void RightClick(IntPtr h, int wx, int wy)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        mouse_event(0x0008, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0010, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(900);
    }

    // Перетаскивание. Промежуточные шаги обязательны: один прыжок из точки в
    // точку не переводит контейнер в состояние drag - редактору нужен сдвиг,
    // превышающий DragStartThreshold, а затем сами move-события.
    public static void Drag(IntPtr h, int wx, int wy, int tx, int ty, string modifier)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(250);
        ModifierDown(modifier);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(150);

        const int steps = 12;
        for (int i = 1; i <= steps; i++)
        {
            int x = wx + ((tx - wx) * i / steps);
            int y = wy + ((ty - wy) * i / steps);
            SetCursorPos(r.Left + x, r.Top + y);
            System.Threading.Thread.Sleep(40);
        }

        System.Threading.Thread.Sleep(150);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        ModifierUp(modifier);
        System.Threading.Thread.Sleep(900);
    }

    public static void Wheel(IntPtr h, int wx, int wy, int notches)
    {
        Focus(h);
        RECT r = Rect(h);
        SetCursorPos(r.Left + wx, r.Top + wy);
        System.Threading.Thread.Sleep(200);
        uint delta = (uint)(notches > 0 ? 120 : -120);
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            mouse_event(0x0800, 0, 0, delta, IntPtr.Zero);
            System.Threading.Thread.Sleep(120);
        }
        System.Threading.Thread.Sleep(800);
    }
}
'@

function Get-Demo {
    $p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $p) { return $null }
    $p.Refresh()
    return $p
}

function Require-Demo {
    $p = Get-Demo
    if ($null -eq $p) { throw "Демо не запущено. Сначала: demo.ps1 -Action start" }
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "У процесса ещё нет окна." }
    return $p
}

switch ($Action) {
    'start' {
        $existing = Get-Demo
        if ($null -ne $existing) { "already running pid=$($existing.Id)"; break }

        $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
        $exe = Join-Path $root 'samples\DesignEditor.Demo\bin\Debug\net10.0\DesignEditor.Demo.exe'
        if (-not (Test-Path $exe)) { throw "Не найден $exe - сначала соберите проект (dotnet build)." }

        $logDir = Join-Path $env:TEMP 'designeditor-demo'
        if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
        $p = Start-Process -FilePath $exe -PassThru `
             -RedirectStandardOutput (Join-Path $logDir 'out.log') `
             -RedirectStandardError  (Join-Path $logDir 'err.log')

        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        while ((Get-Date) -lt $deadline) {
            if ($p.HasExited) {
                $err = Get-Content (Join-Path $logDir 'err.log') -Raw -ErrorAction SilentlyContinue
                throw "Процесс завершился с кодом $($p.ExitCode). stderr:`n$err"
            }
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
                "started pid=$($p.Id) window='$($p.MainWindowTitle)' logs=$logDir"
                break
            }
            Start-Sleep -Milliseconds 400
        }
        if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "Окно не появилось за $TimeoutSec с." }
    }

    'status' {
        $p = Get-Demo
        if ($null -eq $p) { 'not running'; break }
        $r = [DemoDriver]::Rect($p.MainWindowHandle)
        "pid=$($p.Id) responding=$($p.Responding) title='$($p.MainWindowTitle)' rect=$($r.Left),$($r.Top) size=$($r.Right-$r.Left)x$($r.Bottom-$r.Top)"
    }

    'shot' {
        $p = Require-Demo
        if ([string]::IsNullOrWhiteSpace($Out)) { throw '-Out обязателен для shot.' }
        $dir = Split-Path -Parent $Out
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [DemoDriver]::Focus($p.MainWindowHandle)
        [DemoDriver]::Shot($p.MainWindowHandle, $Out)
        "saved $Out ($((Get-Item $Out).Length) bytes)"
    }

    'click' {
        $p = Require-Demo
        [DemoDriver]::Click($p.MainWindowHandle, $X, $Y)
        "clicked window-relative $X,$Y"
    }

    'rightclick' {
        $p = Require-Demo
        [DemoDriver]::RightClick($p.MainWindowHandle, $X, $Y)
        "right-clicked window-relative $X,$Y"
    }

    'drag' {
        $p = Require-Demo
        [DemoDriver]::Drag($p.MainWindowHandle, $X, $Y, $ToX, $ToY, $Modifier)
        "dragged window-relative $X,$Y -> $ToX,$ToY modifier=$Modifier"
    }

    'key' {
        $p = Require-Demo

        # Именно SendKeys, а не keybd_event: последний до приложения не доходит,
        # хотя окно и foreground. Мышь работает иначе, потому что mouse_event
        # адресуется точкой экрана, а не фокусом.
        $token = switch ($Key) {
            'Left'   { '{LEFT}' }
            'Right'  { '{RIGHT}' }
            'Up'     { '{UP}' }
            'Down'   { '{DOWN}' }
            'Delete' { '{DELETE}' }
            'Escape' { '{ESC}' }
            'A'      { 'a' }
        }

        $prefix = switch ($Modifier) {
            'Ctrl'  { '^' }
            'Shift' { '+' }
            'Alt'   { '%' }
            default { '' }
        }

        $shell = New-Object -ComObject WScript.Shell
        $null = $shell.AppActivate($p.Id)
        Start-Sleep -Milliseconds 400
        $shell.SendKeys(($prefix + $token) * [Math]::Max(1, $Notches))
        Start-Sleep -Milliseconds 900
        "key $Key modifier=$Modifier repeat=$Notches"
    }

    'wheel' {
        $p = Require-Demo
        [DemoDriver]::Wheel($p.MainWindowHandle, $X, $Y, $Notches)
        "wheel $Notches at window-relative $X,$Y"
    }

    'stop' {
        $p = Get-Demo
        if ($null -eq $p) { 'not running'; break }
        Stop-Process -Id $p.Id -Force
        Start-Sleep -Milliseconds 500
        "stopped pid=$($p.Id)"
    }
}
