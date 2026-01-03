
namespace EarTrumpet.UI.ViewModels
{
    public class EarTrumpetScreenAudioRouterPageViewModel : SettingsPageViewModel
    {
        public bool EnableScreenAudioRouting
        {
            get => _settings.EnableScreenAudioRouting;
            set => _settings.EnableScreenAudioRouting = value;
        }

        public ScreenAudioDeviceMapViewModel ScreenAudioDeviceView { get; }

        private readonly AppSettings _settings;

        public EarTrumpetScreenAudioRouterPageViewModel(AppSettings settings) : base(null)
        {
            _settings = settings;
            Title = Properties.Resources.ScreenAudioRouterText;
            Glyph = "\xE825";

            ScreenAudioDeviceView = new ScreenAudioDeviceMapViewModel(_settings.ScreenAudioDeviceMap, (newScreenAudioDeviceMap) => { settings.ScreenAudioDeviceMap = newScreenAudioDeviceMap; });
        }
    }
}
