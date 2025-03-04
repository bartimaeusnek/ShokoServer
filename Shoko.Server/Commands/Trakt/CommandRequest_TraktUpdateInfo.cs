﻿using System;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Generic;
using Shoko.Server.Providers.TraktTV;
using Shoko.Server.Server;

namespace Shoko.Server.Commands
{
    [Serializable]
    [Command(CommandRequestType.Trakt_UpdateInfo)]
    public class CommandRequest_TraktUpdateInfo : CommandRequestImplementation
    {
        private readonly TraktTVHelper _helper;
        public string TraktID { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority6;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            message = "Updating info/images on Trakt.TV: {0}",
            queueState = QueueStateEnum.UpdateTraktData,
            extraParams = new[] {TraktID}
        };

        protected override void Process()
        {
            Logger.LogInformation("Processing CommandRequest_TraktUpdateInfo: {0}", TraktID);

            try
            {
                _helper.UpdateAllInfo(TraktID);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error processing CommandRequest_TraktUpdateInfo: {0} - {1}", TraktID,
                    ex);
            }
        }


        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_TraktUpdateInfo{TraktID}";
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
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                TraktID = TryGetProperty(docCreator, "CommandRequest_TraktUpdateInfo", "TraktID");
            }

            return true;
        }

        public override CommandRequest ToDatabaseObject()
        {
            GenerateCommandID();

            CommandRequest cq = new CommandRequest
            {
                CommandID = CommandID,
                CommandType = CommandType,
                Priority = Priority,
                CommandDetails = ToXML(),
                DateTimeUpdated = DateTime.Now
            };
            return cq;
        }

        public CommandRequest_TraktUpdateInfo(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        protected CommandRequest_TraktUpdateInfo()
        {
        }
    }
}