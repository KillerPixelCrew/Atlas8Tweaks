# Atlas8Tweaks

Tweaks for the **Acer Predator Atlas 8 (PA08)** handheld.

- [Power Button: a short press puts the Atlas 8 to sleep](#power-button-a-short-press-puts-the-atlas-8-to-sleep)
- [VRR Fix: 28-120 Hz instead of 48-120 Hz](#vrr-fix-28-120-hz-instead-of-48-120-hz)

## Power Button: a short press puts the Atlas 8 to sleep

On the Atlas 8, Windows only reacts when you **hold** the power button. A short press does nothing, which feels broken.
This small program fixes that. After installing it:

- **Press briefly:** the Atlas 8 goes to sleep. Press again to wake it up.
- **Press and hold** (1.5 seconds): the Atlas 8 shuts down.

You can change both. The choices are: sleep, hibernate, shut down and restart.
It also works on the lock screen and the sign-in screen.

### Install

1. Download **`Atlas8-PowerButton-Setup-x.y.z.exe`** from the [Power Button release](https://github.com/KillerPixelCrew/Atlas8Tweaks/releases/tag/power-button-v1.0.0).
2. Double-click it and click **Yes** when Windows asks for permission.
   If you see *"Windows protected your PC"*, click **More info**, then **Run anyway**.
3. Click **Install**. Done.

To change what the button does, open **Atlas 8 Power Button** from the Start menu (or click the power icon next to the clock).

### Uninstall

Open **Settings > Apps > Installed apps**, find **Atlas 8 Power Button** and click **Uninstall**.
Or run the setup again and click **Remove**. The power button then works like before.

### How it works

The power button reaches Windows through Intel's HID Event Filter. The PDC collection (`HID\INTC816`) reports press and
release as they happen, while Windows' own path only learns about a press after release. A small Windows service reads
that collection, times the press, and sets Windows' power button action to "Do nothing" in every power plan while it can
read the button. The original actions are saved in `C:\ProgramData\Atlas8PowerButton\state.ini` and put back when the
service stops, at shutdown and on uninstall. Sleep (Modern Standby) and screen wake run briefly in the console session as SYSTEM, so
they also work on the lock and sign-in screens. Holding the button on a touch-screen PC makes Windows start its
"slide to shut down" screen; while the tool handles the button, an Image File Execution Options entry for
`SlideToShutDown.exe` starts this tool instead (which exits at once). It is removed when the tool stops. The log is `C:\ProgramData\Atlas8PowerButton\Atlas8PowerButton.log`.

## VRR Fix: 28-120 Hz instead of 48-120 Hz

Out of the box, the Atlas 8's screen only does variable refresh rate (VRR) between 48 and 120 Hz.
When a game drops below 48 fps, the Intel driver starts doubling, tripling or quadrupling frames, and you get stutter.
This fix lowers the floor to **28 Hz**, so games running at 30-48 fps stay smooth.

### Install

1. Download **`Atlas8-VRR-Fix-x.y.z.zip`** from the [VRR Fix release](https://github.com/KillerPixelCrew/Atlas8Tweaks/releases/tag/v1.0.0).
2. Right-click the ZIP, choose **Extract All...**, and open the extracted folder.
3. Double-click **`SETUP.bat`** and click **Yes** when Windows asks for administrator rights.
   If you see *"Windows protected your PC"*, click **More info**, then **Run anyway**.
4. Press **1** to install, then restart when asked.

That's it. A small sign-in task keeps the Intel settings correct after reboots and driver updates.
To check it's working, run `SETUP.bat` again and press **3** (status), or press **4** to open the VRR test tool.

### Uninstall

Run `SETUP.bat` and press **5**. This removes everything, restores your original Intel settings and asks you to restart.

### Is it safe?

- It only runs on the exact Atlas 8 panel (BOE NS080WUM-LX1, verified by its EDID fingerprint). On any other device it stops before changing anything.
- It does not flash firmware. It uses a standard Windows EDID override in the registry, and uninstalling removes it.
- Your original Intel Arc Sync profile and low-FPS settings are saved and restored on uninstall.
- If something goes wrong, the sign-in task changes nothing and writes the reason to `C:\ProgramData\AcerAtlasVrr\AcerAtlasVrr.log`.

Technical details (what gets changed and why Intel's LFC is turned off) are in [`vrr-fix/README.txt`](vrr-fix/README.txt).

### Requirements

- Acer Predator Atlas 8 (PA08) with the internal 1920x1200 panel
- Intel Arc graphics driver
- Windows 11, administrator account
- No other EDID tools active (e.g. CRU). Reset those first.

## Support

If these tweaks are useful to you, you can support their development on Ko-fi.

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/nightstorm1000)

## Building from source

Requires Visual Studio with the C++ desktop tools (for the VRR test tool) and the .NET SDK (for the power button).

```powershell
.\build.ps1                    # -> dist\Atlas8-VRR-Fix-<version>.zip and dist\Atlas8-PowerButton-Setup-<version>.exe
.\build.ps1 -SkipVrr           # power button only
.\power-button\build.ps1       # same, power button only
```

## Credits

Based on the approach and Intel driver interop of the
ClawLab MSI Claw Intel VRR Range Fix 3.0.0 (MIT).
