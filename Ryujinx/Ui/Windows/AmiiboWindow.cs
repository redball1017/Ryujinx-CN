﻿using Gtk;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Utilities;
using Ryujinx.Ui.Common.Configuration;
using Ryujinx.Ui.Widgets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Ryujinx.Ui.Windows
{
    public partial class AmiiboWindow : Window
    {
        private struct AmiiboJson
        {
            [JsonPropertyName("amiibo")]
            public List<AmiiboApi> Amiibo { get; set; }
            [JsonPropertyName("lastUpdated")]
            public DateTime LastUpdated { get; set; }
        }

        private struct AmiiboApi
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("head")]
            public string Head { get; set; }
            [JsonPropertyName("tail")]
            public string Tail { get; set; }
            [JsonPropertyName("image")]
            public string Image { get; set; }
            [JsonPropertyName("amiiboSeries")]
            public string AmiiboSeries { get; set; }
            [JsonPropertyName("character")]
            public string Character { get; set; }
            [JsonPropertyName("gameSeries")]
            public string GameSeries { get; set; }
            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("release")]
            public Dictionary<string, string> Release { get; set; }

            [JsonPropertyName("gamesSwitch")]
            public List<AmiiboApiGamesSwitch> GamesSwitch { get; set; }
        }

        private class AmiiboApiGamesSwitch
        {
            [JsonPropertyName("amiiboUsage")]
            public List<AmiiboApiUsage> AmiiboUsage { get; set; }
            [JsonPropertyName("gameID")]
            public List<string> GameId { get; set; }
            [JsonPropertyName("gameName")]
            public string GameName { get; set; }
        }

        private class AmiiboApiUsage
        {
            [JsonPropertyName("Usage")]
            public string Usage { get; set; }
            [JsonPropertyName("write")]
            public bool Write { get; set; }
        }

        private const string DEFAULT_JSON = "{ \"amiibo\": [] }";

        public string AmiiboId { get; private set; }

        public int    DeviceId                 { get; set; }
        public string TitleId                  { get; set; }
        public string LastScannedAmiiboId      { get; set; }
        public bool   LastScannedAmiiboShowAll { get; set; }

        public ResponseType Response { get; private set; }

        public bool UseRandomUuid
        {
            get
            {
                return _randomUuidCheckBox.Active;
            }
        }

        private readonly HttpClient _httpClient;
        private readonly string     _amiiboJsonPath;

        private readonly byte[] _amiiboLogoBytes;

        private List<AmiiboApi> _amiiboList;

        public AmiiboWindow() : base($"Ryujinx {Program.Version} - Amiibo")
        {
            Icon = new Gdk.Pixbuf(Assembly.GetAssembly(typeof(ConfigurationState)), "Ryujinx.Ui.Common.Resources.Logo_Ryujinx.png");

            InitializeComponent();

            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromMilliseconds(5000)
            };

            Directory.CreateDirectory(System.IO.Path.Join(AppDataManager.BaseDirPath, "system", "amiibo"));

            _amiiboJsonPath = System.IO.Path.Join(AppDataManager.BaseDirPath, "system", "amiibo", "Amiibo.json");
            _amiiboList     = new List<AmiiboApi>();

            _amiiboLogoBytes    = EmbeddedResources.Read("Ryujinx.Ui.Common/Resources/Logo_Amiibo.png");
            _amiiboImage.Pixbuf = new Gdk.Pixbuf(_amiiboLogoBytes);

            _scanButton.Sensitive         = false;
            _randomUuidCheckBox.Sensitive = false;

            _ = LoadContentAsync();
        }

        private async Task LoadContentAsync()
        {
            string amiiboJsonString = DEFAULT_JSON;

            if (File.Exists(_amiiboJsonPath))
            {
                amiiboJsonString = File.ReadAllText(_amiiboJsonPath);

                if (await NeedsUpdate(JsonHelper.Deserialize<AmiiboJson>(amiiboJsonString).LastUpdated))
                {
                    amiiboJsonString = await DownloadAmiiboJson();
                }
            }
            else
            {
                try
                {
                    amiiboJsonString = await DownloadAmiiboJson();
                }
                catch
                {
                    ShowInfoDialog();

                    Close();
                }
            }

            _amiiboList = JsonHelper.Deserialize<AmiiboJson>(amiiboJsonString).Amiibo;
            _amiiboList = _amiiboList.OrderBy(amiibo => amiibo.AmiiboSeries).ToList();

            if (LastScannedAmiiboShowAll)
            {
                _showAllCheckBox.Click();
            }

            ParseAmiiboData();

            _showAllCheckBox.Clicked += ShowAllCheckBox_Clicked;
        }

        private void ParseAmiiboData()
        {
            List<string> comboxItemList = new List<string>();

            for (int i = 0; i < _amiiboList.Count; i++)
            {
                if (!comboxItemList.Contains(_amiiboList[i].AmiiboSeries))
                {
                    if (!_showAllCheckBox.Active)
                    {
                        foreach (var game in _amiiboList[i].GamesSwitch)
                        {
                            if (game != null)
                            {
                                if (game.GameId.Contains(TitleId))
                                {
                                    comboxItemList.Add(_amiiboList[i].AmiiboSeries);
                                    _amiiboSeriesComboBox.Append(_amiiboList[i].AmiiboSeries, _amiiboList[i].AmiiboSeries);

                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        comboxItemList.Add(_amiiboList[i].AmiiboSeries);
                        _amiiboSeriesComboBox.Append(_amiiboList[i].AmiiboSeries, _amiiboList[i].AmiiboSeries);
                    }
                }
            }

            _amiiboSeriesComboBox.Changed += SeriesComboBox_Changed;
            _amiiboCharsComboBox.Changed  += CharacterComboBox_Changed;

            if (LastScannedAmiiboId != "")
            {
                SelectLastScannedAmiibo();
            }
            else
            {
                _amiiboSeriesComboBox.Active = 0;
            }
        }

        private void SelectLastScannedAmiibo()
        {
            bool isSet = _amiiboSeriesComboBox.SetActiveId(_amiiboList.FirstOrDefault(amiibo => amiibo.Head + amiibo.Tail == LastScannedAmiiboId).AmiiboSeries);
            isSet = _amiiboCharsComboBox.SetActiveId(LastScannedAmiiboId);

            if (isSet == false)
            {
                _amiiboSeriesComboBox.Active = 0;
            }
        }

        private async Task<bool> NeedsUpdate(DateTime oldLastModified)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, "https://amiibo.ryujinx.org/"));

                if (response.IsSuccessStatusCode)
                {
                    return response.Content.Headers.LastModified != oldLastModified;
                }

                return false;
            }
            catch
            {
                ShowInfoDialog();

                return false;
            }
        }

        private async Task<string> DownloadAmiiboJson()
        {
            HttpResponseMessage response = await _httpClient.GetAsync("https://amiibo.ryujinx.org/");

            if (response.IsSuccessStatusCode)
            {
                string amiiboJsonString = await response.Content.ReadAsStringAsync();

                using (FileStream dlcJsonStream = File.Create(_amiiboJsonPath, 4096, FileOptions.WriteThrough))
                {
                    dlcJsonStream.Write(Encoding.UTF8.GetBytes(amiiboJsonString));
                }

                return amiiboJsonString;
            }
            else
            {
                GtkDialog.CreateInfoDialog($"Amiibo API", "An error occured while fetching information from the API.");

                Close();
            }

            return DEFAULT_JSON;
        }

        private async Task UpdateAmiiboPreview(string imageUrl)
        {
            HttpResponseMessage response = await _httpClient.GetAsync(imageUrl);

            if (response.IsSuccessStatusCode)
            {
                byte[]     amiiboPreviewBytes = await response.Content.ReadAsByteArrayAsync();
                Gdk.Pixbuf amiiboPreview      = new Gdk.Pixbuf(amiiboPreviewBytes);

                float ratio = Math.Min((float)_amiiboImage.AllocatedWidth  / amiiboPreview.Width,
                                       (float)_amiiboImage.AllocatedHeight / amiiboPreview.Height);

                int resizeHeight = (int)(amiiboPreview.Height * ratio);
                int resizeWidth  = (int)(amiiboPreview.Width  * ratio);

                _amiiboImage.Pixbuf = amiiboPreview.ScaleSimple(resizeWidth, resizeHeight, Gdk.InterpType.Bilinear);
            }
        }

        private void ShowInfoDialog()
        {
            GtkDialog.CreateInfoDialog($"Amiibo API", "Unable to connect to Amiibo API server. The service may be down or you may need to verify your internet connection is online.");
        }

        //
        // Events
        //
        private void SeriesComboBox_Changed(object sender, EventArgs args)
        {
            _amiiboCharsComboBox.Changed -= CharacterComboBox_Changed;

            _amiiboCharsComboBox.RemoveAll();

            List<AmiiboApi> amiiboSortedList = _amiiboList.Where(amiibo => amiibo.AmiiboSeries == _amiiboSeriesComboBox.ActiveId).OrderBy(amiibo => amiibo.Name).ToList();

            List<string> comboxItemList = new List<string>();

            for (int i = 0; i < amiiboSortedList.Count; i++)
            {
                if (!comboxItemList.Contains(amiiboSortedList[i].Head + amiiboSortedList[i].Tail))
                {
                    if (!_showAllCheckBox.Active)
                    {
                        foreach (var game in amiiboSortedList[i].GamesSwitch)
                        {
                            if (game != null)
                            {
                                if (game.GameId.Contains(TitleId))
                                {
                                    comboxItemList.Add(amiiboSortedList[i].Head + amiiboSortedList[i].Tail);
                                    _amiiboCharsComboBox.Append(amiiboSortedList[i].Head + amiiboSortedList[i].Tail, amiiboSortedList[i].Name);

                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        comboxItemList.Add(amiiboSortedList[i].Head + amiiboSortedList[i].Tail);
                        _amiiboCharsComboBox.Append(amiiboSortedList[i].Head + amiiboSortedList[i].Tail, amiiboSortedList[i].Name);
                    }
                }
            }

            _amiiboCharsComboBox.Changed += CharacterComboBox_Changed;

            _amiiboCharsComboBox.Active = 0;

            _scanButton.Sensitive         = true;
            _randomUuidCheckBox.Sensitive = true;
        }

        private void CharacterComboBox_Changed(object sender, EventArgs args)
        {
            AmiiboId = _amiiboCharsComboBox.ActiveId;

            _amiiboImage.Pixbuf = new Gdk.Pixbuf(_amiiboLogoBytes);

            string imageUrl = _amiiboList.FirstOrDefault(amiibo => amiibo.Head + amiibo.Tail == _amiiboCharsComboBox.ActiveId).Image;

            string usageString = "";

            for (int i = 0; i < _amiiboList.Count; i++)
            {
                if (_amiiboList[i].Head + _amiiboList[i].Tail == _amiiboCharsComboBox.ActiveId)
                {
                    bool writable = false;

                    foreach (var item in _amiiboList[i].GamesSwitch)
                    {
                        if (item.GameId.Contains(TitleId))
                        {
                            foreach (AmiiboApiUsage usageItem in item.AmiiboUsage)
                            {
                                usageString += Environment.NewLine + $"- {usageItem.Usage.Replace("/", Environment.NewLine + "-")}";

                                writable = usageItem.Write;
                            }
                        }
                    }

                    if (usageString.Length == 0)
                    {
                        usageString = "Unknown.";
                    }

                    _gameUsageLabel.Text = $"Usage{(writable ? " (Writable)" : "")} : {usageString}";
                }
            }

            _ = UpdateAmiiboPreview(imageUrl);
        }

        private void ShowAllCheckBox_Clicked(object sender, EventArgs e)
        {
            _amiiboImage.Pixbuf = new Gdk.Pixbuf(_amiiboLogoBytes);

            _amiiboSeriesComboBox.Changed -= SeriesComboBox_Changed;
            _amiiboCharsComboBox.Changed  -= CharacterComboBox_Changed;

            _amiiboSeriesComboBox.RemoveAll();
            _amiiboCharsComboBox.RemoveAll();

            _scanButton.Sensitive         = false;
            _randomUuidCheckBox.Sensitive = false;

            new Task(() => ParseAmiiboData()).Start();
        }

        private void ScanButton_Pressed(object sender, EventArgs args)
        {
            LastScannedAmiiboShowAll = _showAllCheckBox.Active;

            Response = ResponseType.Ok;

            Close();
        }

        private void CancelButton_Pressed(object sender, EventArgs args)
        {
            AmiiboId                 = "";
            LastScannedAmiiboId      = "";
            LastScannedAmiiboShowAll = false;

            Response = ResponseType.Cancel;

            Close();
        }

        protected override void Dispose(bool disposing)
        {
            _httpClient.Dispose();

            base.Dispose(disposing);
        }
    }
}
