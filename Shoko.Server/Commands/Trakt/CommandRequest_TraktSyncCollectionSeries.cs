﻿using System;
using System.Globalization;
using System.Threading;
using System.Xml;
using Shoko.Models.Queue;
using Shoko.Server.Repositories.Cached;
using Shoko.Models.Server;
using Shoko.Server.Models;
using Shoko.Server.Providers.TraktTV;
using Shoko.Server.Repositories;

namespace Shoko.Server.Commands
{
    [Serializable]
    public class CommandRequest_TraktSyncCollectionSeries : CommandRequestImplementation, ICommandRequest
    {
        public int AnimeSeriesID { get; set; }
        public string SeriesName { get; set; }

        public CommandRequestPriority DefaultPriority
        {
            get { return CommandRequestPriority.Priority9; }
        }

        public QueueStateStruct PrettyDescription
        {
            get
            {
                return new QueueStateStruct()
                {
                    queueState = QueueStateEnum.SyncTraktSeries,
                    extraParams = new string[] {SeriesName}
                };
            }
        }

        public CommandRequest_TraktSyncCollectionSeries()
        {
        }

        public CommandRequest_TraktSyncCollectionSeries(int animeSeriesID, string seriesName)
        {
            this.AnimeSeriesID = animeSeriesID;
            this.SeriesName = seriesName;
            this.CommandType = (int) CommandRequestType.Trakt_SyncCollectionSeries;
            this.Priority = (int) DefaultPriority;

            GenerateCommandID();
        }

        public override void ProcessCommand()
        {
            logger.Info("Processing CommandRequest_TraktSyncCollectionSeries");

            try
            {
                if (!ServerSettings.Trakt_IsEnabled || string.IsNullOrEmpty(ServerSettings.Trakt_AuthToken)) return;

                SVR_AnimeSeries series = RepoFactory.AnimeSeries.GetByID(AnimeSeriesID);
                if (series == null)
                {
                    logger.Error("Could not find anime series: {0}", AnimeSeriesID);
                    return;
                }

                TraktTVHelper.SyncCollectionToTrakt_Series(series);
            }
            catch (Exception ex)
            {
                logger.Error("Error processing CommandRequest_TraktSyncCollectionSeries: {0}", ex.ToString());
                return;
            }
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            this.CommandID = string.Format("CommandRequest_TraktSyncCollectionSeries_{0}", AnimeSeriesID);
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            this.CommandID = cq.CommandID;
            this.CommandRequestID = cq.CommandRequestID;
            this.CommandType = cq.CommandType;
            this.Priority = cq.Priority;
            this.CommandDetails = cq.CommandDetails;
            this.DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (this.CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(this.CommandDetails);

                // populate the fields
                this.AnimeSeriesID =
                    int.Parse(TryGetProperty(docCreator, "CommandRequest_TraktSyncCollectionSeries", "AnimeSeriesID"));
                this.SeriesName = TryGetProperty(docCreator, "CommandRequest_TraktSyncCollectionSeries", "SeriesName");
            }

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest();
            cq.CommandID = this.CommandID;
            cq.CommandType = this.CommandType;
            cq.Priority = this.Priority;
            cq.CommandDetails = this.ToXML();
            cq.DateTimeUpdated = DateTime.Now;

            return cq;
        }
    }
}