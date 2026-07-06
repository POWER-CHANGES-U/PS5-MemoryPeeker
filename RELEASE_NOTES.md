# Release Notes

## v1.0.0

Initial public release.

- EBOOT-focused PS5 memory scanner/editor.
- PS5Debug/libdebug connection flow.
- Payload send support through a user-provided port.
- Process memory section refresh and filtering.
- First scan and next scan value narrowing.
- Address read/write and locked cheat refresh.
- Cheat save/load support.
- JSON, SHN, and MC4 export options.
- Light/dark theme support.
- Startup fold animation with sound.

Known limitations:

- Pointer-chain generation is not implemented.
- Game-title database detection is not included.
- External cheat-runner export compatibility depends on the target runner format and the quality of the found addresses.
- PS5Debug/libdebug compatibility depends on the user's jailbreak environment.
