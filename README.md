# TMBToMIDI
Converts Trombone Champ custom charts (.tmb files) into MIDI.

 **Contents**
- [How To Use](#how-to-use)
- [Features](#features)
- [Additional Info](#additional-info)

<a id="how-to-use"></a>
## How To Use

![MainWindow](https://github.com/Gloomhonk/TmbToMidi/assets/135999125/92c8cb6f-4fd6-49a0-8101-c0be3d4cac94)

1. Download the latest release.
2. Unzip and run TmbToMidiGUI.exe.
3. Click **Load TMB** to open the file dialog and choose a .tmb file to load.
	* If successful, you should see the song information appear in the main window.
4. Click **Generate MIDI** and choose a location to save the MIDI file.

If you have any questions or issues then feel free to ask the [Trombone Champ Modding discord](https://discord.gg/KVzKRsbetJ). If asked for a log file, this can be found in your app folder.

<a id="features"></a>
## Features

- Slides are converted using the [TCCC](https://tc-chart-converter.github.io/) format, so a single slide will consist of two overlapping notes going from the start of the first note to the end of the second note.
- Lyrics are added to the MIDI as Lyrics MIDI events.
- Improv zones are added to the MIDI as text events with the format **improv_start** and **improv_end**.
- Background events are added to the MIDI as text events with the format **bg_[eventid]**.
- MIDI tempo is set to the given tempo in the chart metadata.
- Notes containing microtones will have pitch bend events added to adjust relevant MIDI notes.
	* The MIDI pitch bend range can be controlled via the Settings window.

### Planned Features
- Support for customizing length of slide end notes.

<a id="additional-info"></a>
## Additional Info

### MIDI Info
- Uses a resolution of 960 ticks per quarter note for conversion (this is same value as TCCC and the default for DAWs such as Reaper and Logic).
- Note On events are added with 100 velocity, Note Off events are added with 0 velocity.
- All events are added to MIDI Channel 1.

### Conversion Limitations
Some information is lost during the conversion process from MIDI to TMB, so when reversing this process there will be some internal differences:
- Slide end notes will be a different size, although the end point will be the same.
- If the original MIDI contained tempo change events they will not be present (notes are in the post-tempo shifted positions).
- The exact time and frequency of pitch bend events will differ.
- If the original MIDI used separate channels for different events then this will not be preserved.

### Licenses
Licensed under the [MIT License](https://github.com/Gloomhonk/TmbToMidi?tab=MIT-1-ov-file).

External libraries used:\
[DryWetMidi](https://github.com/melanchall/drywetmidi) is licensed under the [MIT License](https://github.com/melanchall/drywetmidi?tab=MIT-1-ov-file).\
[JSON.NET](https://github.com/JamesNK/Newtonsoft.Json) is licensed under the [MIT License](https://github.com/JamesNK/Newtonsoft.Json?tab=MIT-1-ov-file).\
[NLog](https://github.com/NLog/NLog) is licensed under the [BSD 3-Clause License](https://github.com/NLog/NLog?tab=BSD-3-Clause-1-ov-file).










