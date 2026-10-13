# LightsOut asset provenance and licenses

## Original audio

The seven WAV files in `Assets/LightsOut/Audio/` were composed and synthesized specifically for this project on 2026-10-13. They contain generated oscillators, envelopes, and deterministic noise. No external recording, sample pack, copyrighted composition, or music generator service was used. No third-party attribution or sample license applies to these original generated sounds.

| File | Purpose | Length |
| --- | --- | --- |
| 01_SwitchClick.wav | Soft mechanical switch click | 0.14 s |
| 02_LightOff.wav | Short descending light-off response | 0.33 s |
| 03_GoldenReward.wav | Golden lamp reward | 1.10 s |
| 04_Upgrade.wav | Upgrade purchase | 0.90 s |
| 05_ZoneUnlock.wav | New area unlocked | 1.70 s |
| 06_ShiftComplete.wav | Shift complete / home time | 2.60 s |
| NightOfficeLoop.wav | Original quiet night-office synth loop, 80 BPM | 24 s |

All files are PCM 16-bit at 48 kHz. Effects are mono; the music loop is stereo. `AudioManifest.json` records checksums, durations, and amplitude validation.

### Roulette presentation audio and UI

The roulette reuses the existing original switch, preparation, and reward effects through a separate audio source. `LightsOutAudio.cs` also synthesizes an original 90 Hz, 80 ms mono pulse at runtime with a short attack, exponential decay, and release envelope. It creates a non-streaming 48 kHz Unity AudioClip from PCM sample values; no external sound or new downloaded media asset is used. This pulse was authored specifically for this project on 2026-10-14 and is destroyed when the component is destroyed.

The separate roulette popup, wheel sectors, pointer, rim lights, income ticket, and result animations are native Unity uGUI geometry and text authored in this project (`LightsOutRoulettePopup.cs`), rather than imported tutorial assets. The project retains the existing Noto Sans KR font and its license below. No new third-party asset license is introduced by this presentation.

## Noto Sans KR Regular

- File: `Assets/LightsOut/Fonts/NotoSansKR-Regular.otf`
- Family / version: Noto Sans KR Regular, 2.004 (static Korean subset OpenType font).
- Copyright embedded in the unmodified font: **© 2014–2021 Adobe (http://www.adobe.com/).**
- Primary project: [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk).
- Download: [official Sans2.004 Korean subset font](https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/SubsetOTF/KR/NotoSansKR-Regular.otf).
- License: **SIL Open Font License 1.1**. Full license is retained as `Assets/LightsOut/Fonts/OFL.txt`; the copyright and license metadata are also embedded in the unchanged font.
- Official license source: [Noto Sans CJK LICENSE](https://github.com/notofonts/noto-cjk/blob/main/Sans/LICENSE).

OFL 1.1 permits commercial game embedding and distribution with software. Keep the font copyright notice and full OFL license when distributing the font or a font-derived asset. The font may not be sold by itself. If modifying the font, follow the reserved-name and license requirements in the full license.

No Unity Asset Store package or paid third-party content has been added for these assets.
