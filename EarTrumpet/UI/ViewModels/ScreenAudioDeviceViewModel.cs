using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.WindowsAudio;

namespace EarTrumpet.UI.ViewModels
{
    public class ScreenAudioDeviceItemViewModel : BindableBase
    {
        private string _screenName;
        private string _selectedAudioDeviceName;

        public string ScreenName
        {
            get => _screenName;
            set { _screenName = value; RaisePropertyChanged(nameof(ScreenName)); }
        }

        public string SelectedAudioDeviceName
        {
            get => _selectedAudioDeviceName;
            set { _selectedAudioDeviceName = value; RaisePropertyChanged(nameof(SelectedAudioDeviceName)); }
        }

        public ScreenAudioDeviceItemViewModel() {
        }
    }

    public class ScreenAudioDeviceMapViewModel : BindableBase
    {
        private IAudioDeviceManager _deviceManager;
        private Dictionary<string, string> _screenAudioDeviceMap;
        private readonly Action<Dictionary<string, string>> _saveFunc;
        private readonly ObservableCollection<ScreenAudioDeviceItemViewModel> _screenAudioMappingList;
        public ObservableCollection<ScreenAudioDeviceItemViewModel> ScreenAudioMappingList => _screenAudioMappingList;
        public ObservableCollection<string> AudioDeviceNameList { get; } = new ObservableCollection<string>();

        public ScreenAudioDeviceMapViewModel(Dictionary<string, string> screenAudioDeviceMap, Action<Dictionary<string, string>> saveFunc)
        {
            _screenAudioMappingList = new ObservableCollection<ScreenAudioDeviceItemViewModel>();
            _screenAudioDeviceMap = screenAudioDeviceMap;
            _saveFunc = saveFunc;

            _deviceManager = WindowsAudioFactory.Create(AudioDeviceKind.Playback);
            RefreshAudioDeviceNameList(null, null);
            _deviceManager.Devices.CollectionChanged += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    (Action)(() => RefreshAudioDeviceNameList(s, e))
                );
            };

            UpdateScreenAudioMapping(_screenAudioDeviceMap);
        }

        private void RefreshAudioDeviceNameList(Object sender, NotifyCollectionChangedEventArgs e)
        {
            AudioDeviceNameList.Clear();
            foreach (var device in _deviceManager.Devices)
            {
                if (!string.IsNullOrEmpty(device.DisplayName))
                    AudioDeviceNameList.Add(device.DisplayName);
            }
        }

        private void UpdateScreenAudioMapping(Dictionary<string, string> savedMap)
        {
            var allScreens = Screen.AllScreens;

            var newScreenAudioDeviceMap = new Dictionary<string, string>();
            foreach (var screen in allScreens)
            {
                var screenName = screen.DeviceName;
                if (string.IsNullOrEmpty(screenName)) continue;

                string defaultName = _deviceManager.Default?.DisplayName ?? string.Empty;
                newScreenAudioDeviceMap[screenName] = savedMap.TryGetValue(screenName, out var dev) ? dev : defaultName;
            }
            _screenAudioDeviceMap = newScreenAudioDeviceMap;

            if (_screenAudioMappingList.Count() > 0) {
                foreach (var item in _screenAudioMappingList) {
                    item.PropertyChanged -= OnScreenAudioMapChanged;
                }
                _screenAudioMappingList.Clear();
            }

            foreach (var kvp in _screenAudioDeviceMap)
            {
                _screenAudioMappingList.Add(new ScreenAudioDeviceItemViewModel
                {
                    ScreenName = kvp.Key,
                    SelectedAudioDeviceName = kvp.Value,
                });
            }

            foreach (var item in _screenAudioMappingList)
            {
                item.PropertyChanged += OnScreenAudioMapChanged;
            }
        }

        public void OnScreenAudioMapChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_screenAudioMappingList == null || e.PropertyName != nameof(ScreenAudioDeviceItemViewModel.SelectedAudioDeviceName)) return;

            var newDict = new Dictionary<string, string>();
            foreach (var item in _screenAudioMappingList)
            {
                if (!string.IsNullOrEmpty(item.ScreenName))
                {
                    newDict[item.ScreenName] = item.SelectedAudioDeviceName ?? string.Empty;
                }
            }
            _saveFunc(newDict);
        }
    } 
}