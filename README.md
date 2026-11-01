# Atlas8Tweaks

Tweaks for the **Acer Predator Atlas 8 (PA08)** handheld.

## VRR Fix: 28-120 Hz instead of 48-120 Hz

Out of the box, the Atlas 8's screen only does variable refresh rate (VRR) between 48 and 120 Hz.
When a game drops below 48 fps, the Intel driver starts doubling, tripling or quadrupling frames, and you get stutter.
This fix lowers the floor to **28 Hz**, so games running at 30-48 fps stay smooth.

### Install

1. Download **`Atlas8-VRR-Fix-x.y.z.zip`** from the [latest release](https://github.com/KillerPixelCrew/Atlas8Tweaks/releases/latest).
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

## Building from source

Requires Visual Studio with the C++ desktop tools (for the VRR test tool).

```powershell
.\build.ps1          # -> dist\Atlas8-VRR-Fix-<version>.zip
```

## Credits

Based on the approach and Intel driver interop of the
ClawLab MSI Claw Intel VRR Range Fix 3.0.0 (MIT).
