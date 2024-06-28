using NLog;
using System;
using System.ComponentModel;
using System.Windows;
using TmbToMidi;
using TmbToMidiGUI;

namespace TmbToMidiGui
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		TmbData _songData;
		TmbConverterSettings _settings;
		SettingsWindow _settingsWindow;
		private static readonly Logger Log = LogManager.GetCurrentClassLogger();

		public MainWindow()
		{
			InitializeComponent();
			ButtonGenerate.IsEnabled = false;
			_settings = new TmbConverterSettings();

			this.Closing += OnMainWindowClosing;
		}

		private void LoadTmb_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog();
			dialog.FileName = "song";
			dialog.DefaultExt = ".tmb";
			dialog.Filter = "Trombone Champ chart (.tmb)|*.tmb";

			bool? result = dialog.ShowDialog();

			if (result == true)
			{
				string filename = dialog.FileName;

				Log.Info("Attempting to load file: {0} ", filename);

				try
				{
					_songData = TmbConverter.LoadTmbData(filename);
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Exception occurred when attempting to load file.");
				}

				if (_songData != null)
				{
					Log.Info("File loaded: {0} ", filename);
					StatusMessageTextBox.Text = "Song data successfully loaded.";
					TrackrefTextBlock.Text = "Trackref: " + _songData.trackref;
					NameTextBlock.Text = "Name: " + _songData.name;
					ShortNameTextBlock.Text = "Short Name: " + _songData.shortName;
					TempoTextBlock.Text = "Tempo: " + _songData.tempo;
					ButtonGenerate.IsEnabled = true;
				}
				else
				{
					Log.Error("File failed to load: {0} ", filename);
					StatusMessageTextBox.Text = "Failed to load song data, see log for more info.";
					TrackrefTextBlock.Text = "Trackref: ";
					NameTextBlock.Text = "Name: ";
					ShortNameTextBlock.Text = "Short Name: ";
					TempoTextBlock.Text = "Tempo: ";
					ButtonGenerate.IsEnabled = false;
				}
			}
		}

		private void GenerateMidi_Click(object sender, RoutedEventArgs e)
		{
			if (_songData == null)
			{
				string message = "Cannot generate MIDI: no song data loaded.";
				Log.Error(message);
				StatusMessageTextBox.Text = message;
				return;
			}

			var dialog = new Microsoft.Win32.SaveFileDialog();
			dialog.FileName = "song";
			dialog.DefaultExt = ".mid";
			dialog.Filter = "MIDI (.mid)|*.mid";

			bool? result = dialog.ShowDialog();

			if (result != true)
			{
				Log.Info("Failed to create filename.");
				return;
			}

			string filename = dialog.FileName;

			Log.Info("Attempting to generate MIDI for trackref: {0} filename: {1}", _songData.trackref, filename);

			try
			{
				TmbConverter.ConvertAndWriteToMidi(_songData, filename, _settings);
				Log.Info("MIDI generated. {0}", filename);
				StatusMessageTextBox.Text = "MIDI generated!";
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Failed to generate MIDI. {0}", filename);
				StatusMessageTextBox.Text = "Failed to generate MIDI, see log for more info.";
			}
		}

		private void Settings_Click(object sender, RoutedEventArgs e)
		{
			if (_settingsWindow == null)
			{
				_settingsWindow = new SettingsWindow(_settings);
				_settingsWindow.Closed += OnSettingsWindowClosed;
				_settingsWindow.Show();
			}
		}

		private void OnSettingsWindowClosed(object sender, EventArgs e)
		{
			_settingsWindow = null;
		}

		private void OnMainWindowClosing(object sender, CancelEventArgs e)
		{
			if (_settingsWindow != null)
			{
				_settingsWindow.Close();
			}
		}
	}
}
