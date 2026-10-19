# Third-party research and attribution

FKGCC Light is supplied under GPL-2.0-or-later; see LICENSE.

Protocol behavior and board mappings were researched with the following primary sources:

- OpenRGB, commit `e6a96c65b15caa28d4b3fdb8c5fc4d89761b9357`, particularly the Gigabyte RGB Fusion 2 USB controller. Copyright notices in the controller identify jackun, megadjc and the OpenRGB contributors. Controller sources state GPL-2.0-or-later; some associated device-definition files state GPL-2.0-only. The preserved research copies retain their own licenses and are not included in the portable binary package. https://github.com/CalcProgrammer1/OpenRGB
- liquidctl, commit `292dc561361cfbc0cdee155c26662131fe160989`, RGB Fusion 2 driver's static-black off behavior. https://github.com/liquidctl/liquidctl
- Microsoft Windows HID / SetupAPI documentation. The program uses DLLs supplied by Windows and does not redistribute them.
- GIGABYTE's official GCC package and B650M AORUS ELITE AX ICE documentation. Vendor installers, DLLs, icons and decompiled sources are research inputs only, excluded from the release package.

The implementation uses its own C# transport, UI, validation and profile format. Upstream protocol formulas and mappings were cross-checked against the vendor artifacts and local device responses. No GCC binary is loaded by this application. GIGABYTE and AORUS names identify compatible hardware; this is an independent project.
