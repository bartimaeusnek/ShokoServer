﻿using System;
using System.Globalization;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Commons.Queue;
using Shoko.Models.Enums;
using Shoko.Models.Queue;
using Shoko.Models.Server;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Generic;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Providers.AniDB.UDP.User;
using Shoko.Server.Server;

namespace Shoko.Server.Commands.AniDB
{
    [Serializable]
    [Command(CommandRequestType.AniDB_VoteAnime)]
    public class CommandRequest_VoteAnime : CommandRequestImplementation
    {
        private readonly IRequestFactory _requestFactory;
        public int AnimeID { get; set; }
        public int VoteType { get; set; }
        public decimal VoteValue { get; set; }

        public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority6;

        public override QueueStateStruct PrettyDescription => new QueueStateStruct
        {
            message = "Voting: {0} - {1}",
            queueState = QueueStateEnum.VoteAnime,
            extraParams = new[] {AnimeID.ToString(), VoteValue.ToString()}
        };

        protected override void Process()
        {
            Logger.LogInformation("Processing CommandRequest_Vote: {CommandID}", CommandID);

            try
            {
                var vote = _requestFactory.Create<RequestVoteAnime>(
                    r =>
                    {
                        r.Temporary = VoteType == (int)AniDBVoteType.AnimeTemp;
                        r.Value = Convert.ToDouble(VoteValue);
                        r.AnimeID = AnimeID;
                    }
                );
                vote.Execute();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing CommandRequest_Vote: {CommandID} - {Exception}", CommandID, ex);
            }
        }

        /// <summary>
        /// This should generate a unique key for a command
        /// It will be used to check whether the command has already been queued before adding it
        /// </summary>
        public override void GenerateCommandID()
        {
            CommandID = $"CommandRequest_Vote_{AnimeID}_{VoteType}_{VoteValue}";
        }

        public override bool LoadFromDBCommand(CommandRequest cq)
        {
            CommandID = cq.CommandID;
            CommandRequestID = cq.CommandRequestID;
            Priority = cq.Priority;
            CommandDetails = cq.CommandDetails;
            DateTimeUpdated = cq.DateTimeUpdated;

            NumberStyles style = NumberStyles.Number;
            CultureInfo culture = CultureInfo.CreateSpecificCulture("en-GB");

            // read xml to get parameters
            if (CommandDetails.Trim().Length > 0)
            {
                XmlDocument docCreator = new XmlDocument();
                docCreator.LoadXml(CommandDetails);

                // populate the fields
                AnimeID = int.Parse(TryGetProperty(docCreator, "CommandRequest_VoteAnime", "AnimeID"));
                VoteType = int.Parse(TryGetProperty(docCreator, "CommandRequest_VoteAnime", "VoteType"));
                VoteValue = decimal.Parse(TryGetProperty(docCreator, "CommandRequest_VoteAnime", "VoteValue"),
                    style, culture);
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

        public CommandRequest_VoteAnime(ILoggerFactory loggerFactory, IRequestFactory requestFactory) : base(loggerFactory)
        {
            _requestFactory = requestFactory;
        }

        protected CommandRequest_VoteAnime()
        {
        }
    }
}
