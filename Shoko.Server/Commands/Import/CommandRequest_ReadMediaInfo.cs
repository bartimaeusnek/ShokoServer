﻿using System;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Generic;
using Shoko.Server.Repositories;
using Shoko.Server.Server;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.ReadMediaInfo)]
    public class CommandRequest_ReadMediaInfo : CommandRequestImplementation
    {
        public int VideoLocalID { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority4;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            message = "Reading media info for file: {0}",
            queueState = QueueStateEnum.ReadingMedia,
            extraParams = new[] {VideoLocalID.ToString()}
        };

        protected override void Process()
        {
            Logger.LogInformation("Reading Media Info for File: {VideoLocalID}", VideoLocalID);

            try
            {
                var vlocal = RepoFactory.VideoLocal.GetByID(VideoLocalID);
                var place = vlocal?.GetBestVideoLocalPlace(true);
                if (place == null)
                {
                    Logger.LogError("Could not find Video: {VideoLocalID}", VideoLocalID);
                    return;
                }
                if (place.RefreshMediaInfo())
                    RepoFactory.VideoLocal.Save(place.VideoLocal, true);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error processing CommandRequest_ReadMediaInfo: {VideoLocalID} - {Ex}", VideoLocalID, ex);
            }
        }


        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_ReadMediaInfo_{VideoLocalID}";
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                var docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                VideoLocalID = int.Parse(
                    TryGetProperty(docCreator, "CommandRequest_ReadMediaInfo", "VideoLocalID"));
            }

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

        public CommandRequest_ReadMediaInfo(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        protected CommandRequest_ReadMediaInfo()
        {
        }
    }
}