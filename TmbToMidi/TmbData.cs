using System;

namespace TmbToMidi
{
	[Serializable]
	public struct Lyric
	{
		public float bar;
		public string text;
	}

	[Serializable]
	public class TmbData
	{
		public string name = "";
		public string shortName = "";
		public string trackref = "";
		public float tempo;

		public float[][] notes = new float[0][];
		public float[][] improv_zones = new float[0][];
		public Lyric[] lyrics = new Lyric[0];
		public float[][] bgdata = new float[0][];
	}


}
