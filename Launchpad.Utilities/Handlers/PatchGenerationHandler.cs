//
//  PatchGenerationHandler.cs
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

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using Launchpad.Common.Handlers.Manifest;

namespace Launchpad.Utilities.Handlers
{
	public class PatchGenerationHandler
	{
		/// <summary>
		/// If we have both an old and new manifest, make copies of the new/changed files in a new folder.
		/// </summary>
		public void GroupPatchedFiles(string targetDirectory, bool zip)
		{
			var parentDirectory = Directory.GetParent(targetDirectory).ToString();
			string manifestPath = Path.Combine(parentDirectory, $"GameManifest.txt");
			string oldManifestPath = manifestPath + ".old";

			// Check to see if we have valid game manifest files, and if not, check for a launchpad manifest instead
			if (!File.Exists(manifestPath) || !File.Exists(oldManifestPath))
			{
				manifestPath = Path.Combine(parentDirectory, $"LaunchpadManifest.txt");
				oldManifestPath = manifestPath + ".old";

				if (!File.Exists(manifestPath) || !File.Exists(oldManifestPath))
				{
					return;
				}
			}

			IReadOnlyList<ManifestEntry> manifest;
			IReadOnlyList<ManifestEntry> oldManifest;
			manifest = ManifestHandler.LoadManifest(manifestPath);
			oldManifest = ManifestHandler.LoadManifest(oldManifestPath);

			if (manifest == null || oldManifest == null)
			{
				return;
			}

			var folderPath = Path.GetFullPath(Path.Combine(parentDirectory, "patch"));
			bool changes = false;

			foreach (var fileEntry in manifest)
			{
				if (!oldManifest.Contains(fileEntry))
				{
					var sourcePath = Path.GetFullPath(Path.Combine(targetDirectory, fileEntry.RelativePath));
					var destPath = Path.GetFullPath(Path.Combine(folderPath, fileEntry.RelativePath));

					// Check to make sure that the relative directory it's trying to copy to actually exists, and if not, create it
					if (!Directory.Exists(Path.GetDirectoryName(destPath)))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(destPath));
					}

					File.Copy(sourcePath, destPath, true);
					changes = true;
				}
			}
			
			// Make a list of any newly deleted files
			var filesDeleted = new List<String>();
			foreach (var oldEntry in oldManifest)
			{
				var matchingEntry =
					manifest.FirstOrDefault(fileEntry => fileEntry.RelativePath == oldEntry.RelativePath);

				// If there is no new entry which matches the old one, then the file was deleted.
				if (matchingEntry == null)
				{
					filesDeleted.Add(oldEntry.RelativePath);
				}
			}

			if (filesDeleted.Capacity > 0)
			{
				File.WriteAllText(Path.GetFullPath(Path.Combine(parentDirectory, "deleted_file_list.txt")), string.Join( "\n", filesDeleted));
			}

			if (zip && changes == true)
			{
				var patchFile = Path.GetFullPath(Path.Combine(parentDirectory, "patch.zip"));

				if (File.Exists(patchFile))
				{
					File.Delete(patchFile);
				}

				// Create a zip with the patched files
				ZipFile.CreateFromDirectory(folderPath, patchFile);

				// Delete folderPath and all of its sub-folders/files after it has been zipped
				System.IO.DirectoryInfo di = new DirectoryInfo(folderPath);

				foreach (FileInfo file in di.GetFiles())
				{
					file.Delete(); 
				}

				foreach (DirectoryInfo dir in di.GetDirectories())
				{
					dir.Delete(true); 
				}

				Directory.Delete(folderPath);
			}

			return;
		}
	}
}

