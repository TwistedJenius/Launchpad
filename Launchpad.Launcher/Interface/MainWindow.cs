//
//  MainWindow.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Timers;
using Gdk;
using GLib;
using Gtk;

using Launchpad.Common;
using Launchpad.Launcher.Configuration;
using Launchpad.Launcher.Handlers;
using Launchpad.Launcher.Handlers.Protocols;
using Launchpad.Launcher.Services;
using Launchpad.Launcher.Utility;
using Launchpad.Launcher.Utility.Enums;

using NGettext;
using NLog;
using SixLabors.ImageSharp;
using Application = Gtk.Application;
using Process = System.Diagnostics.Process;
using Task = System.Threading.Tasks.Task;

namespace Launchpad.Launcher.Interface
{
	/// <summary>
	/// The main UI class for Launchpad. This class acts as a manager for all threaded
	/// actions, such as installing, updating or repairing the game.
	/// </summary>
	public sealed partial class MainWindow : Gtk.Window
	{
		/// <summary>
		/// Logger instance for this class.
		/// </summary>
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		private readonly LocalVersionService LocalVersionService = new LocalVersionService();

		/// <summary>
		/// The configuration instance reference.
		/// </summary>
		private readonly ILaunchpadConfiguration Configuration = ConfigHandler.Instance.Configuration;

		/// <summary>
		/// The checks handler reference.
		/// </summary>
		private readonly ChecksHandler Checks = new ChecksHandler();

		/// <summary>
		/// The launcher handler. Allows updating the launcher and loading the changelog.
		/// </summary>
		private readonly LauncherHandler Launcher = new LauncherHandler();

		/// <summary>
		/// The game handler. Allows updating, installing and repairing the game.
		/// </summary>
		private readonly GameHandler Game = new GameHandler();

		private readonly TagfileService TagfileService = new TagfileService();

		/// <summary>
		/// The current mode that the launcher is in. Determines what the primary button does when pressed.
		/// </summary>
		private ELauncherMode Mode = ELauncherMode.Inactive;

		/// <summary>
		/// The localization catalog.
		/// </summary>
		private static readonly ICatalog LocalizationCatalog = new Catalog("Launchpad", "./Content/locale");

		/// <summary>
		/// Whether or not the launcher UI has been initialized.
		/// </summary>
		private bool IsInitialized;

		/// <summary>
		/// Whether or not we should launch the game after downloading.
		/// </summary>
		private bool ShouldLaunchGame;

		/// <summary>
		/// Initializes a new instance of the <see cref="MainWindow"/> class.
		/// </summary>
		/// <param name="builder">The UI builder.</param>
		/// <param name="handle">The native handle of the window.</param>
		private MainWindow(Builder builder, IntPtr handle)
			: base(handle)
		{
			builder.Autoconnect(this);

			BindUIEvents();

			// Bind the handler events
			this.Game.ProgressChanged += OnModuleInstallationProgressChanged;
			this.Game.DownloadFinished += OnGameDownloadFinished;
			this.Game.DownloadFailed += OnGameDownloadFailed;
			this.Game.LaunchFailed += OnGameLaunchFailed;
			this.Game.GameExited += OnGameExited;

			this.Launcher.LauncherDownloadProgressChanged += OnModuleInstallationProgressChanged;
			this.Launcher.LauncherDownloadFinished += OnLauncherDownloadFinished;

			// Set the initial launcher mode
			SetLauncherMode(ELauncherMode.Inactive, false);

			// Set the window title
			this.Title = LocalizationCatalog.GetString("Launchpad - {0}", this.Configuration.GameName);
			this.StatusLabel.Text = LocalizationCatalog.GetString("Idle");

			// Set locales
			this.MenuHelpItem.Label = LocalizationCatalog.GetString("Help");
			this.MenuToolsItem.Label = LocalizationCatalog.GetString("Tools");
			this.MenuRepairItem.Label = LocalizationCatalog.GetString("Repair");
			this.MenuReinstallItem.Label = LocalizationCatalog.GetString("Reinstall");
		}

		/// <summary>
		/// Initializes the UI of the launcher, performing varying checks against the patching server.
		/// </summary>
		/// <returns>A task that must be awaited.</returns>
		public Task InitializeAsync()
		{
			if (this.IsInitialized)
			{
				return Task.CompletedTask;
			}

			// this.StatusLabel.Text = LocalizationCatalog.GetString("Checking for updates...");

			if (this.Checks.CanPatch())
			{
				LoadBanner();
				LoadChangelog();
			}

			Timer postInitDelay = new Timer(50);
			postInitDelay.Elapsed += PostInitEvent;
			postInitDelay.AutoReset = false;
			postInitDelay.Start();

			this.IsInitialized = true;
			return Task.CompletedTask;
		}

		/// <summary>
		/// Perform post init checks.
		/// </summary>
		private void PostInitEvent(object sender, ElapsedEventArgs e)
		{
			// First of all, check if we can connect to the patching service.
			if (!this.Checks.CanPatch() || !this.Checks.IsPlatformAvailable(this.Configuration.SystemTarget))
			{
				using (var dialog = new MessageDialog
				(
					this,
					DialogFlags.Modal,
					MessageType.Warning,
					ButtonsType.Ok,
					LocalizationCatalog.GetString("Failed to connect to the patch server. Please check your settings.")
				))
				{
					dialog.Run();
				}

				this.StatusLabel.Text = LocalizationCatalog.GetString("Could not connect to server.");
				this.MenuRepairItem.Sensitive = false;

				// If we cannot connect to the server but the game is installed, launch it anyway
				if (this.Checks.IsGameInstalled())
				{
					this.ShouldLaunchGame = true;
					SetLauncherMode(ELauncherMode.Launch, false);
					ExecuteMainAction();

					return;
				}
				else
				{
					Application.Quit();
				}
			}
			else
			{
				// If we can connect, proceed with the rest of our checks.
				if (ChecksHandler.IsInitialStartup() && !this.Checks.IsGameInstalled())
				{
					Log.Info("This instance is the first start of the application in this folder.");
					this.TagfileService.CreateLauncherTagfile();
					this.ShouldLaunchGame = true;
					SetLauncherMode(ELauncherMode.Install, false);
					ExecuteMainAction();

					return;
				}

				// If for some reason the game is not installed just install it again
				if (!this.Checks.IsGameInstalled())
				{
					this.ShouldLaunchGame = true;
					SetLauncherMode(ELauncherMode.Install, false);
					ExecuteMainAction();

					return;
				}

				if (this.Checks.IsLauncherOutdated())
				{
					// The launcher was outdated.
					Log.Info($"The launcher is outdated. \n\tLocal version: {this.LocalVersionService.GetLocalLauncherVersion()}");
					SetLauncherMode(ELauncherMode.Update, false);
					ExecuteMainAction();

					return;
				}

				if (this.Checks.IsGameOutdated())
				{
					// If it does, offer to update it
					Log.Info("The game is outdated.");
					this.ShouldLaunchGame = true;
					SetLauncherMode(ELauncherMode.Update, false);
					ExecuteMainAction();

					return;
				}

				// All checks passed, so we can simply launch the game.
				Log.Info("All checks passed. Game can be launched.");
				SetLauncherMode(ELauncherMode.Launch, false);
				ExecuteMainAction();
			}

			return;
		}

		private void LoadChangelog()
		{
			var protocol = PatchProtocolProvider.GetHandler();
			var markup = protocol.GetChangelogMarkup();

			// Preprocess dot lists
			var dotRegex = new Regex("(?<=^\\s+)\\*", RegexOptions.Multiline);
			markup = dotRegex.Replace(markup, "•");

			// Preprocess line breaks
			var regex = new Regex("(?<!\n)\n(?!\n)(?!  )");
			markup = regex.Replace(markup, string.Empty);

			var startIter = this.ChangelogTextView.Buffer.StartIter;
			this.ChangelogTextView.Buffer.InsertMarkup(ref startIter, markup);
		}

		private void LoadBanner()
		{
			var patchHandler = PatchProtocolProvider.GetHandler();

			// Load the game banner (if there is one)
			if (!patchHandler.CanProvideBanner())
			{
				return;
			}

			Task.Factory.StartNew
			(
				() =>
				{
					// Fetch the banner from the server
					var bannerImage = patchHandler.GetBanner();

					bannerImage.Mutate(i => i.Resize(this.BannerImage.AllocatedWidth, 0));

					// Load the image into a pixel buffer
					return new Pixbuf
					(
						Bytes.NewStatic(bannerImage.SavePixelData()),
						Colorspace.Rgb,
						true,
						8,
						bannerImage.Width,
						bannerImage.Height,
						4 * bannerImage.Width
					);
				}
			)
			.ContinueWith
			(
				async bannerTask => this.BannerImage.Pixbuf = await bannerTask
			);
		}

		/// <summary>
		/// Sets the launcher mode and updates UI elements to match.
		/// </summary>
		/// <param name="newMode">The new mode.</param>
		/// <param name="isInProgress">If set to <c>true</c>, the selected mode is in progress.</param>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Will be thrown if the <see cref="ELauncherMode"/> passed to the function is not a valid value.
		/// </exception>
		private void SetLauncherMode(ELauncherMode newMode, bool isInProgress)
		{
			// Set the global launcher mode
			this.Mode = newMode;

			// Set the UI elements to match
			switch (newMode)
			{
				case ELauncherMode.Install:
				{
					if (isInProgress)
					{
						this.MainButton.Sensitive = false;
						this.MainButton.Label = LocalizationCatalog.GetString("Installing...");
					}
					else
					{
						this.MainButton.Sensitive = true;
						this.MainButton.Label = LocalizationCatalog.GetString("Install");
					}
					break;
				}
				case ELauncherMode.Update:
				{
					if (isInProgress)
					{
						this.MainButton.Sensitive = false;
						this.MainButton.Label = LocalizationCatalog.GetString("Updating...");
					}
					else
					{
						this.MainButton.Sensitive = true;
						this.MainButton.Label = LocalizationCatalog.GetString("Update");
					}
					break;
				}
				case ELauncherMode.Repair:
				{
					if (isInProgress)
					{
						this.MainButton.Sensitive = false;
						this.MainButton.Label = LocalizationCatalog.GetString("Repairing...");
					}
					else
					{
						this.MainButton.Sensitive = true;
						this.MainButton.Label = LocalizationCatalog.GetString("Repair");
					}
					break;
				}
				case ELauncherMode.Launch:
				{
					if (isInProgress)
					{
						this.MainButton.Sensitive = false;
						this.MainButton.Label = LocalizationCatalog.GetString("Launching...");
					}
					else
					{
						this.MainButton.Sensitive = true;
						this.MainButton.Label = LocalizationCatalog.GetString("Launch");
					}
					break;
				}
				case ELauncherMode.Inactive:
				{
					this.MenuRepairItem.Sensitive = false;

					this.MainButton.Sensitive = false;
					this.MainButton.Label = LocalizationCatalog.GetString("Inactive");
					break;
				}
				default:
				{
					throw new ArgumentOutOfRangeException(nameof(newMode), "An invalid launcher mode was passed to SetLauncherMode.");
				}
			}

			if (isInProgress)
			{
				this.MenuRepairItem.Sensitive = false;
				this.MenuReinstallItem.Sensitive = false;
			}
			else
			{
				this.MenuRepairItem.Sensitive = true;
				this.MenuReinstallItem.Sensitive = true;
			}
		}

		/// <summary>
		/// Executes the main action depending on state.
		/// </summary>
		private void ExecuteMainAction()
		{
			// else, run the relevant function
			switch (this.Mode)
			{
				case ELauncherMode.Repair:
				{
					// Repair the game asynchronously
					SetLauncherMode(ELauncherMode.Repair, true);
					this.Game.VerifyGame();

					break;
				}
				case ELauncherMode.Install:
				{
					// Install the game asynchronously
					SetLauncherMode(ELauncherMode.Install, true);
					this.Game.InstallGame();

					break;
				}
				case ELauncherMode.Update:
				{
					if (this.Checks.IsLauncherOutdated())
					{
						// Update the launcher asynchronously
						SetLauncherMode(ELauncherMode.Update, true);
						this.Launcher.UpdateLauncher();
					}
					else
					{
						// Update the game asynchronously
						SetLauncherMode(ELauncherMode.Update, true);
						this.Game.UpdateGame();
					}

					break;
				}
				case ELauncherMode.Launch:
				{
					SetLauncherMode(ELauncherMode.Launch, true);

					Timer launchDelay = new Timer(1000);
					launchDelay.Elapsed += LaunchEvent;
					launchDelay.AutoReset = false;
					launchDelay.Start();
					break;
				}
				default:
				{
					Log.Warn("Trying to execute an action in an invalid active mode.");
					break;
				}
			}
		}

		/// <summary>
		/// Launches the game.
		/// </summary>
		private void LaunchEvent(object sender, ElapsedEventArgs e)
		{
			if (this.Game.LaunchGame() == true)
			{
				Application.Quit();
			}
		}

		/// <summary>
		/// Runs a game repair, no matter what the state the installation is in.
		/// </summary>
		/// <param name="sender">Sender.</param>
		/// <param name="e">E.</param>
		private void OnMenuRepairItemActivated(object sender, EventArgs e)
		{
			SetLauncherMode(ELauncherMode.Repair, false);

			// Simulate a button press from the user.
			this.MainButton.Click();
		}

		/// <summary>
		/// Handles switching between different functionality depending on what is visible on the button to the user,
		/// such as installing, updating, repairing, and launching.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Empty arguments.</param>
		private void OnMainButtonClicked(object sender, EventArgs e)
		{
			// Drop out if the current platform isn't available on the server
			if (!this.Checks.IsPlatformAvailable(this.Configuration.SystemTarget))
			{
				this.StatusLabel.Text =
					LocalizationCatalog.GetString("The server does not provide the game for the selected platform.");
				this.MainProgressBar.Text = string.Empty;

				Log.Info
				(
					$"The server does not provide files for platform \"{PlatformHelpers.GetCurrentPlatform()}\". " +
					"A .provides file must be present in the platforms' root directory."
				);

				SetLauncherMode(ELauncherMode.Inactive, false);

				return;
			}

			// else, run the relevant function
			switch (this.Mode)
			{
				case ELauncherMode.Repair:
				{
					// Repair the game asynchronously
					SetLauncherMode(ELauncherMode.Repair, true);
					this.Game.VerifyGame();

					break;
				}
				case ELauncherMode.Install:
				{
					// Install the game asynchronously
					SetLauncherMode(ELauncherMode.Install, true);
					this.Game.InstallGame();

					break;
				}
				case ELauncherMode.Update:
				{
					if (this.Checks.IsLauncherOutdated())
					{
						// Update the launcher asynchronously
						SetLauncherMode(ELauncherMode.Update, true);
						this.Launcher.UpdateLauncher();
					}
					else
					{
						// Update the game asynchronously
						SetLauncherMode(ELauncherMode.Update, true);
						this.Game.UpdateGame();
					}

					break;
				}
				case ELauncherMode.Launch:
				{
					this.StatusLabel.Text = LocalizationCatalog.GetString("Idle");
					this.MainProgressBar.Text = string.Empty;

					SetLauncherMode(ELauncherMode.Launch, true);
					this.Game.LaunchGame();

					break;
				}
				default:
				{
					Log.Warn("The main button was pressed with an invalid active mode. No functionality has been defined for this mode.");
					break;
				}
			}
		}

		/// <summary>
		/// Starts the launcher update process when its files have finished downloading.
		/// </summary>
		private static void OnLauncherDownloadFinished(object sender, EventArgs e)
		{
			Application.Invoke((o, args) =>
			{
				ProcessStartInfo script = LauncherHandler.CreateUpdateScript();
				Process.Start(script);

				Application.Quit();
			});
		}

		/// <summary>
		/// Warns the user when the game fails to launch, and offers to attempt a repair.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Empty event args.</param>
		private void OnGameLaunchFailed(object sender, EventArgs e)
		{
			Application.Invoke((o, args) =>
			{
				this.StatusLabel.Text = LocalizationCatalog.GetString("The game failed to launch. Try repairing the installation.");
				this.MainProgressBar.Text = string.Empty;

				SetLauncherMode(ELauncherMode.Repair, false);
				ExecuteMainAction();
			});
		}

		/// <summary>
		/// Provides alternatives when the game fails to download, either through an update or through an installation.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the type of failure that occurred.</param>
		private void OnGameDownloadFailed(object sender, EventArgs e)
		{
			Application.Invoke((o, args) =>
			{
				switch (this.Mode)
				{
					case ELauncherMode.Install:
					{
						using (var failDialog = new MessageDialog
						(
							this,
							DialogFlags.Modal,
							MessageType.Question,
							ButtonsType.YesNo,
							// LocalizationCatalog.GetString("Installation failed. Would you like to retry?")
							LocalizationCatalog.GetString("The game failed to launch. Try repairing the installation.")
						))
						{
							if (failDialog.Run() == (int)ResponseType.Yes)
							{
								SetLauncherMode(ELauncherMode.Install, false);
								ExecuteMainAction();
							}
							else
							{
								Application.Quit();
							}
						}
						break;
					}

					case ELauncherMode.Update:
					{
						using (var failDialog = new Dialog
						(
							string.Empty,
							this,
							DialogFlags.Modal
						))
						{
							// Label dlgText = new Label(LocalizationCatalog.GetString("Update failed. You can retry or launch the local version."));
							Label dlgText = new Label(LocalizationCatalog.GetString("Failed to connect to the patch server. Please check your settings."));
							Alignment container = new Alignment(0.5f, 0.5f, 1, 1);
							container.Add(dlgText);
							container.TopPadding = 25;
							failDialog.Resizable = false;
							failDialog.SetPosition(WindowPosition.CenterOnParent);
							failDialog.ContentArea.Add(container);
							failDialog.ContentArea.SetSizeRequest(360, 100);
							failDialog.ContentArea.CenterWidget = container;
							failDialog.AddButton("Launch", 1);
							failDialog.AddButton("Retry", 2);
							failDialog.AddButton("Quit", 3);
							failDialog.ActionArea.LayoutStyle = ButtonBoxStyle.Expand;
							failDialog.DefaultResponse = (ResponseType)1;

							failDialog.ShowAll();
							var dlgRes = failDialog.Run();
							switch (dlgRes)
							{
								case 1:
								{
									SetLauncherMode(ELauncherMode.Launch, false);
									ExecuteMainAction();
									break;
								}
								case 2:
								{
									SetLauncherMode(ELauncherMode.Update, false);
									ExecuteMainAction();
									break;
								}
								case 3:
								case (int)ResponseType.DeleteEvent:
								{
									Application.Quit();
									break;
								}
							}
						}
						break;
					}

					case ELauncherMode.Repair:
					{
						// Set the mode to the same as it was, but no longer in progress.
						// The modes which fall to this case are all capable of repairing an incomplete or
						// broken install on their own.
						SetLauncherMode(this.Mode, false);
						break;
					}

					default:
					{
						// Other cases (such as Launch) will go to the default mode of Repair.
						SetLauncherMode(ELauncherMode.Repair, false);
						break;
					}
				}
			});
		}

		/// <summary>
		/// Updates the progress bar and progress label during installations, repairs and updates.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the progress values and current filename.</param>
		private void OnModuleInstallationProgressChanged(object sender, ModuleProgressChangedArgs e)
		{
			Application.Invoke((o, args) =>
			{
				this.MainProgressBar.Text = e.ProgressBarMessage;
				this.StatusLabel.Text = e.IndicatorLabelMessage;
				this.MainProgressBar.Fraction = e.ProgressFraction;
			});
		}

		/// <summary>
		/// Allows the user to launch or repair the game once installation finishes.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the result of the download.</param>
		private void OnGameDownloadFinished(object sender, EventArgs e)
		{
			Application.Invoke((o, args) =>
			{
				this.StatusLabel.Text = LocalizationCatalog.GetString("Idle");

				switch (this.Mode)
				{
					case ELauncherMode.Install:
					{
						this.MainProgressBar.Text = LocalizationCatalog.GetString("Installation finished");
						break;
					}
					case ELauncherMode.Update:
					{
						this.MainProgressBar.Text = LocalizationCatalog.GetString("Update finished");
						break;
					}
					case ELauncherMode.Repair:
					{
						this.MainProgressBar.Text = LocalizationCatalog.GetString("Repair finished");
						this.ShouldLaunchGame = true;
						break;
					}
					default:
					{
						this.MainProgressBar.Text = string.Empty;
						break;
					}
				}

				if (this.ShouldLaunchGame == true)
				{
					SetLauncherMode(ELauncherMode.Launch, false);
					ExecuteMainAction();
				}
			});
		}

		/// <summary>
		/// Handles offering of repairing the game to the user should the game exit
		/// with a bad exit code.
		/// </summary>
		private void OnGameExited(object sender, int exitCode)
		{
			Application.Invoke((o, args) =>
			{
				if (exitCode != 0)
				{
					using (var crashDialog = new MessageDialog
					(
						this,
						DialogFlags.Modal,
						MessageType.Question,
						ButtonsType.YesNo,
						LocalizationCatalog.GetString
						(
							"Whoops! The game appears to have crashed.\n" +
							"Would you like the launcher to verify the installation?"
						)
					))
					{
						if (crashDialog.Run() == (int)ResponseType.Yes)
						{
							SetLauncherMode(ELauncherMode.Repair, false);
							this.MainButton.Click();
						}
						else
						{
							SetLauncherMode(ELauncherMode.Launch, false);
						}
						crashDialog.Dispose();
					}
				}
				else
				{
					SetLauncherMode(ELauncherMode.Launch, false);
				}
			});
		}

		/// <summary>
		/// Handles starting of a reinstallation procedure as requested by the user.
		/// </summary>
		private void OnReinstallGameActionActivated(object sender, EventArgs e)
		{
			using (var reinstallConfirmDialog = new MessageDialog
			(
				this,
				DialogFlags.Modal,
				MessageType.Question,
				ButtonsType.YesNo,
				LocalizationCatalog.GetString
				(
					"Reinstalling the game will delete all local files and download the entire game again.\n" +
					"Are you sure you want to reinstall the game?"
				)
			))
			{
				if (reinstallConfirmDialog.Run() == (int)ResponseType.Yes)
				{
					SetLauncherMode(ELauncherMode.Install, true);
					this.Game.ReinstallGame();
				}
				reinstallConfirmDialog.Dispose();
			}
		}
	}
}
