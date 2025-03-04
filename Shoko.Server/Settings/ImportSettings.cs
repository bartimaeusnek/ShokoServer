using System.Collections.Generic;
using Shoko.Models.Enums;

namespace Shoko.Server.Settings
{
    public class ImportSettings
    {
        
        public List<string> VideoExtensions { get; set; } = new List<string> { "MKV", "AVI", "MP4", "MOV", "OGM", "WMV", "MPG", "MPEG", "MK3D", "M4V" };

        public List<string> Exclude { get; set; } = new List<string> { @"[\\\/]\$RECYCLE\.BIN[\\\/]", @"[\\\/]\.Recycle\.Bin[\\\/]", @"[\\\/]\.Trash-\d+[\\\/]" };

        public RenamingLanguage DefaultSeriesLanguage { get; set; } = RenamingLanguage.Romaji;

        public RenamingLanguage DefaultEpisodeLanguage { get; set; } = RenamingLanguage.Romaji;

        public bool RunOnStart { get; set; } = false;

        public bool ScanDropFoldersOnStart { get; set; } = false;

        public bool Hash_CRC32 { get; set; } = false;
        public bool Hash_MD5 { get; set; } = false;
        public bool Hash_SHA1 { get; set; } = false;

        public bool UseExistingFileWatchedStatus { get; set; } = true;

        public bool AutomaticallyDeleteDuplicatesOnImport { get; set; } = false;

        public bool FileLockChecking { get; set; } = true;

        public bool AggressiveFileLockChecking { get; set; } = true;

        public int FileLockWaitTimeMS { get; set; } = 4000;

        public int AggressiveFileLockWaitTimeSeconds { get; set; } = 8;
        
        public bool SkipDiskSpaceChecks { get; set; }
        
        public bool RenameThenMove { get; set; }

        public bool RenameOnImport { get; set; } = false;
        public bool MoveOnImport { get; set; } = false;

        public string MediaInfoPath { get; set; }

        public int MediaInfoTimeoutMinutes { get; set; } = 5;
    }
}
