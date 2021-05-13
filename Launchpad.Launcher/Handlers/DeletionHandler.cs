#pragma warning disable SA1515
#pragma warning disable SA1636
//
//  DeletionHandler.cs
//
//  Author:
//       Twisted Jenius LLC
//
//  Copyright (c) 2021 Twisted Jenius LLC
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
#pragma warning restore SA1636
#pragma warning restore SA1515

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Launchpad.Common.Enums;
using Launchpad.Common.Handlers;
using Launchpad.Common.Handlers.Manifest;
using Launchpad.Launcher.Handlers.Protocols;
using Launchpad.Launcher.Handlers.Protocols.Manifest;
using Launchpad.Launcher.Utility;
using NLog;

namespace Launchpad.Launcher.Handlers
{
	/// <summary>
	/// Delete files and folders as needed, base on what mode is set.
	/// </summary>
	public class DeletionHandler
	{
		/// <summary>
		/// Logger instance for this class.
		/// </summary>
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Delete files and folders as needed, base on what mode is set.
		/// </summary>
		/// <param name="fileManifestHandler">The ManifestHandler.</param>
		/// <param name="patch">The ManifestBasedProtocolHandler.</param>
		public void PerformDeletionMode(ManifestHandler fileManifestHandler, ManifestBasedProtocolHandler patch)
		{
			/*
			There are three different deletion modes this can be set to using the values 0, 1, 2 below:

			Mode "0" - Do not delete any files; do nothing. This is the default as well (if set to any other number).

			Mode "1" - Only delete files that are listed within the "DeletedManifest.txt" file. If a file is not listed in there, it will not be deleted.
			It will first attempt to download a "DeletedManifest.txt" file. If a valid one can't be downloaded, no files will be deleted.
			When using this mode, you can create a "DeletedManifest.txt" file by using the "Generate Patch Folder" button.

			Mode "2" - Delete every file that's not specifically listed within the "GameManifest.txt" file. This includes deleting any user created files
			(such as mods), and any files made by the game itself (such as preference/settings files, saved games, and log files)
			that are within the game's folder. If there is no valid "GameManifest.txt", no files will be deleted.

			Both modes 1 and 2 will check if a folder has been made empty after deleting a file from it, and if so, it will delete that now empty folder.
			Files are only checked for and deleted if the ".txt" manifest list file has been updated.

			Order of Operations: Files are deleted after the above mentioned ".txt" files are downloaded, but before any game files are downloaded.
			This means that, when set to mode 1, if a file is listed in both "DeletedManifest.txt" and "GameManifest.txt" (for some reason),
			that file will first be deleted, then downloaded again. Mode 2 will delete any out of date files before they are then updated.
			*/
			// TODO: Maybe deletionMode should use ConfigHandler to possibly override the default value
			int deletionMode = 2;

			if (deletionMode != 1 && deletionMode != 2)
			{
				return;
			}

			Log.Info($"Now deleting files using mode {deletionMode}...");

			// Define the main game folder
			string parentDirectory = DirectoryHelpers.GetLocalGameDirectory();
			EManifestType manifestType;

			if (deletionMode == 1)
			{
				manifestType = EManifestType.Deleted;
				patch.RefreshModuleManifest(EModule.Deleted);
			}
			else
			{
				manifestType = EManifestType.Game;
			}

			// Check if the manifest is valid and lists at least one file/line
			string manifestPath = fileManifestHandler.GetManifestPath(manifestType, false);

			if (!File.Exists(manifestPath))
			{
				return;
			}

			IReadOnlyList<ManifestEntry> manifest;
			manifest = ManifestHandler.LoadManifest(manifestPath);

			if (manifest == null)
			{
				return;
			}

			// TODO: I may want to display status text here (such as "Now deleting old files..."), the text would need translated too
			// TODO: I might want to have this display a progress bar

			if (deletionMode == 1)
			{
				// Loop through all files listed in the manifest and delete each of them
				foreach (var fileEntry in manifest)
				{
					DeleteExcessFile(Path.Combine(parentDirectory, fileEntry.RelativePath));
				}
			}
			else
			{
				// Loop through every file, in every folder for the whole game, starting from parentDirectory
				string[] filenames = Directory.GetFiles(parentDirectory, "*.*", SearchOption.AllDirectories).ToArray();

				foreach (string name in filenames)
				{
					FileInfo file = new FileInfo(name);
					var pathOfFile = file.FullName.ToString();
					var relativeFilePath = pathOfFile.Substring(parentDirectory.Length).TrimStart(Path.DirectorySeparatorChar);

					string hash;
					long fileSize;

					using (var fileStream = File.OpenRead(pathOfFile))
					{
						hash = MD5Handler.GetStreamHash(fileStream);
						fileSize = fileStream.Length;
					}

					// Create an entry for the file to compare it to the one that should already be listed
					var entry = new ManifestEntry
					{
						RelativePath = relativeFilePath,
						Hash = hash,
						Size = fileSize,
					};

					// Check if the file is listed within "GameManifest.txt", delete it if it's not listed
					if (!manifest.Contains(entry))
					{
						DeleteExcessFile(pathOfFile);
					}
				}
			}
		}

		/// <summary>
		/// Delete a give file, then see if the folder(s) it was in now need deleted as well.
		/// </summary>
		private static void DeleteExcessFile(string filepath)
		{
			if (File.Exists(filepath))
			{
				// Log.Info($"Deleting file: \"{filepath}\"");
				File.Delete(filepath);

				// Define the main game folder
				string parentDirectory = DirectoryHelpers.GetLocalGameDirectory();
				string folderPath = Path.GetDirectoryName(filepath);

				// Check if the folder is now empty, if so, delete it
				DirectoryInfo di = new DirectoryInfo(folderPath);

				if (di.GetFiles("*").Length <= 0 && di.GetDirectories().Length <= 0)
				{
					string upper = Directory.GetParent(di.FullName).FullName;

					// Check if the to-be-deleted folder's parent folder is now empty, check recursively until a non-empty folder is found,
					// or we're checking the main game folder
					while (upper != null && upper != parentDirectory && Directory.GetFiles(upper, "*").Length <= 0 && Directory.GetDirectories(upper).Length <= 1)
					{
						di = new DirectoryInfo(upper);
						upper = Directory.GetParent(di.FullName).FullName;
					}

					// Log.Info($"Deleting folder: \"{di}\"");
					di.Delete(true);
				}
			}
		}
	}
}
