using System.Windows;
using TmbToMidi;

namespace TmbToMidiGUI
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        TmbConverterSettings _settings;
        private bool _initialized = false;

        public SettingsWindow(TmbConverterSettings settings)
        {
            _settings = settings;

            InitializeComponent();

            _initialized = true;

            //Assign defaults;
            PitchBendRangeValue.Value = _settings.PitchBendRange;
        }

        private void PitchBendSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //Ignore ValueChanged events during initialization: the slider has a minimum value, which will trigger
            //ValueChanged and override any default settings.
            //https://github.com/xamarin/Xamarin.Forms/issues/4902
            if (_initialized)
			{
				_settings.PitchBendRange = (ushort)PitchBendRangeValue.Value;
            }
        }
    }
}
