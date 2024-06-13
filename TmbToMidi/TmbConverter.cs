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
		public static void ConvertAndWriteToMidi(TmbData songData, string filename)
		{
			//MIDI tempo is in microseconds per beat.
			long midiTempo = (long)Math.Round((60.0f / songData.tempo) * 1000000); 
			//long midiTempo120bpm = 500000;

			TicksPerQuarterNoteTimeDivision timeDivision = new TicksPerQuarterNoteTimeDivision(960);

			List<TrackChunk> trackChunks = new List<TrackChunk>();
			trackChunks.Add(new TrackChunk(new SetTempoEvent(midiTempo)));
			trackChunks.AddRange(ConvertNotes(songData, timeDivision.TicksPerQuarterNote));
			trackChunks.Add(ConvertImprovZones(songData, timeDivision.TicksPerQuarterNote));
			trackChunks.Add(ConvertLyrics(songData, timeDivision.TicksPerQuarterNote));
			trackChunks.Add(ConvertBackgroundEvents(songData, timeDivision.TicksPerQuarterNote));

			Log.Info("Building final MIDI");
			MidiFile midiFile = new MidiFile(trackChunks);

			midiFile.TimeDivision = timeDivision;

			Log.Info("Writing MIDI to file.");
			midiFile.Write(filename, true, MidiFileFormat.SingleTrack);
		}

		private static List<TrackChunk> ConvertNotes(TmbData songData, short timeDivisionTicks)
		{
			//Lowest TC note = B2 = 47. Top note = C#5 = 73. C4 = 0 units (60).
			Log.Info("Converting notes, total = {0}", songData.notes.Length);
			List<TrackChunk> noteChunks = new List<TrackChunk>();

			int currentPass = 0;
			int maxNumPasses = 1000;
			List<float[]> remainingNotes = new List<float[]>(songData.notes);

			//It's possible for notes to overlap in chart data, so perform multiple passes
			//to split any overlapping notes across separate TrackChunks.
			while (remainingNotes.Count > 0 && currentPass < maxNumPasses)
			{
				Log.Info("Note pass {0}", currentPass);
				List<NoteEvent> noteEvents = new List<NoteEvent>();
				long lastEventTicks = 0;
				int i = 0;

				while (i < remainingNotes.Count)
				{
					float[] currentNote = remainingNotes[i];
					long startTicks = (long)Math.Round(currentNote[0] * timeDivisionTicks);
					long endTicks = (long)Math.Round((currentNote[0] + currentNote[1]) * timeDivisionTicks);
					//TODO - Need to support microtonal adjustments (this is rounding notes to the nearest whole note).
					int startPitch = (int)Math.Round(currentNote[2] / PitchUnitsPerSemitone) + 60;
					int endPitch = (int)Math.Round(currentNote[4] / PitchUnitsPerSemitone) + 60;

					Log.Debug("Note start time = {0}({1} ticks) start pitch = {2} end time = {3}({4} ticks) end pitch = {5}",
						currentNote[0], startTicks, startPitch, currentNote[0] + currentNote[1], endTicks, endPitch);

					long startDelta = startTicks - lastEventTicks;

					//Check for suspicious delta values.
					if (Math.Abs(startDelta) == 1)
					{
						//A delta of 1 (1ms at 120bpm) can be caused by floating point precision issues in the TMB.
						//Adjust the end of the previous note so it connects correctly to the current note.
						Log.Warn("Note at beat {0} has a tiny delta ({1}). Adjusting previous note to connect to this one.",
							currentNote[0], startDelta);
						noteEvents.Last<NoteEvent>().DeltaTime += startDelta;
						startDelta = 0;
					}
					else if (startDelta < 0)
					{
						//If an overlapping note is detected, don't process it and move on to the next note.
						Log.Warn("Note at beat {0} has a negative delta ({1}). Leaving note until the next pass.",
							currentNote[0], startDelta);
						i++;
						continue;
					}

					if (startPitch == endPitch)
					{
						//Non-slide note: add a single note on/off pair for the duration of the note.
						noteEvents.Add(new NoteOnEvent((SevenBitNumber)startPitch, (SevenBitNumber)100)
						{
							DeltaTime = startDelta
						});

						noteEvents.Add(new NoteOffEvent((SevenBitNumber)endPitch, (SevenBitNumber)0)
						{
							DeltaTime = endTicks - startTicks
						});
					}
					else
					{
						//Slide note: add an extra note to represent the pitch delta in MIDI (using TCCC rules).
						//The note will be the smaller of half the length of the main note or 1/8th of a beat.
						long noteHalfTicks = (endTicks - startTicks) / 2;
						long eighthBeatTicks = timeDivisionTicks / 8;
						long deltaNoteLength = eighthBeatTicks < noteHalfTicks ? eighthBeatTicks : noteHalfTicks;

						noteEvents.Add(new NoteOnEvent((SevenBitNumber)startPitch, (SevenBitNumber)100)
						{
							DeltaTime = startDelta
						});

						noteEvents.Add(new NoteOnEvent((SevenBitNumber)endPitch, (SevenBitNumber)100)
						{
							DeltaTime = endTicks - deltaNoteLength - startTicks
						});

						noteEvents.Add(new NoteOffEvent((SevenBitNumber)startPitch, (SevenBitNumber)0)
						{
							DeltaTime = deltaNoteLength
						});

						noteEvents.Add(new NoteOffEvent((SevenBitNumber)endPitch, (SevenBitNumber)0)
						{
							DeltaTime = 0
						});
					}

					lastEventTicks = endTicks;
					remainingNotes.RemoveAt(i);
				}

				noteChunks.Add(new TrackChunk(noteEvents.ToArray()));
				currentPass++;

				if (currentPass > maxNumPasses)
				{
					Log.Error("Reached the max number of note passes, final MIDI may have missing notes.");
				}
			}

			return noteChunks;
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

			return new TrackChunk(improvEvents);
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

			return new TrackChunk(lyricEvents);
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

			return new TrackChunk(bgEvents);
		}
	}
}
