ACER ATLAS 8 VRR FIX 1.0.0  (Atlas8Tweaks)
==========================================
https://github.com/KillerPixelCrew/Atlas8Tweaks

Extends the Intel VRR range of the Acer Predator Atlas 8 (PA08) internal panel
from 48-120 Hz to 28-120 Hz. Based on the approach of the ClawLab MSI Claw Intel
VRR Range Fix 3.0.0 (MIT, see engine\LICENSE-ClawLab.txt).

Supported: exactly the BOE0CFF / NS080WUM-LX1 1920x1200 panel with the
validated physical EDID (SHA-256 F05B01B3...ECCE), Intel Arc graphics.
Anything else is refused before a change is made.


HOW TO INSTALL
--------------
1. Right-click the ZIP > "Extract All...". Do not run it from inside the ZIP.
2. Open the extracted folder and double-click SETUP.bat.
3. Click "Yes" when Windows asks for administrator rights.
   If a blue "Windows protected your PC" box appears: "More info" > "Run anyway".
4. Press 1 (Install), then restart Windows when asked.
Run SETUP.bat again any time for the status, the test tool or to uninstall.
Keep external displays disconnected while installing.


FILES
-----
SETUP.bat              Menu: install, status, test tool, uninstall. Start here.
Tools\VrrTest.exe      DX12 VRR tester (see TESTING).
engine\                The fix itself. For command-line use:
  INSTALL.bat          Install or update (asks for administrator rights).
                       Optional floor: INSTALL.bat -MinimumHz 30 (20-47, default 28)
  UNINSTALL.bat        Remove everything and restore the original Intel state.
  STATUS.bat           Read-only report.


WHAT INSTALL DOES
-----------------
1. Writes an EDID override that lowers every VRR floor field of the panel
   (base range descriptor, DisplayID range limits, AMD FreeSync VSDB and both
   DisplayID Adaptive-Sync descriptors) with valid checksums. A restart loads it.
2. Saves the original Intel Arc Sync profile and LFC flags for uninstall.
3. Copies the runtime to C:\ProgramData\AcerAtlasVrr (administrators only).
4. Registers the sign-in task "Acer Atlas 8 VRR Repair". At every sign-in it
   checks that the installed EDID is active, then sets Intel Arc Sync to
   EXCELLENT <floor>-120 Hz and turns the Intel low-FPS solution (LFC) off.
   It changes nothing if the EDID is not exactly the installed one.
   Log: C:\ProgramData\AcerAtlasVrr\AcerAtlasVrr.log


WHY LFC IS TURNED OFF
---------------------
Intel's low-FPS solution engages below 48 fps regardless of the EDID floor and
switches between x2/x3/x4 multipliers, which causes judder. With it off:
- 30-120 fps: native VRR, one refresh per frame
- below the 28 Hz floor: frames are shown exactly twice (panel stays >= 36 Hz)
- around 25-28 fps (right at the floor) the cadence is mixed
The panel visibly flickers below 28 Hz, which is why the default floor is 28.
The high-FPS solution is left on.


TESTING
-------
Tools\VrrTest.exe          Interactive: Up/Down +-1 fps, Left/Right +-10,
                           1-7 presets, S sweep 30-120, M present mode, Esc.
                           The orange graph is the real display refresh; with
                           working VRR it matches the cyan frame-time graph.
Tools\VrrTest.exe --auto   Steps 120 -> 18 fps and writes vrrtest-log.txt.
Tools\VrrTest.exe --vblank 10   Logs the real display refresh for 10 seconds.


AFTER A GRAPHICS DRIVER UPDATE
------------------------------
Usually the sign-in task repairs the Intel settings automatically. Check
SETUP.bat > Show status. If the log says SKIP, run SETUP.bat > Install again
and restart.


UNINSTALL
---------
SETUP.bat > Uninstall (or engine\UNINSTALL.bat) removes the task, the runtime
and the EDID override, restores the saved Intel profile and LFC flags, and
asks to restart.
