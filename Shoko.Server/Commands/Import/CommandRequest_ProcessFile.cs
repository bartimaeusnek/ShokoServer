﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Commons.Extensions;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Commands.AniDB;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Generic;
using Shoko.Server.Models;
using Shoko.Server.Providers.AniDB;
using Shoko.Server.Repositories;
using Shoko.Server.Server;
using Shoko.Server.Settings;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.ProcessFile)]
    public class CommandRequest_ProcessFile : CommandRequestImplementation
    {
        private readonly ICommandRequestFactory _commandFactory;
        public int VideoLocalID { get; set; }
        public bool ForceAniDB { get; set; }
        
        public bool SkipMyList { get; set; }

        private SVR_VideoLocal vlocal;

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority3;

        public override QueueStateStruct PrettyDescription
        {
            get
            {
                if (vlocal != null)
                    return new QueueStateStruct
                    {
                        message = "Getting file info from UDP API: {0}",
                        queueState = QueueStateEnum.FileInfo,
                        extraParams = new[] {vlocal.FileName}
                    };
                return new QueueStateStruct
                {
                    message = "Getting file info from UDP API: {0}",
                    queueState = QueueStateEnum.FileInfo,
                    extraParams = new[] {VideoLocalID.ToString()}
                };
            }
        }

        protected override void Process()
        {
            Logger.LogTrace("Processing File: {VideoLocalID}", VideoLocalID);

            try
            {
                if (vlocal == null) vlocal = RepoFactory.VideoLocal.GetByID(VideoLocalID);
                if (vlocal == null) return;

                //now that we have all the has info, we can get the AniDB Info
                var aniFile = ProcessFile_AniDB(vlocal);
                vlocal.Places.ForEach(a => { a.RenameAndMoveAsRequired(); });

                if (aniFile != null) ShokoEventHandler.Instance.OnFileMatched(vlocal.GetBestVideoLocalPlace());
            }
            catch (Exception ex)
            {
                Logger.LogError("Error processing CommandRequest_ProcessFile: {VideoLocalID} - {Ex}", VideoLocalID, ex);
            }
        }

        private SVR_AniDB_File ProcessFile_AniDB(SVR_VideoLocal vidLocal)
        {
            Logger.LogTrace("Checking for AniDB_File record for: {VidLocalHash} --- {VidLocalFileName}", vidLocal.Hash, vidLocal.FileName);
            // check if we already have this AniDB_File info in the database

            lock (vidLocal)
            {
                SVR_AniDB_File aniFile = null;

                if (!ForceAniDB)
                {
                    aniFile = RepoFactory.AniDB_File.GetByHashAndFileSize(vidLocal.Hash, vlocal.FileSize);

                    if (aniFile == null)
                        Logger.LogTrace("AniDB_File record not found");
                }

                // If cross refs were wiped, but the AniDB_File was not, we unfortunately need to requery the info
                var crossRefs = RepoFactory.CrossRef_File_Episode.GetByHash(vidLocal.Hash);
                if (crossRefs == null || crossRefs.Count == 0) aniFile = null;

                var animeIDs = new Dictionary<int, bool>();

                if (aniFile == null || aniFile.FileSize != vlocal.FileSize) aniFile = TryGetAniDBFileFromAniDB(vidLocal, animeIDs);

                // if we still haven't got the AniDB_File Info we try the web cache or local records
                if (aniFile == null)
                {
                    // check if we have any records from previous imports
                    crossRefs = RepoFactory.CrossRef_File_Episode.GetByHash(vidLocal.Hash);
                    
                    // stop processing if xrefs don't exist
                    if (crossRefs == null || crossRefs.Count == 0) return null;

                    // we assume that all episodes belong to the same anime
                    foreach (var xref in crossRefs)
                    {
                        var ep = RepoFactory.AniDB_Episode.GetByEpisodeID(xref.EpisodeID);
                        if (animeIDs.ContainsKey(xref.AnimeID)) animeIDs[xref.AnimeID] = ep == null;
                        else animeIDs.Add(xref.AnimeID, ep == null);
                    }
                }
                else
                {
                    // check if we have the episode info
                    // if we don't, we will need to re-download the anime info (which also has episode info)
                    var xrefs = aniFile.EpisodeCrossRefs;
                    if (xrefs.Count == 0)
                    {
                        xrefs.Select(a => a.AnimeID).Distinct().ForEach(animeID =>
                        {
                            if (animeIDs.ContainsKey(animeID)) animeIDs[animeID] = true;
                            else animeIDs.Add(animeID, true);
                        });

                        // if we have the AniDB file, but no cross refs it means something has been broken
                        Logger.LogDebug("Could not find any cross ref records for: {Ed2KHash}", vidLocal.ED2KHash);
                    }
                    else
                    {
                        foreach (var xref in xrefs)
                        {
                            var ep = RepoFactory.AniDB_Episode.GetByEpisodeID(xref.EpisodeID);

                            if (animeIDs.ContainsKey(xref.AnimeID)) animeIDs[xref.AnimeID] = animeIDs[xref.AnimeID] || ep == null;
                            else animeIDs.Add(xref.AnimeID, ep == null);
                        }
                    }
                    
                    // we have an AniDB File, so check the release group info
                    if (aniFile.GroupID != 0)
                    {
                        var releaseGroup = RepoFactory.AniDB_ReleaseGroup.GetByGroupID(aniFile.GroupID);
                        if (releaseGroup == null)
                        {
                            // may as well download it immediately. We can change it later if it becomes an issue
                            // this will only happen if it's null, and most people grab mostly the same release groups
                            var groupCommand = _commandFactory.Create<CommandRequest_GetReleaseGroup>(c => c.GroupID = aniFile.GroupID);
                            groupCommand.ProcessCommand();
                        }
                    }
                }

                PopulateAnimeForFile(vidLocal, animeIDs);

                // We do this inside, as the info will not be available as needed otherwise
                var videoLocals =
                    aniFile?.EpisodeIDs?.SelectMany(a => RepoFactory.VideoLocal.GetByAniDBEpisodeID(a))
                        .Where(b => b != null)
                        .ToList();
                if (videoLocals != null)
                {
                    if (ServerSettings.Instance.Import.UseExistingFileWatchedStatus)
                    {
                        // Copy over watched states
                        foreach (var user in RepoFactory.JMMUser.GetAll())
                        {
                            var watchedVideo = videoLocals.FirstOrDefault(a =>
                                a?.GetUserRecord(user.JMMUserID)?.WatchedDate != null);
                            // No files that are watched
                            if (watchedVideo == null) continue;

                            var watchedRecord = watchedVideo.GetUserRecord(user.JMMUserID);
                            var userRecord = vidLocal.GetOrCreateUserRecord(user.JMMUserID);

                            userRecord.WatchedDate = watchedRecord.WatchedDate;
                            userRecord.WatchedCount = watchedRecord.WatchedCount;
                            userRecord.ResumePosition = watchedRecord.ResumePosition;

                            userRecord.LastUpdated = DateTime.Now;
                            RepoFactory.VideoLocalUser.Save(userRecord);
                        }
                    }

                    // update stats for groups and series. The series are not saved until here, so it's absolutely necessary!!
                    animeIDs.Keys.ForEach(SVR_AniDB_Anime.UpdateStatsByAnimeID);

                    if (ServerSettings.Instance.FileQualityFilterEnabled)
                    {
                        videoLocals.Sort(FileQualityFilter.CompareTo);
                        var keep = videoLocals
                            .Take(FileQualityFilter.Settings.MaxNumberOfFilesToKeep)
                            .ToList();
                        foreach (var vl2 in keep) videoLocals.Remove(vl2);
                        if (!FileQualityFilter.Settings.AllowDeletionOfImportedFiles &&
                            videoLocals.Contains(vidLocal)) videoLocals.Remove(vidLocal);
                        videoLocals = videoLocals.Where(a => !FileQualityFilter.CheckFileKeep(a)).ToList();

                        videoLocals.ForEach(a => a.Places.ForEach(b => b.RemoveRecordAndDeletePhysicalFile()));
                    }
                }

                // Add this file to the users list
                if (ServerSettings.Instance.AniDb.MyList_AddFiles && !SkipMyList && vidLocal.MyListID <= 0)
                {
                    _commandFactory.Create<CommandRequest_AddFileToMyList>(c =>
                    {
                        c.Hash = vidLocal.ED2KHash;
                        c.ReadStates = true;
                    }).Save();
                }

                return aniFile;
            }
        }

        private void PopulateAnimeForFile(SVR_VideoLocal vidLocal, Dictionary<int, bool> animeIDs)
        {
            foreach (var kV in animeIDs)
            {
                var animeID = kV.Key;
                var missingEpisodes = kV.Value;
                // get from DB
                var anime = RepoFactory.AniDB_Anime.GetByAnimeID(animeID);
                var update = RepoFactory.AniDB_AnimeUpdate.GetByAnimeID(animeID);
                var animeRecentlyUpdated = false;

                if (anime != null && update != null)
                {
                    var ts = DateTime.Now - update.UpdatedAt;
                    if (ts.TotalHours < 4) animeRecentlyUpdated = true;
                }
                else
                    missingEpisodes = true;

                // even if we are missing episode info, don't get data  more than once every 4 hours
                // this is to prevent banning
                if (missingEpisodes && !animeRecentlyUpdated)
                {
                    Logger.LogDebug("Getting Anime record from AniDB....");
                    // this should detect and handle a ban, which will leave Result null, and defer
                    var animeCommand = _commandFactory.Create<CommandRequest_GetAnimeHTTP>(c =>
                    {
                        c.AnimeID = animeID;
                        c.ForceRefresh = true;
                        c.DownloadRelations = ServerSettings.Instance.AutoGroupSeries || ServerSettings.Instance.AniDb.DownloadRelatedAnime;
                        c.CreateSeriesEntry = true;
                    });

                    animeCommand.ProcessCommand();
                    anime = animeCommand.Result;
                }

                // create the group/series/episode records if needed
                if (anime == null)
                {
                    Logger.LogWarning($"Unable to create AniDB_Anime for file: {vidLocal.FileName}");
                    Logger.LogWarning("Queuing GET for AniDB_Anime: {AnimeID}", animeID);
                    var animeCommand = _commandFactory.Create<CommandRequest_GetAnimeHTTP>(
                        c =>
                        {
                            c.AnimeID = animeID;
                            c.ForceRefresh = true;
                            c.DownloadRelations = ServerSettings.Instance.AutoGroupSeries || ServerSettings.Instance.AniDb.DownloadRelatedAnime;
                            c.CreateSeriesEntry = ServerSettings.Instance.AniDb.AutomaticallyImportSeries;
                        }
                    );
                    animeCommand.Save();
                    return;
                }

                Logger.LogDebug("Creating groups, series and episodes....");
                // check if there is an AnimeSeries Record associated with this AnimeID
                var ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);

                if (ser == null)
                {
                    // We will put UpdatedAt in the CreateAnimeSeriesAndGroup method, to ensure it exists at first write
                    ser = anime.CreateAnimeSeriesAndGroup();
                    ser.CreateAnimeEpisodes(anime);
                }
                else
                {
                    var ts = DateTime.Now - ser.UpdatedAt;

                    // don't even check episodes if we've done it recently...
                    if (ts.TotalHours > 6)
                    {
                        if (ser.NeedsEpisodeUpdate())
                        {
                            Logger.LogInformation(
                                "Series {Title} needs episodes regenerated (an episode was added or deleted from AniDB)", anime.MainTitle
                            );
                            ser.CreateAnimeEpisodes(anime);
                            ser.UpdatedAt = DateTime.Now;
                        }
                    }
                }

                // check if we have any group status data for this associated anime
                // if not we will download it now
                if (RepoFactory.AniDB_GroupStatus.GetByAnimeID(anime.AnimeID).Count == 0)
                    _commandFactory.Create<CommandRequest_GetReleaseGroupStatus>(c => c.AnimeID = anime.AnimeID).Save();

                // Only save the date, we'll update GroupFilters and stats in one pass
                // don't bother saving the series here, it'll happen in SVR_AniDB_Anime.UpdateStatsByAnimeID()
                // just don't do anything that needs this changed data before then
                ser.EpisodeAddedDate = DateTime.Now;

                foreach (var grp in ser.AllGroupsAbove)
                {
                    grp.EpisodeAddedDate = DateTime.Now;
                    RepoFactory.AnimeGroup.Save(grp, false, false, false);
                }
            }
        }

        private SVR_AniDB_File TryGetAniDBFileFromAniDB(SVR_VideoLocal vidLocal, Dictionary<int, bool> animeIDs)
        {
            // check if we already have a record
            var aniFile = RepoFactory.AniDB_File.GetByHashAndFileSize(vidLocal.Hash, vlocal.FileSize);

            if (aniFile == null || aniFile.FileSize != vlocal.FileSize) ForceAniDB = true;

            if (ForceAniDB)
            {
                // get info from AniDB
                Logger.LogDebug("Getting AniDB_File record from AniDB....");
                try
                {
                    var fileCommand = _commandFactory.Create<CommandRequest_GetFile>(c =>
                    {
                        c.VideoLocalID = vlocal.VideoLocalID;
                        c.ForceAniDB = true;
                        c.BubbleExceptions = true;
                    });
                    fileCommand.ProcessCommand();
                    aniFile = fileCommand.Result;
                }
                catch (AniDBBannedException)
                {
                    // We're banned, so queue it for later
                    Logger.LogError("We are banned. Re-queuing {CommandID} for later", CommandID);
                    var fileCommand = _commandFactory.Create<CommandRequest_ProcessFile>(
                        c =>
                        {
                            c.VideoLocalID = vlocal.VideoLocalID;
                            c.ForceAniDB = true;
                        }
                    );
                    fileCommand.Save(true);
                }
            }

            if (aniFile == null) return null;
            // get Anime IDs from the file for processing, the episodes might not be created yet here
            aniFile.EpisodeCrossRefs.Select(a => a.AnimeID).Distinct().ForEach(animeID =>
            {
                if (animeIDs.ContainsKey(animeID)) animeIDs[animeID] = false;
                else animeIDs.Add(animeID, false);
            });

            return aniFile;
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_ProcessFile_{VideoLocalID}";
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length <= 0) return true;
            var docCreator = new XmlDocument();
            docCreator.LoadXml(CommandDetails);

            // populate the fields
            VideoLocalID = int.Parse(TryGetProperty(docCreator, "CommandRequest_ProcessFile", "VideoLocalID"));
            ForceAniDB = bool.Parse(TryGetProperty(docCreator, "CommandRequest_ProcessFile", "ForceAniDB"));
            SkipMyList = bool.Parse(TryGetProperty(docCreator, "CommandRequest_ProcessFile", "SkipMyList"));
            vlocal = RepoFactory.VideoLocal.GetByID(VideoLocalID);

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            var cq = new CommandRequest
            {
                CommandID = CommandID,
                CommandType = CommandType,
                Priority = Priority,
                CommandDetails = ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }

        public CommandRequest_ProcessFile(ILoggerFactory loggerFactory, ICommandRequestFactory commandFactory) : base(loggerFactory)
        {
            _commandFactory = commandFactory;
        }
        
        protected CommandRequest_ProcessFile()
        {
        }
    }
}
