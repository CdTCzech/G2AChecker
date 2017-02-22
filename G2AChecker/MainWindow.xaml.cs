using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using static System.Windows.Controls.Primitives.RangeBase;

namespace G2AChecker
{
    public partial class MainWindow : IDisposable
    {
        private delegate void UpdateProgressBarDelegate(DependencyProperty dp, object value);

        private readonly string VERSION = "201702221040";

        private readonly WebClient _webClient = new WebClient();
        private readonly DispatcherTimer _dispatcherTimer = new DispatcherTimer();
        private readonly Dictionary<int, Game> _games = new Dictionary<int, Game>();

        private int _minutes;

        public MainWindow()
        {
            InitializeComponent();

            GamesDataGrid.IsReadOnly = true;
            _dispatcherTimer.Tick += dispatcherTimer_Tick;
            _dispatcherTimer.Interval = new TimeSpan(1, 0, 0);
            _minutes = 60;

            try
            {
                var json = JObject.Parse(File.ReadAllText(@"db.json"));
                var gamesJson = (json["games"]).ToString();
                var games = JsonConvert.DeserializeObject<List<Game>>(gamesJson);
                _games = games.ToDictionary(g => g.Id, g => g);
                var settingsJson = (json["settings"]).ToString();
                var settings = JsonConvert.DeserializeObject<Settings>(settingsJson);
                if (settings.UpdateEveryXMinutes > 4) _minutes = settings.UpdateEveryXMinutes;
                UpdateTextBox.Text = _minutes.ToString();
                InformationCheckBox.IsChecked = settings.ShowInformation;
                UpdateCheckBox.IsChecked = settings.UpdateAutomaticly;
            }
            catch (Exception)
            {
                ShowMessageBox("Creating new database.", "Information");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var gameViewSource = ((System.Windows.Data.CollectionViewSource)(FindResource("gameViewSource")));
            gameViewSource.Source = _games.Values;
        }

        private ICollection<int> GetSelectedIds(object sender)
        {
            var ids = new List<int>();
            var item = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget as DataGrid;
            var selectedGames = item?.SelectedItems;

            if (selectedGames == null) return ids;
            ids.AddRange(from Game game in selectedGames where game != null && _games.ContainsKey(game.Id) select game.Id);

            return ids;
        }

        private void SaveAndRefresh()
        {
            SaveDatabase();
            GamesDataGrid.Items.Refresh();
        }

        private void SaveDatabase()
        {
            var toStore = new JObject
                    {
                        {
                            "games", JToken.FromObject(_games.Values)
                        },
                        {
                            "settings", JToken.FromObject(
                                new Settings()
                                {
                                    UpdateAutomaticly = UpdateCheckBox.IsChecked != null && UpdateCheckBox.IsChecked.Value,
                                    UpdateEveryXMinutes = _minutes,
                                    ShowInformation = InformationCheckBox.IsChecked != null && InformationCheckBox.IsChecked.Value
                                }
                            )
                        }
                    };
            try
            {
                File.WriteAllText(@"db.json", toStore.ToString());
            }
            catch (Exception)
            {
                ShowMessageBox("Cannot save database.", "Error");
            }
        }

        private void ShowMessageBox(string text, string caption, bool ignoreCheckBox = false)
        {
            if (InformationCheckBox.IsChecked == true || ignoreCheckBox == true)
            {
                MessageBox.Show(text, caption);
            }
        }

        private void UpdateGames(ICollection<int> ids)
        {
            ProgressBar.Minimum = 0;
            ProgressBar.Maximum = ids.Count;
            ProgressBar.Value = 0;

            UpdateProgressBarDelegate updatePbDelegate = ProgressBar.SetValue;

            foreach (var id in ids)
            {
                try
                {
                    var result = JObject.Parse(_webClient.DownloadString("https://www.g2a.com/marketplace/product/auctions/?id=" + id));
                    _games[id].Price = Math.Round(result["a"].First.First.ToObject<JObject>()["p"].Value<decimal>(), 3,
                        MidpointRounding.AwayFromZero);
                    if (_games[id].MinPrice > _games[id].Price || _games[id].MinPrice == 0)
                    {
                        _games[id].MinPrice = _games[id].Price;
                        _games[id].MinPriceDate = DateTime.Now;
                    }
                    _games[id].LastTimeUpdated = DateTime.Now;
                }
                catch (Exception)
                {
                    _games[id].Price = 0;
                    ShowMessageBox("Game " + _games[id].Name + " doesn't have any offers.", "Information");
                }

                Dispatcher.Invoke(updatePbDelegate, DispatcherPriority.Background, ValueProperty, ++ProgressBar.Value);
            }
        }

        private string checkForUpdates()
        {
            var pageString = _webClient.DownloadString("https://raw.githubusercontent.com/CdTCzech/G2AChecker/master/G2AChecker/VERSION.txt");

            long localVersion;
            long.TryParse(VERSION, out localVersion);
            long webVersion;
            long.TryParse(pageString, out webVersion);

            if (webVersion > localVersion)
            {
                return pageString;
            }
            else
            {
                return "";
            }
        }

        #region Buttons

        private void addGameButton_Click(object sender, RoutedEventArgs e)
        {
            string pageString;
            string gameName;
            int gameId;

            try
            {
                pageString = _webClient.DownloadString(UrlTextBox.Text);
            }
            catch (Exception)
            {
                ShowMessageBox("Bad url or G2A connection.", "Error");
                return;
            }

            var productDataString = "var productData = ";
            int startIndexOfProductData;
            if ((startIndexOfProductData = pageString.IndexOf(productDataString, StringComparison.Ordinal)) != -1)
            {
                startIndexOfProductData += productDataString.Length;
                var endIndexOfProductData = pageString.IndexOf("};", startIndexOfProductData, StringComparison.Ordinal);
                var productData = pageString.Substring(startIndexOfProductData, endIndexOfProductData - startIndexOfProductData).Trim();
                dynamic json = JsonConvert.DeserializeObject(productData + '}');

                try
                {
                    gameName = json.products[0].name;
                    gameId = json.products[0].id;
                }
                catch (Exception)
                {
                    ShowMessageBox("G2A changed API. Cannot parse id.", "Error");
                    return;
                }

                if (!_games.ContainsKey(gameId))
                {
                    decimal price;

                    try
                    {
                        var result =
                            JObject.Parse(_webClient.DownloadString("https://www.g2a.com/marketplace/product/auctions/?id=" + gameId));

                        price = Math.Round(result["a"].First.First.ToObject<JObject>()["p"].Value<decimal>(), 3,
                            MidpointRounding.AwayFromZero);
                    }
                    catch (Exception)
                    {
                        price = 0;
                        ShowMessageBox("Game " + gameName + " doesn't have any offers.", "Information");
                    }

                    _games.Add(gameId,
                        new Game()
                        {
                            Id = gameId,
                            Name = gameName,
                            Price = price,
                            MinPrice = price,
                            MinPriceDate = DateTime.Now,
                            LastTimeUpdated = DateTime.Now,
                            Url = UrlTextBox.Text
                        }
                    );
                }
                else
                {
                    ShowMessageBox("Game already added.", "Error");
                    return;
                }
            }
            else
            {
                ShowMessageBox("Bad url or G2A changed API.", "Error");
                return;
            }

            UrlTextBox.Text = "";
            SaveAndRefresh();
            ShowMessageBox("Game " + gameName + " added.", "Information");
        }

        private void updateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateGames(_games.Keys);

            SaveAndRefresh();
            ShowMessageBox("All games refreshed.", "Information");
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCheckBox.IsChecked = false;
            ++_minutes;
            UpdateTextBox.Text = _minutes.ToString();
        }

        private void DownButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCheckBox.IsChecked = false;
            --_minutes;
            UpdateTextBox.Text = _minutes.ToString();
        }

        private void saveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveDatabase();
            ShowMessageBox("Settings saved to \"db.json\".", "Information");
        }

        private void exportButton_Click(object sender, RoutedEventArgs e)
        {
            string output = "";
            foreach (var game in _games)
            {
                var values = new string[] { game.Value.Name, game.Value.Price.ToString(),
                                            game.Value.MinPrice.ToString(), game.Value.MinPriceDate.ToString(),
                                            game.Value.LastTimeUpdated.ToString()
                };

                output += game.Value.Id.ToString();
                foreach (var value in values)
                {
                    var replaced = value.Replace("\"", "\"\"");
                    output += ",\"" + replaced + "\"";
                }
                output += "\n";
            }
            File.WriteAllText(@"games.csv", output);
            ShowMessageBox("Game informations exported to \"games.csv\".", "Information");
        }

        private void updatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var update = checkForUpdates();
                var firstLine = "New version available!\n";
                var isCurrent = string.IsNullOrEmpty(update);
                if (isCurrent)
                {
                    firstLine = "You have current version.\n";
                }
                var localVersion = VERSION.Substring(0, 4) + '.' + VERSION.Substring(4, 2) +'.' + VERSION.Substring(6, 2) + ' ' + VERSION.Substring(8, 2) + ':' + VERSION.Substring(10, 2);
                string webVersion = "";
                if (!isCurrent)
                {
                    webVersion = "\nNew: " + update.Substring(0, 4) + '.' + update.Substring(4, 2) + '.' + update.Substring(6, 2) + ' ' + update.Substring(8, 2) + ':' + update.Substring(10, 2);
                }
                ShowMessageBox(firstLine + "Current: " + localVersion + webVersion, "Information", true);
            }
            catch (Exception)
            {
                ShowMessageBox("Failed to check updates.", "Error");
            }
        }

        #endregion
        #region CheckBoxes

        private void updateCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _dispatcherTimer.Interval = new TimeSpan(0, _minutes, 0);
            _dispatcherTimer.Start();
        }

        private void updateCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _dispatcherTimer.Stop();
        }
        #endregion
        #region Timers

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            updateButton_Click(new object(), new RoutedEventArgs());
        }
        #endregion
        #region MenuItems

        private void DeleteMenuItem(object sender, RoutedEventArgs e)
        {
            foreach (var id in GetSelectedIds(sender))
            {
                _games.Remove(id);
            }

            SaveAndRefresh();
        }

        private void UpdateMenuItem(object sender, RoutedEventArgs e)
        {
            UpdateGames(GetSelectedIds(sender));

            SaveAndRefresh();
        }

        private void OpenInBrowserMenuItem(object sender, RoutedEventArgs e)
        {
            foreach (var id in GetSelectedIds(sender))
            {
                System.Diagnostics.Process.Start(_games[id].Url);
            }
        }

        private void ResetMenuItem(object sender, RoutedEventArgs e)
        {
            foreach (var id in GetSelectedIds(sender))
            {
                _games[id].MinPrice = _games[id].Price;
                _games[id].MinPriceDate = _games[id].LastTimeUpdated;
            }

            SaveAndRefresh();
        }
        #endregion
        #region TextBoxes

        private void UpdateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (UpdateTextBox.Text.Length == 0)
            {
                _minutes = 5;
                return;
            }

            int minutes;
            if (int.TryParse(UpdateTextBox.Text, out minutes))
            {
                UpdateCheckBox.IsChecked = false;
                _minutes = (minutes > 4) ? minutes : 5;
            }
            else
            {
                UpdateTextBox.Text = _minutes.ToString();
            }
        }

        private void UpdateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateTextBox.Text = _minutes.ToString();
        }
        #endregion

        public void Dispose()
        {
            _webClient.Dispose();
        }
    }
}
