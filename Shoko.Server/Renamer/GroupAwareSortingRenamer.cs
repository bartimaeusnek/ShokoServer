using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Plugin.Abstractions;
using Shoko.Plugin.Abstractions.Attributes;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Server.Server;
using Shoko.Server.Utilities;

namespace Shoko.Server.Renamer
{
    [Renamer(RENAMER_ID, Description = "Group Aware Sorter")]
    public class GroupAwareRenamer : IRenamer
    {
        internal const string RENAMER_ID = nameof(GroupAwareRenamer);
        // Defer to whatever else
        public string GetFilename(RenameEventArgs args)
        {
            // Terrible hack to make it forcefully return Legacy Renamer
            var legacy = (IRenamer) ActivatorUtilities.CreateInstance(ShokoServer.ServiceContainer, typeof(LegacyRenamer));
            return legacy.GetFilename(args);
        }

        public (IImportFolder destination, string subfolder) GetDestination(MoveEventArgs args)
        {
            if (args?.EpisodeInfo == null) throw new ArgumentException("File is unrecognized. Not Moving");
            // get the series
            var series = args.AnimeInfo?.FirstOrDefault();

            if (series == null) throw new ArgumentException("Series cannot be found for file");

            // replace the invalid characters
            string name = series.PreferredTitle.ReplaceInvalidPathCharacters();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Series Name is null or empty");

            var group = args.GroupInfo?.FirstOrDefault();
            if (group == null) throw new ArgumentException("Group could not be found for file");
            
            string path;
            if (group.Series.Count == 1)
            {
                path = name;
            }
            else
            {
                var groupName = Utils.ReplaceInvalidFolderNameCharacters(group.Name);
                path = Path.Combine(groupName, name);
            }

            var destFolder = series.Restricted switch
            {
                true => args.AvailableFolders.FirstOrDefault(a => a.Location.Contains("Hentai", StringComparison.InvariantCultureIgnoreCase) && ValidDestinationFolder(a)) ?? args.AvailableFolders.FirstOrDefault(ValidDestinationFolder),
                false => args.AvailableFolders.FirstOrDefault(a => !a.Location.Contains("Hentai", StringComparison.InvariantCultureIgnoreCase) && ValidDestinationFolder(a))
            };

            return (destFolder, path);
        }

        private static bool ValidDestinationFolder(IImportFolder dest) =>
            dest.DropFolderType.HasFlag(DropFolderType.Destination);
    }
}