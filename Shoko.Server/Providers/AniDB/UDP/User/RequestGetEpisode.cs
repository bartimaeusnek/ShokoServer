using System;
using Microsoft.Extensions.Logging;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Providers.AniDB.UDP.Exceptions;
using Shoko.Server.Providers.AniDB.UDP.Generic;

namespace Shoko.Server.Providers.AniDB.UDP.User
{
    public class RequestGetEpisode : UDPRequest<ResponseMyListFile>
    {
        protected override string BaseCommand
        {
            get
            {
                var type = "";
                if (EpisodeType != EpisodeType.Episode)
                    type = EpisodeType.ToString()[..1];
                return $"MYLIST aid={AnimeID}&epno={type+EpisodeNumber}";
            }
        }

        public int AnimeID { get; set; }

        public int EpisodeNumber { get; set; }
        public EpisodeType EpisodeType { get; set; } = EpisodeType.Episode;

        protected override UDPResponse<ResponseMyListFile> ParseResponse(UDPResponse<string> response)
        {
            var code = response.Code;
            var receivedData = response.Response;
            switch (code)
            {
                case UDPReturnCode.NO_SUCH_ENTRY:
                    return new UDPResponse<ResponseMyListFile>
                    {
                        Code = code,
                        Response = null,
                    };
                case UDPReturnCode.MYLIST:
                {
                    /* Response Format
                     * {int4 lid}|{int4 fid}|{int4 eid}|{int4 aid}|{int4 gid}|{int4 date}|{int2 state}|{int4 viewdate}|{str storage}|{str source}|{str other}|{int2 filestate}
                     */
                    //file already exists: read 'watched' status
                    var arrStatus = receivedData.Split('|');
                    // We expect 0 for a MyListID
                    int.TryParse(arrStatus[0], out var myListID);

                    var state = (MyList_State) int.Parse(arrStatus[6]);

                    var viewdate = int.Parse(arrStatus[7]);
                    var updatedate = int.Parse(arrStatus[5]);
                    var watched = viewdate > 0;
                    DateTime? updatedAt = null;
                    DateTime? watchedDate = null;
                    if (updatedate > 0)
                        updatedAt = DateTime.UnixEpoch
                        .AddSeconds(updatedate)
                        .ToLocalTime();
                    if (watched)
                        watchedDate = DateTime.UnixEpoch
                            .AddSeconds(viewdate)
                            .ToLocalTime();

                    return new UDPResponse<ResponseMyListFile>
                    {
                        Code = code,
                        Response = new ResponseMyListFile
                        {
                            MyListID = myListID,
                            State = state,
                            IsWatched = watched,
                            WatchedDate = watchedDate,
                            UpdatedAt = updatedAt,
                        },
                    };
                }
            }
            throw new UnexpectedUDPResponseException(code, receivedData);
        }

        public RequestGetEpisode(ILoggerFactory loggerFactory, IUDPConnectionHandler handler) : base(loggerFactory, handler)
        {
        }
    }
}
