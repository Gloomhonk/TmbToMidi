using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TmbToMidi
{
	public static class TmbConverter
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger();
		private const float PitchUnitsPerSemitone = 13.75f;
		private const int MidiPitchBendMaxValue = 8192;
		private const int DefaultMidiNotePitch = 60;

		/// <summary>
		/// Attempts to load the given filename and retrieve the TMB song data.
		/// </summary>
		public static TmbData LoadTmbData(string filename)
		{
			string fileContents = File.ReadAllText(filename);
			return JsonConvert.DeserializeObject<TmbData>(fileContents);
		}

		/// <summary>
		/// Converts the given TMB song data to MIDI and writes to the given file path.
		/// </summary>
		public static void ConvertAndWriteToMidi(TmbData songData, string filename, TmbConverterSettings settings)
		{
			//MIDI tempo is in microseconds per beat.
			long midiTempo = (long)Math.Round((60.0f / songData.tempo) * 1000000);
			//long midiTempo120bpm = 500000;

			TicksPerQuarterNoteTimeDivision timeDivision = new TicksPerQuarterNoteTimeDivision(960);

			List<TrackChunk> trackChunks = new List<TrackChunk>();
			trackChunks.Add(new TrackChunk(new SetTempoEvent(midiTempo)));
			trackChunks.AddRange(ConvertNotes(songData, timeDivision.TicksPerQuarterNote, settings));
			trackChunks.Add(ConvertImprovZones(songData, timeDivision.TicksPerQuarterNote));
			trackChunks.Add(ConvertLyrics(songData, timeDivision.TicksPerQuarterNote));
			trackChunks.Add(ConvertBackgroundEvents(songData, timeDivision.TicksPerQuarterNote));

			Log.Info("Building final MIDI");
			MidiFile midiFile = new MidiFile(trackChunks);

			midiFile.TimeDivision = timeDivision;

			Log.Info("Writing MIDI to file.");
			midiFile.Write(filename, true, MidiFileFormat.SingleTrack);
		}

		private static List<TrackChunk> ConvertNotes(TmbData songData, short timeDivisionTicks, TmbConverterSettings settings)
		{
			//Lowest TC note = B2 = 47. Top note = C#5 = 73. C4 = 0 units (60).
			Log.Info("Converting notes: total = {0}, pitch bend range = {1}", songData.notes.Length, settings.PitchBendRange);
			List<TrackChunk> noteChunks = new List<TrackChunk>();
			List<float[]> remainingNotes = new List<float[]>(songData.notes);
			int currentPass = 0;
			int maxNumPasses = 1000;

			//It's possible for notes to overlap, so do multiple passes to process overlapping notes in separate TrackChunks.
			while (remainingNotes.Count > 0 && currentPass < maxNumPasses)
			{
				Log.Info("Note pass {0}", currentPass);
				List<NoteEvent> noteEvents = new List<NoteEvent>();
				List<PitchBendEvent> pitchBendEvents = new List<PitchBendEvent>();
				long lastEventTicks = 0;
				long lastPitchBendTicks = 0;
				int lastEndPitchBend = 0;
				int lastEndPitch = 0;
				int i = 0;

				while (i < remainingNotes.Count)
				{
					float[] note = remainingNotes[i];
					long startTicks = (long)Math.Round(note[0] * timeDivisionTicks);
					long endTicks = (long)Math.Round((note[0] + note[1]) * timeDivisionTicks);
					long startDelta = startTicks - lastEventTicks;
					long pitchBendDeltaTicks = startTicks - lastPitchBendTicks;
					long noteLength = endTicks - startTicks;

					Log.Debug("Note start: {0} end: {1} start pitch: {2} end pitch: {3}", note[0].ToString("G9"), (note[0] + note[1]).ToString("G9"), note[2], note[4]);

					//Check for suspicious delta values:
					// - Deltas of +/-1 (1ms at 120bpm) can be caused by floating point precision differences in the TMB.
					// - Larger negative deltas are caused by overlapping notes, which should be ignored until the next pass.
					if (Math.Abs(startDelta) == 1)
					{
						Log.Warn("Note at beat {0} has a midi tick of {1} from the previous note. Adjusting notes to connect to each other.", note[0].ToString("G9"), startDelta);
						noteEvents.Last().DeltaTime += startDelta;

						//If a pitch bend event was aligned with the adjusted note then adjust it too.
						if (pitchBendDeltaTicks == startDelta)
						{
							pitchBendEvents.Last().DeltaTime += pitchBendDeltaTicks;
							pitchBendDeltaTicks = 0;
						}

						startDelta = 0;
					}
					else if (startDelta < 0)
					{
						Log.Warn("Note at beat {0} has a negative midi tick from the previous event ({1}). Leaving until the next pass.", note[0].ToString("G9"), startDelta);
						i++;
						continue;
					}

					//Calculate MIDI pitches.
					ConvertPitchValue(note[2], settings.PitchBendRange, out int startPitch, out int startPitchBend);
					ConvertPitchValue(note[4], settings.PitchBendRange, out int endPitch, out int endPitchBend);

					//Warn if this note is joined to the previous note, but has a different pitch bend value.
					if (startDelta == 0 && lastEndPitch == startPitch && startPitchBend - lastEndPitchBend != 0)
					{
						Log.Warn("Note at beat {0} is connected to previous note, but has a different pitch bend ({1}).",
							note[0].ToString("G9"), startPitchBend);
					}

					Log.Debug("Converted note, startTicks: {0} endTicks: {1} start pitch: {2} start bend: {3} end pitch: {4} end bend: {5}",
						startTicks, endTicks, startPitch, startPitchBend, endPitch, endPitchBend);

					//Add the events. Pitch bend events are added if:
					// - This is the first pass (in other words, don't add pitch bends for overlapping notes).
					// - The pitch bend value is different to the previous note.
					if (startPitch == endPitch)
					{
						//Standard note: add a single note on/off pair for the duration of the note.
						noteEvents.Add(CreateNoteOnEvent(startPitch, startDelta));
						noteEvents.Add(CreateNoteOffEvent(endPitch, noteLength));

						if (currentPass == 0)
						{
							if (startPitchBend != lastEndPitchBend)
							{
								pitchBendEvents.Add(CreatePitchBendEvent(lastEndPitchBend, pitchBendDeltaTicks));
								pitchBendEvents.Add(CreatePitchBendEvent(startPitchBend, 0));
								lastPitchBendTicks = startTicks;
								pitchBendDeltaTicks = 0;
							}

							if (endPitchBend != startPitchBend)
							{
								pitchBendEvents.Add(CreatePitchBendEvent(endPitchBend, pitchBendDeltaTicks + noteLength));
								lastPitchBendTicks = endTicks;
							}
						}
					}
					else
					{
						//Slide note: add an extra note to represent the slide in MIDI (using TCCC rules).
						//Notes are set to overlap, with the first note ending in the middle of the slide end note.
						//This minimizes the chance of floating point precision errors in the TMB affecting the slide.
						long slideNoteLength = GetSlideNoteLength(noteLength, timeDivisionTicks);
						long firstNoteLength = noteLength - (slideNoteLength / 2);
						long slideNoteStartDelta = noteLength - slideNoteLength;
						long firstNoteEndDelta = slideNoteLength / 2;
						long slideNoteEndDelta = noteLength - firstNoteLength;
						
						noteEvents.Add(CreateNoteOnEvent(startPitch, startDelta));
						noteEvents.Add(CreateNoteOnEvent(endPitch, slideNoteStartDelta));
						noteEvents.Add(CreateNoteOffEvent(startPitch, firstNoteEndDelta));
						noteEvents.Add(CreateNoteOffEvent(endPitch, slideNoteEndDelta));

						if (currentPass == 0)
						{
							if (startPitchBend != lastEndPitchBend)
							{
								pitchBendEvents.Add(CreatePitchBendEvent(lastEndPitchBend, pitchBendDeltaTicks));
								pitchBendEvents.Add(CreatePitchBendEvent(startPitchBend, 0));
								lastPitchBendTicks = startTicks;
								pitchBendDeltaTicks = 0;
							}

							if (endPitchBend != startPitchBend)
							{
								pitchBendEvents.Add(CreatePitchBendEvent(startPitchBend, pitchBendDeltaTicks + slideNoteStartDelta - 1));
								pitchBendEvents.Add(CreatePitchBendEvent(endPitchBend, 1));
								pitchBendEvents.Add(CreatePitchBendEvent(endPitchBend, slideNoteLength));
								lastPitchBendTicks = endTicks;
							}
						}
					}

					lastEventTicks = endTicks;
					lastEndPitch = endPitch;
					lastEndPitchBend = endPitchBend;
					remainingNotes.RemoveAt(i);
				}

				noteChunks.Add(new TrackChunk(noteEvents.ToArray()));
				noteChunks.Add(new TrackChunk(pitchBendEvents.ToArray()));
				currentPass++;

				if (currentPass > maxNumPasses)
				{
					Log.Error("Reached the max number of note passes, final MIDI may have missing notes.");
				}
			}

			return noteChunks;
		}

		private static NoteOnEvent CreateNoteOnEvent(int pitch, long deltaTime)
		{
			return new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)100)
			{
				DeltaTime = deltaTime
			};
		}

		private static NoteOffEvent CreateNoteOffEvent(int pitch, long deltaTime)
		{
			return new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0)
			{
				DeltaTime = deltaTime
			};
		}

		private static PitchBendEvent CreatePitchBendEvent(int pitchBend, long deltaTime)
		{
			//MIDI requires pitch bend values in the range 0-16383.
			return new PitchBendEvent((ushort)(pitchBend + MidiPitchBendMaxValue))
			{
				DeltaTime = deltaTime
			};
		}

		/// <summary>
		/// Converts a pitch value from in-game units to a MIDI whole note value plus pitch bend (if required).
		/// </summary>
		private static void ConvertPitchValue(float tmbPitch, int pitchBendRange, out int pitchWholeNote, out int pitchBend)
		{
			float exactPitch = (tmbPitch / PitchUnitsPerSemitone) + DefaultMidiNotePitch;
			pitchWholeNote = (int)Math.Round(exactPitch);

			//Pitch bend = value between -8192 and 8192, where 0 = no pitch bend.
			pitchBend = (int)Math.Round(((exactPitch - pitchWholeNote) / pitchBendRange) * MidiPitchBendMaxValue);

			//A pitch bend of 1 (0.02 cents on default settings) is likely to be a floating point precision issue.
			if (Math.Abs(pitchBend) == 1)
			{
				pitchBend = 0;
			}
		}

		private static long GetSlideNoteLength(long parentNoteLength, short timeDivisionTicks)
		{
			//Slide end notes are set to whichever is smaller:
			//- An eighth of a beat.
			//- Half the size of the parent note.
			//TODO: Allow this to be customized?
			long noteHalfTicks = parentNoteLength / 2;
			long eighthBeatTicks = timeDivisionTicks / 8;
			return eighthBeatTicks < noteHalfTicks ? eighthBeatTicks : noteHalfTicks;
		}

		private static TrackChunk ConvertImprovZones(TmbData songData, short timeDivisionTicks)
		{
			List<TextEvent> improvEvents = new List<TextEvent>();

			if (songData.improv_zones != null)
			{
				Log.Info("Converting improv zones, total = {0}", songData.improv_zones.Length);
				long lastEventTicks = 0;

				for (int i = 0; i < songData.improv_zones.Length; i++)
				{
					long startTicks = (long)Math.Round(songData.improv_zones[i][0] * timeDivisionTicks);
					long endTicks = (long)Math.Round(songData.improv_zones[i][1] * timeDivisionTicks);

					Log.Debug("ImprovZones[{0}] start time = {1}({2} ticks) end time = {3}({4} ticks)",
						i, songData.improv_zones[i][0], startTicks, songData.improv_zones[i][1], endTicks);

					improvEvents.Add(new TextEvent("improv_start")
					{
						DeltaTime = startTicks - lastEventTicks
					});
					improvEvents.Add(new TextEvent("improv_end")
					{
						DeltaTime = endTicks - startTicks
					});

					lastEventTicks = endTicks;
				}
			}

			return new TrackChunk(improvEvents.ToArray());
		}

		private static TrackChunk ConvertLyrics(TmbData songData, short timeDivisionTicks)
		{
			List<LyricEvent> lyricEvents = new List<LyricEvent>();

			if (songData.lyrics != null)
			{
				Log.Info("Converting lyrics, total = {0}", songData.lyrics.Length);
				long lastEventTicks = 0;

				for (int i = 0; i < songData.lyrics.Length; i++)
				{
					long startTicks = (long)Math.Round(songData.lyrics[i].bar * timeDivisionTicks);

					Log.Debug("Lyrics[{0}] start time = {1}({2} ticks) text = {3}",
						i, songData.lyrics[i].bar, startTicks, songData.lyrics[i].text);

					lyricEvents.Add(new LyricEvent(songData.lyrics[i].text)
					{
						DeltaTime = startTicks - lastEventTicks
					});

					lastEventTicks = startTicks;
				}
			}

			return new TrackChunk(lyricEvents.ToArray());
		}

		private static TrackChunk ConvertBackgroundEvents(TmbData songData, short timeDivisionTicks)
		{
			List<TextEvent> bgEvents = new List<TextEvent>();
			float secondsPerBeat = songData.tempo / 60.0f;

			if (songData.bgdata != null)
			{
				Log.Info("Converting background events, total = {0}", songData.bgdata.Length);
				long lastEventTicks = 0;

				for (int i = 0; i < songData.bgdata.Length; i++)
				{
					long startTicks = (long)Math.Round(secondsPerBeat * songData.bgdata[i][0] * timeDivisionTicks);

					Log.Debug("BgEvents[{0}] start time = {1}({2} ticks) id = {3}",
						i, songData.bgdata[i][0], startTicks, songData.bgdata[i][1]);

					bgEvents.Add(new TextEvent("bg_" + (int)songData.bgdata[i][1])
					{
						DeltaTime = startTicks - lastEventTicks
					});

					lastEventTicks = startTicks;
				}
			}

			return new TrackChunk(bgEvents.ToArray());
		}
	}
}
