﻿using Newtonsoft.Json;
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
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : IDisposable
	{
		private WebClient webClient = new WebClient();
		private DispatcherTimer dispatcherTimer = new DispatcherTimer();
		private Dictionary<int, Game> m_games = new Dictionary<int, Game>();

		public MainWindow()
		{
			InitializeComponent();
			try
			{
				var gamesJson = (JObject.Parse(File.ReadAllText(@"db.json"))["games"]).ToString();
				var games = JsonConvert.DeserializeObject<List<Game>>(gamesJson);
				m_games = games.ToDictionary(g => g.Id, g => g);
			}
			catch (Exception)
			{
				ShowMessageBox("Creating new database.", "Information");
			}

			GamesDataGrid.IsReadOnly = true;
			dispatcherTimer.Tick += dispatcherTimer_Tick;
			dispatcherTimer.Interval = new TimeSpan(1, 0, 0);
		}

		private void dispatcherTimer_Tick(object sender, EventArgs e)
		{
			updateButton_Click(new object(), new RoutedEventArgs());
		}

		private void addGameButton_Click(object sender, RoutedEventArgs e)
		{
			string pageString;
			string gameName;

			try
			{
				pageString = webClient.DownloadString(UrlTextBox.Text);
			}
			catch (Exception)
			{
				ShowMessageBox("Bad url or G2A connection.", "Error");
				return;
			}

			int index;
			if ((index = pageString.IndexOf("productForSarex = ", StringComparison.Ordinal)) != -1)
			{
				var endIndex = pageString.IndexOf(";", index, StringComparison.Ordinal);
				var gameJson = pageString.Substring(index + 18, endIndex - (index + 18));

				var parsedGameJson = JObject.Parse(gameJson);
				var gameId = parsedGameJson["_product"]["id"].Value<int>();
				gameName = parsedGameJson["_product"]["name"].ToString();

				if (!m_games.Keys.Contains(gameId))
				{
					decimal price;

					try
					{
						var result =
							JObject.Parse(webClient.DownloadString("https://www.g2a.com/marketplace/product/auctions/?id=" + gameId));

						price = Math.Round(result["a"].First.First.ToObject<JObject>()["p"].Value<decimal>(), 3,
							MidpointRounding.AwayFromZero);
					}
					catch (Exception)
					{
						price = 0;
						ShowMessageBox("Game " + gameName + " doesn't have any offers.", "Information");
					}

					m_games.Add(gameId,
						new Game()
						{
							Id = gameId,
							Name = gameName,
							Price = price,
							MinPrice = price,
							MinPriceDate = DateTime.Now,
							Url = UrlTextBox.Text
						}
					);
					SaveDatabase();
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

			GamesDataGrid.Items.Refresh();
			ShowMessageBox("Game " + gameName + " added.", "Information");
		}

		private delegate void UpdateProgressBarDelegate(DependencyProperty dp, object value);

		private void updateButton_Click(object sender, RoutedEventArgs e)
		{
			UpdateGames(m_games.Keys);

			SaveDatabase();
			GamesDataGrid.Items.Refresh();
			ShowMessageBox("All games refreshed.", "Information");
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
					var result = JObject.Parse(webClient.DownloadString("https://www.g2a.com/marketplace/product/auctions/?id=" + id));
					m_games[id].Price = Math.Round(result["a"].First.First.ToObject<JObject>()["p"].Value<decimal>(), 3,
						MidpointRounding.AwayFromZero);
					if (m_games[id].MinPrice > m_games[id].Price || m_games[id].MinPrice == 0)
					{
						m_games[id].MinPrice = m_games[id].Price;
						m_games[id].MinPriceDate = DateTime.Now;
					}
				}
				catch (Exception)
				{
					m_games[id].Price = 0;
					ShowMessageBox("Game " + m_games[id].Name + " doesn't have any offers.", "Information");
				}

				Dispatcher.Invoke(updatePbDelegate, DispatcherPriority.Background, ValueProperty, ++ProgressBar.Value);
			}
		}

		private ICollection<int> GetSelectedIds(object sender)
		{
			var ids = new List<int>();
			var item = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget as DataGrid;
			var selectedGames = item?.SelectedItems;

			if (selectedGames == null) return ids;
			ids.AddRange(from Game game in selectedGames where game != null && m_games.ContainsKey(game.Id) select game.Id);

			return ids;
		}

		private void DeleteMenuItem(object sender, RoutedEventArgs e)
		{
			foreach (var id in GetSelectedIds(sender))
			{
				m_games.Remove(id);
			}

			SaveDatabase();
			GamesDataGrid.Items.Refresh();
		}

		private void UpdateMenuItem(object sender, RoutedEventArgs e)
		{
			UpdateGames(GetSelectedIds(sender));

			SaveDatabase();
			GamesDataGrid.Items.Refresh();
		}

		private void OpenInBrowserMenuItem(object sender, RoutedEventArgs e)
		{
			foreach (var id in GetSelectedIds(sender))
			{
				System.Diagnostics.Process.Start(m_games[id].Url);
			}
		}

		private void SaveDatabase()
		{
			var toStore = new JObject
					{
						{
							"games", JToken.FromObject(m_games.Values)
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

		private void updateCheckBox_Checked(object sender, RoutedEventArgs e)
		{
			int minutes;
			if (int.TryParse(UpdateTextBox.Text, out minutes))
			{
				dispatcherTimer.Interval = new TimeSpan(0, minutes, 0);
				dispatcherTimer.Start();
			}
			else
			{
				UpdateTextBox.Text = "INVALID";
				UpdateCheckBox.IsChecked = false;
			}
			
		}

		private void updateCheckBox_Unchecked(object sender, RoutedEventArgs e)
		{
			dispatcherTimer.Stop();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			var gameViewSource = ((System.Windows.Data.CollectionViewSource)(FindResource("gameViewSource")));
			gameViewSource.Source = m_games.Values;
		}

		private void ShowMessageBox(string text, string caption)
		{
			if (InformationCheckBox.IsChecked == true)
			{
				MessageBox.Show(text, caption);
			}
		}

		public void Dispose()
		{
			((IDisposable)webClient).Dispose();
		}

		private void UpdateTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			UpdateCheckBox.IsChecked = false;
		}
	}
}
